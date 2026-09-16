using System;
using System.Collections.Generic;
using System.Linq;
using NewLife;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest.Slaves;

/// <summary>ModbusRtuSlave 单元测试（不依赖真实串口，直接调用 ProcessRequest）</summary>
public class ModbusRtuSlaveTests
{
    #region 辅助方法：构造 RTU 帧

    /// <summary>手动构造含 CRC 的 RTU 帧</summary>
    private static Byte[] BuildRtuFrame(Byte host, FunctionCodes code, params Byte[] payload)
    {
        var data = new List<Byte> { host, (Byte)code };
        data.AddRange(payload);

        var crc = ModbusHelper.Crc(data.ToArray(), 0, data.Count);
        data.Add((Byte)(crc & 0xFF));
        data.Add((Byte)(crc >> 8));
        return data.ToArray();
    }

    /// <summary>构造读请求帧（地址 2B + 数量 2B）</summary>
    private static Byte[] BuildReadFrame(Byte host, FunctionCodes code, UInt16 address, UInt16 count)
    {
        var addrBytes = ((UInt16)address).GetBytes(false);
        var cntBytes = ((UInt16)count).GetBytes(false);
        return BuildRtuFrame(host, code, addrBytes[0], addrBytes[1], cntBytes[0], cntBytes[1]);
    }

    /// <summary>从 RTU 响应帧中提取 Payload（去除 host/code/crc）</summary>
    private static Byte[] ExtractPayload(Byte[] frame)
    {
        // host(1) + code(1) + payload + crc(2)
        if (frame.Length < 4) return [];
        return frame[2..(frame.Length - 2)];
    }

    private static ModbusRtuSlave CreateSlave() => new()
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

    #region CRC 校验

    [Fact]
    [System.ComponentModel.DisplayName("CRC 校验失败，返回 null")]
    public void ProcessRequest_InvalidCrc_ReturnsNull()
    {
        var slave = CreateSlave();
        // 构造一个 CRC 损坏的帧
        var frame = BuildReadFrame(1, FunctionCodes.ReadRegister, 0, 1);
        frame[^1] ^= 0xFF;  // 破坏最后一字节 CRC

        var rs = slave.ProcessRequest(frame);
        Assert.Null(rs);
    }

    [Fact]
    [System.ComponentModel.DisplayName("帧太短（<4字节），返回 null")]
    public void ProcessRequest_TooShort_ReturnsNull()
    {
        var slave = CreateSlave();
        var rs = slave.ProcessRequest([0x01, 0x03]);
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

    #endregion

    #region 站号过滤

    [Fact]
    [System.ComponentModel.DisplayName("非本机站号被丢弃，返回 null")]
    public void ProcessRequest_WrongHost_ReturnsNull()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(2, FunctionCodes.ReadRegister, 0, 1);  // 站号 2，但从机是 1
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
    [System.ComponentModel.DisplayName("广播地址（0），不返回响应（返回 null）")]
    public void ProcessRequest_BroadcastHost_ReturnsNull()
    {
        var slave = CreateSlave();
        var frame = BuildRtuFrame(0, FunctionCodes.WriteRegister,
            0x00, 0x00, 0xFF, 0xFF); // 广播写寄存器0=0xFFFF
        var rs = slave.ProcessRequest(frame);
        Assert.Null(rs);  // 广播不响应

        // 但写操作应已执行
        Assert.Equal(0xFFFF, slave.Registers[0].Value);
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
        Assert.Equal((Byte)FunctionCodes.ReadCoil, rs[1]);

        var payload = ExtractPayload(rs);
        Assert.Equal(1, payload[0]);    // ByteCount = 1
        // Coil[i] = i%2: 偶数 OFF, 奇数 ON → 10101010 = 0xAA
        Assert.Equal(0xAA, payload[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC02 读离散输入，位图正确")]
    public void ProcessRequest_ReadDiscrete_Works()
    {
        var slave = CreateSlave();
        var frame = BuildReadFrame(1, FunctionCodes.ReadDiscrete, 0, 4);

        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);
        Assert.Equal((Byte)FunctionCodes.ReadDiscrete, rs[1]);
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
        Assert.Equal((Byte)FunctionCodes.ReadRegister, rs[1]);

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
        Assert.Equal((Byte)FunctionCodes.ReadInput, rs[1]);
    }

    #endregion

    #region FC05/06 写单个

    [Fact]
    [System.ComponentModel.DisplayName("FC05 写单线圈 ON，线圈更新")]
    public void ProcessRequest_WriteCoil_On_Updates()
    {
        var slave = CreateSlave();
        // 写线圈 0 = ON（0xFF00）
        var frame = BuildRtuFrame(1, FunctionCodes.WriteCoil, 0x00, 0x00, 0xFF, 0x00);
        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);
        Assert.Equal(1, slave.Coils[0].Value);  // 已更新为 ON
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC05 写单线圈 OFF，线圈更新")]
    public void ProcessRequest_WriteCoil_Off_Updates()
    {
        var slave = CreateSlave();
        // 先将线圈1设为 OFF（原来是 ON）
        var frame = BuildRtuFrame(1, FunctionCodes.WriteCoil, 0x00, 0x01, 0x00, 0x00);
        slave.ProcessRequest(frame);
        Assert.Equal(0, slave.Coils[1].Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC06 写单寄存器，寄存器更新")]
    public void ProcessRequest_WriteRegister_Updates()
    {
        var slave = CreateSlave();
        var frame = BuildRtuFrame(1, FunctionCodes.WriteRegister, 0x00, 0x02, 0x12, 0x34);
        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);
        Assert.Equal((UInt16)0x1234, slave.Registers[2].Value);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC06 响应回显请求数据")]
    public void ProcessRequest_WriteRegister_EchoesRequest()
    {
        var slave = CreateSlave();
        var frame = BuildRtuFrame(1, FunctionCodes.WriteRegister, 0x00, 0x00, 0xAB, 0xCD);
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
        var frame = BuildRtuFrame(1, FunctionCodes.WriteCoils,
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
        var frame = BuildRtuFrame(1, FunctionCodes.WriteRegisters,
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
        // FC08 Diagnostics 从机不处理
        var frame = BuildRtuFrame(1, FunctionCodes.Diagnostics, 0x00, 0x00, 0x00, 0x00);
        var rs = slave.ProcessRequest(frame);
        Assert.NotNull(rs);
        // 功能码高位置 1：0x08 | 0x80 = 0x88
        Assert.Equal((Byte)(0x08 | 0x80), rs[1]);
        // 错误码 = 1（EC01 IllegalFunction）
        var payload = ExtractPayload(rs);
        Assert.Equal((Byte)ErrorCodes.IllegalFunction, payload[0]);
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

        var frame = BuildRtuFrame(1, FunctionCodes.WriteRegister, 0x00, 0x03, 0xBE, 0xEF);
        slave.ProcessRequest(frame);

        Assert.Equal(FunctionCodes.WriteRegister, firedCode);
        Assert.NotNull(firedValues);
        Assert.Equal((UInt16)0xBEEF, firedValues[0]);
    }

    #endregion
}
