using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC11 GetComEventCounter 获取通信事件计数单元测试</summary>
public class ModbusGetComEventCounterTests
{
    [Fact]
    [System.ComponentModel.DisplayName("FC11 正常响应返回状态和计数")]
    public async Task GetComEventCounter_ValidResponse_ReturnsInfo()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：Status(0x0000) + EventCount(0x002A)
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.GetComEventCounter, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-00-00-2A".ToHex());

        var rs = await mb.Object.GetComEventCounterAsync(1);
        Assert.NotNull(rs);
        Assert.Equal((UInt16)0x0000, rs.Value.Status);
        Assert.Equal((UInt16)42, rs.Value.EventCount);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC11 响应为null返回null")]
    public async Task GetComEventCounter_NullResponse_ReturnsNull()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.GetComEventCounter, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket?)null);

        var rs = await mb.Object.GetComEventCounterAsync(1);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC11 通信错误状态")]
    public async Task GetComEventCounter_ErrorStatus()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // Status=0xFFFF表示通信错误
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.GetComEventCounter, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"FF-FF-00-00".ToHex());

        var rs = await mb.Object.GetComEventCounterAsync(1);
        Assert.NotNull(rs);
        Assert.Equal((UInt16)0xFFFF, rs.Value.Status);
        Assert.Equal((UInt16)0, rs.Value.EventCount);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC11 请求不含负载数据")]
    public async Task GetComEventCounter_NoPayloadInRequest()
    {
        IPacket? capturedPayload = null;
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.GetComEventCounter, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<FunctionCodes, Byte, IPacket, CancellationToken>((code, host, payload, ct) => capturedPayload = payload)
            .ReturnsAsync((ArrayPacket)"00-00-00-00".ToHex());

        await mb.Object.GetComEventCounterAsync(1);

        Assert.NotNull(capturedPayload);
        Assert.True(capturedPayload!.Total == 0 || capturedPayload.GetSpan().IsEmpty);
    }
}
