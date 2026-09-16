using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NewLife.Data;
using NewLife.Net;

namespace NewLife.IoT.Protocols;

/// <summary>ModbusTCP网口通信</summary>
/// <remarks>
/// 支持标准 Modbus TCP/IP。配置 <see cref="SslProtocol"/> 后自动启用 TLS 安全传输。
/// 
/// 使用示例：
/// <code>
/// // 标准 TCP
/// var modbus = new ModbusTcp { Server = "tcp://127.0.0.1:502" };
/// modbus.Open();
/// var data = modbus.ReadRegister(1, 0, 10);
/// 
/// // TLS 模式 — 设置 SslProtocol 即可
/// var tlsModbus = new ModbusTcp
/// {
///     Server = "tcp://192.168.1.100:802",
///     SslProtocol = SslProtocols.Tls12,
/// };
/// tlsModbus.Open();
/// </code>
/// 
/// 服务端 TLS（ModbusSlave）：
/// <code>
/// var slave = new ModbusSlave();
/// slave.Port = 802;
/// slave.SslProtocol = SslProtocols.Tls12;
/// slave.Certificate = new X509Certificate2("server.pfx", "password");
/// slave.Start();
/// </code>
/// </remarks>
public class ModbusTcp : ModbusIp
{
    #region 属性
    /// <summary>协议标识。默认0</summary>
    public UInt16 ProtocolId { get; set; }

    /// <summary>SSL协议版本。默认 None 表示不启用 TLS</summary>
    /// <remarks>设置为 Tls12 或 Tls13 启用安全传输</remarks>
    public SslProtocols SslProtocol { get; set; }

    /// <summary>X509证书。用于客户端验证服务端证书，或客户端证书认证</summary>
    /// <remarks>
    /// 用于 SSL 连接时验证证书指纹，可以直接加载 pem 证书文件，未指定时不验证证书。
    /// <code>
    /// var cert = new X509Certificate2("file", "pass");
    /// </code>
    /// </remarks>
    public X509Certificate? Certificate { get; set; }

    private Int32 _transactionId;
    #endregion

    #region 构造
    #endregion

    #region 方法
    /// <summary>初始化。传入配置</summary>
    /// <param name="parameters"></param>
    public override void Init(IDictionary<String, Object> parameters)
    {
        base.Init(parameters);

        if (parameters.TryGetValue("ProtocolId", out var str)) ProtocolId = (UInt16)str.ToInt();

        if (parameters.TryGetValue("SslProtocol", out var value))
        {
            if (value is SslProtocols sp)
                SslProtocol = sp;
            else if (value != null)
                SslProtocol = (SslProtocols)Enum.Parse(typeof(SslProtocols), value + "");
        }

        if (parameters.TryGetValue("Certificate", out var cert))
        {
            if (cert is X509Certificate x509)
                Certificate = x509;
            else if (cert is Byte[] certData)
                Certificate = new X509Certificate2(certData);
        }
    }

    /// <summary>异步打开。如果配置了 <see cref="SslProtocol"/> 则建立 TLS 安全连接</summary>
    /// <param name="cancellationToken">取消令牌</param>
    public override async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_client != null && !_client.Disposed) return;

        if (Server.IsNullOrEmpty()) throw new Exception($"{Name}未指定服务端地址Server");

        var uri = new NetUri(Server);
        if (uri.Type <= 0) uri.Type = NetType.Tcp;
        if (uri.Port == 0) uri.Port = 502;

        var client = uri.CreateRemote();
        client.Timeout = Timeout;

        // 使用同步接收，每个数据帧最大256字节
        if (client is SessionBase session)
        {
            session.MaxAsync = 0;
            session.BufferSize = BufferSize;
        }

        // 配置 TLS
        if (SslProtocol != SslProtocols.None && client is TcpSession tcpSession)
        {
            tcpSession.SslProtocol = SslProtocol;
            if (Certificate != null)
                tcpSession.Certificate = Certificate;
        }

        await client.OpenAsync(cancellationToken).ConfigureAwait(false);

        _client = client;

        WriteLog("{0}.Open {1} SSL={2}", Name, uri, SslProtocol);
    }

    /// <summary>创建消息</summary>
    /// <returns></returns>
    protected override ModbusMessage CreateMessage() => new ModbusIpMessage
    {
        ProtocolId = ProtocolId,
        TransactionId = (UInt16)Interlocked.Increment(ref _transactionId)
    };

    /// <summary>从数据包中解析Modbus消息</summary>
    /// <param name="request">请求消息</param>
    /// <param name="data">目标数据包</param>
    /// <param name="match">是否匹配请求</param>
    /// <returns>响应消息</returns>
    protected override ModbusMessage? ReadMessage(ModbusMessage request, IPacket data, out Boolean match)
    {
        match = true;

        var rs = ModbusIpMessage.Read(data.GetSpan(), true);
        if (rs == null) return null;

        Log?.Debug("<= {0}", rs);

        // 检查事务标识
        if (request is ModbusIpMessage mtm && mtm.TransactionId != rs.TransactionId)
        {
            WriteLog("TransactionId Error {0}!={1}", rs.TransactionId, mtm.TransactionId);

            // 读取到前一条，抛弃它，继续读取
            if (rs.TransactionId < mtm.TransactionId)
            {
                match = false;
                return rs;
            }

            return null;
        }

        return rs;
    }
    #endregion
}