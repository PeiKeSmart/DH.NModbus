using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC08 Diagnostics 功能码单元测试</summary>
public class ModbusDiagnosticsTests
{
    #region Echo Test (SubCode=0x0000)

    [Fact]
    [System.ComponentModel.DisplayName("FC08 Echo Test 返回回显数据")]
    public async Task Diagnostics_EchoTest_ReturnsEchoData()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：子功能码(0x0000) + 数据(0x1234)
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.Diagnostics, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-00-12-34".ToHex());

        var rs = await mb.Object.DiagnosticsAsync(1, 0x0000, 0x1234);
        Assert.Equal((UInt16)0x1234, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC08 Echo Test 数据为0")]
    public async Task Diagnostics_EchoTest_ZeroData()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.Diagnostics, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-00-00-00".ToHex());

        var rs = await mb.Object.DiagnosticsAsync(1, 0x0000, 0x0000);
        Assert.Equal((UInt16)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC08 Echo Test 最大值")]
    public async Task Diagnostics_EchoTest_MaxData()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.Diagnostics, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-00-FF-FF".ToHex());

        var rs = await mb.Object.DiagnosticsAsync(1, 0x0000, 0xFFFF);
        Assert.Equal((UInt16)0xFFFF, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC08 响应为null返回0")]
    public async Task Diagnostics_NullResponse_ReturnsZero()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.Diagnostics, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var rs = await mb.Object.DiagnosticsAsync(1, 0x0000, 0x1234);
        Assert.Equal((UInt16)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC08 响应数据不足返回0")]
    public async Task Diagnostics_ShortResponse_ReturnsZero()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应只有3字节，不足4字节
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.Diagnostics, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-00-12".ToHex());

        var rs = await mb.Object.DiagnosticsAsync(1, 0x0000, 0x1234);
        Assert.Equal((UInt16)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC08 不同站号")]
    public async Task Diagnostics_DifferentHost()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.Diagnostics, 2, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-00-AB-CD".ToHex());

        var rs = await mb.Object.DiagnosticsAsync(2, 0x0000, 0xABCD);
        Assert.Equal((UInt16)0xABCD, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC08 非Echo子功能码")]
    public async Task Diagnostics_NonEchoSubCode()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 子功能码 0x000A（Reset）响应
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.Diagnostics, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-0A-00-00".ToHex());

        var rs = await mb.Object.DiagnosticsAsync(1, 0x000A, 0x0000);
        Assert.Equal((UInt16)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC08 FunctionCodes枚举值为0x08")]
    public void Diagnostics_FunctionCodeValue()
    {
        Assert.Equal(0x08, (Byte)FunctionCodes.Diagnostics);
    }

    #endregion
}
