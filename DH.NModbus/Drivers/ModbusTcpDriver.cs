using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using NewLife.IoT.Protocols;

namespace NewLife.IoT.Drivers;

/// <summary>TCP网络版Modbus</summary>
/// <remarks>
/// 每个Tcp/Udp从站地址，对应一个Modbus驱动实例，避免多个虚拟设备实例化多个驱动实例导致网络连接过多。
/// 该唯一性由驱动工厂DriverFactory来保证。
/// 配置 <see cref="ModbusTcpParameter.SslProtocol"/> 后自动启用 TLS 安全传输。
/// </remarks>
[Driver("ModbusTcp")]
[DisplayName("TCP网络版Modbus")]
public class ModbusTcpDriver : ModbusDriver, IDriver
{
    #region 方法
    /// <summary>
    /// 创建驱动参数对象，可序列化成Xml/Json作为该协议的参数模板
    /// </summary>
    /// <returns></returns>
    protected override IDriverParameter OnCreateParameter() => new ModbusTcpParameter
    {
        Server = "127.0.0.1:502",

        Host = 1,
        ReadCode = FunctionCodes.ReadRegister,
        WriteCode = FunctionCodes.WriteRegister,
    };

    /// <summary>
    /// 创建Modbus通道
    /// </summary>
    /// <param name="device">逻辑设备</param>
    /// <param name="node">设备节点</param>
    /// <param name="parameter">参数</param>
    /// <returns></returns>
    internal protected override Modbus CreateModbus(IDevice device, ModbusNode node, ModbusParameter parameter)
    {
        var p = parameter as ModbusTcpParameter;
        if (p == null || p.Server.IsNullOrEmpty()) throw new ArgumentException("参数中未指定地址Server");

        node.Parameter = p;

        // Enron 模式使用 ModbusEnron 驱动（32位浮点寄存器）
        ModbusTcp modbus = p.IsEnron
            ? new ModbusEnron
            {
                Server = p.Server,
                ProtocolId = p.ProtocolId,
                SslProtocol = p.SslProtocol,
                Tracer = Tracer,
                Log = Log,
            }
            : new ModbusTcp
            {
                Server = p.Server,
                ProtocolId = p.ProtocolId,
                SslProtocol = p.SslProtocol,
                Tracer = Tracer,
                Log = Log,
            };

        // 加载客户端证书（可选），仅当文件存在时加载
        if (!p.CertificateFile.IsNullOrEmpty() && File.Exists(p.CertificateFile))
        {
            modbus.Certificate = p.CertificatePassword.IsNullOrEmpty()
                ? new X509Certificate2(p.CertificateFile)
                : new X509Certificate2(p.CertificateFile, p.CertificatePassword);
        }

        return modbus;
    }
    #endregion
}