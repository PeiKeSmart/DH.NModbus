using System.Runtime.CompilerServices;
using NewLife.Buffers;
using NewLife.Data;
using NewLife.IoT.Controllers;
using NewLife.Log;
using NewLife.Serialization;

[assembly: InternalsVisibleTo("XUnitTest")]

namespace NewLife.IoT.Protocols;

/// <summary>Modbus协议核心</summary>
public abstract class Modbus : DisposeBase, IModbus
{
    #region 属性
    /// <summary>名称</summary>
    public String Name { get; set; }

    /// <summary>网络超时。发起请求后等待响应的超时时间，默认3000ms</summary>
    public Int32 Timeout { get; set; } = 3000;

    /// <summary>缓冲区大小。默认256</summary>
    public Int32 BufferSize { get; set; } = 256;

    /// <summary>校验响应数据长度。默认true</summary>
    public Boolean ValidResponse { get; set; } = true;

    /// <summary>性能追踪器</summary>
    public ITracer? Tracer { get; set; }

    /// <summary>自定义功能码处理器字典。Key=功能码(1~255)，Value=处理委托</summary>
    /// <remarks>
    /// 用户可注册私有功能码处理器（如 65~72、100~110 等用户自定义区段），
    /// 通过 SendCustomCommandAsync 发送时自动路由到对应处理器。
    /// 委托签名：(host, data, cancellationToken) → 响应负载
    /// </remarks>
    public IDictionary<Byte, Func<Byte, IPacket, CancellationToken, Task<IPacket?>>> CustomFunctionCodes { get; set; } = new Dictionary<Byte, Func<Byte, IPacket, CancellationToken, Task<IPacket?>>>();

    /// <summary>最大自动重连次数。通信失败后自动重连并重试，默认0表示不自动重连</summary>
    /// <remarks>
    /// 当网络/串口通信抛出异常时，如果 MaxRetry > 0，则自动关闭当前连接、
    /// 重新打开连接并重试请求，直到达到最大重试次数。
    /// 适用于网络不稳定或设备临时离线场景。
    /// </remarks>
    public Int32 MaxRetry { get; set; }

    /// <summary>重连间隔。自动重连前等待的时间，默认1000ms</summary>
    public Int32 RetryInterval { get; set; } = 1000;

    /// <summary>异步重连。子类重写此方法实现具体的重连逻辑（如关闭并重新打开 TCP/串口连接）</summary>
    /// <param name="cancellationToken">取消令牌</param>
    protected virtual Task ReconnectAsync(CancellationToken cancellationToken) => TaskEx.CompletedTask;

    /// <summary>
    /// 执行操作并在失败时自动重连重试。
    /// 当 MaxRetry > 0 且操作抛出异常时，自动关闭连接、等待 RetryInterval 后重新打开并重试。
    /// </summary>
    /// <typeparam name="TResult">返回类型</typeparam>
    /// <param name="func">要执行的操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    protected async Task<TResult?> ExecuteWithRetryAsync<TResult>(Func<Task<TResult?>> func, CancellationToken cancellationToken) where TResult : class
    {
        var retries = MaxRetry;
        while (true)
        {
            try
            {
                return await func().ConfigureAwait(false);
            }
            catch (Exception ex) when (retries > 0 && !cancellationToken.IsCancellationRequested)
            {
                retries--;

                WriteLog("ExecuteWithRetry 失败 ({0}/{1}): {2}", MaxRetry - retries, MaxRetry, ex.Message);

                // 重连前等待
                if (RetryInterval > 0)
                    await Task.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);

                // 重连
                await ReconnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
    #endregion

    #region 构造
    /// <summary>实例化</summary>
    public Modbus() => Name = GetType().Name;
    #endregion

    #region 核心方法
    /// <summary>初始化。传入配置</summary>
    /// <param name="parameters"></param>
    public virtual void Init(IDictionary<String, Object> parameters) { }

    /// <summary>异步打开</summary>
    /// <param name="cancellationToken">取消令牌</param>
    public virtual Task OpenAsync(CancellationToken cancellationToken = default) => TaskEx.CompletedTask;

    /// <summary>异步关闭</summary>
    /// <param name="cancellationToken">取消令牌</param>
    public virtual Task CloseAsync(CancellationToken cancellationToken = default) => TaskEx.CompletedTask;

    /// <summary>创建消息</summary>
    /// <returns></returns>
    protected virtual ModbusMessage CreateMessage() => new();

    /// <summary>异步发送命令，并接收返回</summary>
    /// <param name="code">功能码</param>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="value">数据值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>返回响应消息的负载部分</returns>
    public virtual async Task<IPacket?> SendCommandAsync(FunctionCodes code, Byte host, UInt16 address, UInt16 value, CancellationToken cancellationToken = default)
    {
        // 使用 ExecuteWithRetryAsync 包裹实际的发送逻辑，支持自动重连
        return await ExecuteWithRetryAsync(async () =>
        {
            var msg = CreateMessage();
            msg.Host = host;
            msg.Code = code;
            msg.SetRequest(address, value);

            var rs = await SendCommandAsync(msg, cancellationToken).ConfigureAwait(false);
            return rs?.Payload;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>异步发送命令，并接收返回</summary>
    /// <param name="code">功能码</param>
    /// <param name="host">主机。一般是1</param>
    /// <param name="data">数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>返回响应消息的负载部分</returns>
    public virtual async Task<IPacket?> SendCommandAsync(FunctionCodes code, Byte host, IPacket data, CancellationToken cancellationToken = default)
    {
        // 使用 ExecuteWithRetryAsync 包裹实际的发送逻辑，支持自动重连
        return await ExecuteWithRetryAsync(async () =>
        {
            var msg = CreateMessage();
            msg.Host = host;
            msg.Code = code;
            msg.Payload = data;

            var rs = await SendCommandAsync(msg, cancellationToken).ConfigureAwait(false);
            return rs?.Payload;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>异步发送消息并接收返回。子类重写此方法实现真正的网络IO</summary>
    /// <param name="message">Modbus消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    internal protected virtual Task<ModbusMessage?> SendCommandAsync(ModbusMessage message, CancellationToken cancellationToken)
        => Task.FromResult(SendCommand(message));

    /// <summary>同步发送消息并接收返回。Moq测试覆盖此方法，子类应重写 SendCommandAsync</summary>
    /// <param name="message">Modbus消息</param>
    /// <returns></returns>
    internal protected virtual ModbusMessage? SendCommand(ModbusMessage message)
        => throw new NotSupportedException($"请重写 {GetType().Name}.SendCommandAsync 或 SendCommand");
    #endregion

    #region 读取
    /// <summary>按功能码异步读取。用于IoT标准库</summary>
    /// <param name="code">功能码</param>
    /// <param name="host">主机</param>
    /// <param name="address">逻辑地址</param>
    /// <param name="count">个数。寄存器个数或线圈个数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public virtual async Task<IPacket?> ReadAsync(FunctionCodes code, Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan($"modbus:{code}", $"host={host} address={address}/0x{address:X4} count={count}");
        try
        {
            switch (code)
            {
                case FunctionCodes.ReadCoil:
                case FunctionCodes.ReadDiscrete:
                case FunctionCodes.ReadRegister:
                case FunctionCodes.ReadInput:
                    var rs = await SendCommandAsync(code, host, address, count, cancellationToken).ConfigureAwait(false);
                    if (rs == null) return null;

                    var len = -1;
                    if (ValidResponse)
                    {
                        len = rs[0];
                        if (rs.Total < 1 + len) return null;
                    }

                    return rs.Slice(1, len);
                default:
                    break;
            }

            throw new NotSupportedException($"ModbusRead不支持[{code}]");
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读取线圈，0x01</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="count">线圈数量。一般要求8的倍数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>线圈状态字节数组</returns>
    public async Task<Boolean[]> ReadCoilAsync(Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadCoil", $"host={host} address={address}/0x{address:X4} count={count}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.ReadCoil, host, address, count, cancellationToken).ConfigureAwait(false);
            if (rs == null) return [];

            if (ValidResponse)
            {
                var len = count / 8;
                if (count % 8 > 0) len++;
                if (rs.Total < len) return [];
            }

            var sp = rs.GetSpan();
            var bs = new Boolean[count];
            var k = 0;
            for (var i = 1; i < rs.Length && k < count; i++)
            {
                var b = sp[i];
                for (var j = 0; j < 8 && k < count; j++)
                {
                    bs[k++] = ((b >> j) & 1) == 1;
                }
            }

            rs.TryDispose();

            return bs;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读离散量输入，0x02</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="count">输入数量。一般要求8的倍数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>输入状态字节数组</returns>
    public async Task<Boolean[]> ReadDiscreteAsync(Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadDiscrete", $"host={host} address={address}/0x{address:X4} count={count}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.ReadDiscrete, host, address, count, cancellationToken).ConfigureAwait(false);
            if (rs == null) return [];

            if (ValidResponse)
            {
                var len = count / 8;
                if (count % 8 > 0) len++;
                if (rs.Total < len) return [];
            }

            var sp = rs.GetSpan();
            var bs = new Boolean[count];
            var k = 0;
            for (var i = 1; i < rs.Length && k < count; i++)
            {
                var b = sp[i];
                for (var j = 0; j < 8 && k < count; j++)
                {
                    bs[k++] = ((b >> j) & 1) == 1;
                }
            }

            rs.TryDispose();

            return bs;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读取保持寄存器，0x03</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="count">寄存器数量。每个寄存器2个字节</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>寄存器值数组</returns>
    public async Task<UInt16[]> ReadRegisterAsync(Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadRegister", $"host={host} address={address}/0x{address:X4} count={count}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.ReadRegister, host, address, count, cancellationToken).ConfigureAwait(false);
            if (rs == null) return [];

            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            var len = reader.ReadByte();
            if (ValidResponse && rs.Total < 1 + len) return [];

            var bs = new UInt16[count];
            for (var i = 0; i < count; i++)
            {
                bs[i] = reader.ReadUInt16();
            }

            rs.TryDispose();

            return bs;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读取输入寄存器，0x04</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="count">输入寄存器数量。每个寄存器2个字节</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>输入寄存器值数组</returns>
    public async Task<UInt16[]> ReadInputAsync(Byte host, UInt16 address, UInt16 count, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadInput", $"host={host} address={address}/0x{address:X4} count={count}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.ReadInput, host, address, count, cancellationToken).ConfigureAwait(false);
            if (rs == null) return [];

            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            var len = reader.ReadByte();
            if (ValidResponse && rs.Total < 1 + len) return [];

            var bs = new UInt16[count];
            for (var i = 0; i < count; i++)
            {
                bs[i] = reader.ReadUInt16();
            }

            rs.TryDispose();

            return bs;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }
    #endregion

    #region 自定义功能码
    /// <summary>注册自定义功能码处理器</summary>
    /// <param name="code">功能码（1~255）。建议使用用户自定义区段 65~72、100~110</param>
    /// <param name="handler">处理委托。参数：(host, data, cancellationToken)，返回响应负载</param>
    /// <exception cref="ArgumentNullException">handler 为 null</exception>
    /// <exception cref="ArgumentException">功能码已被标准功能码占用</exception>
    public virtual void RegisterFunctionCode(Byte code, Func<Byte, IPacket, CancellationToken, Task<IPacket?>> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        // 检查是否与标准功能码冲突（Enum.IsDefined 检查已定义的 FunctionCodes）
        if (Enum.IsDefined(typeof(FunctionCodes), code))
            throw new ArgumentException($"功能码 {code} (0x{code:X2}) 已被标准功能码占用，请使用用户自定义区段（65~72、100~110）", nameof(code));

        CustomFunctionCodes[code] = handler;
    }

    /// <summary>发送自定义功能码命令</summary>
    /// <remarks>
    /// 通过公共 SendCommandAsync 重载发送消息，网络层自动检测异常响应并抛出 ModbusException。
    /// 成功响应交由用户注册的处理器解析。
    /// </remarks>
    /// <param name="code">功能码</param>
    /// <param name="host">从机站号</param>
    /// <param name="data">请求数据负载</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应负载，失败返回 null</returns>
    /// <exception cref="NotSupportedException">功能码未注册</exception>
    /// <exception cref="ModbusException">从机返回异常响应</exception>
    public virtual async Task<IPacket?> SendCustomCommandAsync(Byte code, Byte host, IPacket data, CancellationToken cancellationToken = default)
    {
        if (!CustomFunctionCodes.TryGetValue(code, out var handler))
            throw new NotSupportedException($"自定义功能码 {code} (0x{code:X2}) 未注册，请先调用 RegisterFunctionCode");

        using var span = Tracer?.NewSpan($"modbus:Custom-0x{code:X2}", $"host={host}");
        try
        {
            // 通过公共重载发送，网络层（ModbusIp/ModbusRtu）自动检测异常响应并抛出 ModbusException
            var payload = await SendCommandAsync((FunctionCodes)code, host, data, cancellationToken).ConfigureAwait(false);
            if (payload == null) return null;

            // 调用用户注册的处理器处理响应
            return await handler(host, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }
    #endregion

    #region 写入
    /// <summary>按功能码异步写入。用于IoT标准库</summary>
    /// <param name="code">功能码</param>
    /// <param name="host">主机</param>
    /// <param name="address">逻辑地址</param>
    /// <param name="values">待写入数值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public virtual async Task<Object> WriteAsync(FunctionCodes code, Byte host, UInt16 address, UInt16[] values, CancellationToken cancellationToken = default)
    {
        switch (code)
        {
            case FunctionCodes.WriteCoil: return await WriteCoilAsync(host, address, values[0], cancellationToken).ConfigureAwait(false);
            case FunctionCodes.WriteRegister: return await WriteRegisterAsync(host, address, values[0], cancellationToken).ConfigureAwait(false);
            case FunctionCodes.WriteCoils: return await WriteCoilsAsync(host, address, values, cancellationToken).ConfigureAwait(false);
            case FunctionCodes.WriteRegisters: return await WriteRegistersAsync(host, address, values, cancellationToken).ConfigureAwait(false);
            default:
                break;
        }

        throw new NotSupportedException($"ModbusWrite不支持[{code}]");
    }

    /// <summary>异步写入单线圈，0x05</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="value">输出值。一般是 0xFF00/0x0000</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>输出值</returns>
    public async Task<Int32> WriteCoilAsync(Byte host, UInt16 address, UInt16 value, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:WriteCoil", $"host={host} address={address}/0x{address:X4} value=0x{value:X4}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.WriteCoil, host, address, value, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 4) return -1;

            // 去掉2字节地址
            return rs.ReadBytes(2, 2).ToUInt16(0, false);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步写入保持寄存器，0x06</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="value">数值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>寄存器值</returns>
    public async Task<Int32> WriteRegisterAsync(Byte host, UInt16 address, UInt16 value, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:WriteRegister", $"host={host} address={address}/0x{address:X4} value=0x{value:X4}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.WriteRegister, host, address, value, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 4) return -1;

            // 去掉2字节地址
            return rs.ReadBytes(2, 2).ToUInt16(0, false);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步写多个线圈，0x0F</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="values">值。一般是 0xFF00/0x0000</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>数量</returns>
    public async Task<Int32> WriteCoilsAsync(Byte host, UInt16 address, UInt16[] values, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:WriteCoils", $"host={host} address={address}/0x{address:X4} values={values.Join("-", e => e.ToString("X4"))}");
        try
        {
            // 多个UInt16数值，合并成为负载数据
            var binary = new Binary { IsLittleEndian = false };
            binary.Write(address);
            binary.Write((UInt16)values.Length);

            // 字节数
            binary.Write((Byte)Math.Ceiling(values.Length / 8.0));

            // 数值转位
            var b = 0;
            var k = 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] != 0)
                    b |= 1 << k;

                if (k++ >= 7)
                {
                    binary.Write((Byte)b);
                    b = 0;
                    k = 0;
                }
            }
            if (k > 0) binary.Write((Byte)b);

            // 直接使用内存流缓冲区，避免拷贝
            binary.Stream.Position = 0;
            var pk = new ArrayPacket(binary.Stream);

            var rs = await SendCommandAsync(FunctionCodes.WriteCoils, host, pk, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 4) return -1;

            // 去掉2字节地址
            return rs.ReadBytes(2, 2).ToUInt16(0, false);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步写多个保持寄存器，0x10</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">地址。例如0x0002</param>
    /// <param name="values">数值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>寄存器数量</returns>
    public async Task<Int32> WriteRegistersAsync(Byte host, UInt16 address, UInt16[] values, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:WriteRegisters", $"host={host} address={address}/0x{address:X4} values={values.Join("-", e => e.ToString("X4"))}");
        try
        {
            // 多个UInt16数值，合并成为负载数据
            var binary = new Binary { IsLittleEndian = false };
            binary.Write(address);
            binary.Write((UInt16)values.Length);

            binary.Write((Byte)(values.Length * 2));
            foreach (var item in values)
            {
                binary.Write(item);
            }

            // 直接使用内存流缓冲区，避免拷贝
            binary.Stream.Position = 0;
            var pk = new ArrayPacket(binary.Stream);

            var rs = await SendCommandAsync(FunctionCodes.WriteRegisters, host, pk, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 4) return -1;

            // 去掉2字节地址
            return rs.ReadBytes(2, 2).ToUInt16(0, false);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步诊断，0x08。子功能码0x0000为Echo Test，返回与请求相同的数据</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="subCode">子功能码。0x0000=Echo Test（回环测试）</param>
    /// <param name="data">请求数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应数据，Echo Test时为回显数据；失败返回0</returns>
    public async Task<UInt16> DiagnosticsAsync(Byte host, UInt16 subCode, UInt16 data, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:Diagnostics", $"host={host} subCode=0x{subCode:X4} data=0x{data:X4}");
        try
        {
            var binary = new Binary { IsLittleEndian = false };
            binary.Write(subCode);
            binary.Write(data);
            binary.Stream.Position = 0;
            var pk = new ArrayPacket(binary.Stream);

            var rs = await SendCommandAsync(FunctionCodes.Diagnostics, host, pk, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 4) return 0;

            // 跳过2字节子功能码，读取2字节响应数据
            return rs.ReadBytes(2, 2).ToUInt16(0, false);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读写多个保持寄存器，0x17。先执行写入，再返回读取结果</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="readAddress">读取起始地址</param>
    /// <param name="readCount">读取寄存器数量</param>
    /// <param name="writeAddress">写入起始地址</param>
    /// <param name="writeValues">写入值数组</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取到的寄存器值数组</returns>
    public async Task<UInt16[]> ReadWriteRegistersAsync(Byte host, UInt16 readAddress, UInt16 readCount, UInt16 writeAddress, UInt16[] writeValues, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadWriteRegisters", $"host={host} readAddr={readAddress} readCount={readCount} writeAddr={writeAddress} writeCount={writeValues.Length}");
        try
        {
            var binary = new Binary { IsLittleEndian = false };
            binary.Write(readAddress);
            binary.Write(readCount);
            binary.Write(writeAddress);
            binary.Write((UInt16)writeValues.Length);
            binary.Write((Byte)(writeValues.Length * 2));
            foreach (var v in writeValues)
                binary.Write(v);

            binary.Stream.Position = 0;
            var pk = new ArrayPacket(binary.Stream);

            var rs = await SendCommandAsync(FunctionCodes.ReadWriteMultipleRegisters, host, pk, cancellationToken).ConfigureAwait(false);
            if (rs == null) return [];

            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            var len = reader.ReadByte();
            if (ValidResponse && rs.Total < 1 + len) return [];

            var bs = new UInt16[readCount];
            for (var i = 0; i < readCount; i++)
                bs[i] = reader.ReadUInt16();

            rs.TryDispose();
            return bs;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读设备标识码，0x2B（MEI传输，类型0x0E）</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设备标识信息字典，Key=ObjectId，Value=字符串值；失败返回null</returns>
    public async Task<IDictionary<Byte, String>?> ReadDevIdAsync(Byte host, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadDevId", $"host={host}");
        try
        {
            // 请求：MEIType=0x0E, ReadDevIdCode=0x01(Basic), ObjectId=0x00(从VendorName开始)
            var binary = new Binary { IsLittleEndian = false };
            binary.Write((Byte)0x0E);   // MEI Type
            binary.Write((Byte)0x01);   // ReadDevIdCode: Basic
            binary.Write((Byte)0x00);   // ObjectId: 0x00 起始
            binary.Stream.Position = 0;
            var pk = new ArrayPacket(binary.Stream);

            var rs = await SendCommandAsync(FunctionCodes.ReadDevId, host, pk, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 6) return null;

            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            _ = reader.ReadByte();  // MEI Type (0x0E)
            _ = reader.ReadByte();  // ReadDevIdCode
            _ = reader.ReadByte();  // ConformityLevel
            _ = reader.ReadByte();  // MoreFollows
            _ = reader.ReadByte();  // NextObjectId
            var count = reader.ReadByte();  // NumberOfObjects

            var result = new Dictionary<Byte, String>();
            for (var i = 0; i < count; i++)
            {
                var objId = reader.ReadByte();
                var objLen = reader.ReadByte();
                var objBytes = reader.ReadBytes(objLen).ToArray();
                result[objId] = System.Text.Encoding.ASCII.GetString(objBytes, 0, objBytes.Length);
            }

            rs.TryDispose();
            return result;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步写文件记录，0x15。将数据写入从机文件存储区</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="fileNumber">文件号（1-65535）</param>
    /// <param name="recordNumber">记录号（0-9999）</param>
    /// <param name="data">要写入的寄存器数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实际写入的寄存器数量；-1 表示失败</returns>
    public async Task<Int32> WriteFileRecordAsync(Byte host, UInt16 fileNumber, UInt16 recordNumber, UInt16[] data, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:WriteFileRecord", $"host={host} fileNumber={fileNumber} recordNumber={recordNumber} count={data.Length}");
        try
        {
            // 每条子请求 = ReferenceType(1B=6) + FileNo(2B) + RecordNo(2B) + RecordLen(2B) + Data(N*2B)
            var recordLen = (UInt16)data.Length;
            var subRequestLen = (Byte)(7 + recordLen * 2);  // 1+2+2+2=7 字节头部 + 数据

            var binary = new Binary { IsLittleEndian = false };
            binary.Write(subRequestLen);    // ByteCount
            binary.Write((Byte)6);          // ReferenceType = 6
            binary.Write(fileNumber);       // FileNumber
            binary.Write(recordNumber);     // RecordNumber
            binary.Write(recordLen);        // RecordLength（寄存器个数）
            foreach (var v in data)
                binary.Write(v);            // Data

            binary.Stream.Position = 0;
            var pk = new ArrayPacket(binary.Stream);

            var rs = await SendCommandAsync(FunctionCodes.WriteFileRecord, host, pk, cancellationToken).ConfigureAwait(false);
            if (rs == null) return -1;

            return data.Length;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读异常状态，0x07。读取从机的8个异常状态位</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>异常状态字节（每个位表示一个异常条件）；失败返回0</returns>
    public async Task<Byte> ReadExceptionStatusAsync(Byte host, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadExceptionStatus", $"host={host}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.ReadExceptionStatus, host, ArrayPacket.Empty, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 1) return 0;

            return rs.GetSpan()[0];
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步报告服务器ID，0x11。读取从机的服务器标识和运行状态</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器标识信息，包含ServerId、RunIndicator和附加数据；失败返回null</returns>
    public async Task<(Byte ServerId, Boolean RunIndicator, Byte[] AdditionalData)?> ReportServerIdAsync(Byte host, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReportServerId", $"host={host}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.ReportServerId, host, ArrayPacket.Empty, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 2) return null;

            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            var byteCount = reader.ReadByte();
            var serverId = reader.ReadByte();
            var runIndicator = reader.ReadByte() == 0xFF;
            var additionalData = reader.ReadBytes(byteCount - 2).ToArray();

            rs.TryDispose();
            return (serverId, runIndicator, additionalData);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步获取通信事件计数，0x0B。读取从机的通信状态和事件计数</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>通信状态和事件计数；失败返回null</returns>
    public async Task<(UInt16 Status, UInt16 EventCount)?> GetComEventCounterAsync(Byte host, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:GetComEventCounter", $"host={host}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.GetComEventCounter, host, ArrayPacket.Empty, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 4) return null;

            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            var status = reader.ReadUInt16();
            var eventCount = reader.ReadUInt16();
            rs.TryDispose();
            return (status, eventCount);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步获取通信事件日志，0x0C。读取从机的通信事件日志</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>通信状态、事件计数、消息计数和事件字节列表；失败返回null</returns>
    public async Task<(UInt16 Status, UInt16 EventCount, UInt16 MessageCount, Byte[] Events)?> GetComEventLogAsync(Byte host, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:GetComEventLog", $"host={host}");
        try
        {
            var rs = await SendCommandAsync(FunctionCodes.GetComEventLog, host, ArrayPacket.Empty, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 7) return null;

            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            var byteCount = reader.ReadByte();
            if (rs.Total < 1 + byteCount) return null;
            var status = reader.ReadUInt16();
            var eventCount = reader.ReadUInt16();
            var messageCount = reader.ReadUInt16();
            var events = reader.ReadBytes(messageCount).ToArray();
            rs.TryDispose();
            return (status, eventCount, messageCount, events);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步屏蔽写寄存器，0x16。通过AND/OR掩码原子修改单个寄存器的指定位</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="address">寄存器地址</param>
    /// <param name="andMask">AND掩码。寄存器当前值与AND掩码做按位与</param>
    /// <param name="orMask">OR掩码。上一步结果与OR掩码做按位或</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>屏蔽后的寄存器值；失败返回0</returns>
    /// <remarks>
    /// 算法：Result = (CurrentValue &amp; AND_Mask) | (OR_Mask &amp; ~AND_Mask)
    /// 可用于原子地设置/清除寄存器中的特定位，不影响其他位。
    /// </remarks>
    public async Task<UInt16> MaskWriteRegisterAsync(Byte host, UInt16 address, UInt16 andMask, UInt16 orMask, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:MaskWriteRegister", $"host={host} address={address}/0x{address:X4} andMask=0x{andMask:X4} orMask=0x{orMask:X4}");
        try
        {
            var buf = new Byte[6];
            buf.Write(address, 0, false);
            buf.Write(andMask, 2, false);
            buf.Write(orMask, 4, false);

            var rs = await SendCommandAsync(FunctionCodes.MaskWriteRegister, host, (ArrayPacket)buf, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 6) return 0;

            // 响应回显：地址 + AND_Mask + OR_Mask
            return rs.ReadBytes(2, 2).ToUInt16(0, false);
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读文件记录，0x14。从从机文件存储区读取记录数据</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="fileNumber">文件号（1-65535）</param>
    /// <param name="recordNumber">记录号（0-9999）</param>
    /// <param name="recordLength">要读取的寄存器数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>记录数据（寄存器值数组）；失败返回空数组</returns>
    public async Task<UInt16[]> ReadFileRecordAsync(Byte host, UInt16 fileNumber, UInt16 recordNumber, UInt16 recordLength, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadFileRecord", $"host={host} fileNumber={fileNumber} recordNumber={recordNumber} length={recordLength}");
        try
        {
            // 请求：ByteCount(1B) + ReferenceType(1B=6) + FileNumber(2B) + RecordNumber(2B) + RecordLength(2B)
            var subRequestLen = (Byte)7;  // 1+2+2+2=7 字节
            var binary = new Binary { IsLittleEndian = false };
            binary.Write(subRequestLen);    // ByteCount
            binary.Write((Byte)6);          // ReferenceType = 6
            binary.Write(fileNumber);       // FileNumber
            binary.Write(recordNumber);     // RecordNumber
            binary.Write(recordLength);     // RecordLength（寄存器个数）
            binary.Stream.Position = 0;
            var pk = new ArrayPacket(binary.Stream);

            var rs = await SendCommandAsync(FunctionCodes.ReadFileRecord, host, pk, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 2) return [];

            // 响应：ByteCount(1B) + ReferenceType(1B=6) + RecordDataLength(1B) + RecordData(N×2B)
            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            _ = reader.ReadByte();  // ByteCount
            _ = reader.ReadByte();  // ReferenceType (=6)
            var dataLen = reader.ReadByte();  // RecordDataLength（字节数）
            var regCount = dataLen / 2;
            var result = new UInt16[regCount];
            for (var i = 0; i < regCount; i++)
                result[i] = reader.ReadUInt16();

            rs.TryDispose();
            return result;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }

    /// <summary>异步读FIFO队列，0x18。从从机FIFO队列中读取数据</summary>
    /// <param name="host">主机。一般是1</param>
    /// <param name="fifoAddress">FIFO指针地址（指向队列的寄存器地址）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>FIFO队列中的寄存器值数组；失败返回空数组</returns>
    public async Task<UInt16[]> ReadFifoQueueAsync(Byte host, UInt16 fifoAddress, CancellationToken cancellationToken = default)
    {
        using var span = Tracer?.NewSpan("modbus:ReadFifoQueue", $"host={host} fifoAddress={fifoAddress}/0x{fifoAddress:X4}");
        try
        {
            var buf = new Byte[2];
            buf.Write(fifoAddress, 0, false);
            var rs = await SendCommandAsync(FunctionCodes.ReadFifoQueue, host, (ArrayPacket)buf, cancellationToken).ConfigureAwait(false);
            if (rs == null || rs.Total < 4) return [];

            // 响应：ByteCount(2B,高字节在前) + FIFOCount(2B,高字节在前) + FIFOValues(N×2B)
            var reader = new SpanReader(rs.GetSpan()) { IsLittleEndian = false };
            var byteCount = reader.ReadUInt16();  // 后续字节数（不含自身）
            var fifoCount = reader.ReadUInt16();  // FIFO中的寄存器数量
            if (byteCount < 2 + fifoCount * 2) return [];

            var result = new UInt16[fifoCount];
            for (var i = 0; i < fifoCount; i++)
                result[i] = reader.ReadUInt16();

            rs.TryDispose();
            return result;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            throw;
        }
    }
    #endregion

    #region 同步兼容桥接（过渡期，供旧版调用方使用）
    /// <summary>打开连接</summary>
    public void Open() => OpenAsync().GetAwaiter().GetResult();

    /// <summary>发送命令并接收返回</summary>
    public IPacket? SendCommand(FunctionCodes code, Byte host, UInt16 address, UInt16 value) =>
        SendCommandAsync(code, host, address, value).GetAwaiter().GetResult();

    /// <summary>发送命令并接收返回</summary>
    public IPacket? SendCommand(FunctionCodes code, Byte host, IPacket data) =>
        SendCommandAsync(code, host, data).GetAwaiter().GetResult();

    /// <summary>按功能码读取</summary>
    public IPacket? Read(FunctionCodes code, Byte host, UInt16 address, UInt16 count) =>
        ReadAsync(code, host, address, count).GetAwaiter().GetResult();

    /// <summary>按功能码写入</summary>
    public Object Write(FunctionCodes code, Byte host, UInt16 address, UInt16[] values) =>
        WriteAsync(code, host, address, values).GetAwaiter().GetResult();

    /// <summary>读取线圈，0x01</summary>
    public Boolean[] ReadCoil(Byte host, UInt16 address, UInt16 count) =>
        ReadCoilAsync(host, address, count).GetAwaiter().GetResult();

    /// <summary>读离散量输入，0x02</summary>
    public Boolean[] ReadDiscrete(Byte host, UInt16 address, UInt16 count) =>
        ReadDiscreteAsync(host, address, count).GetAwaiter().GetResult();

    /// <summary>读取保持寄存器，0x03</summary>
    public UInt16[] ReadRegister(Byte host, UInt16 address, UInt16 count) =>
        ReadRegisterAsync(host, address, count).GetAwaiter().GetResult();

    /// <summary>读取输入寄存器，0x04</summary>
    public UInt16[] ReadInput(Byte host, UInt16 address, UInt16 count) =>
        ReadInputAsync(host, address, count).GetAwaiter().GetResult();

    /// <summary>写单线圈，0x05</summary>
    public Int32 WriteCoil(Byte host, UInt16 address, UInt16 value) =>
        WriteCoilAsync(host, address, value).GetAwaiter().GetResult();

    /// <summary>写单寄存器，0x06</summary>
    public Int32 WriteRegister(Byte host, UInt16 address, UInt16 value) =>
        WriteRegisterAsync(host, address, value).GetAwaiter().GetResult();

    /// <summary>写多线圈，0x0F</summary>
    public Int32 WriteCoils(Byte host, UInt16 address, UInt16[] values) =>
        WriteCoilsAsync(host, address, values).GetAwaiter().GetResult();

    /// <summary>写多寄存器，0x10</summary>
    public Int32 WriteRegisters(Byte host, UInt16 address, UInt16[] values) =>
        WriteRegistersAsync(host, address, values).GetAwaiter().GetResult();

    /// <summary>诊断，0x08</summary>
    public UInt16 Diagnostics(Byte host, UInt16 subCode, UInt16 data) =>
        DiagnosticsAsync(host, subCode, data).GetAwaiter().GetResult();

    /// <summary>读写多寄存器，0x17</summary>
    public UInt16[] ReadWriteRegisters(Byte host, UInt16 readAddress, UInt16 readCount, UInt16 writeAddress, UInt16[] writeValues) =>
        ReadWriteRegistersAsync(host, readAddress, readCount, writeAddress, writeValues).GetAwaiter().GetResult();

    /// <summary>读取设备标识，0x2B</summary>
    public IDictionary<Byte, String>? ReadDevId(Byte host, Byte objectId = 0x00) =>
        ReadDevIdAsync(host).GetAwaiter().GetResult();

    /// <summary>写文件记录，0x15</summary>
    public Int32 WriteFileRecord(Byte host, UInt16 fileNumber, UInt16 recordNumber, UInt16[] data) =>
        WriteFileRecordAsync(host, fileNumber, recordNumber, data).GetAwaiter().GetResult();

    /// <summary>获取通信事件计数，0x0B</summary>
    public (UInt16 Status, UInt16 EventCount)? GetComEventCounter(Byte host) =>
        GetComEventCounterAsync(host).GetAwaiter().GetResult();

    /// <summary>获取通信事件日志，0x0C</summary>
    public (UInt16 Status, UInt16 EventCount, UInt16 MessageCount, Byte[] Events)? GetComEventLog(Byte host) =>
        GetComEventLogAsync(host).GetAwaiter().GetResult();

    /// <summary>读异常状态，0x07</summary>
    public Byte ReadExceptionStatus(Byte host) =>
        ReadExceptionStatusAsync(host).GetAwaiter().GetResult();

    /// <summary>报告服务器ID，0x11</summary>
    public (Byte ServerId, Boolean RunIndicator, Byte[] AdditionalData)? ReportServerId(Byte host) =>
        ReportServerIdAsync(host).GetAwaiter().GetResult();

    /// <summary>屏蔽写寄存器，0x16</summary>
    public UInt16 MaskWriteRegister(Byte host, UInt16 address, UInt16 andMask, UInt16 orMask) =>
        MaskWriteRegisterAsync(host, address, andMask, orMask).GetAwaiter().GetResult();

    /// <summary>读文件记录，0x14</summary>
    public UInt16[] ReadFileRecord(Byte host, UInt16 fileNumber, UInt16 recordNumber, UInt16 recordLength) =>
        ReadFileRecordAsync(host, fileNumber, recordNumber, recordLength).GetAwaiter().GetResult();

    /// <summary>读FIFO队列，0x18</summary>
    public UInt16[] ReadFifoQueue(Byte host, UInt16 fifoAddress) =>
        ReadFifoQueueAsync(host, fifoAddress).GetAwaiter().GetResult();
    #endregion

    #region 日志
    /// <summary>日志</summary>
    public ILog? Log { get; set; }

    /// <summary>写日志</summary>
    /// <param name="format"></param>
    /// <param name="args"></param>
    public void WriteLog(String format, params Object[] args) => Log?.Info(format, args);
    #endregion
}