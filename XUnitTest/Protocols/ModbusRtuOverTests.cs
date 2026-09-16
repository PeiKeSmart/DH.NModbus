using System;
using System.ComponentModel;
using NewLife;
using NewLife.Data;
using NewLife.IoT;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Protocols;

/// <summary>ModbusRtuOverTcp / ModbusRtuOverUdp 单元测试</summary>
/// <remarks>
/// 覆盖 CreateMessage 返回类型、ReadMessage 返回 ModbusRtuMessage。
/// </remarks>
public class ModbusRtuOverTests
{
    // ────────────────────────────────────────────────────────────────────────
    #region 辅助：可测试子类

    private sealed class TestableRtuOverTcp : ModbusRtuOverTcp
    {
        /// <summary>公开暴露 CreateMessage</summary>
        public new ModbusMessage PublicCreateMessage() => base.CreateMessage();

        /// <summary>公开暴露 ReadMessage</summary>
        public ModbusMessage? CallReadMessage(ModbusMessage request, IPacket data, out Boolean match)
            => ReadMessage(request, data, out match);
    }

    private sealed class TestableRtuOverUdp : ModbusRtuOverUdp
    {
        /// <summary>公开暴露 CreateMessage</summary>
        public new ModbusMessage PublicCreateMessage() => base.CreateMessage();

        /// <summary>公开暴露 ReadMessage</summary>
        public ModbusMessage? CallReadMessage(ModbusMessage request, IPacket data, out Boolean match)
            => ReadMessage(request, data, out match);
    }

    /// <summary>构造一个合法的 RTU 响应包（含 CRC）</summary>
    private static IPacket MakeRtuResponse(Byte host = 0x01, Byte code = 0x03, Byte[]? data = null)
    {
        data ??= [0x02, 0x00, 0x64];
        var buf = new Byte[2 + 1 + data.Length + 2]; // host + code + data + CRC
        buf[0] = host;
        buf[1] = code;
        Array.Copy(data, 0, buf, 2, data.Length);
        var crc = ModbusHelper.Crc(buf, 0, 2 + data.Length);
        buf[^2] = (Byte)(crc & 0xFF);
        buf[^1] = (Byte)(crc >> 8);
        return new ArrayPacket(buf);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverTcp.CreateMessage

    [Fact]
    [DisplayName("ModbusRtuOverTcp.CreateMessage 返回 ModbusRtuMessage")]
    public void RtuOverTcp_CreateMessage_ReturnsModbusRtuMessage()
    {
        var rt = new TestableRtuOverTcp();
        var msg = rt.PublicCreateMessage();
        Assert.IsType<ModbusRtuMessage>(msg);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverUdp.CreateMessage

    [Fact]
    [DisplayName("ModbusRtuOverUdp.CreateMessage 返回 ModbusRtuMessage")]
    public void RtuOverUdp_CreateMessage_ReturnsModbusRtuMessage()
    {
        var ru = new TestableRtuOverUdp();
        var msg = ru.PublicCreateMessage();
        Assert.IsType<ModbusRtuMessage>(msg);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverTcp.ReadMessage

    [Fact]
    [DisplayName("ModbusRtuOverTcp.ReadMessage 有效数据返回 ModbusRtuMessage")]
    public void RtuOverTcp_ReadMessage_ValidData_ReturnsMessage()
    {
        var rt = new TestableRtuOverTcp();
        var request = new ModbusRtuMessage { Host = 1, Code = FunctionCodes.ReadRegister };
        var data = MakeRtuResponse(0x01, 0x03);

        var rs = rt.CallReadMessage(request, data, out var match);

        Assert.NotNull(rs);
        Assert.True(match);
        Assert.IsType<ModbusRtuMessage>(rs);
        Assert.Equal(1, rs.Host);
        Assert.Equal(FunctionCodes.ReadRegister, rs.Code);
    }

    [Fact]
    [DisplayName("ModbusRtuOverTcp.ReadMessage 空数据抛异常")]
    public void RtuOverTcp_ReadMessage_EmptyData_Throws()
    {
        var rt = new TestableRtuOverTcp();
        var request = new ModbusRtuMessage();
        var data = new ArrayPacket([]); // 空数据

        Assert.ThrowsAny<Exception>(() => rt.CallReadMessage(request, data, out _));
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverUdp.ReadMessage

    [Fact]
    [DisplayName("ModbusRtuOverUdp.ReadMessage 有效数据返回 ModbusRtuMessage")]
    public void RtuOverUdp_ReadMessage_ValidData_ReturnsMessage()
    {
        var ru = new TestableRtuOverUdp();
        var request = new ModbusRtuMessage { Host = 1, Code = FunctionCodes.ReadRegister };
        var data = MakeRtuResponse(0x01, 0x03);

        var rs = ru.CallReadMessage(request, data, out var match);

        Assert.NotNull(rs);
        Assert.True(match);
        Assert.IsType<ModbusRtuMessage>(rs);
    }

    [Fact]
    [DisplayName("ModbusRtuOverUdp.ReadMessage 空数据抛异常")]
    public void RtuOverUdp_ReadMessage_EmptyData_Throws()
    {
        var ru = new TestableRtuOverUdp();
        var request = new ModbusRtuMessage();
        var data = new ArrayPacket([]); // 空数据

        Assert.ThrowsAny<Exception>(() => ru.CallReadMessage(request, data, out _));
    }

    #endregion
}
