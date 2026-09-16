using NewLife.Data;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.Net;
using NewLife.Net.Handlers;

namespace NewLife.IoT;

/// <summary>Modbus从机/服务器</summary>
public class ModbusSlave : NetServer<ModbusSession>
{
    #region 属性
    /// <summary>本机站号。默认1。设为0时接受所有站号的请求</summary>
    public Byte Host { get; set; } = 1;

    /// <summary>寄存器区（保持寄存器 AO/FC03 + 输入寄存器 AI/FC04）</summary>
    public List<RegisterUnit> Registers { get; set; } = [];

    /// <summary>线圈区（数字输出 DO/FC01 + 离散输入 DI/FC02）</summary>
    public List<CoilUnit> Coils { get; set; } = [];

    /// <summary>设备标识信息。用于响应 FC43 ReadDevId 请求</summary>
    public ModbusDeviceInfo DeviceInfo { get; set; } = new();

    /// <summary>读取数据前触发。可在回调中更新 Registers/Coils 以提供最新数据</summary>
    /// <remarks>参数：(功能码, 起始地址, 数量)</remarks>
    public event Action<FunctionCodes, UInt16, UInt16>? BeforeRead;

    /// <summary>写入数据后触发。通知应用层寄存器或线圈发生变更</summary>
    /// <remarks>参数：(功能码, 起始地址, 写入值数组，线圈写入时为0或1的UInt16)</remarks>
    public event Action<FunctionCodes, UInt16, UInt16[]>? AfterWrite;

    /// <summary>异常状态字节。用于 FC07 ReadExceptionStatus 响应，每个位表示一个异常条件</summary>
    public Byte ExceptionStatus { get; set; }

    /// <summary>服务器标识。用于 FC17 ReportServerId 响应</summary>
    /// <remarks>0x00-0xFF，通常用于标识从机类型或固件版本</remarks>
    public Byte ServerId { get; set; } = 0x01;

    /// <summary>运行指示状态。用于 FC17 ReportServerId 响应，true=ON(0xFF)</summary>
    public Boolean RunIndicator { get; set; } = true;

    /// <summary>通信状态。用于 FC11 GetComEventCounter/FC12 GetComEventLog 响应，0x0000=正常</summary>
    public UInt16 ComEventStatus { get; set; }

    /// <summary>通信事件计数。用于 FC11/FC12 响应，记录通信事件发生次数</summary>
    public UInt16 ComEventCount { get; set; }

    /// <summary>通信事件日志。用于 FC12 响应，每个字节表示一个事件码</summary>
    public List<Byte> ComEventLog { get; set; } = [];

    /// <summary>多从站实例字典。Key=站号(1-247)，Value=从站实例。
    /// 设置后，请求按站号路由到对应实例；未匹配的站号回退到本实例（Registers/Coils）。
    /// 每个实例拥有独立的 Registers/Coils/DeviceInfo 和事件属性。</summary>
    public IDictionary<Byte, ModbusSlave> SlaveInstances { get; set; } = new Dictionary<Byte, ModbusSlave>();

    /// <summary>自定义功能码处理器字典。Key=功能码(1~255)，Value=处理委托。
    /// 注册后，从机收到对应功能码请求时优先调用此处理器，而非标准 switch 分支。
    /// 委托签名：(请求消息) → 响应负载，返回 null 表示无响应。</summary>
    /// <remarks>
    /// 建议使用用户自定义区段（65~72、100~110）避免与标准功能码冲突。
    /// 处理器抛出异常时，从机自动返回 EC04 SlaveDeviceFailure 异常响应。
    /// </remarks>
    public IDictionary<Byte, Func<ModbusMessage, IPacket?>> CustomFunctionHandlers { get; set; } = new Dictionary<Byte, Func<ModbusMessage, IPacket?>>();
    #endregion

    #region 构造
    /// <summary>实例化</summary>
    public ModbusSlave()
    {
        Port = 502;
    }

    /// <summary>
    /// 启动
    /// </summary>
    protected override void OnStart()
    {
        // 加入定长编码器，处理Tcp粘包
        Add(new LengthFieldCodec { Offset = 4, Size = -2 });

        base.OnStart();
    }
    #endregion

    #region 方法
    /// <summary>注册自定义功能码响应处理器</summary>
    /// <param name="code">功能码（1~255）。建议使用用户自定义区段 65~72、100~110</param>
    /// <param name="handler">处理委托。参数：(请求消息)，返回响应负载（null 表示无响应）</param>
    /// <exception cref="ArgumentNullException">handler 为 null</exception>
    public void RegisterFunctionHandler(Byte code, Func<ModbusMessage, IPacket?> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        CustomFunctionHandlers[code] = handler;
    }
    #endregion

    #region 辅助
    /// <summary>触发读取前事件</summary>
    /// <param name="code">功能码</param>
    /// <param name="address">起始地址</param>
    /// <param name="count">数量</param>
    internal void RaiseBeforeRead(FunctionCodes code, UInt16 address, UInt16 count) =>
        BeforeRead?.Invoke(code, address, count);

    /// <summary>触发写入后事件</summary>
    /// <param name="code">功能码</param>
    /// <param name="address">起始地址</param>
    /// <param name="values">写入的值数组</param>
    internal void RaiseAfterWrite(FunctionCodes code, UInt16 address, UInt16[] values) =>
        AfterWrite?.Invoke(code, address, values);
    #endregion
}
