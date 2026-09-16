using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

public class ModbusTests
{
    [Fact]
    public async Task ReadAsync()
    {
        // 模拟Modbus。CallBase 指定调用基类方法
        var mb = new Mock<Modbus>() { CallBase = true };
        //mb.Setup(e => e.Read(FunctionCodes.ReadRegister, 1, 100, 1))
        //    .Returns("01-02-00".ToHex());
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-02-00".ToHex());
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, It.IsAny<UInt16>(), 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-01-02-03-04".ToHex());

        var modbus = mb.Object;

        Assert.Equal("ModbusProxy", modbus.Name);

        await Assert.ThrowsAsync<NotSupportedException>(() => modbus.ReadAsync(FunctionCodes.ReadWriteMultipleRegisters, 1, 1, 1));

        // 读取
        var rs = await modbus.ReadAsync(FunctionCodes.ReadRegister, 1, 100, 1) as IPacket;
        Assert.NotNull(rs);
        Assert.Equal(0x0200, rs.ReadBytes().ToUInt16(0, false));

        rs = await modbus.ReadAsync(FunctionCodes.ReadRegister, 1, 102, 2) as IPacket;
        Assert.NotNull(rs);
        Assert.Equal(0x01020304u, rs.ReadBytes().ToUInt32(0, false));
    }

    [Fact]
    public async Task ReadCoilAsync()
    {
        // 模拟Modbus
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadCoil, 1, 100, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-12-34-56-78".ToHex());

        var modbus = mb.Object;

        // 读取
        var rs = await modbus.ReadCoilAsync(1, 100, 2);
        Assert.NotNull(rs);

        Assert.Equal(2, rs.Length);
        Assert.False(rs[0]);
        Assert.True(rs[1]);
        Assert.False(((0x12 >> 0) & 1) == 1);
        Assert.True(((0x12 >> 1) & 1) == 1);
    }

    [Fact]
    public async Task ReadDiscreteAsync()
    {
        // 模拟Modbus
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDiscrete, 1, 100, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-12-34-56-78".ToHex());

        var modbus = mb.Object;

        // 读取
        var rs = await modbus.ReadDiscreteAsync(1, 100, 2);
        Assert.NotNull(rs);

        Assert.Equal(2, rs.Length);
        Assert.False(rs[0]);
        Assert.True(rs[1]);
        Assert.False(((0x12 >> 0) & 1) == 1);
        Assert.True(((0x12 >> 1) & 1) == 1);
    }

    [Fact]
    public async Task ReadRegisterAsync()
    {
        // 模拟Modbus
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, 100, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-12-34-56-78".ToHex());

        var modbus = mb.Object;

        // 读取
        var rs = await modbus.ReadRegisterAsync(1, 100, 2);
        Assert.NotNull(rs);

        Assert.Equal(2, rs.Length);
        Assert.Equal(0x1234, rs[0]);
        Assert.Equal(0x5678, rs[1]);
    }

    [Fact]
    public async Task ReadInputAsync()
    {
        // 模拟Modbus
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadInput, 1, 100, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-12-34-56-78".ToHex());

        var modbus = mb.Object;

        // 读取
        var rs = await modbus.ReadInputAsync(1, 100, 2);
        Assert.NotNull(rs);

        Assert.Equal(2, rs.Length);
        Assert.Equal(0x1234, rs[0]);
        Assert.Equal(0x5678, rs[1]);
    }

    [Fact]
    public async Task WriteAsync()
    {
        // 模拟Modbus。CallBase 指定调用基类方法
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage e, CancellationToken _) => new ModbusMessage
            {
                Reply = true,
                Host = e.Host,
                Code = e.Code,
                Payload = e.Payload.Slice(0, 2).Append("03-04".ToHex())
            });

        var modbus = mb.Object;

        Assert.Equal("ModbusProxy", modbus.Name);

        await Assert.ThrowsAsync<NotSupportedException>(() => modbus.WriteAsync(FunctionCodes.ReadWriteMultipleRegisters, 1, 1, new UInt16[] { 1 }));

        // 读取
        var rs = (Int32)await modbus.WriteAsync(FunctionCodes.WriteRegister, 1, 100, new UInt16[] { 1 });
        Assert.NotEqual(-1, rs);
        Assert.Equal(0x0304, rs);
    }

    [Fact]
    public async Task WriteCoilAsync()
    {
        // 模拟Modbus
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage e, CancellationToken _) => new ModbusMessage
            {
                Reply = true,
                Host = e.Host,
                Code = e.Code,
                Payload = e.Payload.Slice(0, 4)
            });

        var modbus = mb.Object;

        // 读取
        var rs = await modbus.WriteCoilAsync(1, 100, 0xFF00);
        Assert.Equal(0xFF00, rs);
    }

    [Fact]
    public async Task WriteRegisterAsync()
    {
        // 模拟Modbus
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage e, CancellationToken _) => new ModbusMessage
            {
                Reply = true,
                Host = e.Host,
                Code = e.Code,
                Payload = e.Payload.Slice(0, 2).Append(e.Payload.Slice(2, 2))
            });

        var modbus = mb.Object;

        // 读取
        var rs = await modbus.WriteRegisterAsync(1, 100, 0x1234);
        Assert.Equal(0x1234, rs);
    }

    [Fact]
    public async Task WriteCoilsAsync()
    {
        // 模拟Modbus
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage e, CancellationToken _) => new ModbusMessage
            {
                Reply = true,
                Host = e.Host,
                Code = e.Code,
                Payload = e.Payload.Slice(0, 2).Append(e.Payload.Slice(2, 2))
            });

        var modbus = mb.Object;

        // 读取
        var rs = await modbus.WriteCoilsAsync(1, 100, new UInt16[] { 0xFF00, 0x0000, 0xFF00, 0x0000 });
        Assert.Equal(4, rs);
    }

    [Fact]
    public async Task WriteRegistersAsync()
    {
        // 模拟Modbus
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage e, CancellationToken _) => new ModbusMessage
            {
                Reply = true,
                Host = e.Host,
                Code = e.Code,
                Payload = e.Payload.Slice(0, 2).Append(e.Payload.Slice(2, 2))
            });

        var modbus = mb.Object;

        // 读取
        var rs = await modbus.WriteRegistersAsync(1, 100, new UInt16[] { 2, 3, 4, 5, 7 });
        Assert.Equal(5, rs);
    }
}