using System.ComponentModel;

namespace NewLife.IoT.Protocols;

/// <summary>Modbus功能码</summary>
public enum FunctionCodes : Byte
{
    /// <summary>读单个线圈</summary>
    [Description("01读单个线圈")]
    ReadCoil = 1,

    /// <summary>读离散量输入状态</summary>
    [Description("02读离散量输入")]
    ReadDiscrete = 2,

    /// <summary>读保持寄存器</summary>
    [Description("03读保持寄存器")]
    ReadRegister = 3,

    /// <summary>读输入寄存器</summary>
    [Description("04读输入寄存器")]
    ReadInput = 4,

    /// <summary>写单个线圈</summary>
    [Description("05写单个线圈")]
    WriteCoil = 5,

    /// <summary>写单个保持寄存器</summary>
    [Description("06写保持寄存器")]
    WriteRegister = 6,

    /// <summary>诊断</summary>
    [Description("08诊断")]
    Diagnostics = 8,

    /// <summary>写多个线圈</summary>
    [Description("15写多个线圈")]
    WriteCoils = 15,

    /// <summary>写多个保持寄存器</summary>
    [Description("16写多个保持寄存器")]
    WriteRegisters = 16,

    /// <summary>写文件</summary>
    [Description("21写文件")]
    WriteFileRecord = 21,

    /// <summary>读写多个保持寄存器</summary>
    [Description("23读写多个保持寄存器")]
    ReadWriteMultipleRegisters = 23,

    /// <summary>读异常状态</summary>
    [Description("07读异常状态")]
    ReadExceptionStatus = 7,

    /// <summary>获取通信事件计数</summary>
    [Description("11获取通信事件计数")]
    GetComEventCounter = 11,

    /// <summary>获取通信事件日志</summary>
    [Description("12获取通信事件日志")]
    GetComEventLog = 12,

    /// <summary>报告服务器ID</summary>
    [Description("17报告服务器ID")]
    ReportServerId = 17,

    /// <summary>读文件记录</summary>
    [Description("20读文件记录")]
    ReadFileRecord = 20,

    /// <summary>屏蔽写寄存器。通过AND/OR掩码修改单个寄存器的指定位</summary>
    [Description("22屏蔽写寄存器")]
    MaskWriteRegister = 22,

    /// <summary>读FIFO队列</summary>
    [Description("24读FIFO队列")]
    ReadFifoQueue = 24,

    /// <summary>读设备识别码</summary>
    [Description("43读设备识别码")]
    ReadDevId = 43,
}