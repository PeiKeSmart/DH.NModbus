using System;
using System.ComponentModel;
using System.Linq;
using NewLife;
using NewLife.Data;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Slaves;

public class ModbusSessionTests
{
    private sealed class TestableModbusSession : ModbusSession
    {
        public IPacket? LastSentPacket { get; private set; }
        public Int32 SendCount { get; private set; }
        public void SetSlave(ModbusSlave slave) => _slave = slave;
        internal protected override void SendResponse(IPacket packet) { LastSentPacket = packet; SendCount++; }
        public void Receive(Byte host, FunctionCodes code, Byte[]? payload = null)
        {
            var msg = ModbusIpMessage.Read(MakePacket(host, code, payload).GetSpan());
            if (msg == null) return;

            // 站号过滤 + 多从站路由（模拟 OnReceive 行为）
            var slaveHost = _slave.Host;
            var isBroadcast = msg.Host == 0;
            if (!isBroadcast && _slave.SlaveInstances.TryGetValue(msg.Host, out var inst))
                _slave = inst;

            if (!isBroadcast && slaveHost != 0 && msg.Host != slaveHost)
                return; // 站号不匹配，静默丢弃

            ProcessMessage(msg, isBroadcast);
        }
    }

    private static IPacket MakePacket(Byte host, FunctionCodes code, Byte[]? payload, UInt16 txId = 1, UInt16 protoId = 0)
    {
        payload ??= [];
        // MBAP(7) + UnitId(1) + Code(1) + Payload(N) = 8 + N
        var totalLen = (UInt16)(1 + 1 + payload.Length);
        var buf = new Byte[8 + payload.Length];
        buf[0] = (Byte)(txId >> 8); buf[1] = (Byte)(txId & 0xFF);
        buf[2] = (Byte)(protoId >> 8); buf[3] = (Byte)(protoId & 0xFF);
        buf[4] = (Byte)(totalLen >> 8); buf[5] = (Byte)(totalLen & 0xFF);
        buf[6] = host; buf[7] = (Byte)code;
        if (payload.Length > 0) Array.Copy(payload, 0, buf, 8, payload.Length);
        return new ArrayPacket(buf);
    }

    private static ModbusSlave CreateSlave(Int32 regCount = 10, Int32 coilCount = 16, UInt16 regBase = 100, Byte coilPattern = 0xAA) => new()
    {
        Host = 1,
        Registers = Enumerable.Range(0, regCount).Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(regBase + i) }).ToList(),
        Coils = Enumerable.Range(0, coilCount).Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)((coilPattern >> (i % 8)) & 1) }).ToList(),
    };

    private static ModbusMessage? ParseResponse(IPacket? p) => p == null ? null : ModbusIpMessage.Read(p.GetSpan(), true);

    private static void W16(Byte[] b, Int32 o, UInt16 v) { b[o] = (Byte)(v >> 8); b[o + 1] = (Byte)(v & 0xFF); }

    // ── FC01 ───────────────────────────────────────────────────────────────

    [Fact] [DisplayName("FC01 ReadCoil 正常读取")]
    public void FC01_ReadCoil_Normal()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 0); W16(p, 2, 8);
        ses.Receive(1, FunctionCodes.ReadCoil, p);
        Assert.Equal(1, ses.SendCount);
        var rs = ParseResponse(ses.LastSentPacket);
        Assert.NotNull(rs?.Payload);
    }

    [Fact] [DisplayName("FC01 ReadCoil 越界")]
    public void FC01_ReadCoil_OutOfRange()
    {
        var s = CreateSlave(coilCount: 8); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 100); W16(p, 2, 1);
        ses.Receive(1, FunctionCodes.ReadCoil, p);
        Assert.Null(ParseResponse(ses.LastSentPacket)?.Payload);
    }

    [Fact] [DisplayName("FC01 ReadCoil 无线圈")]
    public void FC01_ReadCoil_NoCoils()
    {
        var s = new ModbusSlave { Host = 1, Coils = null! }; var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 0); W16(p, 2, 1);
        ses.Receive(1, FunctionCodes.ReadCoil, p);
        Assert.Null(ParseResponse(ses.LastSentPacket)?.Payload);
    }

    // ── FC02 / FC03 / FC04 ─────────────────────────────────────────────────

    [Fact] [DisplayName("FC02 ReadDiscrete")]
    public void FC02_ReadDiscrete()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.ReadDiscrete, [0, 0, 0, 8]);
        Assert.Equal(1, ses.SendCount);
    }

    [Fact] [DisplayName("FC03 ReadRegister")]
    public void FC03_ReadRegister()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 0); W16(p, 2, 5);
        ses.Receive(1, FunctionCodes.ReadRegister, p);
        var rs = ParseResponse(ses.LastSentPacket);
        Assert.NotNull(rs?.Payload);
        Assert.Equal(11, rs.Payload.Total);
        Assert.Equal((UInt16)100, rs.Payload.ReadBytes(1, 2).ToUInt16(0, false));
    }

    [Fact] [DisplayName("FC03 ReadRegister 越界")]
    public void FC03_ReadRegister_OutOfRange()
    {
        var s = CreateSlave(regCount: 5); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 100); W16(p, 2, 1);
        ses.Receive(1, FunctionCodes.ReadRegister, p);
        Assert.Null(ParseResponse(ses.LastSentPacket)?.Payload);
    }

    [Fact] [DisplayName("FC03 无寄存器")]
    public void FC03_ReadRegister_NoRegs()
    {
        var s = new ModbusSlave { Host = 1, Registers = null! }; var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 0); W16(p, 2, 1);
        ses.Receive(1, FunctionCodes.ReadRegister, p);
        Assert.Null(ParseResponse(ses.LastSentPacket)?.Payload);
    }

    [Fact] [DisplayName("FC04 ReadInput")]
    public void FC04_ReadInput()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.ReadInput, [0, 0, 0, 3]);
        Assert.Equal(1, ses.SendCount);
    }

    // ── FC05 / FC06 ────────────────────────────────────────────────────────

    [Fact] [DisplayName("FC05 WriteCoil")]
    public void FC05_WriteCoil()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 0); W16(p, 2, 0xFF00);
        ses.Receive(1, FunctionCodes.WriteCoil, p);
        Assert.Equal(1, s.Coils[0].Value);
    }

    [Fact] [DisplayName("FC05 WriteCoil OFF")]
    public void FC05_WriteCoil_OFF()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 1); W16(p, 2, 0x0000);
        ses.Receive(1, FunctionCodes.WriteCoil, p);
        Assert.Equal(0, s.Coils[1].Value);
    }

    [Fact] [DisplayName("FC05 short payload")]
    public void FC05_WriteCoil_Short()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.WriteCoil, [0, 0]);
        Assert.Null(ParseResponse(ses.LastSentPacket)?.Payload);
    }

    [Fact] [DisplayName("FC06 WriteRegister")]
    public void FC06_WriteRegister()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[4]; W16(p, 0, 3); W16(p, 2, 0xABCD);
        ses.Receive(1, FunctionCodes.WriteRegister, p);
        Assert.Equal((UInt16)0xABCD, s.Registers[3].Value);
    }

    // ── FC15 / FC16 ────────────────────────────────────────────────────────

    [Fact] [DisplayName("FC15 WriteCoils")]
    public void FC15_WriteCoils()
    {
        var s = CreateSlave(coilCount: 16); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var d = new Byte[] { 0b10101010 };
        var p = new Byte[5 + d.Length]; W16(p, 0, 0); W16(p, 2, 8); p[4] = (Byte)d.Length;
        Array.Copy(d, 0, p, 5, d.Length);
        ses.Receive(1, FunctionCodes.WriteCoils, p);
        Assert.Equal(1, s.Coils[1].Value); Assert.Equal(0, s.Coils[2].Value);
    }

    [Fact] [DisplayName("FC15 short payload")]
    public void FC15_WriteCoils_Short()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.WriteCoils, [0, 0, 0, 1]);
        Assert.Null(ParseResponse(ses.LastSentPacket)?.Payload);
    }

    [Fact] [DisplayName("FC16 WriteRegisters")]
    public void FC16_WriteRegisters()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var vals = new UInt16[] { 0x1234, 0x5678, 0x9ABC };
        var d = new Byte[vals.Length * 2]; for (var i = 0; i < vals.Length; i++) W16(d, i * 2, vals[i]);
        var p = new Byte[5 + d.Length]; W16(p, 0, 5); W16(p, 2, 3); p[4] = (Byte)d.Length;
        Array.Copy(d, 0, p, 5, d.Length);
        ses.Receive(1, FunctionCodes.WriteRegisters, p);
        Assert.Equal((UInt16)0x1234, s.Registers[5].Value);
        Assert.Equal((UInt16)0x9ABC, s.Registers[7].Value);
    }

    // ── FC07 / FC11 / FC12 / FC17 ──────────────────────────────────────────

    [Fact] [DisplayName("FC07 ReadExceptionStatus")]
    public void FC07_ReadExceptionStatus()
    {
        var s = CreateSlave(); s.ExceptionStatus = 0xAB; var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.ReadExceptionStatus);
        var pk = ParseResponse(ses.LastSentPacket)?.Payload;
        Assert.NotNull(pk);
        var d0 = pk.ReadBytes(0, 1); Assert.Equal(0xAB, d0[0]);
    }

    [Fact] [DisplayName("FC11 GetComEventCounter")]
    public void FC11_GetComEventCounter()
    {
        var s = CreateSlave(); s.ComEventStatus = 0x0001; s.ComEventCount = 42; var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.GetComEventCounter);
        var pk = ParseResponse(ses.LastSentPacket)?.Payload;
        Assert.NotNull(pk);
        Assert.Equal((UInt16)0x0001, pk.ReadBytes(0, 2).ToUInt16(0, false));
        Assert.Equal((UInt16)42, pk.ReadBytes(2, 2).ToUInt16(0, false));
    }

    [Fact] [DisplayName("FC12 GetComEventLog")]
    public void FC12_GetComEventLog()
    {
        var s = CreateSlave(); s.ComEventStatus = 0x0002; s.ComEventCount = 10; s.ComEventLog = [0x01, 0x02, 0x03];
        var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.GetComEventLog);
        Assert.Equal(10, ParseResponse(ses.LastSentPacket)?.Payload?.Total);
    }

    [Fact] [DisplayName("FC17 ReportServerId")]
    public void FC17_ReportServerId()
    {
        var s = CreateSlave(); s.ServerId = 0x05; s.RunIndicator = true; var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.ReportServerId);
        var pk = ParseResponse(ses.LastSentPacket)?.Payload;
        Assert.NotNull(pk);
        Assert.Equal(0x05, pk.ReadBytes(1, 1)[0]); Assert.Equal(0xFF, pk.ReadBytes(2, 1)[0]);
    }

    [Fact] [DisplayName("FC17 RunIndicatorOff")]
    public void FC17_ReportServerId_RunIndicatorOff()
    {
        var s = CreateSlave(); s.RunIndicator = false; var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.ReportServerId);
        var v = ParseResponse(ses.LastSentPacket)?.Payload?.ReadBytes(2, 1); Assert.NotNull(v); Assert.Equal(0x00, v[0]);
    }

    // ── FC22 / FC23 / FC43 ────────────────────────────────────────────────

    [Fact] [DisplayName("FC22 MaskWriteRegister")]
    public void FC22_MaskWriteRegister()
    {
        var s = CreateSlave(); s.Registers[3].Value = 0x0067; var ses = new TestableModbusSession(); ses.SetSlave(s);
        var p = new Byte[6]; W16(p, 0, 3); W16(p, 2, 0xFFFE); W16(p, 4, 0);
        ses.Receive(1, FunctionCodes.MaskWriteRegister, p);
        Assert.Equal((UInt16)0x0066, s.Registers[3].Value);
    }

    [Fact] [DisplayName("FC23 ReadWriteMultipleRegisters")]
    public void FC23_ReadWriteMultipleRegisters()
    {
        var s = CreateSlave(regCount: 20); var ses = new TestableModbusSession(); ses.SetSlave(s);
        var wv = new UInt16[] { 0x1111, 0x2222 }; var wd = new Byte[wv.Length * 2];
        for (var i = 0; i < wv.Length; i++) W16(wd, i * 2, wv[i]);
        var p = new Byte[9 + wd.Length];
        W16(p, 0, 0); W16(p, 2, 3); W16(p, 4, 10); W16(p, 6, 2);
        p[8] = (Byte)wd.Length; Array.Copy(wd, 0, p, 9, wd.Length);
        ses.Receive(1, FunctionCodes.ReadWriteMultipleRegisters, p);
        Assert.Equal((UInt16)0x1111, s.Registers[10].Value);
        var rs = ParseResponse(ses.LastSentPacket);
        Assert.NotNull(rs?.Payload);
        Assert.Equal((UInt16)100, rs.Payload.ReadBytes(1, 2).ToUInt16(0, false));
    }

    [Fact] [DisplayName("FC43 ReadDevId")]
    public void FC43_ReadDevId()
    {
        var s = CreateSlave();
        s.DeviceInfo = new ModbusDeviceInfo { VendorName = "TV", ProductCode = "TP", Revision = "2.0" };
        var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.ReadDevId, [0x0E, 0x01, 0x00]);
        var pk = ParseResponse(ses.LastSentPacket)?.Payload;
        Assert.NotNull(pk);
        Assert.Equal(0x0E, pk.ReadBytes(0, 1)[0]);
        Assert.Equal(3, pk.ReadBytes(5, 1)[0]);
    }

    // ── Custom / Broadcast / Edge ─────────────────────────────────────────

    [Fact] [DisplayName("CustomFunctionHandler")]
    public void CustomFunctionHandler_Normal()
    {
        var s = CreateSlave(); s.RegisterFunctionHandler(65, _ => new ArrayPacket([0xCA, 0xFE]));
        var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, (FunctionCodes)65);
        var v = ParseResponse(ses.LastSentPacket)?.Payload?.ReadBytes(0, 1); Assert.NotNull(v); Assert.Equal(0xCA, v[0]);
    }

    [Fact] [DisplayName("CustomFunctionHandler Exception EC04")]
    public void CustomFunctionHandler_Exception_EC04()
    {
        var s = CreateSlave(); s.RegisterFunctionHandler(66, _ => throw new InvalidOperationException());
        var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, (FunctionCodes)66);
        var rs = ParseResponse(ses.LastSentPacket);
        Assert.NotNull(rs);
        Assert.Equal((FunctionCodes)66, rs.Code);
        Assert.Equal(ErrorCodes.SlaveDeviceFailure, rs.ErrorCode);
    }

    [Fact] [DisplayName("Broadcast write no response")]
    public void Broadcast_Write()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(0, FunctionCodes.WriteRegister, [0, 3, 0xAB, 0xCD]);
        Assert.Equal((UInt16)0xABCD, s.Registers[3].Value);
        Assert.Equal(0, ses.SendCount);
    }

    [Fact] [DisplayName("Broadcast read ignored")]
    public void Broadcast_Read()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(0, FunctionCodes.ReadRegister, [0, 0, 0, 5]);
        Assert.Equal(0, ses.SendCount);
    }

    [Fact] [DisplayName("Unknown FC EC01")]
    public void UnknownFC()
    {
        var s = CreateSlave(); var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, (FunctionCodes)25);
        var rs = ParseResponse(ses.LastSentPacket);
        Assert.NotNull(rs);
        Assert.Equal((FunctionCodes)25, rs.Code);
        Assert.Equal(ErrorCodes.IllegalFunction, rs.ErrorCode);
    }

    [Fact] [DisplayName("Host mismatch silently dropped")]
    public void HostMismatch()
    {
        var s = CreateSlave(); s.Host = 2; var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.ReadRegister, [0, 0, 0, 1]);
        Assert.Equal(0, ses.SendCount);
    }

    [Fact] [DisplayName("Host 0 accepts all")]
    public void HostZero()
    {
        var s = CreateSlave(); s.Host = 0; var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(5, FunctionCodes.ReadRegister, [0, 0, 0, 1]);
        Assert.Equal(1, ses.SendCount);
    }

    [Fact] [DisplayName("SlaveInstances routing")]
    public void SlaveInstances()
    {
        var main = CreateSlave(regCount: 5); main.Host = 0;
        var inst2 = CreateSlave(regCount: 5, regBase: 200);
        main.SlaveInstances[2] = inst2;
        var ses = new TestableModbusSession(); ses.SetSlave(main);
        ses.Receive(2, FunctionCodes.ReadRegister, [0, 0, 0, 1]);
        Assert.Equal((UInt16)200, ParseResponse(ses.LastSentPacket)?.Payload?.ReadBytes(1, 2).ToUInt16(0, false));
    }

    [Fact] [DisplayName("BeforeRead event")]
    public void BeforeRead()
    {
        var s = CreateSlave(); var fc = FunctionCodes.ReadCoil; UInt16 fa = 0, fn = 0;
        s.BeforeRead += (c, a, n) => { fc = c; fa = a; fn = n; };
        var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.ReadRegister, [0, 3, 0, 2]);
        Assert.Equal(FunctionCodes.ReadRegister, fc);
        Assert.Equal((UInt16)3, fa); Assert.Equal((UInt16)2, fn);
    }

    [Fact] [DisplayName("AfterWrite event")]
    public void AfterWrite()
    {
        var s = CreateSlave(); var fc = FunctionCodes.ReadCoil; UInt16 fa = 0; UInt16[]? fv = null;
        s.AfterWrite += (c, a, v) => { fc = c; fa = a; fv = v; };
        var ses = new TestableModbusSession(); ses.SetSlave(s);
        ses.Receive(1, FunctionCodes.WriteRegister, [0, 4, 0xAB, 0xCD]);
        Assert.Equal(FunctionCodes.WriteRegister, fc);
        Assert.Equal((UInt16)4, fa);
        Assert.Equal((UInt16)0xABCD, fv![0]);
    }
}
