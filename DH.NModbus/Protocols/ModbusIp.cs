using System.Diagnostics.CodeAnalysis;
using NewLife.Data;
using NewLife.Net;
using NewLife.Serialization;

namespace NewLife.IoT.Protocols;

/// <summary>Modbus以太网通信</summary>
/// <remarks>
/// ADU规定为256
/// </remarks>
public abstract class ModbusIp : Modbus
{
    #region 属性
    /// <summary>服务端地址。tcp://127.0.0.1:502</summary>
    public String Server { get; set; } = null!;

    /// <summary>网络客户端</summary>
    protected ISocketClient? _client;
    #endregion

    #region 构造
    /// <summary>实例化</summary>
    public ModbusIp()
    {
        // ADU大小。默认标准256，可扩大到65535
        BufferSize = 1024;
    }

    /// <summary>销毁</summary>
    /// <param name="disposing"></param>
    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            CloseAsync(CancellationToken.None).GetAwaiter().GetResult();

        _client.TryDispose();
        _client = null;
    }
    #endregion

    #region 方法
    /// <summary>初始化。传入配置</summary>
    /// <param name="parameters"></param>
    public override void Init(IDictionary<String, Object> parameters)
    {
        if (parameters.TryGetValue("Server", out var str))
            Server = str + "";
        if (Server.IsNullOrEmpty() && parameters.TryGetValue("Address", out str))
            Server = str + "";
    }

    /// <summary>异步打开</summary>
    /// <param name="cancellationToken">取消令牌</param>
    [MemberNotNull(nameof(_client))]
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

        await client.OpenAsync(cancellationToken).ConfigureAwait(false);

        _client = client;

        WriteLog("{0}.Open {1}", Name, uri);
    }

    /// <summary>异步关闭</summary>
    /// <param name="cancellationToken">取消令牌</param>
    public override async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        var client = _client;
        if (client != null)
        {
            await client.CloseAsync("Close", cancellationToken).ConfigureAwait(false);
            client.TryDispose();
            _client = null;

            WriteLog("{0}.Close", Name);
        }
    }

    /// <summary>异步重连。关闭现有连接后重新打开</summary>
    /// <param name="cancellationToken">取消令牌</param>
    protected override async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        WriteLog("{0}.Reconnect", Name);

        await CloseAsync(cancellationToken).ConfigureAwait(false);
        await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>从数据包中解析Modbus消息</summary>
    /// <param name="request">请求消息</param>
    /// <param name="data">目标数据包</param>
    /// <param name="match">是否匹配请求</param>
    /// <returns>响应消息</returns>
    protected abstract ModbusMessage? ReadMessage(ModbusMessage request, IPacket data, out Boolean match);

    /// <summary>异步接收响应</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    protected virtual async Task<IOwnerPacket?> ReceiveCommandAsync(CancellationToken cancellationToken = default)
    {
        await OpenAsync(cancellationToken).ConfigureAwait(false);

        // 设置协议最短长度，避免读取指令不完整。由于请求响应机制，不存在粘包返回。
        var dataLength = 8; // 2+2+2+1+1
        IOwnerPacket? pk = null;
        for (var i = 0; i < 8; i++)
        {
            // 阻塞读取
            var pk2 = _client.Receive();
            if (pk2 == null || pk2.Total == 0) continue;

            if (pk == null)
                pk = pk2;
            else
                pk.Append(pk2);

            // 已取得请求头，计算真实长度
            var count = pk.Total;
            if (count >= 6) dataLength = pk.ReadBytes(4, 2).ToUInt16(0, false);
            if (count >= dataLength) break;
        }

        return pk;
    }

    /// <summary>异步发送消息并接收返回</summary>
    /// <param name="message">Modbus消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    internal protected override async Task<ModbusMessage?> SendCommandAsync(ModbusMessage message, CancellationToken cancellationToken)
    {
        await OpenAsync(cancellationToken).ConfigureAwait(false);

        Log?.Debug("=> {0}", message);

        var cmd = message.ToPacket();
        using var span = Tracer?.NewSpan("modbus:SendCommand", cmd.ToHex(64, "-"));
        try
        {
            _client.Send(cmd);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }

        //using var span = Tracer?.NewSpan("modbus:ReceiveCommand");
        try
        {
            while (true)
            {
                // 设置协议最短长度，避免读取指令不完整。由于请求响应机制，不存在粘包返回。
                using var pk = await ReceiveCommandAsync(cancellationToken).ConfigureAwait(false);
                if (pk == null) continue;

                if (span != null) span.Tag += Environment.NewLine + pk.ToHex(64, "-");

                var rs = ReadMessage(message, pk, out var match);
                if (rs == null) return null;

                Log?.Debug("<= {0}", rs);

                // 检查是否匹配
                if (!match) continue;

                // 检查功能码
                if (rs.ErrorCode > 0) throw new ModbusException(rs.ErrorCode, rs.ErrorCode.GetDescription()!);

                return rs;
            }
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            if (ex is TimeoutException) return null;
            throw;
        }
    }

    /// <summary>按功能码异步写入。用于IoT标准库</summary>
    /// <param name="code">功能码</param>
    /// <param name="host">主机</param>
    /// <param name="address">逻辑地址</param>
    /// <param name="values">待写入数值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public override async Task<Object> WriteAsync(FunctionCodes code, Byte host, UInt16 address, UInt16[] values, CancellationToken cancellationToken = default)
    {
        switch (code)
        {
            case FunctionCodes.WriteCoil:
                {
                    var rs = await SendCommandAsync(FunctionCodes.WriteCoil, host, address, values[0], cancellationToken).ConfigureAwait(false);
                    if (rs == null || rs.Total < 4) return -1;
                    return rs.ReadBytes(2, 2).ToUInt16(0, false);
                }
            case FunctionCodes.WriteRegister:
                {
                    var rs = await SendCommandAsync(FunctionCodes.WriteRegister, host, address, values[0], cancellationToken).ConfigureAwait(false);
                    if (rs == null || rs.Total < 4) return -1;
                    return rs.ReadBytes(2, 2).ToUInt16(0, false);
                }
            case FunctionCodes.WriteCoils:
                {
                    // 多个UInt16数值，合并成为负载数据
                    var binary = new Binary { IsLittleEndian = false };
                    binary.Write(address);
                    binary.Write((UInt16)values.Length);
                    binary.Write((Byte)Math.Ceiling(values.Length / 8.0));

                    var b = 0;
                    var k = 0;
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (values[i] != 0) b |= 1 << k;
                        if (k++ >= 7) { binary.Write((Byte)b); b = 0; k = 0; }
                    }
                    if (k > 0) binary.Write((Byte)b);

                    binary.Stream.Position = 0;
                    var pk3 = new ArrayPacket(binary.Stream);
                    var rs3 = await SendCommandAsync(FunctionCodes.WriteCoils, host, pk3, cancellationToken).ConfigureAwait(false);
                    if (rs3 == null || rs3.Total < 4) return -1;
                    return rs3.ReadBytes(2, 2).ToUInt16(0, false);
                }
            case FunctionCodes.WriteRegisters:
                {
                    var binary = new Binary { IsLittleEndian = false };
                    binary.Write(address);
                    binary.Write((UInt16)values.Length);
                    binary.Write((Byte)(values.Length * 2));
                    foreach (var item in values) binary.Write(item);

                    binary.Stream.Position = 0;
                    var pk4 = new ArrayPacket(binary.Stream);
                    var rs4 = await SendCommandAsync(FunctionCodes.WriteRegisters, host, pk4, cancellationToken).ConfigureAwait(false);
                    if (rs4 == null || rs4.Total < 4) return -1;
                    return rs4.ReadBytes(2, 2).ToUInt16(0, false);
                }
            default:
                break;
        }

        throw new NotSupportedException($"ModbusWrite不支持[{code}]");
    }
    #endregion
}