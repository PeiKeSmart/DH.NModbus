using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC07 ReadExceptionStatus 读异常状态单元测试</summary>
public class ModbusReadExceptionStatusTests
{
    [Fact]
    [System.ComponentModel.DisplayName("FC07 正常响应返回异常状态字节")]
    public async Task ReadExceptionStatus_ValidResponse_ReturnsStatus()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：1字节异常状态
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadExceptionStatus, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)new Byte[] { 0x03 });

        var rs = await mb.Object.ReadExceptionStatusAsync(1);
        Assert.Equal((Byte)0x03, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC07 响应为null返回0")]
    public async Task ReadExceptionStatus_NullResponse_ReturnsZero()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadExceptionStatus, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket?)null);

        var rs = await mb.Object.ReadExceptionStatusAsync(1);
        Assert.Equal((Byte)0, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC07 无异常状态时返回0x00")]
    public async Task ReadExceptionStatus_NoException_ReturnsZero()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadExceptionStatus, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)new Byte[] { 0x00 });

        var rs = await mb.Object.ReadExceptionStatusAsync(1);
        Assert.Equal((Byte)0x00, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC07 请求不含负载数据")]
    public async Task ReadExceptionStatus_NoPayloadInRequest()
    {
        IPacket? capturedPayload = null;
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadExceptionStatus, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<FunctionCodes, Byte, IPacket, CancellationToken>((code, host, payload, ct) => capturedPayload = payload)
            .ReturnsAsync((ArrayPacket)new Byte[] { 0x00 });

        await mb.Object.ReadExceptionStatusAsync(1);

        Assert.NotNull(capturedPayload);
        // 请求不应包含有效负载数据
        Assert.True(capturedPayload!.Total == 0 || capturedPayload.GetSpan().IsEmpty);
    }
}
