using System.IO.Ports;
using NewLife.Data;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.Log;

namespace NewLife.Serial.Protocols;

/// <summary>Modbus ASCII 串口从机（Slave/Server）</summary>
/// <remarks>
/// 监听串口，响应 Master 的 FC01/02/03/04/05/06/15/16 请求（ASCII 帧格式）。
/// ASCII 帧格式：冒号开头 `:HHHHH...LRLF\r\n`，LRC 校验。
/// </remarks>
public class ModbusAsciiSlave : DisposeBase
{
    #region 属性
    /// <summary>串口名。如 COM3 / /dev/ttyS0</summary>
    public String PortName { get; set; } = null!;

    /// <summary>波特率。默认 9600</summary>
    public Int32 Baudrate { get; set; } = 9600;

    /// <summary>数据位。默认 7（ASCII 模式通常使用 7 位）</summary>
    public Int32 DataBits { get; set; } = 7;

    /// <summary>奇偶校验位。默认 Even</summary>
    public Parity Parity { get; set; } = Parity.Even;

    /// <summary>停止位。默认 One</summary>
    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>本机站号。默认 1；设为 0 时接受所有站号</summary>
    public Byte Host { get; set; } = 1;

    /// <summary>保持寄存器区（AO/FC03 + 输入寄存器 AI/FC04）</summary>
    public List<RegisterUnit> Registers { get; set; } = [];

    /// <summary>线圈区（DO/FC01 + 离散输入 DI/FC02）</summary>
    public List<CoilUnit> Coils { get; set; } = [];

    /// <summary>读取数据前触发，可在回调中刷新数据源</summary>
    public event Action<FunctionCodes, UInt16, UInt16>? BeforeRead;

    /// <summary>写入数据后触发，可将写入同步到执行器</summary>
    public event Action<FunctionCodes, UInt16, UInt16[]>? AfterWrite;

    /// <summary>通信状态。用于 FC11/FC12 响应，0x0000=正常</summary>
    public UInt16 ComEventStatus { get; set; }

    /// <summary>通信事件计数。用于 FC11/FC12 响应</summary>
    public UInt16 ComEventCount { get; set; }

    /// <summary>通信事件日志。用于 FC12 响应，每个字节表示一个事件码</summary>
    public List<Byte> ComEventLog { get; set; } = [];

    /// <summary>异常状态字节。用于 FC07 响应</summary>
    public Byte ExceptionStatus { get; set; }

    /// <summary>服务器标识。用于 FC17 响应</summary>
    public Byte ServerId { get; set; } = 0x01;

    /// <summary>运行指示状态。用于 FC17 响应</summary>
    public Boolean RunIndicator { get; set; } = true;

    /// <summary>日志</summary>
    public ILog? Log { get; set; }

    private SerialPort? _port;
    private Thread? _thread;
    private volatile Boolean _running;
    #endregion

    #region 构造
    /// <summary>销毁</summary>
    /// <param name="disposing"></param>
    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);
        Stop();
    }
    #endregion

    #region 启动/停止
    /// <summary>启动从机监听</summary>
    public void Start()
    {
        if (_running) return;

        _port = new SerialPort(PortName, Baudrate)
        {
            DataBits = DataBits,
            Parity = Parity,
            StopBits = StopBits,
            ReadTimeout = 50,
            WriteTimeout = 1000,
        };
        _port.Open();

        _running = true;
        _thread = new Thread(ListenLoop) { IsBackground = true, Name = $"ModbusAsciiSlave-{PortName}" };
        _thread.Start();

        WriteLog("ModbusAsciiSlave.Start {0} Baudrate={1}", PortName, Baudrate);
    }

    /// <summary>停止从机</summary>
    public void Stop()
    {
        _running = false;
        _port?.Close();
        _port?.Dispose();
        _port = null;
        _thread = null;

        WriteLog("ModbusAsciiSlave.Stop");
    }
    #endregion

    #region 监听循环
    private void ListenLoop()
    {
        var lineBuffer = new System.Text.StringBuilder(256);

        while (_running)
        {
            try
            {
                var ch = (Char)_port!.ReadChar();
                lineBuffer.Append(ch);

                // ASCII 帧以 \n 结尾（前置 \r）
                var line = lineBuffer.ToString();
                if (line.EndsWith("\r\n") && line.StartsWith(":"))
                {
                    lineBuffer.Clear();
                    var frameBytes = System.Text.Encoding.ASCII.GetBytes(line);
                    var response = ProcessRequest(frameBytes);
                    if (response != null && response.Length > 0)
                        _port.Write(response, 0, response.Length);
                }
                else if (lineBuffer.Length > 512)
                {
                    // 防止缓冲区溢出
                    lineBuffer.Clear();
                }
            }
            catch (TimeoutException)
            {
                // 正常超时，继续轮询
            }
            catch (Exception ex)
            {
                WriteLog("ModbusAsciiSlave.ListenLoop 异常: {0}", ex.Message);
                if (!_running) break;
                Thread.Sleep(100);
            }
        }
    }
    #endregion

    #region 请求处理（可供单元测试直接调用）
    /// <summary>处理一帧 ASCII 请求，返回 ASCII 响应字节；帧非法则返回 null</summary>
    /// <param name="frame">完整 ASCII 帧字节（含冒号和 CR/LF）</param>
    /// <returns>ASCII 响应帧字节，或 null</returns>
    public Byte[]? ProcessRequest(Byte[] frame)
    {
        if (frame == null || frame.Length < 9) return null;  // 最短：:HHFFFFCCR\n

        var msg = ModbusAsciiMessage.Read(frame);
        if (msg == null) return null;

        // LRC 校验失败也不返回（ModbusAsciiMessage.Read 内部会记录）
        if (msg.Lrc != msg.Lrc2)
        {
            WriteLog("LRC 校验失败: 帧内={0:X2}, 计算={1:X2}", msg.Lrc, msg.Lrc2);
            return null;
        }

        var frameHost = msg.Host;
        var isBroadcast = frameHost == 0;
        if (!isBroadcast && Host != 0 && frameHost != Host) return null;

        var code = msg.Code;
        IPacket? rsPayload = null;
        switch (code)
        {
            case FunctionCodes.ReadCoil:
            case FunctionCodes.ReadDiscrete:
                if (!isBroadcast)
                {
                    var (addr0, cnt0) = msg.GetRequest();
                    BeforeRead?.Invoke(code, addr0, cnt0);
                    rsPayload = OnReadCoil(msg);
                }
                else
                    rsPayload = null;
                break;

            case FunctionCodes.ReadRegister:
            case FunctionCodes.ReadInput:
                if (!isBroadcast)
                {
                    var (addr1, cnt1) = msg.GetRequest();
                    BeforeRead?.Invoke(code, addr1, cnt1);
                    rsPayload = OnReadRegister(msg);
                }
                else
                    rsPayload = null;
                break;

            case FunctionCodes.WriteCoil:
                rsPayload = OnWriteCoil(msg);
                break;

            case FunctionCodes.WriteRegister:
                rsPayload = OnWriteRegister(msg);
                break;

            case FunctionCodes.WriteCoils:
                rsPayload = OnWriteCoils(msg);
                break;

            case FunctionCodes.WriteRegisters:
                rsPayload = OnWriteRegisters(msg);
                break;

            case FunctionCodes.ReadExceptionStatus:
                if (!isBroadcast) rsPayload = (ArrayPacket)new Byte[] { ExceptionStatus };
                break;

            case FunctionCodes.GetComEventCounter:
                if (!isBroadcast) rsPayload = OnGetComEventCounter();
                break;

            case FunctionCodes.GetComEventLog:
                if (!isBroadcast) rsPayload = OnGetComEventLog();
                break;

            case FunctionCodes.ReportServerId:
                if (!isBroadcast) rsPayload = OnReportServerId();
                break;

            case FunctionCodes.MaskWriteRegister:
                rsPayload = OnMaskWriteRegister(msg);
                break;

            default:
                rsPayload = (ArrayPacket)new Byte[] { (Byte)ErrorCodes.IllegalFunction };
                code = (FunctionCodes)((Byte)code | 0x80);
                break;
        }

        if (isBroadcast) return null;
        if (rsPayload == null) return null;

        var rsMsg = new ModbusAsciiMessage { Host = frameHost, Code = code, Payload = rsPayload, Reply = true };
        var rsPacket = rsMsg.ToPacket();
        return rsPacket.ReadBytes();
    }
    #endregion

    #region 功能码处理
    private IPacket? OnReadCoil(ModbusMessage msg)
    {
        if (Coils == null || Coils.Count == 0) return null;

        var (regAddr, regCount) = msg.GetRequest();
        var addr = regAddr - Coils[0].Address;
        if (addr < 0 || addr + regCount > Coils.Count) return null;

        var cs = Coils.Skip(addr).Take(regCount).ToList();
        var count = (Int32)Math.Ceiling(regCount / 8.0);
        var rs = new Byte[1 + count];
        rs[0] = (Byte)count;
        for (var i = 0; i < count; i++)
        {
            var b = 0;
            var max = Math.Min(8, regCount - i * 8);
            for (var j = 0; j < max; j++)
                if (cs[i * 8 + j].Value > 0) b |= 1 << j;
            rs[1 + i] = (Byte)b;
        }
        return (ArrayPacket)rs;
    }

    private IPacket? OnReadRegister(ModbusMessage msg)
    {
        if (Registers == null || Registers.Count == 0) return null;

        var (regAddr, regCount) = msg.GetRequest();
        var addr = regAddr - Registers[0].Address;
        if (addr < 0 || addr + regCount > Registers.Count) return null;

        var buf = Registers.Skip(addr).Take(regCount).SelectMany(e => e.GetData()).ToArray();
        var rs = new Byte[1 + buf.Length];
        rs[0] = (Byte)buf.Length;
        Array.Copy(buf, 0, rs, 1, buf.Length);
        return (ArrayPacket)rs;
    }

    private IPacket? OnWriteCoil(ModbusMessage msg)
    {
        if (Coils == null || msg.Payload == null || msg.Payload.Total < 4) return null;

        var reqAddr = msg.GetAddress();
        var value = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);

        var addr = reqAddr - Coils[0].Address;
        if (addr >= 0 && addr < Coils.Count)
            Coils[addr].Value = value == 0xFF00 ? (Byte)1 : (Byte)0;

        AfterWrite?.Invoke(msg.Code, reqAddr, [value == 0xFF00 ? (UInt16)1 : (UInt16)0]);
        return msg.Payload;
    }

    private IPacket? OnWriteRegister(ModbusMessage msg)
    {
        if (Registers == null || msg.Payload == null || msg.Payload.Total < 4) return null;

        var reqAddr = msg.GetAddress();
        var value = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);

        var addr = reqAddr - Registers[0].Address;
        if (addr >= 0 && addr < Registers.Count)
            Registers[addr].Value = value;

        AfterWrite?.Invoke(msg.Code, reqAddr, [value]);
        return msg.Payload;
    }

    private IPacket? OnWriteCoils(ModbusMessage msg)
    {
        if (Coils == null || msg.Payload == null || msg.Payload.Total < 5) return null;

        var (reqAddr, coilCount) = msg.GetRequest();
        var baseAddr = reqAddr - Coils[0].Address;
        var byteCount = (coilCount + 7) / 8;
        var writtenValues = new UInt16[coilCount];
        var k = 0;
        for (var i = 0; i < byteCount; i++)
        {
            var b = msg.Payload.ReadBytes(5 + i, 1)[0];
            for (var j = 0; j < 8 && k < coilCount; j++, k++)
            {
                var idx = baseAddr + k;
                var v = (Byte)((b >> j) & 1);
                if (idx >= 0 && idx < Coils.Count) Coils[idx].Value = v;
                writtenValues[k] = v;
            }
        }
        AfterWrite?.Invoke(msg.Code, reqAddr, writtenValues);

        var buf = new Byte[4];
        buf.Write(reqAddr, 0, false);
        buf.Write(coilCount, 2, false);
        return (ArrayPacket)buf;
    }

    private IPacket? OnWriteRegisters(ModbusMessage msg)
    {
        if (Registers == null || msg.Payload == null || msg.Payload.Total < 5) return null;

        var (reqAddr, regCount) = msg.GetRequest();
        if (msg.Payload.Total < 5 + regCount * 2) return null;

        var writtenValues = new UInt16[regCount];
        for (var i = 0; i < regCount; i++)
        {
            var value = msg.Payload.ReadBytes(5 + i * 2, 2).ToUInt16(0, false);
            var idx = (reqAddr + i) - Registers[0].Address;
            if (idx >= 0 && idx < Registers.Count) Registers[idx].Value = value;
            writtenValues[i] = value;
        }
        AfterWrite?.Invoke(msg.Code, reqAddr, writtenValues);

        var buf = new Byte[4];
        buf.Write(reqAddr, 0, false);
        buf.Write(regCount, 2, false);
        return (ArrayPacket)buf;
    }

    /// <summary>处理FC11获取通信事件计数</summary>
    private IPacket? OnGetComEventCounter()
    {
        var buf = new Byte[4];
        buf.Write(ComEventStatus, 0, false);
        buf.Write(ComEventCount, 2, false);
        return (ArrayPacket)buf;
    }

    /// <summary>处理FC12获取通信事件日志</summary>
    private IPacket? OnGetComEventLog()
    {
        var messageCount = (UInt16)(ComEventLog?.Count ?? 0);
        var byteCount = (Byte)(6 + messageCount);
        var rs = new Byte[1 + byteCount];
        rs[0] = byteCount;
        rs.Write(ComEventStatus, 1, false);
        rs.Write(ComEventCount, 3, false);
        rs.Write(messageCount, 5, false);
        if (messageCount > 0 && ComEventLog != null)
        {
            for (var i = 0; i < messageCount; i++)
                rs[7 + i] = ComEventLog[i];
        }
        return (ArrayPacket)rs;
    }

    /// <summary>处理FC17报告服务器ID</summary>
    private IPacket? OnReportServerId()
    {
        var runIndicator = RunIndicator ? (Byte)0xFF : (Byte)0x00;
        var byteCount = (Byte)2;
        var rs = new Byte[1 + byteCount];
        rs[0] = byteCount;
        rs[1] = ServerId;
        rs[2] = runIndicator;
        return (ArrayPacket)rs;
    }

    /// <summary>处理FC22屏蔽写寄存器</summary>
    private IPacket? OnMaskWriteRegister(ModbusMessage msg)
    {
        if (Registers == null || msg.Payload == null || msg.Payload.Total < 6) return null;

        var reqAddr = msg.Payload.ReadBytes(0, 2).ToUInt16(0, false);
        var andMask = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);
        var orMask = msg.Payload.ReadBytes(4, 2).ToUInt16(0, false);

        var addr = reqAddr - Registers[0].Address;
        if (addr >= 0 && addr < Registers.Count)
        {
            var current = Registers[addr].Value;
            var result = (UInt16)((current & andMask) | (orMask & ~andMask));
            Registers[addr].Value = result;
        }

        AfterWrite?.Invoke(msg.Code, reqAddr, [andMask, orMask]);
        return msg.Payload;
    }
    #endregion

    #region 日志
    /// <summary>写日志</summary>
    /// <param name="format">日志格式</param>
    /// <param name="args">参数</param>
    public void WriteLog(String format, params Object[] args) => Log?.Info(format, args);
    #endregion
}
