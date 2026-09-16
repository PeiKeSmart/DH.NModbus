using System.ComponentModel;
using System.Security.Authentication;

namespace NewLife.IoT.Drivers;

/// <summary>ModbusTcp参数</summary>
public class ModbusTcpParameter : ModbusIpParameter
{
    /// <summary>协议标识。默认0</summary>
    [Description("协议标识。默认0")]
    public UInt16 ProtocolId { get; set; }

    /// <summary>SSL协议版本。默认 None 表示不启用 TLS</summary>
    [Description("SSL协议版本。默认 None 不启用，设置 Tls12 或 Tls13 启用安全传输")]
    public SslProtocols SslProtocol { get; set; }

    /// <summary>证书文件路径。用于 TLS 客户端验证服务端证书（可选）</summary>
    [Description("证书文件路径。用于TLS客户端验证服务端证书（可选）")]
    public String? CertificateFile { get; set; }

    /// <summary>证书密码</summary>
    [Description("证书密码")]
    public String? CertificatePassword { get; set; }

    /// <summary>Enron Modbus 32位浮点模式。启用后使用 ModbusEnron 驱动，寄存器值为 32 位 IEEE 754 浮点数</summary>
    [Description("Enron Modbus 32位浮点模式。启用后寄存器值为32位IEEE754浮点数")]
    public Boolean IsEnron { get; set; }
}