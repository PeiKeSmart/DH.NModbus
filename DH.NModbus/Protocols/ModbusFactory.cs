namespace NewLife.IoT.Protocols;

/// <summary>Modbus协议工厂。根据配置URI自动创建对应传输变体实例</summary>
/// <remarks>
/// 内置支持 tcp/enron/udp/rtuovertcp/rtuoverudp 五种 URI Scheme；
/// 串口变体（rtu/ascii）由 NewLife.ModbusRTU 包在静态构造中调用 <see cref="Register"/> 注册。
/// </remarks>
/// <example>
/// var modbus = ModbusFactory.Create("tcp://192.168.1.100:502");
/// var modbus = ModbusFactory.Create("enron://192.168.1.100:502");
/// var modbus = ModbusFactory.Create("udp://192.168.1.100:502");
/// var modbus = ModbusFactory.Create("rtuovertcp://192.168.1.100:502");
/// </example>
public static class ModbusFactory
{
    #region 注册表
    private static readonly Dictionary<String, Func<String, Modbus>> _creators =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>静态构造：注册内置传输变体</summary>
    static ModbusFactory()
    {
        Register("tcp", config => new ModbusTcp { Server = config });
        Register("enron", config => new ModbusEnron { Server = config });
        Register("udp", config => new ModbusUdp { Server = config });
        Register("rtuovertcp", config => new ModbusRtuOverTcp { Server = config });
        Register("rtuoverudp", config => new ModbusRtuOverUdp { Server = config });
    }

    /// <summary>注册协议变体工厂方法</summary>
    /// <param name="scheme">URI Scheme，如 "tcp" / "rtu" / "ascii"，大小写不敏感</param>
    /// <param name="creator">工厂委托，接收完整配置字符串，返回 Modbus 实例</param>
    public static void Register(String scheme, Func<String, Modbus> creator)
    {
        if (scheme.IsNullOrEmpty()) throw new ArgumentNullException(nameof(scheme));
        if (creator == null) throw new ArgumentNullException(nameof(creator));

        _creators[scheme] = creator;
    }
    #endregion

    #region 工厂方法
    /// <summary>根据配置字符串创建 Modbus 实例</summary>
    /// <remarks>
    /// 配置字符串格式为 URI，Scheme 对应传输变体：
    /// <list type="bullet">
    ///   <item>tcp://192.168.1.100:502 → <see cref="ModbusTcp"/>（配置 SslProtocol 后自动启用 TLS）</item>
    ///   <item>enron://192.168.1.100:502 → <see cref="ModbusEnron"/>（32 位浮点寄存器）</item>
    ///   <item>udp://192.168.1.100:502 → <see cref="ModbusUdp"/></item>
    ///   <item>rtuovertcp://192.168.1.100:502 → <see cref="ModbusRtuOverTcp"/></item>
    ///   <item>rtuoverudp://192.168.1.100:502 → <see cref="ModbusRtuOverUdp"/></item>
    ///   <item>rtu://COM3?baudrate=9600 → ModbusRtu（由 NewLife.ModbusRTU 注册）</item>
    ///   <item>ascii://COM3?baudrate=9600 → ModbusAscii（由 NewLife.ModbusRTU 注册）</item>
    /// </list>
    /// </remarks>
    /// <param name="config">配置字符串，格式为 URI（含 Scheme）</param>
    /// <returns>对应传输变体的 Modbus 实例；config 为空或 Scheme 未注册时返回 null</returns>
    public static Modbus? Create(String config)
    {
        if (config.IsNullOrEmpty()) return null;

        // 解析 Scheme
        var idx = config.IndexOf("://", StringComparison.Ordinal);
        if (idx <= 0) return null;

        var scheme = config[..idx];
        if (!_creators.TryGetValue(scheme, out var creator)) return null;

        return creator(config);
    }
    #endregion
}
