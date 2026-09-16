namespace NewLife.IoT.Protocols;

/// <summary>Enron Modbus（32位浮点扩展）</summary>
/// <remarks>
/// 兼容 Enron/Daniel Modbus 32 位 IEEE 754 浮点寄存器规范。
/// 与标准 Modbus TCP 使用相同的 MBAP 帧结构和功能码（FC03/FC04/FC06/FC16），
/// 但寄存器值为 32 位浮点数（4 字节）而非 16 位整数（2 字节）。
/// 
/// 主要差异：
/// <list type="bullet">
///   <item>FC03/FC04：读取返回 32 位 float 值数组</item>
///   <item>FC06：写入单个 32 位 float 值</item>
///   <item>FC16：批量写入 32 位 float 值数组</item>
///   <item>地址空间：每个地址对应一个 32 位寄存器</item>
/// </list>
/// 
/// 使用示例：
/// <code>
/// var modbus = new ModbusEnron
/// {
///     Server = "tcp://192.168.1.100:502",
/// };
/// modbus.Open();
/// 
/// // 读取 32 位浮点寄存器
/// var floats = modbus.ReadFloat(1, 0, 10);
/// 
/// // 写入单个 32 位浮点寄存器
/// modbus.WriteFloat(1, 0, 3.14f);
/// 
/// // 批量写入 32 位浮点寄存器
/// modbus.WriteFloats(1, 0, new Single[] { 1.1f, 2.2f, 3.3f });
/// </code>
/// </remarks>
public class ModbusEnron : ModbusTcp
{
    #region 构造
    /// <summary>实例化</summary>
    public ModbusEnron() { }
    #endregion

    #region 读取方法
    /// <summary>读取 32 位浮点保持寄存器（FC03）</summary>
    /// <param name="host">从机地址</param>
    /// <param name="address">起始地址（32位寄存器地址）</param>
    /// <param name="count">寄存器数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>浮点值数组</returns>
    public async Task<Single[]?> ReadFloatAsync(Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        var rs = await ReadRegisterEnronAsync(host, address, count, cancellationToken).ConfigureAwait(false);
        return RawToFloats(rs);
    }

    /// <summary>读取 32 位浮点输入寄存器（FC04）</summary>
    /// <param name="host">从机地址</param>
    /// <param name="address">起始地址（32位寄存器地址）</param>
    /// <param name="count">寄存器数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>浮点值数组</returns>
    public async Task<Single[]?> ReadInputFloatAsync(Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        var rs = await ReadInputEnronAsync(host, address, count, cancellationToken).ConfigureAwait(false);
        return RawToFloats(rs);
    }

    /// <summary>同步读取 32 位浮点保持寄存器（FC03）</summary>
    /// <param name="host">从机地址</param>
    /// <param name="address">起始地址（32位寄存器地址）</param>
    /// <param name="count">寄存器数量</param>
    /// <returns>浮点值数组</returns>
    public Single[]? ReadFloat(Byte host, UInt16 address, UInt16 count) =>
        ReadFloatAsync(host, address, count).GetAwaiter().GetResult();

    /// <summary>同步读取 32 位浮点输入寄存器（FC04）</summary>
    /// <param name="host">从机地址</param>
    /// <param name="address">起始地址（32位寄存器地址）</param>
    /// <param name="count">寄存器数量</param>
    /// <returns>浮点值数组</returns>
    public Single[]? ReadInputFloat(Byte host, UInt16 address, UInt16 count) =>
        ReadInputFloatAsync(host, address, count).GetAwaiter().GetResult();

    /// <summary>写入单个 32 位浮点保持寄存器（FC06）</summary>
    /// <param name="host">从机地址</param>
    /// <param name="address">寄存器地址</param>
    /// <param name="value">浮点值</param>
    /// <returns>写入的寄存器地址（成功时 &gt;=0）</returns>
    public virtual Int32 WriteFloat(Byte host, UInt16 address, Single value)
    {
        var data = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(data);

        var vals = new UInt16[] { (UInt16)((data[0] << 8) | data[1]), (UInt16)((data[2] << 8) | data[3]) };

        // 标准 FC06 写单个 16 位寄存器，Enron 用 FC06 写 32 位值（两个连续地址）
        // 实际使用 FC16 写多个寄存器来写入 32 位值
        return WriteRegisters(host, address, vals);
    }

    /// <summary>批量写入 32 位浮点保持寄存器（FC16）</summary>
    /// <param name="host">从机地址</param>
    /// <param name="address">起始地址</param>
    /// <param name="values">浮点值数组</param>
    /// <returns>写入的起始地址（成功时 &gt;=0）</returns>
    public virtual Int32 WriteFloats(Byte host, UInt16 address, Single[] values)
    {
        if (values == null || values.Length == 0) return -1;

        var data = new Byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            var bytes = BitConverter.GetBytes(values[i]);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, data, i * 4, 4);
        }

        // 转换为 UInt16 数组写入
        var vals = new UInt16[values.Length * 2];
        for (var i = 0; i < data.Length; i += 2)
        {
            vals[i / 2] = (UInt16)((data[i] << 8) | data[i + 1]);
        }

        return WriteRegisters(host, address, vals);
    }

    /// <summary>异步读取保持寄存器（FC03），返回 16 位原始值（Enron 模式下 count 个 32 位寄存器对应 count*2 个 16 位值）</summary>
    /// <param name="host">从机地址</param>
    /// <param name="address">起始地址</param>
    /// <param name="count">寄存器数量（32 位寄存器个数）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>16 位原始值数组（需调用方按 Enron 32 位格式解析）</returns>
    public async Task<UInt16[]> ReadRegisterEnronAsync(Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        // Enron 模式下，count 个 32 位寄存器需要读取 count*2 个 16 位寄存器
        return await ReadRegisterAsync(host, address, (UInt16)(count * 2), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>异步读取输入寄存器（FC04），返回 16 位原始值</summary>
    /// <param name="host">从机地址</param>
    /// <param name="address">起始地址</param>
    /// <param name="count">寄存器数量（32 位寄存器个数）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>16 位原始值数组</returns>
    public async Task<UInt16[]> ReadInputEnronAsync(Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        return await ReadInputAsync(host, address, (UInt16)(count * 2), cancellationToken).ConfigureAwait(false);
    }
    #endregion

    #region 辅助
    /// <summary>将原始 UInt16 数组转换为浮点数组</summary>
    /// <param name="raw">原始 UInt16 数据（大端字节序）</param>
    /// <returns>浮点值数组</returns>
    protected static Single[] RawToFloats(UInt16[] raw)
    {
        var data = new Byte[raw.Length * 2];
        for (var i = 0; i < raw.Length; i++)
        {
            data[i * 2] = (Byte)(raw[i] >> 8);
            data[i * 2 + 1] = (Byte)(raw[i] & 0xFF);
        }

        var result = new Single[raw.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            var bytes = new Byte[4];
            Array.Copy(data, i * 4, bytes, 0, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            result[i] = BitConverter.ToSingle(bytes, 0);
        }

        return result;
    }
    #endregion
}
