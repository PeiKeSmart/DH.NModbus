using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC24 ReadFifoQueue 读FIFO队列单元测试</summary>
public class ModbusReadFifoQueueTests
{
    [Fact]
    [System.ComponentModel.DisplayName("FC24 正常响应返回寄存器数组")]
    public async Task ReadFifoQueue_ValidResponse_ReturnsRegisters()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：ByteCount(2B=0x0006) + FIFOCount(2B=0x0002) + 2个寄存器值
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFifoQueue, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-06-00-02-12-34-56-78".ToHex());

        var rs = await mb.Object.ReadFifoQueueAsync(1, 0x0100);
        Assert.Equal(2, rs.Length);
        Assert.Equal((UInt16)0x1234, rs[0]);
        Assert.Equal((UInt16)0x5678, rs[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC24 响应为null返回空数组")]
    public async Task ReadFifoQueue_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFifoQueue, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket?)null);

        var rs = await mb.Object.ReadFifoQueueAsync(1, 0x0100);
        Assert.Empty(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC24 空FIFO队列返回空数组")]
    public async Task ReadFifoQueue_EmptyFifo_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // ByteCount=0x0002, FIFOCount=0x0000, 无寄存器值
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFifoQueue, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-02-00-00".ToHex());

        var rs = await mb.Object.ReadFifoQueueAsync(1, 0x0100);
        Assert.Empty(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC24 请求负载仅含FIFO指针地址2字节")]
    public async Task ReadFifoQueue_RequestPayloadFormat()
    {
        IPacket? capturedPayload = null;
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFifoQueue, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<FunctionCodes, Byte, IPacket, CancellationToken>((code, host, payload, ct) => capturedPayload = payload)
            .ReturnsAsync((ArrayPacket)"00-04-00-01-AB-CD".ToHex());

        await mb.Object.ReadFifoQueueAsync(1, 0x0200);

        Assert.NotNull(capturedPayload);
        Assert.Equal(2, capturedPayload!.Total);
        var fifoAddr = capturedPayload.ReadBytes(0, 2).ToUInt16(0, false);
        Assert.Equal((UInt16)0x0200, fifoAddr);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC24 读取单个FIFO值")]
    public async Task ReadFifoQueue_SingleValue()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFifoQueue, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-04-00-01-FF-FF".ToHex());

        var rs = await mb.Object.ReadFifoQueueAsync(1, 0x0300);
        Assert.Single(rs);
        Assert.Equal((UInt16)0xFFFF, rs[0]);
    }
}
