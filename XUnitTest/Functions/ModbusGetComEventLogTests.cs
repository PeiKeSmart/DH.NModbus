using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC12 GetComEventLog 获取通信事件日志单元测试</summary>
public class ModbusGetComEventLogTests
{
    [Fact]
    [System.ComponentModel.DisplayName("FC12 正常响应返回事件日志")]
    public async Task GetComEventLog_ValidResponse_ReturnsLog()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：ByteCount(0x08) + Status(0x0000) + EventCount(0x0002) + MessageCount(0x0002) + 2个事件字节
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.GetComEventLog, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"08-00-00-00-02-00-02-01-03".ToHex());

        var rs = await mb.Object.GetComEventLogAsync(1);
        Assert.NotNull(rs);
        Assert.Equal((UInt16)0x0000, rs.Value.Status);
        Assert.Equal((UInt16)2, rs.Value.EventCount);
        Assert.Equal((UInt16)2, rs.Value.MessageCount);
        Assert.Equal(2, rs.Value.Events.Length);
        Assert.Equal((Byte)0x01, rs.Value.Events[0]);
        Assert.Equal((Byte)0x03, rs.Value.Events[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC12 响应为null返回null")]
    public async Task GetComEventLog_NullResponse_ReturnsNull()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.GetComEventLog, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket?)null);

        var rs = await mb.Object.GetComEventLogAsync(1);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC12 空事件日志")]
    public async Task GetComEventLog_EmptyLog()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // ByteCount(0x06) + Status(0x0000) + EventCount(0x0000) + MessageCount(0x0000) + 无事件
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.GetComEventLog, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"06-00-00-00-00-00-00".ToHex());

        var rs = await mb.Object.GetComEventLogAsync(1);
        Assert.NotNull(rs);
        Assert.Equal((UInt16)0, rs.Value.EventCount);
        Assert.Equal((UInt16)0, rs.Value.MessageCount);
        Assert.Empty(rs.Value.Events);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC12 请求不含负载数据")]
    public async Task GetComEventLog_NoPayloadInRequest()
    {
        IPacket? capturedPayload = null;
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.GetComEventLog, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<FunctionCodes, Byte, IPacket, CancellationToken>((code, host, payload, ct) => capturedPayload = payload)
            .ReturnsAsync((ArrayPacket)"06-00-00-00-00-00-00".ToHex());

        await mb.Object.GetComEventLogAsync(1);

        Assert.NotNull(capturedPayload);
        Assert.True(capturedPayload!.Total == 0 || capturedPayload.GetSpan().IsEmpty);
    }
}
