using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC22 MaskWriteRegister 屏蔽写寄存器单元测试</summary>
public class ModbusMaskWriteRegisterTests
{
    [Fact]
    [System.ComponentModel.DisplayName("FC22 正常响应返回屏蔽后的值")]
    public async Task MaskWriteRegister_ValidResponse_ReturnsValue()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 从机回显请求：地址(0x0064) + AND_Mask(0xFFF0) + OR_Mask(0x0001)
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.MaskWriteRegister, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-64-FF-F0-00-01".ToHex());

        var rs = await mb.Object.MaskWriteRegisterAsync(1, 0x0064, 0xFFF0, 0x0001);
        // 响应回显AND_Mask，实际值取决于从机
        Assert.NotEqual((UInt16)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC22 响应为null返回0")]
    public async Task MaskWriteRegister_NullResponse_ReturnsZero()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.MaskWriteRegister, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket?)null);

        var rs = await mb.Object.MaskWriteRegisterAsync(1, 0x0064, 0xFFF0, 0x0001);
        Assert.Equal((UInt16)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC22 设置单个位（AND=0xFFFE, OR=0x0001）")]
    public async Task MaskWriteRegister_SetSingleBit()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.MaskWriteRegister, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-01-FF-FE-00-01".ToHex());

        var rs = await mb.Object.MaskWriteRegisterAsync(1, 0x0001, 0xFFFE, 0x0001);
        Assert.NotEqual((UInt16)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC22 清除单个位（AND=0xFFFE, OR=0x0000）")]
    public async Task MaskWriteRegister_ClearSingleBit()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.MaskWriteRegister, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-01-FF-FE-00-00".ToHex());

        var rs = await mb.Object.MaskWriteRegisterAsync(1, 0x0001, 0xFFFE, 0x0000);
        Assert.NotEqual((UInt16)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC22 请求负载格式正确：地址+AND_Mask+OR_Mask共6字节")]
    public async Task MaskWriteRegister_RequestPayloadFormat()
    {
        IPacket? capturedPayload = null;
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.MaskWriteRegister, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<FunctionCodes, Byte, IPacket, CancellationToken>((code, host, payload, ct) => capturedPayload = payload)
            .ReturnsAsync((ArrayPacket)"00-64-FF-F0-00-01".ToHex());

        await mb.Object.MaskWriteRegisterAsync(1, 0x0064, 0xFFF0, 0x0001);

        Assert.NotNull(capturedPayload);
        Assert.Equal(6, capturedPayload!.Total);
        var addr = capturedPayload.ReadBytes(0, 2).ToUInt16(0, false);
        var andMask = capturedPayload.ReadBytes(2, 2).ToUInt16(0, false);
        var orMask = capturedPayload.ReadBytes(4, 2).ToUInt16(0, false);
        Assert.Equal((UInt16)0x0064, addr);
        Assert.Equal((UInt16)0xFFF0, andMask);
        Assert.Equal((UInt16)0x0001, orMask);
    }
}
