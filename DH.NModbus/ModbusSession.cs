using NewLife.Buffers;
using NewLife.Data;
using NewLife.IoT.Protocols;
using NewLife.Net;

namespace NewLife.IoT;

/// <summary>Modbus请求会话</summary>
public class ModbusSession : NetSession<ModbusSlave>
{
    /// <summary>当前请求解析到的从站实例（支持多从站路由）</summary>
    internal ModbusSlave _slave = null!;

    /// <summary>发送响应数据。可被子类重写用于测试拦截</summary>
    internal protected virtual void SendResponse(IPacket packet) => Send(packet);

    /// <summary>
    /// 接收请求
    /// </summary>
    /// <param name="e"></param>
    protected override void OnReceive(ReceivedEventArgs e)
    {
        if (e.Packet == null) return;

        var msg = ModbusIpMessage.Read(e.Packet.GetSpan());
        if (msg == null) return;

        Log?.Debug("<= {0}", msg);

        // 站号过滤：广播地址(0)执行写操作但不响应；非本机站号的请求静默丢弃
        _slave = Host;
        var slaveHost = _slave.Host;
        var isBroadcast = msg.Host == 0;

        // 多从站实例路由：查找指定站号的从站实例
        if (!isBroadcast && _slave.SlaveInstances.TryGetValue(msg.Host, out var instance))
            _slave = instance;

        if (!isBroadcast && slaveHost != 0 && msg.Host != slaveHost)
        {
            base.OnReceive(e);
            return;
        }

        ProcessMessage(msg, isBroadcast);

        base.OnReceive(e);
    }

    /// <summary>处理已验证站号的 Modbus 消息。用于单元测试</summary>
    internal void ProcessMessage(ModbusMessage msg, Boolean isBroadcast)
    {
        var regs = _slave.Registers;
        var coils = _slave.Coils;
        var rs = msg.CreateReply();

        // 优先匹配自定义功能码处理器
        if (_slave.CustomFunctionHandlers.TryGetValue((Byte)msg.Code, out var customHandler))
        {
            try
            {
                rs.Payload = customHandler(msg);
            }
            catch (Exception ex)
            {
                // 自定义处理器异常时返回 EC04 SlaveDeviceFailure
                Log?.Error("自定义功能码 0x{0:X2} 处理器异常: {1}", (Byte)msg.Code, ex.Message);
                rs.Code = (FunctionCodes)((Byte)msg.Code | 0x80);
                rs.Payload = (ArrayPacket)new Byte[] { (Byte)ErrorCodes.SlaveDeviceFailure };
            }
        }
        else switch (msg.Code)
        {
            case FunctionCodes.ReadCoil:
            case FunctionCodes.ReadDiscrete:
                if (!isBroadcast && coils != null)
                {
                    var (addr, cnt) = msg.GetRequest();
                    _slave.RaiseBeforeRead(msg.Code, addr, cnt);
                    rs.Payload = OnReadCoil(msg);
                }
                break;
            case FunctionCodes.ReadRegister:
            case FunctionCodes.ReadInput:
                if (!isBroadcast && regs != null)
                {
                    var (addr, cnt) = msg.GetRequest();
                    _slave.RaiseBeforeRead(msg.Code, addr, cnt);
                    rs.Payload = OnReadRegister(msg);
                }
                break;
            case FunctionCodes.WriteCoil:
                if (coils != null) rs.Payload = OnWriteCoil(msg);
                break;
            case FunctionCodes.WriteRegister:
                if (regs != null) rs.Payload = OnWriteRegister(msg);
                break;
            case FunctionCodes.WriteCoils:
                if (coils != null) rs.Payload = OnWriteCoils(msg);
                break;
            case FunctionCodes.WriteRegisters:
                if (regs != null) rs.Payload = OnWriteRegisters(msg);
                break;
            case FunctionCodes.ReadWriteMultipleRegisters:
                if (!isBroadcast && regs != null) rs.Payload = OnReadWriteRegisters(msg);
                break;
            case FunctionCodes.ReadDevId:
                if (!isBroadcast) rs.Payload = OnReadDevId(msg);
                break;
            case FunctionCodes.MaskWriteRegister:
                if (regs != null) rs.Payload = OnMaskWriteRegister(msg);
                break;
            case FunctionCodes.ReadExceptionStatus:
                if (!isBroadcast) rs.Payload = OnReadExceptionStatus(msg);
                break;
            case FunctionCodes.ReportServerId:
                if (!isBroadcast) rs.Payload = OnReportServerId(msg);
                break;
            case FunctionCodes.GetComEventCounter:
                if (!isBroadcast) rs.Payload = OnGetComEventCounter(msg);
                break;
            case FunctionCodes.GetComEventLog:
                if (!isBroadcast) rs.Payload = OnGetComEventLog(msg);
                break;
            default:
                // 不支持的功能码：返回标准异常响应（EC01 IllegalFunction）
                rs.Code = (FunctionCodes)((Byte)msg.Code | 0x80);
                rs.Payload = (ArrayPacket)new Byte[] { (Byte)ErrorCodes.IllegalFunction };
                break;
        }

        Log?.Debug("=> {0}", rs);

        // 广播模式下不发送响应
        if (!isBroadcast)
            SendResponse(rs.ToPacket(8192));
    }

    IPacket? OnReadCoil(ModbusMessage msg)
    {
        var coils = _slave.Coils;
        if (coils == null) return null;

        // 连续地址，其实地址有可能不是8的倍数
        var (regAddr, regCount) = msg.GetRequest();
        var addr = regAddr - coils[0].Address;
        if (addr >= 0 && addr + regCount <= coils.Count)
        {
            // 取出该段存储单元
            var cs = coils.Skip(addr).Take(regCount).ToList();
            var count = (Int32)Math.Ceiling(regCount / 8.0);
            // 遍历存储单元，把数据聚合成为字节数组返回
            var rs = new Byte[1 + count];
            rs[0] = (Byte)count;
            for (var i = 0; i < count; i++)
            {
                var b = 0;
                // 每个字节最大可存储8位数据，最后一个字节可能不足8位
                var max = regCount - i * 8;
                if (max > 8) max = 8;
                for (var j = 0; j < max; j++)
                {
                    if (cs[i * 8 + j].Value > 0)
                        b |= 1 << j;
                }
                rs[1 + i] = (Byte)b;
            }

            return (ArrayPacket)rs;
        }

        return null;
    }

    IPacket? OnReadRegister(ModbusMessage msg)
    {
        var regs = _slave.Registers;
        if (regs == null) return null;

        // 连续地址
        var (regAddr, regCount) = msg.GetRequest();
        var addr = regAddr - regs[0].Address;
        if (addr >= 0 && addr + regCount <= regs.Count)
        {
            var buf = regs.Skip(addr).Take(regCount).SelectMany(e => e.GetData()).ToArray();
            // 使用单一连续数组避免链式包序列化丢失后半段数据
            var rs = new Byte[1 + buf.Length];
            rs[0] = (Byte)buf.Length;
            Array.Copy(buf, 0, rs, 1, buf.Length);
            return (ArrayPacket)rs;
        }

        return null;
    }

    IPacket? OnWriteCoil(ModbusMessage msg)
    {
        var coils = _slave.Coils;
        if (coils == null || msg.Payload == null || msg.Payload.Total < 4) return null;

        var reqAddr = msg.GetAddress();
        var value = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);

        var addr = reqAddr - coils[0].Address;
        if (addr >= 0 && addr < coils.Count)
            coils[addr].Value = value == 0xFF00 ? (Byte)1 : (Byte)0;

        _slave.RaiseAfterWrite(msg.Code, reqAddr, [value == 0xFF00 ? (UInt16)1 : (UInt16)0]);

        // 响应回显请求：地址 + 值
        return msg.Payload;
    }

    IPacket? OnWriteRegister(ModbusMessage msg)
    {
        var regs = _slave.Registers;
        if (regs == null || msg.Payload == null || msg.Payload.Total < 4) return null;

        // 地址在前两字节，值在后两字节
        var reqAddr = msg.GetAddress();
        var value = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);

        var addr = reqAddr - regs[0].Address;
        if (addr >= 0 && addr < regs.Count)
            regs[addr].Value = value;

        _slave.RaiseAfterWrite(msg.Code, reqAddr, [value]);

        // 响应回显请求：地址 + 值
        return msg.Payload;
    }

    IPacket? OnWriteCoils(ModbusMessage msg)
    {
        var coils = _slave.Coils;
        if (coils == null || msg.Payload == null || msg.Payload.Total < 5) return null;

        var (reqAddr, coilCount) = msg.GetRequest();
        var baseAddr = reqAddr - coils[0].Address;

        var byteCount = (coilCount + 7) / 8;
        var writtenValues = new UInt16[coilCount];
        var k = 0;
        for (var i = 0; i < byteCount; i++)
        {
            var b = msg.Payload.ReadBytes(5 + i, 1)[0];
            for (var j = 0; j < 8 && k < coilCount; j++, k++)
            {
                var addr = baseAddr + k;
                var v = (Byte)((b >> j) & 1);
                if (addr >= 0 && addr < coils.Count)
                    coils[addr].Value = v;
                writtenValues[k] = v;
            }
        }

        _slave.RaiseAfterWrite(msg.Code, reqAddr, writtenValues);

        // 响应：地址 + 数量
        var buf = new Byte[4];
        buf.Write(reqAddr, 0, false);
        buf.Write(coilCount, 2, false);
        return (ArrayPacket)buf;
    }

    IPacket? OnWriteRegisters(ModbusMessage msg)
    {
        var regs = _slave.Registers;
        if (regs == null || msg.Payload == null || msg.Payload.Total < 5) return null;

        var (reqAddr, regCount) = msg.GetRequest();
        if (msg.Payload.Total < 5 + regCount * 2) return null;

        var writtenValues = new UInt16[regCount];
        for (var i = 0; i < regCount; i++)
        {
            var value = msg.Payload.ReadBytes(5 + i * 2, 2).ToUInt16(0, false);
            var addr = (reqAddr + i) - regs[0].Address;
            if (addr >= 0 && addr < regs.Count)
                regs[addr].Value = value;
            writtenValues[i] = value;
        }

        _slave.RaiseAfterWrite(msg.Code, reqAddr, writtenValues);

        // 响应：地址 + 数量
        var buf = new Byte[4];
        buf.Write(reqAddr, 0, false);
        buf.Write(regCount, 2, false);
        return (ArrayPacket)buf;
    }

    IPacket? OnReadWriteRegisters(ModbusMessage msg)
    {
        var regs = _slave.Registers;
        if (regs == null || msg.Payload == null || msg.Payload.Total < 9) return null;

        var reader = new SpanReader(msg.Payload.GetSpan()) { IsLittleEndian = false };
        var readAddr = reader.ReadUInt16();
        var readCount = reader.ReadUInt16();
        var writeAddr = reader.ReadUInt16();
        var writeCount = reader.ReadUInt16();
        _ = reader.ReadByte();  // ByteCount

        // 先执行写入
        var writtenValues = new UInt16[writeCount];
        for (var i = 0; i < writeCount; i++)
        {
            var value = reader.ReadUInt16();
            var addr = (writeAddr + i) - regs[0].Address;
            if (addr >= 0 && addr < regs.Count)
                regs[addr].Value = value;
            writtenValues[i] = value;
        }

        _slave.RaiseAfterWrite(msg.Code, writeAddr, writtenValues);

        // 再读取并返回
        var (addr2, cnt2) = ((Int32)(readAddr - regs[0].Address), (Int32)readCount);
        if (addr2 >= 0 && addr2 + cnt2 <= regs.Count)
        {
            _slave.RaiseBeforeRead(msg.Code, readAddr, readCount);
            var buf2 = regs.Skip(addr2).Take(cnt2).SelectMany(e => e.GetData()).ToArray();
            var rs2 = new Byte[1 + buf2.Length];
            rs2[0] = (Byte)buf2.Length;
            Array.Copy(buf2, 0, rs2, 1, buf2.Length);
            return (ArrayPacket)rs2;
        }

        return null;
    }

    IPacket? OnReadDevId(ModbusMessage msg)
    {
        var info = _slave.DeviceInfo;
        var objects = info.GetObjects();

        // 计算总字节数：MEIType(1)+DevIdCode(1)+ConformityLevel(1)+MoreFollows(1)+NextObjId(1)+Count(1) + 每个对象[ObjId(1)+ObjLen(1)+Value(N)]
        var totalSize = 6;
        var objBytes = new List<(Byte Id, Byte[] Data)>();
        foreach (var (id, value) in objects)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            totalSize += 2 + bytes.Length;
            objBytes.Add((id, bytes));
        }

        var rs = new Byte[totalSize];
        var pos = 0;
        rs[pos++] = 0x0E;                       // MEI Type
        rs[pos++] = 0x01;                       // ReadDevIdCode: Basic
        rs[pos++] = 0x01;                       // ConformityLevel: Basic stream
        rs[pos++] = 0x00;                       // MoreFollows: No
        rs[pos++] = 0x00;                       // NextObjectId
        rs[pos++] = (Byte)objBytes.Count;       // NumberOfObjects

        foreach (var (id, data) in objBytes)
        {
            rs[pos++] = id;
            rs[pos++] = (Byte)data.Length;
            Array.Copy(data, 0, rs, pos, data.Length);
            pos += data.Length;
        }

        return (ArrayPacket)rs;
    }

    /// <summary>处理FC22屏蔽写寄存器。算法：Result = (Current &amp; AND_Mask) | (OR_Mask &amp; ~AND_Mask)</summary>
    /// <param name="msg">请求消息</param>
    /// <returns>响应负载</returns>
    IPacket? OnMaskWriteRegister(ModbusMessage msg)
    {
        var regs = _slave.Registers;
        if (regs == null || msg.Payload == null || msg.Payload.Total < 6) return null;

        var reqAddr = msg.Payload.ReadBytes(0, 2).ToUInt16(0, false);
        var andMask = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);
        var orMask = msg.Payload.ReadBytes(4, 2).ToUInt16(0, false);

        var addr = reqAddr - regs[0].Address;
        if (addr >= 0 && addr < regs.Count)
        {
            var current = regs[addr].Value;
            regs[addr].Value = (UInt16)((current & andMask) | (orMask & ~andMask));
        }

        _slave.RaiseAfterWrite(msg.Code, reqAddr, [andMask, orMask]);

        // 响应回显请求：地址 + AND_Mask + OR_Mask
        return msg.Payload;
    }

    /// <summary>处理FC07读异常状态。返回1字节异常状态码</summary>
    /// <param name="msg">请求消息</param>
    /// <returns>响应负载（1字节异常状态）</returns>
    IPacket? OnReadExceptionStatus(ModbusMessage msg)
        => (ArrayPacket)new Byte[] { _slave.ExceptionStatus };

    /// <summary>处理FC17报告服务器ID。返回服务器标识、运行状态和附加数据</summary>
    /// <param name="msg">请求消息</param>
    /// <returns>响应负载</returns>
    IPacket? OnReportServerId(ModbusMessage msg)
    {
        var serverId = _slave.ServerId;
        var runIndicator = _slave.RunIndicator ? (Byte)0xFF : (Byte)0x00;
        // 附加数据为空（可扩展）
        var additional = new Byte[0];
        var byteCount = (Byte)(2 + additional.Length);
        var rs = new Byte[1 + byteCount];
        rs[0] = byteCount;
        rs[1] = serverId;
        rs[2] = runIndicator;
        return (ArrayPacket)rs;
    }

    /// <summary>处理FC11获取通信事件计数。返回状态和事件计数</summary>
    /// <param name="msg">请求消息</param>
    /// <returns>响应负载（4字节：Status+EventCount）</returns>
    IPacket? OnGetComEventCounter(ModbusMessage msg)
    {
        var buf = new Byte[4];
        buf.Write(_slave.ComEventStatus, 0, false);
        buf.Write(_slave.ComEventCount, 2, false);
        return (ArrayPacket)buf;
    }

    /// <summary>处理FC12获取通信事件日志。返回状态、事件计数、消息计数和事件字节列表</summary>
    /// <param name="msg">请求消息</param>
    /// <returns>响应负载</returns>
    IPacket? OnGetComEventLog(ModbusMessage msg)
    {
        var log = _slave.ComEventLog;
        var messageCount = (UInt16)(log?.Count ?? 0);
        var byteCount = (Byte)(6 + messageCount);
        var rs = new Byte[1 + byteCount];
        rs[0] = byteCount;
        rs.Write(_slave.ComEventStatus, 1, false);
        rs.Write(_slave.ComEventCount, 3, false);
        rs.Write(messageCount, 5, false);
        if (messageCount > 0 && log != null)
        {
            for (var i = 0; i < messageCount; i++)
                rs[7 + i] = log[i];
        }
        return (ArrayPacket)rs;
    }
}
