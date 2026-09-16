using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>Enron Modbus（32位浮点扩展）单元测试</summary>
/// <remarks>
/// 所有测试通过 Mock <see cref="ModbusEnron"/> 的
/// <see cref="Modbus.SendCommandAsync(ModbusMessage,CancellationToken)"/> 方法实现，
/// 该方法是所有 SendCommandAsync 重载的最终汇聚点，受 InternalsVisibleTo 保护。
/// </remarks>
public class ModbusEnronTests
{
    #region 辅助方法

    /// <summary>创建 Mock 并设置 SendCommandAsync(ModbusMessage) 返回指定负载</summary>
    private static Mock<ModbusEnron> CreateMockWithResponse(FunctionCodes code, Byte host, IPacket payload)
    {
        var mb = new Mock<ModbusEnron>() { CallBase = true };

        var response = new ModbusMessage
        {
            Host = host,
            Code = code,
            Payload = payload,
        };

        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        return mb;
    }

    /// <summary>构造 ReadRegister/ReadInput 响应包（字节数 + 寄存器数据）</summary>
    private static IPacket BuildRegisterResponse(UInt16[] registers)
    {
        var data = new Byte[1 + registers.Length * 2];
        data[0] = (Byte)(registers.Length * 2);
        for (var i = 0; i < registers.Length; i++)
        {
            data[1 + i * 2] = (Byte)(registers[i] >> 8);
            data[1 + i * 2 + 1] = (Byte)(registers[i] & 0xFF);
        }
        return (ArrayPacket)data;
    }

    /// <summary>构造 WriteRegisters 响应包（地址 + 数量）</summary>
    private static IPacket BuildWriteResponse(UInt16 address, UInt16 count)
    {
        var data = new Byte[4];
        data[0] = (Byte)(address >> 8);
        data[1] = (Byte)(address & 0xFF);
        data[2] = (Byte)(count >> 8);
        data[3] = (Byte)(count & 0xFF);
        return (ArrayPacket)data;
    }

    #endregion

    #region RawToFloats

    [Fact]
    [System.ComponentModel.DisplayName("RawToFloats 空数组返回空数组")]
    public void RawToFloats_Empty_ReturnsEmpty()
    {
        var result = InvokeRawToFloats([]);
        Assert.Empty(result);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RawToFloats 2个UInt16=1个float")]
    public void RawToFloats_TwoUInt16_OneFloat()
    {
        var result = InvokeRawToFloats([0x3F80, 0x0000]);
        Assert.Single(result);
        Assert.Equal(1.0f, result[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RawToFloats 4个UInt16=2个float")]
    public void RawToFloats_FourUInt16_TwoFloats()
    {
        var result = InvokeRawToFloats([0x3F80, 0x0000, 0x4000, 0x0000]);
        Assert.Equal(2, result.Length);
        Assert.Equal(1.0f, result[0]);
        Assert.Equal(2.0f, result[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RawToFloats 负数和零")]
    public void RawToFloats_NegativeAndZero()
    {
        var result = InvokeRawToFloats([0xBF80, 0x0000, 0x0000, 0x0000]);
        Assert.Equal(2, result.Length);
        Assert.Equal(-1.0f, result[0]);
        Assert.Equal(0.0f, result[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RawToFloats float最大值")]
    public void RawToFloats_FloatMaxValue()
    {
        var result = InvokeRawToFloats([0x7F7F, 0xFFFF]);
        Assert.Single(result);
        Assert.Equal(Single.MaxValue, result[0]);
    }

    /// <summary>通过 ReadInputFloat 间接测试 protected static RawToFloats</summary>
    private static Single[] InvokeRawToFloats(UInt16[] raw)
    {
        var payload = BuildRegisterResponse(raw);
        var mb = CreateMockWithResponse(FunctionCodes.ReadInput, 1, payload);
        var result = mb.Object.ReadInputFloat(1, 0, (UInt16)(raw.Length / 2));
        return result ?? [];
    }

    #endregion

    #region ReadFloatAsync / ReadFloat

    [Fact]
    [System.ComponentModel.DisplayName("ReadFloatAsync 读取2个float保持寄存器")]
    public async Task ReadFloatAsync_TwoRegisters_ReturnsFloats()
    {
        var payload = BuildRegisterResponse([0x3F80, 0x0000, 0x4000, 0x0000]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadRegister, 1, payload);

        var result = await mb.Object.ReadFloatAsync(1, 0, 2);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal(1.0f, result[0]);
        Assert.Equal(2.0f, result[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadFloatAsync 响应为null返回空数组")]
    public async Task ReadFloatAsync_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<ModbusEnron>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage?)null);

        var result = await mb.Object.ReadFloatAsync(1, 0, 2);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadFloatAsync 读取单个float")]
    public async Task ReadFloatAsync_SingleRegister_ReturnsFloat()
    {
        var payload = BuildRegisterResponse([0x4048, 0xF5C3]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadRegister, 1, payload);

        var result = await mb.Object.ReadFloatAsync(1, 0, 1);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(3.14f, result[0], 3);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadFloat 同步版本正确转换")]
    public void ReadFloat_Sync_ReturnsFloats()
    {
        var payload = BuildRegisterResponse([0x3F80, 0x0000, 0x4000, 0x0000]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadRegister, 1, payload);

        var result = mb.Object.ReadFloat(1, 0, 2);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal(1.0f, result[0]);
        Assert.Equal(2.0f, result[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadFloatAsync 不同站号")]
    public async Task ReadFloatAsync_DifferentHost()
    {
        var payload = BuildRegisterResponse([0x3F80, 0x0000]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadRegister, 5, payload);

        var result = await mb.Object.ReadFloatAsync(5, 0, 1);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(1.0f, result[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadFloatAsync 0个寄存器返回空数组")]
    public async Task ReadFloatAsync_ZeroCount_ReturnsEmpty()
    {
        var payload = BuildRegisterResponse([]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadRegister, 1, payload);

        var result = await mb.Object.ReadFloatAsync(1, 0, 0);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region ReadInputFloatAsync / ReadInputFloat

    [Fact]
    [System.ComponentModel.DisplayName("ReadInputFloatAsync 读取2个float输入寄存器")]
    public async Task ReadInputFloatAsync_TwoRegisters_ReturnsFloats()
    {
        var payload = BuildRegisterResponse([0x4000, 0x0000, 0x4040, 0x0000]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadInput, 1, payload);

        var result = await mb.Object.ReadInputFloatAsync(1, 0, 2);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal(2.0f, result[0]);
        Assert.Equal(3.0f, result[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadInputFloatAsync 响应为null返回空数组")]
    public async Task ReadInputFloatAsync_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<ModbusEnron>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage?)null);

        var result = await mb.Object.ReadInputFloatAsync(1, 0, 1);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadInputFloat 同步版本")]
    public void ReadInputFloat_Sync_ReturnsFloats()
    {
        var payload = BuildRegisterResponse([0x3F80, 0x0000]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadInput, 1, payload);

        var result = mb.Object.ReadInputFloat(1, 0, 1);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(1.0f, result[0]);
    }

    #endregion

    #region WriteFloat

    [Fact]
    [System.ComponentModel.DisplayName("WriteFloat 写入单个float 返回写入的寄存器数量")]
    public void WriteFloat_SingleValue()
    {
        // WriteFloat 内部调用 WriteRegisters，后者返回写入的寄存器数量
        var payload = BuildWriteResponse(0, 2);
        var mb = CreateMockWithResponse(FunctionCodes.WriteRegisters, 1, payload);

        var result = mb.Object.WriteFloat(1, 0, 1.0f);

        // 1个float = 2个寄存器，WriteRegisters 返回 count=2
        Assert.Equal(2, result);
    }

    [Fact]
    [System.ComponentModel.DisplayName("WriteFloat 不同地址")]
    public void WriteFloat_DifferentAddress()
    {
        var payload = BuildWriteResponse(5, 2);
        var mb = CreateMockWithResponse(FunctionCodes.WriteRegisters, 1, payload);

        var result = mb.Object.WriteFloat(1, 5, 3.14f);

        // 返回写入的寄存器数量（2个），不是地址
        Assert.Equal(2, result);
    }

    [Fact]
    [System.ComponentModel.DisplayName("WriteFloat null响应返回-1")]
    public void WriteFloat_NullResponse_ReturnsMinusOne()
    {
        var mb = new Mock<ModbusEnron>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModbusMessage?)null);

        var result = mb.Object.WriteFloat(1, 0, 1.0f);

        Assert.Equal(-1, result);
    }

    #endregion

    #region WriteFloats

    [Fact]
    [System.ComponentModel.DisplayName("WriteFloats 批量写入3个float 返回寄存器数量")]
    public void WriteFloats_MultipleValues()
    {
        // 3个float = 6个UInt16寄存器
        var payload = BuildWriteResponse(0, 6);
        var mb = CreateMockWithResponse(FunctionCodes.WriteRegisters, 1, payload);

        var result = mb.Object.WriteFloats(1, 0, [1.0f, 2.0f, 3.0f]);

        // WriteRegisters 返回写入的寄存器数量
        Assert.Equal(6, result);
    }

    [Fact]
    [System.ComponentModel.DisplayName("WriteFloats 单个float 返回寄存器数量")]
    public void WriteFloats_SingleValue()
    {
        var payload = BuildWriteResponse(10, 2);
        var mb = CreateMockWithResponse(FunctionCodes.WriteRegisters, 1, payload);

        var result = mb.Object.WriteFloats(1, 10, [3.14f]);

        Assert.Equal(2, result);
    }

    [Fact]
    [System.ComponentModel.DisplayName("WriteFloats 空数组不触发写入 返回-1")]
    public void WriteFloats_EmptyArray()
    {
        var mb = new Mock<ModbusEnron>() { CallBase = true };

        var result = mb.Object.WriteFloats(1, 0, []);

        Assert.Equal(-1, result);
        mb.Verify(e => e.SendCommandAsync(It.IsAny<ModbusMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region ReadRegisterEnronAsync / ReadInputEnronAsync

    [Fact]
    [System.ComponentModel.DisplayName("ReadRegisterEnronAsync count=2 读取4个16位寄存器")]
    public async Task ReadRegisterEnronAsync_DoubleCount()
    {
        var payload = BuildRegisterResponse([0x3F80, 0x0000, 0x4000, 0x0000]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadRegister, 1, payload);

        var result = await mb.Object.ReadRegisterEnronAsync(1, 0, 2);

        Assert.NotNull(result);
        Assert.Equal(4, result.Length);
        Assert.Equal((UInt16)0x3F80, result[0]);
        Assert.Equal((UInt16)0x4000, result[2]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ReadInputEnronAsync 正确传递count*2")]
    public async Task ReadInputEnronAsync_DoubleCount()
    {
        var payload = BuildRegisterResponse([0x3F80, 0x0000]);
        var mb = CreateMockWithResponse(FunctionCodes.ReadInput, 1, payload);

        var result = await mb.Object.ReadInputEnronAsync(1, 0, 1);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
    }

    #endregion
}
