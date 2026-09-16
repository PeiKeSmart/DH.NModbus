using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>Modbus读写操作的补充单元测试，覆盖未测试的代码路径</summary>
public class ModbusReadWriteTests
{
    #region Read - 空/null响应
    [Fact]
    public async Task Read_NullResponse_ReturnsNull()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var modbus = mb.Object;
        var rs = await modbus.ReadAsync(FunctionCodes.ReadRegister, 1, 100, 1);
        Assert.Null(rs);
    }

    [Fact]
    public async Task Read_ReadCoil_NullResponse_ReturnsNull()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadCoil, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var modbus = mb.Object;
        var rs = await modbus.ReadAsync(FunctionCodes.ReadCoil, 1, 100, 1);
        Assert.Null(rs);
    }

    [Fact]
    public async Task Read_ReadDiscrete_Works()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDiscrete, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"01-03".ToHex());

        var modbus = mb.Object;
        var rs = await modbus.ReadAsync(FunctionCodes.ReadDiscrete, 1, 100, 1);
        Assert.NotNull(rs);
    }

    [Fact]
    public async Task Read_ReadInput_Works()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadInput, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-00-64".ToHex());

        var modbus = mb.Object;
        var rs = await modbus.ReadAsync(FunctionCodes.ReadInput, 1, 100, 1);
        Assert.NotNull(rs);
    }
    #endregion

    #region Read - ValidResponse=false
    [Fact]
    public async Task Read_ValidResponseFalse_SkipsLengthCheck()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 返回不完整的数据，但关闭校验
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-00".ToHex());

        var modbus = mb.Object;
        modbus.ValidResponse = false;

        var rs = await modbus.ReadAsync(FunctionCodes.ReadRegister, 1, 100, 1);
        Assert.NotNull(rs);
    }

    [Fact]
    public async Task Read_ValidResponseTrue_InsufficientData_ReturnsNull()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // len=0x04 但只有2字节数据，Total < 1+len
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-00".ToHex());

        var modbus = mb.Object;
        modbus.ValidResponse = true;

        var rs = await modbus.ReadAsync(FunctionCodes.ReadRegister, 1, 100, 1);
        Assert.Null(rs);
    }
    #endregion

    #region Read - 不支持的功能码
    [Fact]
    public async Task Read_UnsupportedCode_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        await Assert.ThrowsAsync<NotSupportedException>(() => modbus.ReadAsync(FunctionCodes.Diagnostics, 1, 100, 1));
    }

    [Fact]
    public async Task Read_WriteCoilCode_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        await Assert.ThrowsAsync<NotSupportedException>(() => modbus.ReadAsync(FunctionCodes.WriteCoil, 1, 100, 1));
    }
    #endregion

    #region ReadCoil - 边界情况
    [Fact]
    public async Task ReadCoil_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadCoil, 1, It.IsAny<UInt16>(), 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var modbus = mb.Object;
        var rs = await modbus.ReadCoilAsync(1, 0, 8);
        Assert.Empty(rs);
    }

    [Fact]
    public async Task ReadCoil_ValidResponse_InsufficientData_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 请求16个线圈需要2字节数据，但只返回1字节
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadCoil, 1, It.IsAny<UInt16>(), 16, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"01".ToHex());

        var modbus = mb.Object;
        modbus.ValidResponse = true;
        var rs = await modbus.ReadCoilAsync(1, 0, 16);
        Assert.Empty(rs);
    }

    [Fact]
    public async Task ReadCoil_NonMultipleOf8()
    {
        // 请求10个线圈（非8的倍数），需要2字节
        var mb = new Mock<Modbus>() { CallBase = true };
        // 返回: count_byte=02, data: 0xFF, 0x03 (前8位全开，后2位全开)
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadCoil, 1, It.IsAny<UInt16>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-FF-03".ToHex());

        var modbus = mb.Object;
        var rs = await modbus.ReadCoilAsync(1, 0, 10);
        Assert.Equal(10, rs.Length);
        for (var i = 0; i < 10; i++)
            Assert.True(rs[i]);
    }

    [Fact]
    public async Task ReadCoil_ValidResponseFalse_SkipsLengthCheck()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadCoil, 1, It.IsAny<UInt16>(), 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"01-AA".ToHex());

        var modbus = mb.Object;
        modbus.ValidResponse = false;
        var rs = await modbus.ReadCoilAsync(1, 0, 8);
        Assert.Equal(8, rs.Length);
    }

    [Fact]
    public async Task ReadCoil_SingleCoil()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadCoil, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"01-01".ToHex());

        var modbus = mb.Object;
        var rs = await modbus.ReadCoilAsync(1, 0, 1);
        Assert.Single(rs);
        Assert.True(rs[0]);
    }

    [Fact]
    public async Task ReadCoil_AllOff()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadCoil, 1, It.IsAny<UInt16>(), 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"01-00".ToHex());

        var modbus = mb.Object;
        var rs = await modbus.ReadCoilAsync(1, 0, 8);
        Assert.Equal(8, rs.Length);
        for (var i = 0; i < 8; i++)
            Assert.False(rs[i]);
    }
    #endregion

    #region ReadDiscrete - 边界情况
    [Fact]
    public async Task ReadDiscrete_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDiscrete, 1, It.IsAny<UInt16>(), 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var modbus = mb.Object;
        var rs = await modbus.ReadDiscreteAsync(1, 0, 8);
        Assert.Empty(rs);
    }

    [Fact]
    public async Task ReadDiscrete_ValidResponse_InsufficientData_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 请求16个离散量需要2字节数据，但只返回1字节
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDiscrete, 1, It.IsAny<UInt16>(), 16, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"01".ToHex());

        var modbus = mb.Object;
        modbus.ValidResponse = true;
        var rs = await modbus.ReadDiscreteAsync(1, 0, 16);
        Assert.Empty(rs);
    }

    [Fact]
    public async Task ReadDiscrete_NonMultipleOf8()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadDiscrete, 1, It.IsAny<UInt16>(), 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"01-05".ToHex());

        var modbus = mb.Object;
        var rs = await modbus.ReadDiscreteAsync(1, 0, 3);
        Assert.Equal(3, rs.Length);
        Assert.True(rs[0]);   // bit0 = 1
        Assert.False(rs[1]);  // bit1 = 0
        Assert.True(rs[2]);   // bit2 = 1
    }
    #endregion

    #region ReadRegister - 边界情况
    [Fact]
    public async Task ReadRegister_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, It.IsAny<UInt16>(), 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var modbus = mb.Object;
        var rs = await modbus.ReadRegisterAsync(1, 0, 2);
        Assert.Empty(rs);
    }

    [Fact]
    public async Task ReadRegister_ValidResponse_InsufficientData_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // len=04，但只有2字节数据
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, It.IsAny<UInt16>(), 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-00-01".ToHex());

        var modbus = mb.Object;
        modbus.ValidResponse = true;
        var rs = await modbus.ReadRegisterAsync(1, 0, 2);
        Assert.Empty(rs);
    }

    [Fact]
    public async Task ReadRegister_SingleRegister()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadRegister, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-AB-CD".ToHex());

        var modbus = mb.Object;
        var rs = await modbus.ReadRegisterAsync(1, 100, 1);
        Assert.Single(rs);
        Assert.Equal(0xABCD, rs[0]);
    }
    #endregion

    #region ReadInput - 边界情况
    [Fact]
    public async Task ReadInput_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadInput, 1, It.IsAny<UInt16>(), 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var modbus = mb.Object;
        var rs = await modbus.ReadInputAsync(1, 0, 2);
        Assert.Empty(rs);
    }

    [Fact]
    public async Task ReadInput_ValidResponse_InsufficientData_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadInput, 1, It.IsAny<UInt16>(), 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"04-00-01".ToHex());

        var modbus = mb.Object;
        modbus.ValidResponse = true;
        var rs = await modbus.ReadInputAsync(1, 0, 2);
        Assert.Empty(rs);
    }

    [Fact]
    public async Task ReadInput_SingleRegister()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadInput, 1, It.IsAny<UInt16>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-12-34".ToHex());

        var modbus = mb.Object;
        var rs = await modbus.ReadInputAsync(1, 100, 1);
        Assert.Single(rs);
        Assert.Equal(0x1234, rs[0]);
    }
    #endregion

    #region WriteCoil - 边界情况
    [Fact]
    public async Task WriteCoil_NullResponse_ReturnsMinus1()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage _, CancellationToken _) => null);

        var modbus = mb.Object;
        var rs = await modbus.WriteCoilAsync(1, 100, 0xFF00);
        Assert.Equal(-1, rs);
    }

    [Fact]
    public async Task WriteCoil_ShortResponse_ReturnsMinus1()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage e, CancellationToken _) => new ModbusMessage
            {
                Reply = true,
                Host = e.Host,
                Code = e.Code,
                Payload = (ArrayPacket)"00-01".ToHex()
            });

        var modbus = mb.Object;
        var rs = await modbus.WriteCoilAsync(1, 100, 0xFF00);
        Assert.Equal(-1, rs);
    }

    [Fact]
    public async Task WriteCoil_WriteOff()
    {
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
        var rs = await modbus.WriteCoilAsync(1, 100, 0x0000);
        Assert.Equal(0x0000, rs);
    }
    #endregion

    #region WriteRegister - 边界情况
    [Fact]
    public async Task WriteRegister_NullResponse_ReturnsMinus1()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage _, CancellationToken _) => null);

        var modbus = mb.Object;
        var rs = await modbus.WriteRegisterAsync(1, 100, 0x1234);
        Assert.Equal(-1, rs);
    }

    [Fact]
    public async Task WriteRegister_ShortResponse_ReturnsMinus1()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage e, CancellationToken _) => new ModbusMessage
            {
                Reply = true,
                Host = e.Host,
                Code = e.Code,
                Payload = (ArrayPacket)"00".ToHex()
            });

        var modbus = mb.Object;
        var rs = await modbus.WriteRegisterAsync(1, 100, 0x1234);
        Assert.Equal(-1, rs);
    }

    [Fact]
    public async Task WriteRegister_ZeroValue()
    {
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
        var rs = await modbus.WriteRegisterAsync(1, 100, 0x0000);
        Assert.Equal(0, rs);
    }

    [Fact]
    public async Task WriteRegister_MaxValue()
    {
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
        var rs = await modbus.WriteRegisterAsync(1, 100, 0xFFFF);
        Assert.Equal(0xFFFF, rs);
    }
    #endregion

    #region WriteCoils - 边界情况
    [Fact]
    public async Task WriteCoils_NullResponse_ReturnsMinus1()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage _, CancellationToken _) => null);

        var modbus = mb.Object;
        var rs = await modbus.WriteCoilsAsync(1, 0, new UInt16[] { 0xFF00 });
        Assert.Equal(-1, rs);
    }

    [Fact]
    public async Task WriteCoils_SingleCoil()
    {
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
        var rs = await modbus.WriteCoilsAsync(1, 100, new UInt16[] { 0xFF00 });
        Assert.Equal(1, rs);
    }

    [Fact]
    public async Task WriteCoils_NonMultipleOf8()
    {
        // 10个线圈（非8的倍数），测试余数位的打包
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
        var values = new UInt16[] { 0xFF00, 0, 0xFF00, 0, 0xFF00, 0, 0xFF00, 0, 0xFF00, 0xFF00 };
        var rs = await modbus.WriteCoilsAsync(1, 0, values);
        Assert.Equal(10, rs);
    }

    [Fact]
    public async Task WriteCoils_AllOff()
    {
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
        var rs = await modbus.WriteCoilsAsync(1, 0, new UInt16[] { 0, 0, 0, 0 });
        Assert.Equal(4, rs);
    }
    #endregion

    #region WriteRegisters - 边界情况
    [Fact]
    public async Task WriteRegisters_NullResponse_ReturnsMinus1()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage _, CancellationToken _) => null);

        var modbus = mb.Object;
        var rs = await modbus.WriteRegistersAsync(1, 0, new UInt16[] { 1 });
        Assert.Equal(-1, rs);
    }

    [Fact]
    public async Task WriteRegisters_SingleRegister()
    {
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
        var rs = await modbus.WriteRegistersAsync(1, 100, new UInt16[] { 0xABCD });
        Assert.Equal(1, rs);
    }
    #endregion

    #region Write - 派发不支持的功能码
    [Fact]
    public async Task Write_UnsupportedCode_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        await Assert.ThrowsAsync<NotSupportedException>(() => modbus.WriteAsync(FunctionCodes.ReadRegister, 1, 100, new UInt16[] { 1 }));
    }

    [Fact]
    public async Task Write_Diagnostics_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        await Assert.ThrowsAsync<NotSupportedException>(() => modbus.WriteAsync(FunctionCodes.Diagnostics, 1, 100, new UInt16[] { 1 }));
    }

    [Fact]
    public async Task Write_WriteCoil_Dispatches()
    {
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
        var rs = (Int32)await modbus.WriteAsync(FunctionCodes.WriteCoil, 1, 100, new UInt16[] { 0xFF00 });
        Assert.Equal(0xFF00, rs);
    }

    [Fact]
    public async Task Write_WriteCoils_Dispatches()
    {
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
        var rs = (Int32)await modbus.WriteAsync(FunctionCodes.WriteCoils, 1, 0, new UInt16[] { 0xFF00, 0, 0xFF00 });
        Assert.Equal(3, rs);
    }

    [Fact]
    public async Task Write_WriteRegisters_Dispatches()
    {
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
        var rs = (Int32)await modbus.WriteAsync(FunctionCodes.WriteRegisters, 1, 100, new UInt16[] { 1, 2 });
        Assert.Equal(2, rs);
    }
    #endregion

    #region SendCommand 重载
    [Fact]
    public async Task SendCommand_CodeHostData_Works()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage e, CancellationToken _) => new ModbusMessage
            {
                Reply = true,
                Host = e.Host,
                Code = e.Code,
                Payload = (ArrayPacket)"01-02-03-04".ToHex()
            });

        var modbus = mb.Object;
        var pk = (ArrayPacket)"AA-BB".ToHex();
        var rs = await modbus.SendCommandAsync(FunctionCodes.WriteCoils, 1, pk);
        Assert.NotNull(rs);
        Assert.Equal("01-02-03-04", rs.ToHex(256, "-"));
    }

    [Fact]
    public async Task SendCommand_CodeHostData_NullResponse()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage _, CancellationToken _) => null);

        var modbus = mb.Object;
        var pk = (ArrayPacket)"AA-BB".ToHex();
        var rs = await modbus.SendCommandAsync(FunctionCodes.WriteCoils, 1, pk);
        Assert.Null(rs);
    }
    #endregion

    #region 属性测试
    [Fact]
    public void Properties_DefaultValues()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        Assert.Equal(3000, modbus.Timeout);
        Assert.Equal(256, modbus.BufferSize);
        Assert.True(modbus.ValidResponse);
        Assert.Null(modbus.Tracer);
        Assert.Null(modbus.Log);
    }

    [Fact]
    public void Properties_CanSet()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        modbus.Timeout = 5000;
        modbus.BufferSize = 512;
        modbus.ValidResponse = false;

        Assert.Equal(5000, modbus.Timeout);
        Assert.Equal(512, modbus.BufferSize);
        Assert.False(modbus.ValidResponse);
    }
    #endregion

    #region 自动重连（ENH-1）
    [Fact]
    [System.ComponentModel.DisplayName("MaxRetry=0 时不重试，异常直接抛出")]
    public async Task AutoReconnect_Disabled_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("连接已断开"));

        var modbus = mb.Object;
        modbus.MaxRetry = 0; // 默认不重连

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            modbus.SendCommandAsync(FunctionCodes.ReadCoil, 1, 0, 8));
    }

    [Fact]
    [System.ComponentModel.DisplayName("MaxRetry=1 且重连成功时，操作最终成功")]
    public async Task AutoReconnect_RetryOnce_Success()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var callCount = 0;

        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("连接已断开");
                // 第二次调用返回成功响应
                return Task.FromResult(new ModbusMessage
                {
                    Reply = true,
                    Host = 1,
                    Code = FunctionCodes.ReadCoil,
                    Payload = (ArrayPacket)"01-05".ToHex()
                })!;
            });

        var modbus = mb.Object;
        modbus.MaxRetry = 1;
        modbus.RetryInterval = 10; // 快速重连

        var rs = await modbus.SendCommandAsync(FunctionCodes.ReadCoil, 1, 0, 8);

        Assert.NotNull(rs);
        Assert.Equal(2, callCount); // 第1次失败 + 第2次成功
    }

    [Fact]
    [System.ComponentModel.DisplayName("MaxRetry 耗尽后异常最终抛出")]
    public async Task AutoReconnect_MaxRetryExceeded_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var callCount = 0;

        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                throw new InvalidOperationException($"连接已断开 (第{callCount}次)");
            });

        var modbus = mb.Object;
        modbus.MaxRetry = 2;
        modbus.RetryInterval = 10; // 快速重连

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            modbus.SendCommandAsync(FunctionCodes.ReadCoil, 1, 0, 8));

        Assert.Contains("第3次", ex.Message); // 初始 1 次 + 重试 2 次 = 3 次
        Assert.Equal(3, callCount);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusIp 重连：ReconnectAsync 关闭后重新打开")]
    public async Task AutoReconnect_ModbusIp_Reconnects()
    {
        // ModbusTcp 继承 ModbusIp，验证 ReconnectAsync 路径
        var mb = new Mock<ModbusTcp>() { CallBase = true };
        var reconnectCalled = false;

        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("连接已断开"));

        // 验证 ReconnectAsync 被调用：模拟 CloseAsync 和 OpenAsync
        mb.Setup(e => e.CloseAsync(It.IsAny<CancellationToken>()))
            .Callback(() => reconnectCalled = true)
            .Returns(TaskEx.CompletedTask);
        mb.Setup(e => e.OpenAsync(It.IsAny<CancellationToken>()))
            .Returns(TaskEx.CompletedTask);

        var modbus = mb.Object;
        modbus.MaxRetry = 1;
        modbus.RetryInterval = 10;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            modbus.SendCommandAsync(FunctionCodes.ReadCoil, 1, 0, 8));

        Assert.True(reconnectCalled, "ReconnectAsync 未被调用，自动重连路径未触发");
    }

    [Fact]
    [System.ComponentModel.DisplayName("取消令牌提前终止重试")]
    public async Task AutoReconnect_CancellationToken_StopsRetry()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("连接已断开"));

        var modbus = mb.Object;
        modbus.MaxRetry = 5; // 足够大
        modbus.RetryInterval = 1000; // 长间隔，确保取消生效

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 提前取消

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            modbus.SendCommandAsync(FunctionCodes.ReadCoil, 1, 0, 8, cts.Token));
    }
    #endregion
}
