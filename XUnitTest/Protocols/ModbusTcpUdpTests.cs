using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Protocols;

/// <summary>ModbusTcp / ModbusUdp 单元测试（不依赖网络）</summary>
/// <remarks>
/// 覆盖 Init(ProtocolId 解析)、ReadMessage(TransactionId 不匹配路径)
/// 以及 ModbusIp.Init 的 Address 回退逻辑和缺失 Server 时 Open 抛异常。
/// </remarks>
public class ModbusTcpUdpTests
{
    // ────────────────────────────────────────────────────────────────────────
    #region 辅助：可测试子类（暴露 protected ReadMessage）

    /// <summary>暴露 ModbusTcp.ReadMessage 的可测试子类</summary>
    private sealed class TestableTcp : ModbusTcp
    {
        public ModbusMessage? CallReadMessage(ModbusMessage request, IPacket data, out Boolean match)
            => ReadMessage(request, data, out match);
    }

    /// <summary>暴露 ModbusUdp.ReadMessage 的可测试子类</summary>
    private sealed class TestableUdp : ModbusUdp
    {
        public ModbusMessage? CallReadMessage(ModbusMessage request, IPacket data, out Boolean match)
            => ReadMessage(request, data, out match);
    }

    /// <summary>暴露 ModbusTcp 更多 protected 成员的可测试子类</summary>
    private sealed class ExtendedTestableTcp : ModbusTcp
    {
        public new ModbusMessage PublicCreateMessage() => base.CreateMessage();

        /// <summary>不连真实网络，仅测试 Init + CreateMessage</summary>
        public void CallInit(IDictionary<String, Object> parameters) => Init(parameters);
    }

    /// <summary>构造一个合法的 MBAP+PDU 响应包，指定 TransactionId</summary>
    private static IPacket MakeIpResponse(UInt16 transactionId, FunctionCodes code = FunctionCodes.ReadRegister)
    {
        // MBAP: TxId(2) ProtocolId(2) Length(2) UnitId(1)  PDU: Code(1) ByteCount(1) Data(2)
        var buf = new Byte[]
        {
            (Byte)(transactionId >> 8), (Byte)(transactionId & 0xFF),  // TransactionId
            0x00, 0x00,                                                  // ProtocolId
            0x00, 0x05,                                                  // Length = 5 (UnitId+Code+BC+2bytes)
            0x01,                                                         // UnitId
            (Byte)code,                                                  // FunctionCode
            0x02,                                                        // ByteCount
            0x00, 0x64,                                                  // Data (100)
        };
        return new ArrayPacket(buf);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusTcp.Init

    [Fact]
    [DisplayName("ModbusTcp.Init 从字典中解析 ProtocolId")]
    public void ModbusTcp_Init_ParsesProtocolId()
    {
        var tcp = new ModbusTcp();
        tcp.Init(new Dictionary<String, Object> { ["ProtocolId"] = "5", ["Server"] = "tcp://127.0.0.1:502" });

        Assert.Equal(5, tcp.ProtocolId);
    }

    [Fact]
    [DisplayName("ModbusTcp.Init 无 ProtocolId 时保持默认 0")]
    public void ModbusTcp_Init_DefaultProtocolId_Zero()
    {
        var tcp = new ModbusTcp();
        tcp.Init(new Dictionary<String, Object> { ["Server"] = "tcp://127.0.0.1:502" });

        Assert.Equal(0, tcp.ProtocolId);
    }

    [Fact]
    [DisplayName("ModbusTcp.Init Server 优先于 Address 作为连接地址")]
    public void ModbusTcp_Init_ServerTakesPriorityOverAddress()
    {
        var tcp = new ModbusTcp();
        tcp.Init(new Dictionary<String, Object>
        {
            ["Server"] = "tcp://192.168.1.1:502",
            ["Address"] = "tcp://10.0.0.1:502"
        });

        Assert.Equal("tcp://192.168.1.1:502", tcp.Server);
    }

    [Fact]
    [DisplayName("ModbusTcp.Init 无 Server 时使用 Address 作为回退")]
    public void ModbusTcp_Init_FallbackToAddress()
    {
        var tcp = new ModbusTcp();
        tcp.Init(new Dictionary<String, Object> { ["Address"] = "tcp://10.0.0.1:502" });

        Assert.Equal("tcp://10.0.0.1:502", tcp.Server);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusUdp.Init

    [Fact]
    [DisplayName("ModbusUdp.Init 从字典中解析 ProtocolId")]
    public void ModbusUdp_Init_ParsesProtocolId()
    {
        var udp = new ModbusUdp();
        udp.Init(new Dictionary<String, Object> { ["ProtocolId"] = "3", ["Server"] = "udp://127.0.0.1:502" });

        Assert.Equal(3, udp.ProtocolId);
    }

    [Fact]
    [DisplayName("ModbusUdp.Init 无 ProtocolId 时保持默认 0")]
    public void ModbusUdp_Init_DefaultProtocolId_Zero()
    {
        var udp = new ModbusUdp();
        udp.Init(new Dictionary<String, Object> { ["Server"] = "udp://127.0.0.1:502" });

        Assert.Equal(0, udp.ProtocolId);
    }

    [Fact]
    [DisplayName("ModbusUdp.Init Server 回退至 Address")]
    public void ModbusUdp_Init_FallbackToAddress()
    {
        var udp = new ModbusUdp();
        udp.Init(new Dictionary<String, Object> { ["Address"] = "udp://10.0.0.2:502" });

        Assert.Equal("udp://10.0.0.2:502", udp.Server);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusIp.Open —— 缺失 Server 时抛异常

    [Fact]
    [DisplayName("ModbusIp.Open 未配置 Server 时抛 Exception")]
    public async Task ModbusTcp_Open_MissingServer_Throws()
    {
        var tcp = new ModbusTcp();  // Server 未设置

        var ex = await Assert.ThrowsAsync<Exception>(async () => await tcp.OpenAsync());
        Assert.Contains("Server", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [DisplayName("ModbusUdp.Open 未配置 Server 时抛 Exception")]
    public async Task ModbusUdp_Open_MissingServer_Throws()
    {
        var udp = new ModbusUdp();

        var ex = await Assert.ThrowsAsync<Exception>(async () => await udp.OpenAsync());
        Assert.Contains("Server", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusTcp.ReadMessage —— TransactionId 不匹配路径

    [Fact]
    [DisplayName("ReadMessage TxId 与请求一致 → 正常返回，match=true")]
    public void ModbusTcp_ReadMessage_MatchingTxId_ReturnsMessage()
    {
        var tcp = new TestableTcp();
        var request = new ModbusIpMessage { TransactionId = 42, Code = FunctionCodes.ReadRegister };
        var data = MakeIpResponse(42);

        var rs = tcp.CallReadMessage(request, data, out var match);

        Assert.NotNull(rs);
        Assert.True(match);
    }

    [Fact]
    [DisplayName("ReadMessage 响应 TxId 小于请求（旧包） → 返回消息，match=false（继续等待）")]
    public void ModbusTcp_ReadMessage_StaleTransactionId_MatchFalse()
    {
        var tcp = new TestableTcp();
        var request = new ModbusIpMessage { TransactionId = 10, Code = FunctionCodes.ReadRegister };
        var data = MakeIpResponse(8);   // 旧包 TxId=8 < 请求 TxId=10

        var rs = tcp.CallReadMessage(request, data, out var match);

        Assert.NotNull(rs);   // 返回了消息，但 match=false 告知调用者继续等待
        Assert.False(match);
    }

    [Fact]
    [DisplayName("ReadMessage 响应 TxId 大于请求（未来包） → 返回 null（异常情况）")]
    public void ModbusTcp_ReadMessage_FutureTransactionId_ReturnsNull()
    {
        var tcp = new TestableTcp();
        var request = new ModbusIpMessage { TransactionId = 10, Code = FunctionCodes.ReadRegister };
        var data = MakeIpResponse(20);  // 未来包 TxId=20 > 请求 TxId=10

        var rs = tcp.CallReadMessage(request, data, out var match);

        Assert.Null(rs);
    }

    [Fact]
    [DisplayName("ReadMessage 数据包无效（空/太短） → 抛出异常")]
    public void ModbusTcp_ReadMessage_InvalidData_ThrowsOnShortData()
    {
        var tcp = new TestableTcp();
        var request = new ModbusIpMessage { TransactionId = 1 };
        var data = new ArrayPacket([0x00, 0x01]);   // 不足以解析 MBAP

        Assert.ThrowsAny<Exception>(() => tcp.CallReadMessage(request, data, out _));
    }

    [Fact]
    [DisplayName("ReadMessage 请求非 ModbusIpMessage → 跳过 TxId 检查，正常返回")]
    public void ModbusTcp_ReadMessage_NonIpRequest_SkipsTxIdCheck()
    {
        var tcp = new TestableTcp();
        var request = new ModbusMessage { Code = FunctionCodes.ReadRegister };  // 普通 request
        var data = MakeIpResponse(99);

        var rs = tcp.CallReadMessage(request, data, out var match);

        Assert.NotNull(rs);
        Assert.True(match);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusUdp.ReadMessage —— 同上

    [Fact]
    [DisplayName("ModbusUdp ReadMessage TxId 一致 → 正常返回")]
    public void ModbusUdp_ReadMessage_MatchingTxId_ReturnsMessage()
    {
        var udp = new TestableUdp();
        var request = new ModbusIpMessage { TransactionId = 5 };
        var data = MakeIpResponse(5);

        var rs = udp.CallReadMessage(request, data, out var match);

        Assert.NotNull(rs);
        Assert.True(match);
    }

    [Fact]
    [DisplayName("ModbusUdp ReadMessage 旧包 TxId < 请求 → match=false")]
    public void ModbusUdp_ReadMessage_StaleTransactionId_MatchFalse()
    {
        var udp = new TestableUdp();
        var request = new ModbusIpMessage { TransactionId = 7 };
        var data = MakeIpResponse(3);

        var rs = udp.CallReadMessage(request, data, out var match);

        Assert.NotNull(rs);
        Assert.False(match);
    }

    [Fact]
    [DisplayName("ModbusUdp ReadMessage 未来包 TxId > 请求 → 返回 null")]
    public void ModbusUdp_ReadMessage_FutureTxId_ReturnsNull()
    {
        var udp = new TestableUdp();
        var request = new ModbusIpMessage { TransactionId = 5 };
        var data = MakeIpResponse(100);

        var rs = udp.CallReadMessage(request, data, out var match);

        Assert.Null(rs);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusTcp CreateMessage —— TransactionId 自增

    [Fact]
    [DisplayName("ModbusTcp 连续 SendCommand 产生递增 TransactionId")]
    public void ModbusTcp_CreateMessage_TransactionIdIncrement()
    {
        // 通过反射调用受保护的 CreateMessage() 验证 TxId 自增
        var tcp = new ModbusTcp();
        var method = typeof(ModbusTcp).GetMethod("CreateMessage",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var msg1 = method.Invoke(tcp, null) as ModbusIpMessage;
        var msg2 = method.Invoke(tcp, null) as ModbusIpMessage;
        Assert.NotNull(msg1);
        Assert.NotNull(msg2);
        Assert.Equal(msg1.TransactionId + 1, msg2.TransactionId);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusTcp.Init —— SslProtocol / Certificate 解析

    [Fact]
    [DisplayName("Init 解析 SslProtocol 枚举字符串")]
    public void ModbusTcp_Init_ParsesSslProtocol_FromString()
    {
        var tcp = new ExtendedTestableTcp();
        tcp.CallInit(new Dictionary<String, Object>
        {
            ["SslProtocol"] = "Tls12",
            ["Server"] = "tcp://127.0.0.1:502",
        });

        Assert.Equal(SslProtocols.Tls12, tcp.SslProtocol);
    }

    [Fact]
    [DisplayName("Init 解析 SslProtocol 枚举值（Int32）")]
    public void ModbusTcp_Init_ParsesSslProtocol_FromInt()
    {
        var tcp = new ExtendedTestableTcp();
        tcp.CallInit(new Dictionary<String, Object>
        {
            ["SslProtocol"] = (Int32)SslProtocols.Tls13,
            ["Server"] = "tcp://127.0.0.1:502",
        });

        Assert.Equal(SslProtocols.Tls13, tcp.SslProtocol);
    }

    [Fact]
    [DisplayName("Init 无 SslProtocol 时保持默认 None")]
    public void ModbusTcp_Init_DefaultSslProtocol_None()
    {
        var tcp = new ExtendedTestableTcp();
        tcp.CallInit(new Dictionary<String, Object> { ["Server"] = "tcp://127.0.0.1:502" });

        Assert.Equal(SslProtocols.None, tcp.SslProtocol);
    }

    [Fact]
    [DisplayName("Init 解析 Certificate 为 X509Certificate")]
    public void ModbusTcp_Init_ParsesCertificate_X509()
    {
        using var cert = new X509Certificate2(); // 空证书，仅测试赋值路径
        var tcp = new ExtendedTestableTcp();
        tcp.CallInit(new Dictionary<String, Object>
        {
            ["Certificate"] = cert,
            ["Server"] = "tcp://127.0.0.1:502",
        });

        Assert.NotNull(tcp.Certificate);
    }

    [Fact]
    [DisplayName("Init 解析 Certificate 为 Byte[]（非法数据抛 CryptographicException）")]
    public void ModbusTcp_Init_ParsesCertificate_ByteArray_Throws()
    {
        // 使用无效的证书数据，验证 byte[] 赋值路径被 Init 方法执行（然后抛异常）
        var certBytes = new Byte[] { 0x00, 0x01, 0x02 };
        var tcp = new ExtendedTestableTcp();
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
            tcp.CallInit(new Dictionary<String, Object>
            {
                ["Certificate"] = certBytes,
                ["Server"] = "tcp://127.0.0.1:502",
            }));
    }

    [Fact]
    [DisplayName("Init 无 Certificate 时保持 null")]
    public void ModbusTcp_Init_DefaultCertificate_Null()
    {
        var tcp = new ExtendedTestableTcp();
        tcp.CallInit(new Dictionary<String, Object> { ["Server"] = "tcp://127.0.0.1:502" });

        Assert.Null(tcp.Certificate);
    }

    #endregion
}
