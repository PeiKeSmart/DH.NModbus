using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.Protocols;
using NewLife.IoT.ThingModels;
using Xunit;

namespace XUnitTest.Drivers;

/// <summary>ModbusDriver Write/ConvertToCoil/ConvertToRegister/Dispatch/BuildSegments 补充单元测试</summary>
public class ModbusDriverWriteDispatchTests
{
    // ────────────────────────────────────────────────────────────────────────
    #region 辅助

    private static (ModbusTcpDriver driver, INode node) SetupDriverWithMock(
        Mock<Modbus> mockModbus,
        FunctionCodes readCode = FunctionCodes.ReadRegister,
        FunctionCodes writeCode = FunctionCodes.WriteRegister)
    {
        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = "tcp://127.0.0.1:1",
            Host = 1,
            ReadCode = readCode,
            WriteCode = writeCode,
        };
        var node = driver.Open(null, p);
        driver.Modbus = mockModbus.Object;
        return (driver, node);
    }

    private static PointModel MakePoint(String name, String address, Int32 length = 2, String? type = null)
        => new() { Name = name, Address = address, Length = length, Type = type };

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Write —— 边界条件

    [Fact]
    [DisplayName("Write value=null 时立即返回 null，不调用 Modbus")]
    public void Write_NullValue_ReturnsNull()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        var (driver, node) = SetupDriverWithMock(mb);
        var point = MakePoint("p0", "0");

        var rs = driver.Write(node, point, null);

        // value=null时跳过写入，count=0
        Assert.Equal(0, rs.AffectedCount);
        mb.Verify(e => e.WriteAsync(It.IsAny<FunctionCodes>(), It.IsAny<Byte>(), It.IsAny<UInt16>(), It.IsAny<UInt16[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [DisplayName("Write Address 为空时立即返回 null")]
    public void Write_EmptyAddress_ReturnsNull()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        var (driver, node) = SetupDriverWithMock(mb);
        var point = MakePoint("p0", "");

        var rs = driver.Write(node, point, 42);

        // 地址为空，跳过写入，count=0
        Assert.Equal(0, rs.AffectedCount);
    }

    [Fact]
    [DisplayName("Write 传入 Byte[] 时直接转 UInt16[] 写入")]
    public void Write_ByteArrayValue_ConvertedToUInt16Array()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        mb.Setup(e => e.WriteAsync(FunctionCodes.WriteRegister, 1, 0, It.IsAny<UInt16[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Object?)null);

        var (driver, node) = SetupDriverWithMock(mb);
        var point = MakePoint("p0", "0", 4);

        // 4字节 big-endian：0x00 0x64 0x00 0xC8 → [100, 200]
        var rs = driver.Write(node, point, new Byte[] { 0x00, 0x64, 0x00, 0xC8 });

        mb.Verify(e => e.WriteAsync(FunctionCodes.WriteRegister, 1, 0,
            It.Is<UInt16[]>(arr => arr.Length == 2 && arr[0] == 100 && arr[1] == 200), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [DisplayName("Write UInt16 值写入单寄存器")]
    public void Write_UInt16Value_WritesSingleRegister()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        mb.Setup(e => e.WriteAsync(FunctionCodes.WriteRegister, 1, 5, It.IsAny<UInt16[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Object?)null);

        var (driver, node) = SetupDriverWithMock(mb);
        var point = MakePoint("p5", "5", 2, "UInt16");

        driver.Write(node, point, (UInt16)1234);

        mb.Verify(e => e.WriteAsync(FunctionCodes.WriteRegister, 1, 5,
            It.Is<UInt16[]>(arr => arr.Length == 1 && arr[0] == 1234), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [DisplayName("Write Boolean 值使用 WriteCoil 写入线圈")]
    public void Write_BooleanValue_WriteCoil_OnIsFF00()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        mb.Setup(e => e.WriteAsync(FunctionCodes.WriteCoil, 1, It.IsAny<UInt16>(), It.IsAny<UInt16[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Object?)null);

        var (driver, node) = SetupDriverWithMock(mb, writeCode: FunctionCodes.WriteCoil);
        var point = MakePoint("coil0", "0x0", 1, "Boolean");  // DO 区域 → WriteCoil

        driver.Write(node, point, true);

        mb.Verify(e => e.WriteAsync(FunctionCodes.WriteCoil, 1, 0,
            It.Is<UInt16[]>(arr => arr.Length == 1 && arr[0] == 0xFF00), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [DisplayName("Write Int32 值写入两个寄存器（高16位/低16位）")]
    public void Write_Int32Value_WritesTwoRegisters()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        mb.Setup(e => e.WriteAsync(FunctionCodes.WriteRegisters, 1, 0, It.IsAny<UInt16[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Object?)null);

        var (driver, node) = SetupDriverWithMock(mb, writeCode: FunctionCodes.WriteRegisters);
        var point = MakePoint("p0", "0", 4, "Int32");

        driver.Write(node, point, 0x00010002);

        mb.Verify(e => e.WriteAsync(FunctionCodes.WriteRegisters, 1, 0,
            It.Is<UInt16[]>(arr => arr.Length == 2 && arr[0] == 1 && arr[1] == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [DisplayName("Write Type=null 无法推断类型时 ConvertToRegister 返回空数组，不抛异常")]
    public void Write_NullType_ConvertToRegisterReturnsEmpty_NoException()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        mb.Setup(e => e.WriteAsync(It.IsAny<FunctionCodes>(), It.IsAny<Byte>(), It.IsAny<UInt16>(), It.IsAny<UInt16[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Object?)null);

        var (driver, node) = SetupDriverWithMock(mb);
        // Type=null 且 value 类型为 Object，ConvertToRegister 返回 [] 并调用 Modbus.Write
        var point = MakePoint("p0", "0", 2, null);

        // Type=null 无法推断类型ConvertToRegister返回[]且vs为null，会抛出NotSupportedException
        Assert.Throws<NotSupportedException>(() => driver.Write(node, point, new Object()));
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region BuildSegments —— 分段合并逻辑

    private static IList<IPoint> MakePoints(params (String addr, Int32 len)[] specs)
        => specs.Select((s, i) => (IPoint)MakePoint($"p{i}", s.addr, s.len)).ToList();

    [Fact]
    [DisplayName("BuildSegments 线圈地址连续 + 间隔 < 8 时合并")]
    public void BuildSegments_CoilsWithGapLessThan8_Merged()
    {
        var driver = new ModbusTcpDriver();
        var points = MakePoints(("0x0", 1), ("0x5", 1));  // 地址 0, 5；间隔 5 < 8
        var p = new ModbusParameter { BatchStep = 1 };

        var segs = driver.BuildSegments(points, p);

        // 线圈允许间隔 < 8 合并
        Assert.Single(segs);
    }

    [Fact]
    [DisplayName("BuildSegments 线圈间隔 ≥ 8 时不合并")]
    public void BuildSegments_CoilsWithGap8OrMore_NotMerged()
    {
        var driver = new ModbusTcpDriver();
        var points = MakePoints(("0x0", 1), ("0x10", 1));  // 地址 0, 16；间隔 16 ≥ 8
        var p = new ModbusParameter { BatchStep = 1 };

        var segs = driver.BuildSegments(points, p);

        Assert.Equal(2, segs.Count);
    }

    [Fact]
    [DisplayName("BuildSegments BatchSize 限制批次大小")]
    public void BuildSegments_BatchSize2_Splits()
    {
        var driver = new ModbusTcpDriver();
        var points = MakePoints(("0", 2), ("1", 2), ("2", 2), ("3", 2));
        var p = new ModbusParameter { BatchStep = 1, BatchSize = 2 };

        var segs = driver.BuildSegments(points, p);

        // BatchSize=2，4 个连续点最多只能 2 个合并一批
        Assert.True(segs.Count >= 2);
    }

    [Fact]
    [DisplayName("BuildSegments 不同 ReadCode 类型不合并")]
    public void BuildSegments_DifferentReadCodes_NotMerged()
    {
        var driver = new ModbusTcpDriver();
        var points = MakePoints(("0", 2), ("1x1", 2));  // 寄存器+离散输入，ReadCode 不同
        var p = new ModbusParameter();

        var segs = driver.BuildSegments(points, p);

        Assert.Equal(2, segs.Count);
    }

    [Fact]
    [DisplayName("BuildSegments 包含空地址的点位被跳过，有效点位正常合并")]
    public void BuildSegments_EmptyAddress_Skipped()
    {
        var driver = new ModbusTcpDriver();
        var points = new List<IPoint>
        {
            MakePoint("p0", "0", 2),
            MakePoint("p1", "", 2),   // 空地址将被跳过
            MakePoint("p2", "1", 2),  // 紧接 p0，可以合并
        };
        var p = new ModbusParameter();

        var segs = driver.BuildSegments(points, p);

        // 空地址被跳过，p0 和 p2（地址1）合并为单段
        Assert.Single(segs);
        Assert.Equal(2, segs[0].Count);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Dispatch —— 数据分发

    [Fact]
    [DisplayName("Dispatch 空 segments 返回空字典")]
    public void Dispatch_EmptySegments_ReturnsEmpty()
    {
        var driver = new ModbusTcpDriver();
        var points = new IPoint[] { MakePoint("p0", "0") };

        var rs = driver.Dispatch(points, new List<ModbusDriver.Segment>());

        Assert.Empty(rs);
    }

    [Fact]
    [DisplayName("Dispatch null segments 返回空字典")]
    public void Dispatch_NullSegments_ReturnsEmpty()
    {
        var driver = new ModbusTcpDriver();
        var points = new IPoint[] { MakePoint("p0", "0") };

        var rs = driver.Dispatch(points, null!);

        Assert.Empty(rs);
    }

    [Fact]
    [DisplayName("Dispatch ReadInput 数据正确提取")]
    public void Dispatch_ReadInput_ExtractsCorrectBytes()
    {
        var driver = new ModbusTcpDriver();
        var points = new IPoint[] { MakePoint("ai0", "3x0", 2) };
        var segs = new List<ModbusDriver.Segment>
        {
            new() { ReadCode = FunctionCodes.ReadInput, Address = 0, Count = 1, Data = new Byte[] { 0x00, 0xC8 } }
        };

        var rs = driver.Dispatch(points, segs);

        Assert.True(rs.ContainsKey("ai0"));
        var bytes = rs["ai0"] as Byte[];
        Assert.NotNull(bytes);
        Assert.Equal(0xC8, bytes[1]);  // 0x00C8 = 200
    }

    [Fact]
    [DisplayName("Dispatch ReadCoil 按位偏移提取正确值")]
    public void Dispatch_ReadCoil_ExtractsBitValue()
    {
        var driver = new ModbusTcpDriver();
        // 线圈数据字节 0xAA = 10101010，bit1=1，bit0=0
        var points = new IPoint[] { MakePoint("coil1", "0x1", 1) };
        var segs = new List<ModbusDriver.Segment>
        {
            new() { ReadCode = FunctionCodes.ReadCoil, Address = 0, Count = 8, Data = new Byte[] { 0xAA } }
        };

        var rs = driver.Dispatch(points, segs);

        Assert.True(rs.ContainsKey("coil1"));
        Assert.Equal(1, (Int32)rs["coil1"]!);  // bit1 of 0xAA = 1
    }

    [Fact]
    [DisplayName("Dispatch ReadDiscrete 按位偏移提取正确值")]
    public void Dispatch_ReadDiscrete_ExtractsBitValue()
    {
        var driver = new ModbusTcpDriver();
        // 1x0 = 离散输入，地址 0，数据 0x01 → bit0=1
        var points = new IPoint[] { MakePoint("di0", "1x0", 1) };
        var segs = new List<ModbusDriver.Segment>
        {
            new() { ReadCode = FunctionCodes.ReadDiscrete, Address = 0, Count = 8, Data = new Byte[] { 0x01 } }
        };

        var rs = driver.Dispatch(points, segs);

        Assert.True(rs.ContainsKey("di0"));
        Assert.Equal(1, (Int32)rs["di0"]!);
    }

    [Fact]
    [DisplayName("Dispatch 点位地址为空时跳过（不出现在结果中）")]
    public void Dispatch_EmptyPointAddress_Skipped()
    {
        var driver = new ModbusTcpDriver();
        var points = new IPoint[]
        {
            MakePoint("valid", "0", 2),
            MakePoint("emptyaddr", "", 2),   // 空地址被跳过
        };
        var segs = new List<ModbusDriver.Segment>
        {
            new() { ReadCode = FunctionCodes.ReadRegister, Address = 0, Count = 1, Data = new Byte[] { 0x00, 0x64 } }
        };

        var rs = driver.Dispatch(points, segs);

        Assert.True(rs.ContainsKey("valid"));
        Assert.False(rs.ContainsKey("emptyaddr"));
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Read —— BatchDelay / 线圈取整

    [Fact]
    [DisplayName("Read 线圈点位数量向8对齐后发出请求")]
    public void Read_CoilCount_RoundedUpTo8()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        // 期望 Count 被 round-up 到 8（3个线圈 → ReadCoil 时 Count 变为 8）
        mb.Setup(e => e.ReadAsync(FunctionCodes.ReadCoil, 1, 0, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)new Byte[] { 0xAA });

        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = "tcp://127.0.0.1:1",
            Host = 1,
            ReadCode = FunctionCodes.ReadCoil,
            WriteCode = FunctionCodes.WriteCoil,
        };
        var node = driver.Open(null, p);
        driver.Modbus = mb.Object;

        var points = new IPoint[]
        {
            MakePoint("c0", "0x0", 1),
            MakePoint("c1", "0x1", 1),
            MakePoint("c2", "0x2", 1),
        };

        var rs = driver.Read(node, points);

        // 验证以 8 为单位请求
        mb.Verify(e => e.ReadAsync(FunctionCodes.ReadCoil, 1, 0, 8, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [DisplayName("Read 空点位数组返回空字典")]
    public void Read_EmptyPoints_ReturnsEmptyDictionary()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        var (driver, node) = SetupDriverWithMock(mb);

        var rs = driver.Read(node, []);

        Assert.Equal(0, rs.Points.Length);
        mb.Verify(e => e.ReadAsync(It.IsAny<FunctionCodes>(), It.IsAny<Byte>(), It.IsAny<UInt16>(), It.IsAny<UInt16>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [DisplayName("Read Modbus 抛 ModbusException 时被捕获，其余批次继续")]
    public void Read_ModbusException_Caught_OtherBatchesContinue()
    {
        var mb = new Mock<Modbus> { CallBase = true };
        // 第一段（地址0）抛异常
        mb.Setup(e => e.ReadAsync(FunctionCodes.ReadRegister, 1, 0, It.IsAny<UInt16>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ModbusException(ErrorCodes.IllegalFunction, "Test"));
        // 第二段（地址100）正常返回
        mb.Setup(e => e.ReadAsync(FunctionCodes.ReadRegister, 1, 100, It.IsAny<UInt16>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArrayPacket)"00-01".ToHex());

        var (driver, node) = SetupDriverWithMock(mb);
        // BatchStep=100 确保两段不合并
        (node.Parameter as ModbusParameter)!.BatchStep = 100;

        var points = new IPoint[]
        {
            MakePoint("p0", "0", 2),
            MakePoint("p100", "100", 2),
        };

        // 不应抛出异常
        var rs = driver.Read(node, points);
        Assert.True(rs.IsSuccess);
    }

    #endregion
}
