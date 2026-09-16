using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NewLife;
using NewLife.Data;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest.Slaves;

/// <summary>ModbusAsciiSlave 单元测试（不依赖真实串口，直接调用 ProcessRequest）</summary>
public class ModbusAsciiSlaveTests
{
    #region 辅助方法：构造 ASCII 帧

    /// <summary>手动构造含 LRC 的 ASCII 帧，输入为二进制 PDU 字节（host+code+payload）</summary>
    private static Byte[] BuildAsciiFrame(Byte host, FunctionCodes code, params Byte[] payload)
    {
        var pdu = new Byte[2 + payload.Length];
        pdu[0] = host;
        pdu[1] = (Byte)code;
        Array.Copy(payload, 0, pdu, 2, payload.Length);

        var lrc = ModbusHelper.Lrc(pdu, 0, pdu.Length);

        var sb = new StringBuilder();
        sb.Append(':');
        foreach (var b in pdu) sb.Append(b.ToString("X2"));
        sb.Append(lrc.ToString("X2"));
        sb.Append("\r\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>构造读请求 ASCII 帧（地址 2B + 数量 2B）</summary>
    private static Byte[] BuildReadFrame(Byte host, FunctionCodes code, UInt16 address, UInt16 count)
    {
        var addrHi = (Byte)(address >> 8);
        var addrLo = (Byte)(address & 0xFF);
        var cntHi = (Byte)(count >> 8);
        var cntLo = (Byte)(count & 0xFF);
        return BuildAsciiFrame(host, code, addrHi, addrLo, cntHi, cntLo);
    }

    /// <summary>从 ASCII 响应字节中解析消息（含 LRC 校验）</summary>
    private static ModbusAsciiMessage? ParseResponse(Byte[] response)
        => ModbusAsciiMessage.Read((ReadOnlySpan<Byte>)response, reply: true);

    /// <summary>从 ASCII 响应中提取 Payload 字节</summary>
    private static Byte[] ExtractPayload(Byte[] response)
    {
        var msg = ParseResponse(response);
        if (msg?.Payload == null) return [];
        return msg.Payload.ReadBytes(0, msg.Payload.Total);
    }

    private static ModbusAsciiSlave CreateSlave() => new()
    {
        Host = 1,
        Registers = Enumerable.Range(0, 10)
            .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(100 + i) })
            .ToList(),
        Coils = Enumerable.Range(0, 16)
            .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
            .ToList(),
    };

    #endregion

    #region LRC 校验

    [Fact]
    [System.ComponentModel.DisplayName("LRC 校验失败，返回 null")]
    public void ProcessRequest_InvalidLrc_ReturnsNull()
    {
        var slave = CreateSlave();
        // 构造合法帧后，修改 LRC 字节（倒数第 4、5 个字符是 LRC 的两位十六进制）
        var frame = BuildReadFrame(1, FunctionCodes.ReadRegister, 0, 1);
        // 将 LRC 最后一个十六进制字符反转
        frame[^3] ^= 0x01;

        var rs = slave.ProcessRequest(frame);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("帧太短（<9字节），返回 null")]
    public void ProcessRequest_TooShort_ReturnsNull()
    {
        var slave = CreateSlave();
        var rs = slave.ProcessRequest(Encoding.ASCII.GetBytes(":0103\r\n"));
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("null 帧，返回 null")]
    public void ProcessRequest_Null_ReturnsNull()
    {
        var slave = CreateSlave();
        var rs = slave.ProcessRequest(null!);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("不以冒号开头的帧，返回 null")]
    public void ProcessRequest_NoColon_ReturnsNull()
    {
        var slave = CreateSlave();
        // 合法帧但去掉开头冒号
        var frame = BuildReadFrame(1, FunctionCodes.ReadRegister, 0, 1);
        var modified = new Byte[frame.Length];
        Array.Copy(frame, 1, modified, 0, frame.Length - 1);
        modified[^1] = (Byte)'\n';

        var rs = slave.ProcessRequest(modified);
        Assert.Null(rs);
    }

    #endregion

    #region 站号过滤

    [Fact]
    [System.ComponentModel.DisplayName("非本机站号被丢弃，返回 null")]
    public void ProcessRequest_WrongHost_ReturnsNull()
    {
        var slave = CreateSlave();
        // 站号 2，但从机是 1
        var frame = BuildReadFrame(2, FunctionCodes.ReadRegister, 0, 1);
        var rs = slave.ProcessRequest(frame);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("Host=0 接受任意站号")]
    public void ProcessRequest_HostZero_AcceptsAll()
    {
        var slave = CreateSlave();
        slave.Host = 0;

        var frame = BuildReadFrame(5, FunctionCodes.ReadRegister, 0, 1);
        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("广播地址（0），写操作执行但不返回响应")]
    public void ProcessRequest_BroadcastHost_ExecutesWriteNoResponse()
    {
        var slave = CreateSlave();
        // 广播写寄存器 0 = 0xFFFF
        var frame = BuildAsciiFrame(0, FunctionCodes.WriteRegister, 0x00, 0x00, 0xFF, 0xFF);
        var rs = slave.ProcessRequest(frame);

        // 广播不响应
        Assert.Null(rs);
        // 写操作已执行
        Assert.Equal((UInt16)0xFFFF, slave.Registers[0].Value);
    }

    #endregion

    #region FC01/02 读线圈

    [Fact]
    [System.ComponentModel.DisplayName("FC01 读 8 个线圈，返回正确位图")]
    public void ProcessRequest_ReadCoil_8Coils()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(1, FunctionCodes.ReadCoil, 0, 8);

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        var msg = ParseResponse(rs);
        Assert.NotNull(msg);
        Assert.Equal(FunctionCodes.ReadCoil, msg.Code);

        var payload = ExtractPayload(rs);
        Assert.Equal(1, payload[0]);    // ByteCount = 1
        // Coil[i] = i%2: 偶数 OFF, 奇数 ON → 10101010 = 0xAA
        Assert.Equal(0xAA, payload[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC02 读离散输入，功能码正确")]
    public void ProcessRequest_ReadDiscrete_Works()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(1, FunctionCodes.ReadDiscrete, 0, 4);

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        var msg = ParseResponse(rs);
        Assert.NotNull(msg);
        Assert.Equal(FunctionCodes.ReadDiscrete, msg.Code);
    }

    #endregion

    #region FC03/04 读寄存器

    [Fact]
    [System.ComponentModel.DisplayName("FC03 读保持寄存器，值正确")]
    public void ProcessRequest_ReadRegister_Values()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(1, FunctionCodes.ReadRegister, 0, 3);

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        var msg = ParseResponse(rs);
        Assert.NotNull(msg);
        Assert.Equal(FunctionCodes.ReadRegister, msg.Code);

        var payload = ExtractPayload(rs);
        Assert.Equal(6, payload[0]);    // ByteCount = 3*2
        Assert.Equal(0x00, payload[1]);
        Assert.Equal(100, payload[2]); // 寄存器0 = 100 = 0x0064
        Assert.Equal(0x00, payload[3]);
        Assert.Equal(101, payload[4]); // 寄存器1 = 101
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC04 读输入寄存器，功能码正确")]
    public void ProcessRequest_ReadInput_FunctionCode()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(1, FunctionCodes.ReadInput, 0, 1);

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        var msg = ParseResponse(rs);
        Assert.NotNull(msg);
        Assert.Equal(FunctionCodes.ReadInput, msg.Code);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC03 响应站号与请求一致")]
    public void ProcessRequest_ReadRegister_ResponseHostMatches()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(1, FunctionCodes.ReadRegister, 0, 1);

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        var msg = ParseResponse(rs);
        Assert.NotNull(msg);
        Assert.Equal(1, msg.Host);
    }

    #endregion

    #region FC05/06 写单个

    [Fact]
    [System.ComponentModel.DisplayName("FC05 写单线圈 ON，线圈更新")]
    public void ProcessRequest_WriteCoil_On_Updates()
    {
        var slave = CreateSlave();
        // 写线圈 0 = ON（0xFF00）
        var frame = BuildAsciiFrame(1, FunctionCodes.WriteCoil, 0x00, 0x00, 0xFF, 0x00);
        var rs = slave.ProcessRequest(frame);

        Assert.NotNull(rs);
        Assert.Equal(1, slave.Coils[0].Value);  // 已更新为 ON
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC05 写单线圈 OFF，线圈更新")]
    public void ProcessRequest_WriteCoil_Off_Updates()
    {
        var slave = CreateSlave();
        // 线圈1 原来是 ON (i%2=1)，写 OFF（0x0000）
        var frame = BuildAsciiFrame(1, FunctionCodes.WriteCoil, 0x00, 0x01, 0x00, 0x00);
        slave.ProcessRequest(frame);

        Assert.Equal(0, slave.Coils[1].Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC06 写单寄存器，寄存器更新")]
    public void ProcessRequest_WriteRegister_Updates()
    {
        var slave = CreateSlave();
        var frame = BuildAsciiFrame(1, FunctionCodes.WriteRegister, 0x00, 0x02, 0x12, 0x34);
        var rs = slave.ProcessRequest(frame);

        Assert.NotNull(rs);
        Assert.Equal((UInt16)0x1234, slave.Registers[2].Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC06 响应回显请求数据（地址+值）")]
    public void ProcessRequest_WriteRegister_EchoesRequest()
    {
        var slave = CreateSlave();
        var frame = BuildAsciiFrame(1, FunctionCodes.WriteRegister, 0x00, 0x00, 0xAB, 0xCD);
        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        var payload = ExtractPayload(rs);
        // 响应应回显地址+值：0x0000, 0xABCD
        Assert.Equal(0x00, payload[0]);
        Assert.Equal(0x00, payload[1]);
        Assert.Equal(0xAB, payload[2]);
        Assert.Equal(0xCD, payload[3]);
    }

    #endregion

    #region FC15/16 写多个

    [Fact]
    [System.ComponentModel.DisplayName("FC15 写多线圈，线圈状态正确")]
    public void ProcessRequest_WriteCoils_SetsValues()
    {
        var slave = CreateSlave();
        // 写线圈 0-7：值 = 0b10101010 = 0xAA
        var frame = BuildAsciiFrame(1, FunctionCodes.WriteCoils,
            0x00, 0x00,  // 地址
            0x00, 0x08,  // 数量 8
            0x01,        // ByteCount = 1
            0xAA);       // 位图 10101010

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        for (var i = 0; i < 8; i++)
            Assert.Equal((Byte)((0xAA >> i) & 1), slave.Coils[i].Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC16 写多寄存器，寄存器更新")]
    public void ProcessRequest_WriteRegisters_SetsValues()
    {
        var slave = CreateSlave();
        // 写寄存器 0-1：[0x1111, 0x2222]
        var frame = BuildAsciiFrame(1, FunctionCodes.WriteRegisters,
            0x00, 0x00,  // 地址
            0x00, 0x02,  // 数量 2
            0x04,        // ByteCount = 4
            0x11, 0x11,  // 寄存器0 = 0x1111
            0x22, 0x22); // 寄存器1 = 0x2222

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);
        Assert.Equal((UInt16)0x1111, slave.Registers[0].Value);
        Assert.Equal((UInt16)0x2222, slave.Registers[1].Value);
    }

    #endregion

    #region 非法功能码

    [Fact]
    [System.ComponentModel.DisplayName("不支持的功能码，返回 EC01 异常响应帧")]
    public void ProcessRequest_UnsupportedCode_ReturnsEC01()
    {
        var slave = CreateSlave();
        // FC08 Diagnostics 从机 ASCII Slave 不支持
        var frame = BuildAsciiFrame(1, FunctionCodes.Diagnostics, 0x00, 0x00, 0x00, 0x00);
        var rs = slave.ProcessRequest(frame);

        Assert.NotNull(rs);

        // ModbusMessage.Read 解析时会将 Code = code & 0x7F，错误码单独放在 ErrorCode
        var msg = ParseResponse(rs);
        Assert.NotNull(msg);
        Assert.Equal(FunctionCodes.Diagnostics, msg.Code);
        Assert.Equal(ErrorCodes.IllegalFunction, msg.ErrorCode);
    }

    #endregion

    #region 响应帧格式验证

    [Fact]
    [System.ComponentModel.DisplayName("响应帧以冒号开头、CRLF 结尾")]
    public void ProcessRequest_ResponseFrame_AsciiFormat()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(1, FunctionCodes.ReadRegister, 0, 1);

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        var text = Encoding.ASCII.GetString(rs);
        Assert.StartsWith(":", text);
        Assert.EndsWith("\r\n", text);
    }

    [Fact]
    [System.ComponentModel.DisplayName("响应帧 LRC 校验通过")]
    public void ProcessRequest_ResponseLrc_Valid()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(1, FunctionCodes.ReadRegister, 0, 2);

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);

        // 使用 ModbusAsciiMessage.Read 解析，内部会校验 LRC
        var msg = ParseResponse(rs);
        Assert.NotNull(msg);
        Assert.Equal(msg.Lrc, msg.Lrc2);
    }

    #endregion

    #region 回调事件

    [Fact]
    [System.ComponentModel.DisplayName("BeforeRead 在读取前触发")]
    public void ProcessRequest_BeforeRead_EventFired()
    {
        var slave = CreateSlave();
        FunctionCodes? firedCode = null;
        UInt16? firedAddr = null;
        slave.BeforeRead += (code, addr, cnt) => { firedCode = code; firedAddr = addr; };

        var frame = BuildReadFrame(1, FunctionCodes.ReadRegister, 5, 2);
        slave.ProcessRequest(frame);

        Assert.Equal(FunctionCodes.ReadRegister, firedCode);
        Assert.Equal((UInt16)5, firedAddr);
    }

    [Fact]
    [System.ComponentModel.DisplayName("AfterWrite 在写入后触发")]
    public void ProcessRequest_AfterWrite_EventFired()
    {
        var slave = CreateSlave();
        FunctionCodes? firedCode = null;
        UInt16[]? firedValues = null;
        slave.AfterWrite += (code, addr, vals) => { firedCode = code; firedValues = vals; };

        var frame = BuildAsciiFrame(1, FunctionCodes.WriteRegister, 0x00, 0x03, 0xBE, 0xEF);
        slave.ProcessRequest(frame);

        Assert.Equal(FunctionCodes.WriteRegister, firedCode);
        Assert.NotNull(firedValues);
        Assert.Equal((UInt16)0xBEEF, firedValues[0]);
    }

    #endregion
}
