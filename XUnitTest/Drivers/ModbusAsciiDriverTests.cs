using System;
using NewLife.IoT.Drivers;
using NewLife.IoT.Protocols;
using NewLife.Serial.Drivers;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest.Drivers;

/// <summary>ModbusAsciiDriver 单元测试</summary>
public class ModbusAsciiDriverTests
{
    #region Open

    [Fact]
    [System.ComponentModel.DisplayName("Open 创建 ModbusAscii 实例，属性与参数一致")]
    public void Open_CreatesModbusAscii_WithCorrectProperties()
    {
        var driver = new ModbusAsciiDriver();
        var p = new ModbusRtuParameter
        {
            Host = 2,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            PortName = "COM2",
            Baudrate = 19200,
        };

        var node = driver.Open(null, p);

        var node2 = node as ModbusNode;
        Assert.NotNull(node2);
        Assert.Equal(p.Host, node2.Host);
        Assert.Equal(p.ReadCode, node2.ReadCode);
        Assert.Equal(p.WriteCode, node2.WriteCode);

        // Open 时未传 device，Device 应为 null
        Assert.Null(node2.Device);

        var modbus = driver.Modbus as ModbusAscii;
        Assert.NotNull(modbus);
        Assert.Equal("COM2", modbus.PortName);
        Assert.Equal(19200, modbus.Baudrate);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Open 空 PortName 时抛 ArgumentException")]
    public void Open_ThrowsArgumentException_WhenPortNameEmpty()
    {
        var driver = new ModbusAsciiDriver();
        var p = new ModbusRtuParameter { PortName = "" };

        Assert.Throws<ArgumentException>(() => driver.Open(null, p));
    }

    [Fact]
    [System.ComponentModel.DisplayName("Open Baudrate 为 0 时自动设为 9600")]
    public void Open_DefaultBaudrate9600_WhenBaudrateIsZero()
    {
        var driver = new ModbusAsciiDriver();
        var p = new ModbusRtuParameter { PortName = "COM5", Baudrate = 0 };

        var node = driver.Open(null, p);

        var modbus = driver.Modbus as ModbusAscii;
        Assert.NotNull(modbus);
        Assert.Equal(9600, modbus.Baudrate);
        // 参数对象的 Baudrate 也被修正
        Assert.Equal(9600, p.Baudrate);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Open 两次返回同一 Modbus 实例（连接复用）")]
    public void Open_SecondOpen_ReusesSameModbus()
    {
        var driver = new ModbusAsciiDriver();
        var p = new ModbusRtuParameter { PortName = "COM3" };

        var node1 = driver.Open(null, p);
        var m1 = driver.Modbus;

        var node2 = driver.Open(null, p);
        var m2 = driver.Modbus;

        Assert.NotNull(m1);
        Assert.Same(m1, m2);
    }

    #endregion

    #region CreateParameter

    [Fact]
    [System.ComponentModel.DisplayName("CreateParameter 返回 ModbusRtuParameter，默认端口 COM1")]
    public void CreateParameter_ReturnsModbusRtuParameter_WithDefaults()
    {
        var driver = new ModbusAsciiDriver();
        var p = driver.CreateParameter(null) as ModbusRtuParameter;

        Assert.NotNull(p);
        Assert.Equal("COM1", p.PortName);
        Assert.Equal(9600, p.Baudrate);
        Assert.Equal(1, p.Host);
        Assert.Equal(FunctionCodes.ReadRegister, p.ReadCode);
        Assert.Equal(FunctionCodes.WriteRegister, p.WriteCode);
    }

    #endregion

    #region Close

    [Fact]
    [System.ComponentModel.DisplayName("Close 一次后 Modbus 仍存在（另一节点持有）")]
    public void Close_FirstClose_ModbusStillExists()
    {
        var driver = new ModbusAsciiDriver();
        var p = new ModbusRtuParameter { PortName = "COM1" };

        var node1 = driver.Open(null, p);
        var node2 = driver.Open(null, p);
        Assert.NotNull(driver.Modbus);

        driver.Close(node1);
        Assert.NotNull(driver.Modbus);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Close 两次后 Modbus 被释放为 null")]
    public void Close_AllNodesClosed_ModbusDisposed()
    {
        var driver = new ModbusAsciiDriver();
        var p = new ModbusRtuParameter { PortName = "COM1" };

        var node1 = driver.Open(null, p);
        var node2 = driver.Open(null, p);
        Assert.NotNull(driver.Modbus);

        driver.Close(node1);
        driver.Close(node2);

        Assert.Null(driver.Modbus);
    }

    #endregion
}
