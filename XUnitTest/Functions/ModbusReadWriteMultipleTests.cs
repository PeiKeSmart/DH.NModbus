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

/// <summary>FC23 ReadWriteMultipleRegisters 功能码测试</summary>
public class ModbusReadWriteMultipleTests
{
    #region 主机侧单元测试（Moq）

    [Fact]
    [System.ComponentModel.DisplayName("FC23 响应为null返回空数组")]
    public async Task ReadWriteRegisters_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadWriteMultipleRegisters, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var rs = await mb.Object.ReadWriteRegistersAsync(1, 0, 2, 10, [0x1234, 0x5678]);
        Assert.Empty(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC23 正常响应返回读取数据")]
    public async Task ReadWriteRegisters_ValidResponse_ReturnsData()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：ByteCount(4) + 两个寄存器值(0x00C8=200, 0x00C9=201)
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadWriteMultipleRegisters, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-00-C8-00-C9".ToHex());

        var rs = await mb.Object.ReadWriteRegistersAsync(1, 0, 2, 10, [0x1234, 0x5678]);
        Assert.Equal(2, rs.Length);
        Assert.Equal((UInt16)200, rs[0]);
        Assert.Equal((UInt16)201, rs[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC23 ValidResponse=false 跳过长度校验")]
    public async Task ReadWriteRegisters_ValidResponseFalse_SkipsCheck()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 返回不完整数据，但关闭校验
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadWriteMultipleRegisters, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-00-C8".ToHex());

        var modbus = mb.Object;
        modbus.ValidResponse = false;
        var rs = await modbus.ReadWriteRegistersAsync(1, 0, 1, 10, [0x1234]);
        Assert.Single(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC23 响应数据不足返回空数组")]
    public async Task ReadWriteRegisters_InsufficientData_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // ByteCount=4 但实际只有2字节数据
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadWriteMultipleRegisters, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-00-C8".ToHex());

        var rs = await mb.Object.ReadWriteRegistersAsync(1, 0, 2, 10, [0x1234, 0x5678]);
        Assert.Empty(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC23 FunctionCodes枚举值为0x17")]
    public void ReadWriteMultipleRegisters_FunctionCodeValue()
    {
        Assert.Equal(0x17, (Byte)FunctionCodes.ReadWriteMultipleRegisters);
    }

    #endregion

    #region Slave 集成测试

    private (ModbusSlave Slave, Int32 Port) CreateSlave()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            Registers = Enumerable.Range(0, 20)
                .Select(i => new RegisterUnit { Address = i, Value = (UInt16)(100 + i) })
                .ToList(),
            Coils = [],
        };
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
    [System.ComponentModel.DisplayName("FC23 集成测试：先写后读寄存器")]
    public async Task ReadWriteRegisters_Integration_WriteFirst_ThenRead()
    {
        var (slave, port) = CreateSlave();
        try
        {
            using var client = await CreateClientAsync(port);
            // 写地址10和11，同时读地址0和1（初始值100、101）
            var rs = await client.ReadWriteRegistersAsync(1, 0, 2, 10, [0xAAAA, 0xBBBB]);

            // 读取结果应为写之前的读地址0-1的值（Modbus 规范：先写后读）
            Assert.Equal(2, rs.Length);
            Assert.Equal((UInt16)100, rs[0]);
            Assert.Equal((UInt16)101, rs[1]);

            // 验证写入生效
            var written = await client.ReadRegisterAsync(1, 10, 2);
            Assert.Equal((UInt16)0xAAAA, written[0]);
            Assert.Equal((UInt16)0xBBBB, written[1]);
        }
        finally
        {
            slave.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC23 集成测试：读写地址不重叠")]
    public async Task ReadWriteRegisters_Integration_NonOverlappingAddresses()
    {
        var (slave, port) = CreateSlave();
        try
        {
            using var client = await CreateClientAsync(port);
            // 写地址15，读地址5
            var rs = await client.ReadWriteRegistersAsync(1, 5, 1, 15, [0x9999]);
            Assert.Single(rs);
            Assert.Equal((UInt16)105, rs[0]);   // addr=5, initial value=100+5

            // 写入已生效
            var written = await client.ReadRegisterAsync(1, 15, 1);
            Assert.Equal((UInt16)0x9999, written[0]);
        }
        finally
        {
            slave.Dispose();
        }
    }

    #endregion
}
