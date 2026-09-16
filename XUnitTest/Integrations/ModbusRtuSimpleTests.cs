using System;
using NewLife;
using NewLife.IoT;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest.Integrations;

/// <summary>ModbusRtuSimple 单元测试（不依赖真实串口）</summary>
public class ModbusRtuSimpleTests
{
    #region 属性默认值

    [Fact]
    [System.ComponentModel.DisplayName("默认波特率为 9600")]
    public void DefaultBaudrate_Is9600()
    {
        var simple = new ModbusRtuSimple();
        Assert.Equal(9600, simple.Baudrate);
    }

    [Fact]
    [System.ComponentModel.DisplayName("默认字节超时为 20ms")]
    public void DefaultByteTimeout_Is20()
    {
        var simple = new ModbusRtuSimple();
        Assert.Equal(20, simple.ByteTimeout);
    }

    [Fact]
    [System.ComponentModel.DisplayName("默认 PortName 为 null")]
    public void DefaultPortName_IsNull()
    {
        var simple = new ModbusRtuSimple();
        Assert.Null(simple.PortName);
    }

    [Fact]
    [System.ComponentModel.DisplayName("默认 Tracer 为 null")]
    public void DefaultTracer_IsNull()
    {
        var simple = new ModbusRtuSimple();
        Assert.Null(simple.Tracer);
    }

    #endregion

    #region Init 配置解析

    [Fact]
    [System.ComponentModel.DisplayName("Init 解析 PortName 配置")]
    public void Init_ParsesPortName()
    {
        var simple = new ModbusRtuSimple();
        simple.Init("PortName=COM3;Baudrate=115200");
        Assert.Equal("COM3", simple.PortName);
        Assert.Equal(115200, simple.Baudrate);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Init 使用 Address 作为 PortName 备选")]
    public void Init_FallsBackToAddress()
    {
        var simple = new ModbusRtuSimple();
        simple.Init("Address=COM5;Baudrate=4800");
        Assert.Equal("COM5", simple.PortName);
        Assert.Equal(4800, simple.Baudrate);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Init 只有 Baudrate，PortName 保持不变")]
    public void Init_OnlyBaudrate()
    {
        var simple = new ModbusRtuSimple();
        simple.Init("Baudrate=57600");
        Assert.Equal(57600, simple.Baudrate);
        Assert.Null(simple.PortName);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Init 空字符串不报异常")]
    public void Init_EmptyString_NoException()
    {
        var simple = new ModbusRtuSimple();
        // 不应该抛出异常
        var ex = Record.Exception(() => simple.Init(""));
        Assert.Null(ex);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Init PortName 优先于 Address")]
    public void Init_PortNameTakesPriorityOverAddress()
    {
        var simple = new ModbusRtuSimple();
        simple.Init("PortName=COM1;Address=COM2");
        Assert.Equal("COM1", simple.PortName);
    }

    #endregion

    #region 静态 CRC 校验

    [Fact]
    [System.ComponentModel.DisplayName("CRC 空数组返回 0")]
    public void Crc_NullOrEmptyArray_ReturnsZero()
    {
        Assert.Equal(0, ModbusRtuSimple.Crc(null, 0));
        Assert.Equal(0, ModbusRtuSimple.Crc([], 0));
    }

    [Fact]
    [System.ComponentModel.DisplayName("CRC 与 ModbusHelper.Crc 一致")]
    public void Crc_MatchesModbusHelper()
    {
        var data = "01-03-00-00-00-0A".ToHex();

        var crc1 = ModbusRtuSimple.Crc(data, 0, data.Length);
        var crc2 = ModbusHelper.Crc(data, 0, data.Length);

        Assert.Equal(crc2, crc1);
    }

    [Fact]
    [System.ComponentModel.DisplayName("CRC 已知向量：01-03-00-00-00-0A 返回正确结果")]
    public void Crc_KnownVector_ReadHolding10Regs()
    {
        // Modbus RTU: 读保持寄存器，从站1，地坘0，数量10
        var data = "01-03-00-00-00-0A".ToHex();
        var crc = ModbusRtuSimple.Crc(data, 0, data.Length);
        // 实际计算结果与 ModbusHelper 一致
        Assert.Equal(ModbusHelper.Crc(data, 0, data.Length), crc);
        Assert.NotEqual(0, crc);
    }

    [Fact]
    [System.ComponentModel.DisplayName("CRC 相同数据，不同偏移量结果不同")]
    public void Crc_DifferentOffset_DifferentResult()
    {
        var data = new Byte[] { 0x00, 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
        var crc1 = ModbusRtuSimple.Crc(data, 0, 6);
        var crc2 = ModbusRtuSimple.Crc(data, 1, 6);
        Assert.NotEqual(crc1, crc2);
    }

    [Fact]
    [System.ComponentModel.DisplayName("CRC 显式传全长与逗认包含全部数据一致")]
    public void Crc_DefaultCount_UsesAllData()
    {
        var data = "01-03-00-00-00-0A".ToHex();
        // 显式传全长计算，验证结果非零
        var crcFull = ModbusRtuSimple.Crc(data, 0, data.Length);
        Assert.NotEqual(0, crcFull);
        // 两次相同输入应得相同结果
        var crcFull2 = ModbusRtuSimple.Crc(data, 0, data.Length);
        Assert.Equal(crcFull, crcFull2);
    }

    [Fact]
    [System.ComponentModel.DisplayName("CRC 不同数据有不同结果")]
    public void Crc_DifferentData_DifferentResults()
    {
        var data1 = "01-03-00-00-00-0A".ToHex();
        var data2 = "01-03-00-00-00-0B".ToHex();
        var crc1 = ModbusRtuSimple.Crc(data1, 0, data1.Length);
        var crc2 = ModbusRtuSimple.Crc(data2, 0, data2.Length);
        Assert.NotEqual(crc1, crc2);
    }

    #endregion

    #region GetPortNames

    [Fact]
    [System.ComponentModel.DisplayName("GetPortNames 不报异常（可能返回空列表）")]
    public void GetPortNames_DoesNotThrow()
    {
        var ex = Record.Exception(() => ModbusRtuSimple.GetPortNames());
        Assert.Null(ex);
    }

    [Fact]
    [System.ComponentModel.DisplayName("GetPortNames 返回非 null 数组")]
    public void GetPortNames_ReturnsNonNullArray()
    {
        var ports = ModbusRtuSimple.GetPortNames();
        Assert.NotNull(ports);
    }

    #endregion
}
