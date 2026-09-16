using System;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Protocols;

/// <summary>ModbusFactory 单元测试</summary>
public class ModbusFactoryTests
{
    #region 内置类型创建

    [Fact]
    [System.ComponentModel.DisplayName("tcp:// 创建 ModbusTcp 实例")]
    public void Create_Tcp_ReturnsModbusTcp()
    {
        var modbus = ModbusFactory.Create("tcp://192.168.1.100:502");
        Assert.NotNull(modbus);
        Assert.IsType<ModbusTcp>(modbus);

        var tcp = (ModbusTcp)modbus;
        Assert.Equal("tcp://192.168.1.100:502", tcp.Server);
    }

    [Fact]
    [System.ComponentModel.DisplayName("udp:// 创建 ModbusUdp 实例")]
    public void Create_Udp_ReturnsModbusUdp()
    {
        var modbus = ModbusFactory.Create("udp://192.168.1.100:502");
        Assert.NotNull(modbus);
        Assert.IsType<ModbusUdp>(modbus);

        var udp = (ModbusUdp)modbus;
        Assert.Equal("udp://192.168.1.100:502", udp.Server);
    }

    [Fact]
    [System.ComponentModel.DisplayName("rtuovertcp:// 创建 ModbusRtuOverTcp 实例")]
    public void Create_RtuOverTcp_ReturnsModbusRtuOverTcp()
    {
        var modbus = ModbusFactory.Create("rtuovertcp://192.168.1.100:502");
        Assert.NotNull(modbus);
        Assert.IsType<ModbusRtuOverTcp>(modbus);

        var rot = (ModbusRtuOverTcp)modbus;
        Assert.Equal("rtuovertcp://192.168.1.100:502", rot.Server);
    }

    [Fact]
    [System.ComponentModel.DisplayName("rtuoverudp:// 创建 ModbusRtuOverUdp 实例")]
    public void Create_RtuOverUdp_ReturnsModbusRtuOverUdp()
    {
        var modbus = ModbusFactory.Create("rtuoverudp://192.168.1.100:502");
        Assert.NotNull(modbus);
        Assert.IsType<ModbusRtuOverUdp>(modbus);

        var rou = (ModbusRtuOverUdp)modbus;
        Assert.Equal("rtuoverudp://192.168.1.100:502", rou.Server);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Scheme 大小写不敏感")]
    public void Create_SchemeCaseInsensitive()
    {
        var modbus1 = ModbusFactory.Create("TCP://192.168.1.1:502");
        Assert.IsType<ModbusTcp>(modbus1);

        var modbus2 = ModbusFactory.Create("Tcp://192.168.1.1:502");
        Assert.IsType<ModbusTcp>(modbus2);

        var modbus3 = ModbusFactory.Create("UDP://192.168.1.1:502");
        Assert.IsType<ModbusUdp>(modbus3);
    }

    [Fact]
    [System.ComponentModel.DisplayName("完整地址含路径，Server 属性完整保留")]
    public void Create_Tcp_FullAddress_ServerPreserved()
    {
        var config = "tcp://10.0.0.1:502";
        var modbus = ModbusFactory.Create(config) as ModbusTcp;
        Assert.NotNull(modbus);
        Assert.Equal(config, modbus.Server);
    }

    #endregion

    #region 边界与异常

    [Fact]
    [System.ComponentModel.DisplayName("null 配置返回 null")]
    public void Create_NullConfig_ReturnsNull()
    {
        var modbus = ModbusFactory.Create(null!);
        Assert.Null(modbus);
    }

    [Fact]
    [System.ComponentModel.DisplayName("空字符串配置返回 null")]
    public void Create_EmptyConfig_ReturnsNull()
    {
        var modbus = ModbusFactory.Create(String.Empty);
        Assert.Null(modbus);
    }

    [Fact]
    [System.ComponentModel.DisplayName("无 Scheme（纯地址字符串）返回 null")]
    public void Create_NoScheme_ReturnsNull()
    {
        var modbus = ModbusFactory.Create("192.168.1.100:502");
        Assert.Null(modbus);
    }

    [Fact]
    [System.ComponentModel.DisplayName("未注册的 Scheme 返回 null")]
    public void Create_UnknownScheme_ReturnsNull()
    {
        var modbus = ModbusFactory.Create("modbus://192.168.1.100:502");
        Assert.Null(modbus);
    }

    [Fact]
    [System.ComponentModel.DisplayName("只有 Scheme 无 :// 返回 null")]
    public void Create_NoDelimiter_ReturnsNull()
    {
        var modbus = ModbusFactory.Create("tcp:192.168.1.100");
        Assert.Null(modbus);
    }

    #endregion

    #region 自定义注册

    [Fact]
    [System.ComponentModel.DisplayName("Register 注册自定义 Scheme 后可创建")]
    public void Register_CustomScheme_CanCreate()
    {
        // 注册一个测试用 Scheme（利用已有的 ModbusTcp 模拟自定义类型）
        const String testScheme = "custom_test_xyz";
        ModbusFactory.Register(testScheme, config => new ModbusTcp { Server = config });

        var modbus = ModbusFactory.Create($"{testScheme}://192.168.1.100:502");
        Assert.NotNull(modbus);
        Assert.IsType<ModbusTcp>(modbus);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Register null scheme 抛 ArgumentNullException")]
    public void Register_NullScheme_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ModbusFactory.Register(null!, config => new ModbusTcp()));
    }

    [Fact]
    [System.ComponentModel.DisplayName("Register null creator 抛 ArgumentNullException")]
    public void Register_NullCreator_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ModbusFactory.Register("test_null_creator", null!));
    }

    [Fact]
    [System.ComponentModel.DisplayName("重复 Register 覆盖旧工厂")]
    public void Register_Override_LastWins()
    {
        const String overrideScheme = "override_scheme_xyz";
        // 先注册返回 ModbusTcp
        ModbusFactory.Register(overrideScheme, _ => new ModbusTcp());
        // 再注册返回 ModbusUdp，覆盖
        ModbusFactory.Register(overrideScheme, _ => new ModbusUdp());

        var modbus = ModbusFactory.Create($"{overrideScheme}://127.0.0.1:502");
        Assert.IsType<ModbusUdp>(modbus);
    }

    #endregion
}
