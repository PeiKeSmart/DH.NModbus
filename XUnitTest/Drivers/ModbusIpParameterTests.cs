using System;
using System.ComponentModel;
using NewLife.IoT.Drivers;
using Xunit;

namespace XUnitTest.Drivers;

/// <summary>ModbusIpParameter 单元测试</summary>
public class ModbusIpParameterTests
{
    #region 默认值

    [Fact]
    [DisplayName("ModbusIpParameter 默认 Server 为 null")]
    public void Default_Server_IsNull()
    {
        var p = new ModbusIpParameter();
        Assert.Null(p.Server);
    }

    [Fact]
    [DisplayName("ModbusIpParameter 默认 Host 为 0")]
    public void Default_Host_IsZero()
    {
        var p = new ModbusIpParameter();
        Assert.Equal(0, p.Host);
    }

    #endregion

    #region GetKey

    [Fact]
    [DisplayName("GetKey 返回 Server 字符串")]
    public void GetKey_ReturnsServer()
    {
        var p = new ModbusIpParameter { Server = "tcp://192.168.1.100:502" };
        Assert.Equal("tcp://192.168.1.100:502", p.GetKey());
    }

    [Fact]
    [DisplayName("GetKey Server 为 null 时返回 null")]
    public void GetKey_NullServer_ReturnsNull()
    {
        var p = new ModbusIpParameter { Server = null! };
        Assert.Null(p.GetKey());
    }

    [Fact]
    [DisplayName("GetKey Server 为空字符串时返回空字符串")]
    public void GetKey_EmptyServer_ReturnsEmpty()
    {
        var p = new ModbusIpParameter { Server = "" };
        Assert.Equal("", p.GetKey());
    }

    [Fact]
    [DisplayName("GetKey 两个相同 Server 的参数 GetKey 相同")]
    public void GetKey_SameServer_SameKey()
    {
        var p1 = new ModbusIpParameter { Server = "tcp://10.0.0.1:502" };
        var p2 = new ModbusIpParameter { Server = "tcp://10.0.0.1:502" };
        Assert.Equal(p1.GetKey(), p2.GetKey());
    }

    [Fact]
    [DisplayName("GetKey 不同 Server 产生不同 Key")]
    public void GetKey_DifferentServer_DifferentKey()
    {
        var p1 = new ModbusIpParameter { Server = "tcp://10.0.0.1:502" };
        var p2 = new ModbusIpParameter { Server = "tcp://10.0.0.2:502" };
        Assert.NotEqual(p1.GetKey(), p2.GetKey());
    }

    #endregion

    #region IDriverParameterKey 接口

    [Fact]
    [DisplayName("ModbusIpParameter 实现 IDriverParameterKey 接口")]
    public void Implements_IDriverParameterKey()
    {
        var p = new ModbusIpParameter();
        Assert.IsAssignableFrom<IDriverParameterKey>(p);
    }

    #endregion
}
