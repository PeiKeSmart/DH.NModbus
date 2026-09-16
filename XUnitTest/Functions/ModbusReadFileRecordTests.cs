using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC20 ReadFileRecord 读文件记录单元测试</summary>
public class ModbusReadFileRecordTests
{
    [Fact]
    [System.ComponentModel.DisplayName("FC20 正常响应返回寄存器数组")]
    public async Task ReadFileRecord_ValidResponse_ReturnsRegisters()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 响应：ByteCount(0x07) + ReferenceType(0x06) + RecordDataLen(0x06) + 3个寄存器
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"07-06-06-00-01-00-02-00-03".ToHex());

        var rs = await mb.Object.ReadFileRecordAsync(1, 1, 0, 3);
        Assert.Equal(3, rs.Length);
        Assert.Equal((UInt16)1, rs[0]);
        Assert.Equal((UInt16)2, rs[1]);
        Assert.Equal((UInt16)3, rs[2]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC20 响应为null返回空数组")]
    public async Task ReadFileRecord_NullResponse_ReturnsEmpty()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket?)null);

        var rs = await mb.Object.ReadFileRecordAsync(1, 1, 0, 2);
        Assert.Empty(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC20 读取单个寄存器")]
    public async Task ReadFileRecord_SingleRegister()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"05-06-02-AB-CD".ToHex());

        var rs = await mb.Object.ReadFileRecordAsync(1, 1, 0, 1);
        Assert.Single(rs);
        Assert.Equal((UInt16)0xABCD, rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC20 请求负载格式：ByteCount+ReferenceType+FileNo+RecordNo+RecordLen")]
    public async Task ReadFileRecord_RequestPayloadFormat()
    {
        IPacket? capturedPayload = null;
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<FunctionCodes, Byte, IPacket, CancellationToken>((code, host, payload, ct) => capturedPayload = payload)
            .ReturnsAsync((ArrayPacket)"05-06-02-00-10".ToHex());

        await mb.Object.ReadFileRecordAsync(1, 2, 5, 1);

        Assert.NotNull(capturedPayload);
        // ByteCount=7 (0x07)
        var byteCount = capturedPayload!.GetSpan()[0];
        Assert.Equal((Byte)7, byteCount);
        // ReferenceType=6
        var refType = capturedPayload.GetSpan()[1];
        Assert.Equal((Byte)6, refType);
        // FileNumber=2
        var fileNo = capturedPayload.ReadBytes(2, 2).ToUInt16(0, false);
        Assert.Equal((UInt16)2, fileNo);
        // RecordNumber=5
        var recNo = capturedPayload.ReadBytes(4, 2).ToUInt16(0, false);
        Assert.Equal((UInt16)5, recNo);
        // RecordLength=1
        var recLen = capturedPayload.ReadBytes(6, 2).ToUInt16(0, false);
        Assert.Equal((UInt16)1, recLen);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC20 不同文件号可正常请求")]
    public async Task ReadFileRecord_DifferentFileNumber()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.ReadFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"05-06-02-12-34".ToHex());

        var rs = await mb.Object.ReadFileRecordAsync(1, 10, 100, 1);
        Assert.Single(rs);
        Assert.Equal((UInt16)0x1234, rs[0]);
    }
}
