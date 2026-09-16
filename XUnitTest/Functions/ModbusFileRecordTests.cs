using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>FC21 WriteFileRecord 功能码单元测试</summary>
public class ModbusFileRecordTests
{
    [Fact]
    [System.ComponentModel.DisplayName("FC21 正常响应返回写入数量")]
    public async Task WriteFileRecord_ValidResponse_ReturnsCount()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        // 从机回显请求体
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.WriteFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"09-06-00-01-00-00-00-02-00-01-00-02".ToHex());

        var rs = await mb.Object.WriteFileRecordAsync(1, 1, 0, [0x0001, 0x0002]);
        Assert.Equal(2, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC21 响应为null返回-1")]
    public async Task WriteFileRecord_NullResponse_ReturnsMinusOne()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.WriteFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IPacket)null);

        var rs = await mb.Object.WriteFileRecordAsync(1, 1, 0, [0x1234]);
        Assert.Equal(-1, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC21 写入单个寄存器")]
    public async Task WriteFileRecord_SingleRegister()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.WriteFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"07-06-00-01-00-00-00-01-FF-FF".ToHex());

        var rs = await mb.Object.WriteFileRecordAsync(1, 1, 0, [0xFFFF]);
        Assert.Equal(1, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC21 不同文件号和记录号")]
    public async Task WriteFileRecord_DifferentFileAndRecord()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.WriteFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"07-06-00-02-00-05-00-01-12-34".ToHex());

        var rs = await mb.Object.WriteFileRecordAsync(1, 2, 5, [0x1234]);
        Assert.Equal(1, rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC21 FunctionCodes枚举值为0x15")]
    public void WriteFileRecord_FunctionCodeValue()
    {
        Assert.Equal(0x15, (Byte)FunctionCodes.WriteFileRecord);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC21 发送内容中包含正确的ByteCount")]
    public async Task WriteFileRecord_RequestByteCountIsCorrect()
    {
        IPacket? capturedPayload = null;
        var mb = new Mock<Modbus>() { CallBase = true };
        mb.Setup(e => e.SendCommandAsync(FunctionCodes.WriteFileRecord, 1, It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<FunctionCodes, Byte, IPacket, CancellationToken>((code, host, payload, ct) => capturedPayload = payload)
            .ReturnsAsync((ArrayPacket)"07-06-00-01-00-00-00-01-AB-CD".ToHex());

        await mb.Object.WriteFileRecordAsync(1, 1, 0, [0xABCD]);

        Assert.NotNull(capturedPayload);
        // ByteCount = 7 = 1(ReferenceType) + 2(FileNo) + 2(RecordNo) + 2(RecordLen) + 1*2(Data)
        var firstByte = capturedPayload!.GetSpan()[0];
        Assert.Equal((Byte)9, firstByte); // 1+2+2+2+2 = 9 for 1 register
    }
}
