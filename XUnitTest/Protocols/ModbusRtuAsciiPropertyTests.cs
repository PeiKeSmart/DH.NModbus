using System;
using System.IO.Ports;
using System.ComponentModel;
using NewLife.IoT.Protocols;
using NewLife.Serial.Protocols;
using Xunit;
using System.Collections.Generic;

namespace XUnitTest.Protocols;

/// <summary>ModbusRtu / ModbusAscii 单元测试（不依赖真实串口）</summary>
/// <remarks>
/// 覆盖默认属性值、Init() 配置解析、Dispose() 无串口时安全释放。
/// Open() 与 SendCommand() 需真实串口，不在此测试。
/// </remarks>
public class ModbusRtuAsciiPropertyTests
{
    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtu 默认属性

    [Fact]
    [DisplayName("ModbusRtu 默认波特率为 9600")]
    public void ModbusRtu_DefaultBaudrate_Is9600()
    {
        var rtu = new ModbusRtu();
        Assert.Equal(9600, rtu.Baudrate);
    }

    [Fact]
    [DisplayName("ModbusRtu 默认数据位为 8")]
    public void ModbusRtu_DefaultDataBits_Is8()
    {
        var rtu = new ModbusRtu();
        Assert.Equal(8, rtu.DataBits);
    }

    [Fact]
    [DisplayName("ModbusRtu 默认校验位为 None")]
    public void ModbusRtu_DefaultParity_IsNone()
    {
        var rtu = new ModbusRtu();
        Assert.Equal(Parity.None, rtu.Parity);
    }

    [Fact]
    [DisplayName("ModbusRtu 默认停止位为 One")]
    public void ModbusRtu_DefaultStopBits_IsOne()
    {
        var rtu = new ModbusRtu();
        Assert.Equal(StopBits.One, rtu.StopBits);
    }

    [Fact]
    [DisplayName("ModbusRtu 默认字节超时为 10ms")]
    public void ModbusRtu_DefaultByteTimeout_Is10()
    {
        var rtu = new ModbusRtu();
        Assert.Equal(10, rtu.ByteTimeout);
    }

    [Fact]
    [DisplayName("ModbusRtu 默认 PortName 为 null")]
    public void ModbusRtu_DefaultPortName_IsNull()
    {
        var rtu = new ModbusRtu();
        Assert.Null(rtu.PortName);
    }

    [Fact]
    [DisplayName("ModbusRtu 继承 Modbus 基类")]
    public void ModbusRtu_InheritsModbus()
    {
        var rtu = new ModbusRtu();
        Assert.IsAssignableFrom<Modbus>(rtu);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtu.Init

    [Fact]
    [DisplayName("ModbusRtu.Init 解析 PortName")]
    public void ModbusRtu_Init_ParsesPortName()
    {
        var rtu = new ModbusRtu();
        rtu.Init(new Dictionary<String, Object> { ["PortName"] = "COM3" });
        Assert.Equal("COM3", rtu.PortName);
    }

    [Fact]
    [DisplayName("ModbusRtu.Init 无 PortName 时回退至 Address")]
    public void ModbusRtu_Init_FallsBackToAddress()
    {
        var rtu = new ModbusRtu();
        rtu.Init(new Dictionary<String, Object> { ["Address"] = "COM5" });
        Assert.Equal("COM5", rtu.PortName);
    }

    [Fact]
    [DisplayName("ModbusRtu.Init PortName 优先于 Address")]
    public void ModbusRtu_Init_PortNameTakesPriorityOverAddress()
    {
        var rtu = new ModbusRtu();
        rtu.Init(new Dictionary<String, Object> { ["PortName"] = "COM1", ["Address"] = "COM2" });
        Assert.Equal("COM1", rtu.PortName);
    }

    [Fact]
    [DisplayName("ModbusRtu.Init 解析 Baudrate")]
    public void ModbusRtu_Init_ParsesBaudrate()
    {
        var rtu = new ModbusRtu();
        rtu.Init(new Dictionary<String, Object> { ["Baudrate"] = "115200", ["PortName"] = "COM1" });
        Assert.Equal(115200, rtu.Baudrate);
    }

    [Fact]
    [DisplayName("ModbusRtu.Init 空字典不抛异常")]
    public void ModbusRtu_Init_EmptyDict_NoException()
    {
        var rtu = new ModbusRtu();
        var ex = Record.Exception(() => rtu.Init(new Dictionary<String, Object>()));
        Assert.Null(ex);
    }

    [Fact]
    [DisplayName("ModbusRtu.Init 仅有 Baudrate，PortName 保持 null")]
    public void ModbusRtu_Init_OnlyBaudrate_PortNameStillNull()
    {
        var rtu = new ModbusRtu();
        rtu.Init(new Dictionary<String, Object> { ["Baudrate"] = "9600" });
        Assert.Null(rtu.PortName);
        Assert.Equal(9600, rtu.Baudrate);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtu Dispose（无串口时安全释放）

    [Fact]
    [DisplayName("ModbusRtu Dispose 未 Open 时不抛异常")]
    public void ModbusRtu_Dispose_WithoutOpen_NoException()
    {
        var rtu = new ModbusRtu { PortName = "COM1" };
        var ex = Record.Exception(() => rtu.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    [DisplayName("ModbusRtu Dispose 多次调用不抛异常")]
    public void ModbusRtu_Dispose_Twice_NoException()
    {
        var rtu = new ModbusRtu { PortName = "COM1" };
        rtu.Dispose();
        var ex = Record.Exception(() => rtu.Dispose());
        Assert.Null(ex);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusAscii 默认属性

    [Fact]
    [DisplayName("ModbusAscii 默认波特率为 9600")]
    public void ModbusAscii_DefaultBaudrate_Is9600()
    {
        var ascii = new ModbusAscii();
        Assert.Equal(9600, ascii.Baudrate);
    }

    [Fact]
    [DisplayName("ModbusAscii 默认字节超时为 10ms")]
    public void ModbusAscii_DefaultByteTimeout_Is10()
    {
        var ascii = new ModbusAscii();
        Assert.Equal(10, ascii.ByteTimeout);
    }

    [Fact]
    [DisplayName("ModbusAscii 继承 Modbus 基类")]
    public void ModbusAscii_InheritsModbus()
    {
        var ascii = new ModbusAscii();
        Assert.IsAssignableFrom<Modbus>(ascii);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusAscii.Init

    [Fact]
    [DisplayName("ModbusAscii.Init 解析 PortName")]
    public void ModbusAscii_Init_ParsesPortName()
    {
        var ascii = new ModbusAscii();
        ascii.Init(new Dictionary<String, Object> { ["PortName"] = "COM4" });
        Assert.Equal("COM4", ascii.PortName);
    }

    [Fact]
    [DisplayName("ModbusAscii.Init 无 PortName 时回退至 Address")]
    public void ModbusAscii_Init_FallsBackToAddress()
    {
        var ascii = new ModbusAscii();
        ascii.Init(new Dictionary<String, Object> { ["Address"] = "COM6" });
        Assert.Equal("COM6", ascii.PortName);
    }

    [Fact]
    [DisplayName("ModbusAscii.Init PortName 优先于 Address")]
    public void ModbusAscii_Init_PortNameTakesPriorityOverAddress()
    {
        var ascii = new ModbusAscii();
        ascii.Init(new Dictionary<String, Object> { ["PortName"] = "COM1", ["Address"] = "COM3" });
        Assert.Equal("COM1", ascii.PortName);
    }

    [Fact]
    [DisplayName("ModbusAscii.Init 解析 Baudrate")]
    public void ModbusAscii_Init_ParsesBaudrate()
    {
        var ascii = new ModbusAscii();
        ascii.Init(new Dictionary<String, Object> { ["Baudrate"] = "19200", ["PortName"] = "COM2" });
        Assert.Equal(19200, ascii.Baudrate);
    }

    [Fact]
    [DisplayName("ModbusAscii.Init 空字典不抛异常")]
    public void ModbusAscii_Init_EmptyDict_NoException()
    {
        var ascii = new ModbusAscii();
        var ex = Record.Exception(() => ascii.Init(new Dictionary<String, Object>()));
        Assert.Null(ex);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusAscii Dispose（无串口时安全释放）

    [Fact]
    [DisplayName("ModbusAscii Dispose 未 Open 时不抛异常")]
    public void ModbusAscii_Dispose_WithoutOpen_NoException()
    {
        var ascii = new ModbusAscii { PortName = "COM1" };
        var ex = Record.Exception(() => ascii.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    [DisplayName("ModbusAscii Dispose 多次调用不抛异常")]
    public void ModbusAscii_Dispose_Twice_NoException()
    {
        var ascii = new ModbusAscii { PortName = "COM1" };
        ascii.Dispose();
        var ex = Record.Exception(() => ascii.Dispose());
        Assert.Null(ex);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtu 属性设置

    [Fact]
    [DisplayName("ModbusRtu 可以自由设置 PortName、Baudrate 等属性")]
    public void ModbusRtu_Properties_SetGet()
    {
        var rtu = new ModbusRtu
        {
            PortName = "COM9",
            Baudrate = 38400,
            DataBits = 7,
            Parity = Parity.Even,
            StopBits = StopBits.Two,
            ByteTimeout = 50,
        };

        Assert.Equal("COM9", rtu.PortName);
        Assert.Equal(38400, rtu.Baudrate);
        Assert.Equal(7, rtu.DataBits);
        Assert.Equal(Parity.Even, rtu.Parity);
        Assert.Equal(StopBits.Two, rtu.StopBits);
        Assert.Equal(50, rtu.ByteTimeout);
    }

    #endregion
}
