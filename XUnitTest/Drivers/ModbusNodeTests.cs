using System;
using System.ComponentModel;
using NewLife.IoT.Drivers;
using NewLife.IoT.Protocols;
using Moq;
using Xunit;

namespace XUnitTest.Drivers;

/// <summary>ModbusNode 单元测试</summary>
public class ModbusNodeTests
{
    [Fact]
    [DisplayName("IsConnected Driver.Modbus != null 时返回 true")]
    public void IsConnected_ModbusNotNull_ReturnsTrue()
    {
        var mockDriver = new Mock<ModbusDriver> { CallBase = true };
        mockDriver.Object.Modbus = new Mock<Modbus> { CallBase = true }.Object;

        var node = new ModbusNode
        {
            Host = 1,
            Driver = mockDriver.Object,
        };

        Assert.True(node.IsConnected);
    }

    [Fact]
    [DisplayName("IsConnected Driver.Modbus == null 时返回 false")]
    public void IsConnected_ModbusNull_ReturnsFalse()
    {
        var mockDriver = new Mock<ModbusDriver> { CallBase = true };
        // Modbus 默认 null

        var node = new ModbusNode
        {
            Host = 1,
            Driver = mockDriver.Object,
        };

        Assert.False(node.IsConnected);
    }

    [Fact]
    [DisplayName("IsConnected Driver 为 null 时返回 false（?. 保护）")]
    public void IsConnected_DriverNull_ReturnsFalse()
    {
        var node = new ModbusNode
        {
            Host = 1,
            Driver = null!,
        };

        // Driver 为 null 时，(Driver as ModbusDriver)?.Modbus 为 null，返回 false
        Assert.False(node.IsConnected);
    }

    [Fact]
    [DisplayName("ModbusNode 默认属性值正确")]
    public void ModbusNode_DefaultValues()
    {
        var node = new ModbusNode();

        Assert.Equal((Byte)0, node.Host);
        Assert.Equal((FunctionCodes)0, node.ReadCode);
        Assert.Equal((FunctionCodes)0, node.WriteCode);
        Assert.Null(node.Driver);
        Assert.Null(node.Device);
        Assert.Null(node.Parameter);
    }
}
