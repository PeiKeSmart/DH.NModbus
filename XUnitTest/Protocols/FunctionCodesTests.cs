using System;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Protocols;

/// <summary>FunctionCodes枚举单元测试</summary>
public class FunctionCodesTests
{
    [Fact]
    public void ReadCoil_Value()
    {
        Assert.Equal(1, (Byte)FunctionCodes.ReadCoil);
    }

    [Fact]
    public void ReadDiscrete_Value()
    {
        Assert.Equal(2, (Byte)FunctionCodes.ReadDiscrete);
    }

    [Fact]
    public void ReadRegister_Value()
    {
        Assert.Equal(3, (Byte)FunctionCodes.ReadRegister);
    }

    [Fact]
    public void ReadInput_Value()
    {
        Assert.Equal(4, (Byte)FunctionCodes.ReadInput);
    }

    [Fact]
    public void WriteCoil_Value()
    {
        Assert.Equal(5, (Byte)FunctionCodes.WriteCoil);
    }

    [Fact]
    public void WriteRegister_Value()
    {
        Assert.Equal(6, (Byte)FunctionCodes.WriteRegister);
    }

    [Fact]
    public void Diagnostics_Value()
    {
        Assert.Equal(8, (Byte)FunctionCodes.Diagnostics);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC07 ReadExceptionStatus 枚举值为0x07")]
    public void ReadExceptionStatus_Value()
    {
        Assert.Equal(7, (Byte)FunctionCodes.ReadExceptionStatus);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC11 GetComEventCounter 枚举值为0x0B")]
    public void GetComEventCounter_Value()
    {
        Assert.Equal(11, (Byte)FunctionCodes.GetComEventCounter);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC12 GetComEventLog 枚举值为0x0C")]
    public void GetComEventLog_Value()
    {
        Assert.Equal(12, (Byte)FunctionCodes.GetComEventLog);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC17 ReportServerId 枚举值为0x11")]
    public void ReportServerId_Value()
    {
        Assert.Equal(17, (Byte)FunctionCodes.ReportServerId);
    }

    [Fact]
    public void WriteCoils_Value()
    {
        Assert.Equal(15, (Byte)FunctionCodes.WriteCoils);
    }

    [Fact]
    public void WriteRegisters_Value()
    {
        Assert.Equal(16, (Byte)FunctionCodes.WriteRegisters);
    }

    [Fact]
    public void WriteFileRecord_Value()
    {
        Assert.Equal(21, (Byte)FunctionCodes.WriteFileRecord);
    }

    [Fact]
    public void ReadWriteMultipleRegisters_Value()
    {
        Assert.Equal(23, (Byte)FunctionCodes.ReadWriteMultipleRegisters);
    }

    [Fact]
    public void ReadDevId_Value()
    {
        Assert.Equal(43, (Byte)FunctionCodes.ReadDevId);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC20 ReadFileRecord 枚举值为0x14")]
    public void ReadFileRecord_Value()
    {
        Assert.Equal(20, (Byte)FunctionCodes.ReadFileRecord);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC22 MaskWriteRegister 枚举值为0x16")]
    public void MaskWriteRegister_Value()
    {
        Assert.Equal(22, (Byte)FunctionCodes.MaskWriteRegister);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC24 ReadFifoQueue 枚举值为0x18")]
    public void ReadFifoQueue_Value()
    {
        Assert.Equal(24, (Byte)FunctionCodes.ReadFifoQueue);
    }
}
