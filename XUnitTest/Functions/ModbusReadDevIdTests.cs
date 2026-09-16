using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC43 ReadDevId 功能码测试</summary>
public class ModbusReadDevIdTests
{
    #region 主机侧单元测试（Moq）

    [Fact]
    [System.ComponentModel.DisplayName("FC43 响应为null返回null")]
    public async Task ReadDevId_NullResponse_ReturnsNull()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDevId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var rs = await mb.Object.ReadDevIdAsync(1);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC43 响应数据不足返回null")]
    public async Task ReadDevId_ShortResponse_ReturnsNull()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDevId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"0E-01-01".ToHex());

        var rs = await mb.Object.ReadDevIdAsync(1);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC43 正常响应解析3个标准对象")]
    public async Task ReadDevId_ValidResponse_ThreeObjects()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 构建响应：MEI=0x0E, Code=0x01, Level=0x01, MoreFollows=0, NextObj=0, Count=3
        // Obj0: Id=0, Len=7, "NewLife"
        // Obj1: Id=1, Len=6, "Modbus"
        // Obj2: Id=2, Len=3, "1.0"
        var response = new System.Collections.Generic.List<Byte>
        {
            0x0E, 0x01, 0x01, 0x00, 0x00, 0x03,
            0x00, 0x07, (Byte)'N', (Byte)'e', (Byte)'w', (Byte)'L', (Byte)'i', (Byte)'f', (Byte)'e',
            0x01, 0x06, (Byte)'M', (Byte)'o', (Byte)'d', (Byte)'b', (Byte)'u', (Byte)'s',
            0x02, 0x03, (Byte)'1', (Byte)'.', (Byte)'0',
        };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDevId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)response.ToArray());

        var rs = await mb.Object.ReadDevIdAsync(1);
        Assert.NotNull(rs);
        Assert.Equal(3, rs.Count);
        Assert.Equal("NewLife", rs[0x00]);
        Assert.Equal("Modbus", rs[0x01]);
        Assert.Equal("1.0", rs[0x02]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC43 响应只有1个对象")]
    public async Task ReadDevId_ValidResponse_OneObject()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var response = new System.Collections.Generic.List<Byte>
        {
            0x0E, 0x01, 0x01, 0x00, 0x00, 0x01,
            0x00, 0x04, (Byte)'T', (Byte)'e', (Byte)'s', (Byte)'t',
        };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDevId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)response.ToArray());

        var rs = await mb.Object.ReadDevIdAsync(1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal("Test", rs[0x00]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC43 FunctionCodes枚举值为0x2B")]
    public void ReadDevId_FunctionCodeValue()
    {
        Assert.Equal(0x2B, (Byte)FunctionCodes.ReadDevId);
    }

    #endregion

    #region ModbusDeviceInfo 模型测试

    [Fact]
    [System.ComponentModel.DisplayName("ModbusDeviceInfo 默认值")]
    public void DeviceInfo_DefaultValues()
    {
        var info = new ModbusDeviceInfo();
        Assert.Equal("NewLife", info.VendorName);
        Assert.Equal("Modbus", info.ProductCode);
        Assert.Equal("1.0", info.Revision);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusDeviceInfo GetObjects 返回3个对象")]
    public void DeviceInfo_GetObjects_ThreeItems()
    {
        var info = new ModbusDeviceInfo { VendorName = "VendorA", ProductCode = "ProductB", Revision = "2.0" };
        var objects = info.GetObjects();
        Assert.Equal(3, objects.Count);
        Assert.Equal((Byte)0x00, objects[0].Id);
        Assert.Equal("VendorA", objects[0].Value);
        Assert.Equal((Byte)0x01, objects[1].Id);
        Assert.Equal("ProductB", objects[1].Value);
        Assert.Equal((Byte)0x02, objects[2].Id);
        Assert.Equal("2.0", objects[2].Value);
    }

    #endregion

    #region Slave 集成测试

    private (ModbusSlave Slave, Int32 Port) CreateSlave(String? vendor = null, String? product = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave { Port = port };
        if (vendor != null) slave.DeviceInfo.VendorName = vendor;
        if (product != null) slave.DeviceInfo.ProductCode = product;
        slave.Start();
        Thread.Sleep(200);
        return (slave, port);
    }

    private static async Task<ModbusTcp> CreateClientAsync(Int32 port)
    {
        var client = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
        await client.OpenAsync();
        return client;
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC43 集成测试：读取默认设备信息")]
    public async Task ReadDevId_Integration_DefaultInfo()
    {
        var (slave, port) = CreateSlave();
        try
        {
            using var client = await CreateClientAsync(port);
            var rs = await client.ReadDevIdAsync(1);
            Assert.NotNull(rs);
            Assert.True(rs.Count >= 1);
            Assert.Equal("NewLife", rs[0x00]);
        }
        finally
        {
            slave.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC43 集成测试：读取自定义设备信息")]
    public async Task ReadDevId_Integration_CustomInfo()
    {
        var (slave, port) = CreateSlave("MyVendor", "MyProduct");
        try
        {
            using var client = await CreateClientAsync(port);
            var rs = await client.ReadDevIdAsync(1);
            Assert.NotNull(rs);
            Assert.Equal("MyVendor", rs[0x00]);
            Assert.Equal("MyProduct", rs[0x01]);
        }
        finally
        {
            slave.Dispose();
        }
    }

    #endregion
}
