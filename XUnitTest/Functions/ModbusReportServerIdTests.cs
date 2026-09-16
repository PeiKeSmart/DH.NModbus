using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC17 ReportServerId 报告服务器ID单元测试</summary>
public class ModbusReportServerIdTests
{
    [Fact]
    [System.ComponentModel.DisplayName("FC17 正常响应返回服务器信息")]
    public async Task ReportServerId_ValidResponse_ReturnsInfo()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：ByteCount(0x02) + ServerId(0x01) + RunIndicator(0xFF)
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReportServerId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-01-FF".ToHex());

        var rs = await mb.Object.ReportServerIdAsync(1);
        Assert.NotNull(rs);
        Assert.Equal((Byte)0x01, rs.Value.ServerId);
        Assert.True(rs.Value.RunIndicator);
        Assert.Empty(rs.Value.AdditionalData);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC17 从机未运行返回RunIndicator=false")]
    public async Task ReportServerId_NotRunning_ReturnsFalse()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：ByteCount(0x02) + ServerId(0x02) + RunIndicator(0x00)
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReportServerId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"02-02-00".ToHex());

        var rs = await mb.Object.ReportServerIdAsync(1);
        Assert.NotNull(rs);
        Assert.Equal((Byte)0x02, rs.Value.ServerId);
        Assert.False(rs.Value.RunIndicator);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC17 响应为null返回null")]
    public async Task ReportServerId_NullResponse_ReturnsNull()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReportServerId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket?)null);

        var rs = await mb.Object.ReportServerIdAsync(1);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC17 请求不含负载数据")]
    public async Task ReportServerId_NoPayloadInRequest()
    {
        IPacket? capturedPayload = null;
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReportServerId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<FunctionCodes, Byte, IPacket, CancellationToken>((code, host, payload, ct) => capturedPayload = payload)
            .ReturnsAsync((ArrayPacket)"02-03-FF".ToHex());

        await mb.Object.ReportServerIdAsync(1);

        Assert.NotNull(capturedPayload);
        // 请求不应包含有效负载数据
        Assert.True(capturedPayload!.Total == 0 || capturedPayload.GetSpan().IsEmpty);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC17 带附加数据的响应")]
    public async Task ReportServerId_WithAdditionalData()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：ByteCount(0x05) + ServerId(0x01) + RunIndicator(0xFF) + Additional(3B: 0xAA, 0xBB, 0xCC)
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReportServerId, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"05-01-FF-AA-BB-CC".ToHex());

        var rs = await mb.Object.ReportServerIdAsync(1);
        Assert.NotNull(rs);
        Assert.Equal((Byte)0x01, rs.Value.ServerId);
        Assert.True(rs.Value.RunIndicator);
        Assert.Equal(3, rs.Value.AdditionalData.Length);
        Assert.Equal((Byte)0xAA, rs.Value.AdditionalData[0]);
        Assert.Equal((Byte)0xBB, rs.Value.AdditionalData[1]);
        Assert.Equal((Byte)0xCC, rs.Value.AdditionalData[2]);
    }
}
