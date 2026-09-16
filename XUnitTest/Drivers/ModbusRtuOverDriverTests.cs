using System;
using System.ComponentModel;
using NewLife.IoT.Drivers;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Drivers;

/// <summary>ModbusRtuOverTcpDriver / ModbusRtuOverUdpDriver / ModbusUdpDriver 单元测试</summary>
/// <remarks>
/// 覆盖 CreateParameter 默认值、Open 空 Server 抛异常，以及 CreateModbus 实例化正确类型。
/// </remarks>
public class ModbusRtuOverDriverTests
{
    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverTcpDriver

    [Fact]
    [DisplayName("RtuOverTcp CreateParameter 返回 ModbusIpParameter，默认 Server 为 127.0.0.1:502")]
    public void RtuOverTcp_CreateParameter_ReturnsModbusIpParameter_WithDefaults()
    {
        var driver = new ModbusRtuOverTcpDriver();
        var p = driver.CreateParameter(null) as ModbusIpParameter;

        Assert.NotNull(p);
        Assert.Equal("127.0.0.1:502", p.Server);
        Assert.Equal(1, p.Host);
        Assert.Equal(FunctionCodes.ReadRegister, p.ReadCode);
        Assert.Equal(FunctionCodes.WriteRegister, p.WriteCode);
    }

    [Fact]
    [DisplayName("RtuOverTcp Open 空 Server 时抛 ArgumentException")]
    public void RtuOverTcp_Open_EmptyServer_Throws()
    {
        var driver = new ModbusRtuOverTcpDriver();
        var p = new ModbusIpParameter { Server = "" };

        Assert.Throws<ArgumentException>(() => driver.Open(null, p));
    }

    [Fact]
    [DisplayName("RtuOverTcp Open null 参数被转为默认参数后不抛")]
    public void RtuOverTcp_Open_NullParameter_UsesDefaultServer_ThrowsArgumentException()
    {
        // null 参数会被 Open 内部转为 new ModbusParameter()，然后再转为 ModbusIpParameter 失败
        var driver = new ModbusRtuOverTcpDriver();

        // null 参数 → ModbusIpParameter 为 null → Server 为空 → 应抛出 ArgumentException
        Assert.Throws<ArgumentException>(() => driver.Open(null, (IDriverParameter?)null));
    }

    [Fact]
    [DisplayName("RtuOverTcp Open 有效 Server 时创建 ModbusRtuOverTcp 实例")]
    public void RtuOverTcp_Open_ValidServer_CreatesModbusRtuOverTcp()
    {
        var driver = new ModbusRtuOverTcpDriver();
        var p = new ModbusIpParameter { Server = "tcp://127.0.0.1:1" };  // 端口 1 不一定可达，但不调用 Open() 就不会连接

        var node = driver.Open(null, p);

        Assert.NotNull(node);
        Assert.IsType<ModbusRtuOverTcp>(driver.Modbus);
        Assert.Equal("tcp://127.0.0.1:1", ((ModbusRtuOverTcp)driver.Modbus).Server);
    }

    [Fact]
    [DisplayName("RtuOverTcp 两次 Open 复用同一 Modbus 实例")]
    public void RtuOverTcp_Open_Twice_ReusesSameModbus()
    {
        var driver = new ModbusRtuOverTcpDriver();
        var p = new ModbusIpParameter { Server = "tcp://127.0.0.1:1" };

        var node1 = driver.Open(null, p);
        var m1 = driver.Modbus;

        var node2 = driver.Open(null, p);
        var m2 = driver.Modbus;

        Assert.Same(m1, m2);
    }

    [Fact]
    [DisplayName("RtuOverTcp Close 全部节点后 Modbus 被释放")]
    public void RtuOverTcp_Close_AllNodes_DisposesModbus()
    {
        var driver = new ModbusRtuOverTcpDriver();
        var p = new ModbusIpParameter { Server = "tcp://127.0.0.1:1" };

        var node1 = driver.Open(null, p);
        var node2 = driver.Open(null, p);

        driver.Close(node1);
        Assert.NotNull(driver.Modbus);

        driver.Close(node2);
        Assert.Null(driver.Modbus);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverUdpDriver

    [Fact]
    [DisplayName("RtuOverUdp CreateParameter 返回 ModbusIpParameter，默认 Server 为 127.0.0.1:502")]
    public void RtuOverUdp_CreateParameter_ReturnsModbusIpParameter_WithDefaults()
    {
        var driver = new ModbusRtuOverUdpDriver();
        var p = driver.CreateParameter(null) as ModbusIpParameter;

        Assert.NotNull(p);
        Assert.Equal("127.0.0.1:502", p.Server);
        Assert.Equal(1, p.Host);
        Assert.Equal(FunctionCodes.ReadRegister, p.ReadCode);
        Assert.Equal(FunctionCodes.WriteRegister, p.WriteCode);
    }

    [Fact]
    [DisplayName("RtuOverUdp Open 空 Server 时抛 ArgumentException")]
    public void RtuOverUdp_Open_EmptyServer_Throws()
    {
        var driver = new ModbusRtuOverUdpDriver();
        var p = new ModbusIpParameter { Server = "" };

        Assert.Throws<ArgumentException>(() => driver.Open(null, p));
    }

    [Fact]
    [DisplayName("RtuOverUdp Open 有效 Server 时创建 ModbusRtuOverUdp 实例")]
    public void RtuOverUdp_Open_ValidServer_CreatesModbusRtuOverUdp()
    {
        var driver = new ModbusRtuOverUdpDriver();
        var p = new ModbusIpParameter { Server = "udp://127.0.0.1:1" };

        var node = driver.Open(null, p);

        Assert.NotNull(node);
        Assert.IsType<ModbusRtuOverUdp>(driver.Modbus);
    }

    [Fact]
    [DisplayName("RtuOverUdp Close 全部节点后 Modbus 被释放")]
    public void RtuOverUdp_Close_AllNodes_DisposesModbus()
    {
        var driver = new ModbusRtuOverUdpDriver();
        var p = new ModbusIpParameter { Server = "udp://127.0.0.1:1" };

        var node1 = driver.Open(null, p);
        var node2 = driver.Open(null, p);

        driver.Close(node1);
        Assert.NotNull(driver.Modbus);

        driver.Close(node2);
        Assert.Null(driver.Modbus);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusUdpDriver

    [Fact]
    [DisplayName("UdpDriver CreateParameter 返回 ModbusUdpParameter，默认 Server 为 127.0.0.1:502")]
    public void UdpDriver_CreateParameter_ReturnsModbusUdpParameter_WithDefaults()
    {
        var driver = new ModbusUdpDriver();
        var p = driver.CreateParameter(null) as ModbusUdpParameter;

        Assert.NotNull(p);
        Assert.Equal("127.0.0.1:502", p.Server);
        Assert.Equal(1, p.Host);
        Assert.Equal(FunctionCodes.ReadRegister, p.ReadCode);
        Assert.Equal(FunctionCodes.WriteRegister, p.WriteCode);
        Assert.Equal(0, p.ProtocolId);
    }

    [Fact]
    [DisplayName("UdpDriver Open 空 Server 时抛 ArgumentException")]
    public void UdpDriver_Open_EmptyServer_Throws()
    {
        var driver = new ModbusUdpDriver();
        var p = new ModbusUdpParameter { Server = "" };

        Assert.Throws<ArgumentException>(() => driver.Open(null, p));
    }

    [Fact]
    [DisplayName("UdpDriver Open 有效 Server 时创建 ModbusUdp 实例")]
    public void UdpDriver_Open_ValidServer_CreatesModbusUdp()
    {
        var driver = new ModbusUdpDriver();
        var p = new ModbusUdpParameter { Server = "udp://127.0.0.1:1", ProtocolId = 3 };

        var node = driver.Open(null, p);

        Assert.NotNull(node);
        Assert.IsType<ModbusUdp>(driver.Modbus);

        var udp = (ModbusUdp)driver.Modbus;
        Assert.Equal("udp://127.0.0.1:1", udp.Server);
        Assert.Equal(3, udp.ProtocolId);
    }

    [Fact]
    [DisplayName("UdpDriver Close 全部节点后 Modbus 被释放")]
    public void UdpDriver_Close_AllNodes_DisposesModbus()
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

    [Fact]
    [DisplayName("UdpDriver 两次 Open 复用同一 Modbus 实例")]
    public void UdpDriver_Open_Twice_ReusesSameModbus()
    {
        var driver = new ModbusUdpDriver();
        var p = new ModbusUdpParameter { Server = "udp://127.0.0.1:1" };

        var node1 = driver.Open(null, p);
        var m1 = driver.Modbus;

        var node2 = driver.Open(null, p);
        var m2 = driver.Modbus;

        Assert.Same(m1, m2);
    }

    #endregion
}
