using System;
using System.ComponentModel;
using NewLife.IoT.Drivers;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Drivers;

/// <summary>ModbusUdpDriver 单元测试</summary>
/// <remarks>
/// 覆盖 CreateParameter、CreateModbus、Open/Close 生命周期。
/// </remarks>
public class ModbusUdpDriverTests
{
    // ────────────────────────────────────────────────────────────────────────
    #region CreateParameter

    [Fact]
    [DisplayName("CreateParameter 返回 ModbusUdpParameter，默认值正确")]
    public void CreateParameter_ReturnsModbusUdpParameter_WithDefaults()
    {
        var driver = new ModbusUdpDriver();
        var p = driver.CreateParameter(null) as ModbusUdpParameter;

        Assert.NotNull(p);
        Assert.Equal("127.0.0.1:502", p.Server);
        Assert.Equal(1, p.Host);
        Assert.Equal(FunctionCodes.ReadRegister, p.ReadCode);
        Assert.Equal(FunctionCodes.WriteRegister, p.WriteCode);
        Assert.Equal(3000, p.Timeout);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Open / Close

    [Fact]
    [DisplayName("Open 有效 Server 时创建 ModbusUdp 实例")]
    public void Open_ValidServer_CreatesModbusUdp()
    {
        var driver = new ModbusUdpDriver();
        var p = new ModbusUdpParameter
        {
            Server = "udp://127.0.0.1:1",  // 端口 1 不可达，但不调用 Open() 就不会连接
            Host = 2,
            ReadCode = FunctionCodes.ReadInput,
            WriteCode = FunctionCodes.WriteRegister,
            ProtocolId = 0,
        };

        var node = driver.Open(null, p);

        Assert.NotNull(node);
        var modbus = Assert.IsType<ModbusUdp>(driver.Modbus);
        Assert.Equal("udp://127.0.0.1:1", modbus.Server);
        Assert.Equal(0, modbus.ProtocolId);
    }

    [Fact]
    [DisplayName("Open 空 Server 时抛 ArgumentException")]
    public void Open_EmptyServer_Throws()
    {
        var driver = new ModbusUdpDriver();
        var p = new ModbusUdpParameter { Server = "" };

        Assert.Throws<ArgumentException>(() => driver.Open(null, p));
    }

    [Fact]
    [DisplayName("Open 两次复用同一 Modbus 实例")]
    public void Open_Twice_ReusesSameModbus()
    {
        var driver = new ModbusUdpDriver();
        var p = new ModbusUdpParameter { Server = "udp://127.0.0.1:1" };

        var node1 = driver.Open(null, p);
        var m1 = driver.Modbus;

        var node2 = driver.Open(null, p);
        var m2 = driver.Modbus;

        Assert.Same(m1, m2);
        Assert.NotNull(driver.Modbus);
    }

    [Fact]
    [DisplayName("Close 全部节点后 Modbus 被释放")]
    public void Close_AllNodes_DisposesModbus()
    {
        var driver = new ModbusUdpDriver();
        var p = new ModbusUdpParameter { Server = "udp://127.0.0.1:1" };

        var node1 = driver.Open(null, p);
        var node2 = driver.Open(null, p);

        driver.Close(node1);
        Assert.NotNull(driver.Modbus);

        driver.Close(node2);
        Assert.Null(driver.Modbus);
    }

    #endregion
}
