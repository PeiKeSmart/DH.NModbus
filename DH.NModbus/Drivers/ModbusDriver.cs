using System.Diagnostics;
using NewLife.Data;
using NewLife.IoT.Protocols;
using NewLife.IoT.ThingModels;
using NewLife.IoT.ThingSpecification;
using NewLife.Reflection;

namespace NewLife.IoT.Drivers;

/// <summary>Modbus协议驱动</summary>
/// <remarks>
/// 每个串口或Tcp/Udp从站地址，对应一个Modbus驱动实例，避免多个虚拟设备实例化多个驱动实例导致串口争夺。
/// 该唯一性由驱动工厂DriverFactory来保证。
/// </remarks>
public abstract class ModbusDriver : DriverBase
{
    #region 属性
    /// <summary>
    /// Modbus通道
    /// </summary>
    public Modbus Modbus { get; set; } = null!;

    private Int32 _nodes;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    #endregion

    #region 构造
    /// <summary>
    /// 销毁时，关闭连接
    /// </summary>
    /// <param name="disposing"></param>
    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);

        Modbus.TryDispose();
        Modbus = null!;
    }
    #endregion

    #region 元数据
    #endregion

    #region 方法
    /// <summary>
    /// 创建Modbus通道
    /// </summary>
    /// <param name="device">逻辑设备</param>
    /// <param name="node">设备节点</param>
    /// <param name="parameter">参数</param>
    /// <returns></returns>
    internal protected abstract Modbus CreateModbus(IDevice device, ModbusNode node, ModbusParameter parameter);

    /// <summary>
    /// 打开通道。一个ModbusTcp设备可能分为多个通道读取，需要共用Tcp连接，以不同节点区分
    /// </summary>
    /// <param name="device">通道</param>
    /// <param name="parameter">参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public override async Task<INode> OpenAsync(IDevice device, IDriverParameter? parameter, CancellationToken cancellationToken = default)
    {
        var p = parameter as ModbusParameter ?? new ModbusParameter();

        var node = new ModbusNode
        {
            Host = p.Host,
            ReadCode = p.ReadCode,
            WriteCode = p.WriteCode,

            Driver = this,
            Device = device,
            Parameter = p,
        };

        // 实例化一次Tcp连接
        if (Modbus == null)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Modbus == null)
                {
                    var modbus = CreateModbus(device, node, p);
                    if (p.Timeout > 0) modbus.Timeout = p.Timeout;

                    // 外部已指定通道时，打开连接
                    if (device != null) await modbus.OpenAsync(cancellationToken).ConfigureAwait(false);

                    Modbus = modbus;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        Interlocked.Increment(ref _nodes);

        return node;
    }

    /// <summary>
    /// 关闭设备驱动
    /// </summary>
    /// <param name="node">节点对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    public override async Task CloseAsync(INode node, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Decrement(ref _nodes) <= 0)
        {
            var modbus = Modbus;
            if (modbus != null)
                await modbus.CloseAsync(cancellationToken).ConfigureAwait(false);

            Modbus.TryDispose();
            Modbus = null!;
        }
    }

    /// <summary>
    /// 读取数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="points">点位集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public override async Task<ReadResult> ReadAsync(INode node, IPoint[] points, CancellationToken cancellationToken = default)
    {
        if (points == null || points.Length == 0)
            return ReadResult.Success([], []);

        var n = (node as ModbusNode)!;
        var p = (node.Parameter as ModbusParameter)!;

        var list = BuildSegments(points, p);

        // 加锁，避免冲突
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 分段整体读取
            for (var i = 0; i < list.Count; i++)
            {
                var seg = list[i];

                if (seg.ReadCode == 0) seg.ReadCode = n.ReadCode;

                // 读取线圈时，个数向8对齐
                if (seg.ReadCode == FunctionCodes.ReadCoil)
                {
                    var y = seg.Count % 8;
                    seg.Count += 8 - y;
                }

                // 其中一项读取报错时，直接跳过，不要影响其它批次
                try
                {
                    seg.Data = (await Modbus.ReadAsync(seg.ReadCode, n.Host, (UInt16)seg.Address, (UInt16)seg.Count, cancellationToken).ConfigureAwait(false))?.ReadBytes();
                }
                catch (ModbusException ex)
                {
                    Log?.Error(ex.ToString());
                }

                // 读取时延迟一点时间
                if (i < list.Count - 1 && p.BatchDelay > 0) await Task.Delay(p.BatchDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _semaphore.Release();
        }

        // 分割数据
        var dic = Dispatch(points, list);

        // 借助物模型转换数据类型
        var spec = node.Device?.Specification;
        if (spec != null)
        {
            foreach (var item in dic)
            {
                var pt = points.FirstOrDefault(e => e.Name == item.Key);
                if (pt != null && item.Value is Byte[] data)
                {
                    var v = spec.Decode(data, pt);
                    if (v != null) dic[item.Key] = v;
                }
            }
        }

        // 构造 ReadResult
        var resultPoints = new IPoint[dic.Count];
        var resultValues = new Object?[dic.Count];
        var idx = 0;
        foreach (var kv in dic)
        {
            resultPoints[idx] = points.First(e => (e.Name ?? e.Address) == kv.Key);
            resultValues[idx] = kv.Value;
            idx++;
        }

        return ReadResult.Success(resultPoints, resultValues);
    }

    internal IList<Segment> BuildSegments(IList<IPoint> points, ModbusParameter p)
    {
        // 组合多个片段，减少读取次数
        var list = new List<Segment>();
        foreach (var point in points)
        {
            if (!point.Address.IsNullOrEmpty() && ModbusAddress.TryParse(point.Address, out var maddr))
            {
                list.Add(new Segment
                {
                    ReadCode = maddr.GetReadCode(),
                    Address = maddr.Address,
                    Count = GetCount(point)
                });
            }
        }
        list = list.OrderBy(e => e.ReadCode).ThenBy(e => e.Address).ThenByDescending(e => e.Count).ToList();

        //// 只有读寄存器合并，其它指令合并可能有问题，将来再优化
        //if (!merge) return list;
        //if (list.Any(e => e.ReadCode != FunctionCodes.ReadRegister && e.ReadCode != FunctionCodes.ReadInput)) return list;

        var step = p.BatchStep > 1 ? p.BatchStep : 1;
        var k = 1;
        var rs = new List<Segment>();
        var prv = list[0];
        rs.Add(prv);
        for (var i = 1; i < list.Count; i++)
        {
            var cur = list[i];

            // 前一段末尾碰到了当前段开始，可以合并
            var flag = prv.Address + prv.Count + step > cur.Address;
            // 如果是读取线圈，间隔小于8都可以合并
            if (!flag && cur.ReadCode == FunctionCodes.ReadCoil)
            {
                flag = prv.Address + prv.Count + 8 > cur.Address;
            }

            // 前一段末尾碰到了当前段开始，可以合并
            if (flag && prv.ReadCode == cur.ReadCode)
            {
                if (p.BatchSize <= 0 || k < p.BatchSize)
                {
                    // 要注意，可能前后重叠，也可能前面区域比后面还大
                    var size = cur.Address + cur.Count - prv.Address;
                    if (size > prv.Count) prv.Count = size;

                    // 连续合并数累加
                    k++;
                }
                else
                {
                    rs.Add(cur);

                    prv = cur;
                    k = 1;
                }
            }
            else
            {
                rs.Add(cur);

                prv = cur;
                k = 1;
            }
        }

        return rs;
    }

    internal IDictionary<String, Object?> Dispatch(IPoint[] points, IList<Segment> segments)
    {
        var dic = new Dictionary<String, Object?>();
        if (segments == null || segments.Count == 0) return dic;

        foreach (var point in points)
        {
            if (point.Address.IsNullOrEmpty() || !ModbusAddress.TryParse(point.Address, out var maddr))
                continue;

            var name = point.Name ?? point.Address;
            var count = GetCount(point);

            // 找到片段 需要补充类型过滤参数避免不同类型相同地址取值错误问题
            var seg = segments.FirstOrDefault(e => e.Address <= maddr.Address && maddr.Address + count <= e.Address + e.Count && (maddr.Range == null || e.ReadCode == maddr.Range.ReadCode));
            if (seg != null && seg.Data != null)
            {
                var code = seg.ReadCode;
                if (code is FunctionCodes.ReadRegister or FunctionCodes.ReadInput)
                {
                    // 校验数据完整性
                    var offset = (maddr.Address - seg.Address) * 2;
                    var size = count * 2;
                    if (seg.Data.Length >= offset + size)
                        dic[name] = seg.Data.ReadBytes(offset, size);
                }
                else if (code is FunctionCodes.ReadCoil or FunctionCodes.ReadDiscrete)
                {
                    // 计算偏移，每8位一个字节，地址低3位是该字节内的偏移量
                    var offset = maddr.Address - seg.Address;
                    var idx = offset >> 3;
                    offset &= 0x07;
                    if (seg.Data.Length >= idx)
                        dic[name] = (seg.Data[idx] >> offset) & 0x01;
                }
                else
                    throw new NotSupportedException($"无法拆分{code}");
            }
        }
        return dic;
    }

    [DebuggerDisplay("{ReadCode}({Address}, {Count})")]
    internal class Segment
    {
        public FunctionCodes ReadCode { get; set; }

        public Int32 Address { get; set; }

        public Int32 Count { get; set; }

        public Byte[]? Data { get; set; }
    }

    /// <summary>
    /// 从点位中计算寄存器个数
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public virtual Int32 GetCount(IPoint point)
    {
        // 字节数转寄存器数，要除以2
        var count = point.GetLength() / 2;
        return count > 0 ? count : 1;
    }

    /// <summary>
    /// 写入数据
    /// </summary>
    /// <param name="node">节点对象，可存储站号等信息，仅驱动自己识别</param>
    /// <param name="requests">写入请求数组，每项含目标点位和值</param>
    /// <param name="cancellationToken">取消令牌</param>
    public override async Task<WriteResult> WriteAsync(INode node, WriteRequest[] requests, CancellationToken cancellationToken = default)
    {
        var n = (node as ModbusNode)!;
        var spec = node.Device?.Specification;
        var count = 0;

        foreach (var req in requests)
        {
            var point = req.Point;
            var value = req.Value;

            if (value == null || point == null) continue;
            if (point.Address.IsNullOrEmpty()) continue;
            if (!ModbusAddress.TryParse(point.Address, out var maddr)) continue;

            var code = maddr.GetWriteCode();
            if (code == 0) code = n.WriteCode;

            // 借助物模型转换数据类型
            if (spec != null && value is not Byte[])
            {
                value = spec.Encode(value, point);
            }

            UInt16[] vs;
            if (value is Byte[] buf)
            {
                vs = new UInt16[(Int32)Math.Ceiling(buf.Length / 2d)];
                for (var i = 0; i < vs.Length; i++)
                {
                    vs[i] = buf.ToUInt16(i * 2, false);
                }
            }
            else
            {
                // 根据写入操作码决定转换为线圈还是寄存器
                if (code == FunctionCodes.WriteCoil || code == FunctionCodes.WriteCoils)
                    vs = ConvertToCoil(value, point, spec);
                else
                    vs = ConvertToRegister(value, point, spec);

                if (vs == null || vs.Length == 0) throw new NotSupportedException($"点位[{point.Name}][Type={point.Type}]不支持数据[{value}]");
            }

            // 加锁，避免冲突
            Object? echo;
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                echo = await Modbus.WriteAsync(code, n.Host, maddr.Address, vs, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }

            // 单点写入：直接返回成功带回显
            if (requests.Length == 1)
                return WriteResult.Success(echo);

            count++;
        }

        return WriteResult.SuccessBatch(count);
    }

    /// <summary>原始数据转为线圈</summary>
    /// <param name="data"></param>
    /// <param name="point"></param>
    /// <param name="spec"></param>
    /// <returns></returns>
    protected virtual UInt16[] ConvertToCoil(Object? data, IPoint point, ThingSpec? spec)
    {
        var type = TypeHelper.GetNetType(point);
        if (type == null)
        {
            // 找到物属性定义
            var pi = spec?.Properties?.FirstOrDefault(e => e.Id.EqualIgnoreCase(point.Name));
            type = TypeHelper.GetNetType(pi?.DataType?.Type);
        }
        if (type == null) return [];

        return type.GetTypeCode() switch
        {
            TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte => data.ToBoolean() ? [0xFF00] : [0x00],
            TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 => data.ToInt() > 0 ? [0xFF00] : [0x00],
            TypeCode.Int64 or TypeCode.UInt64 => data.ToLong() > 0 ? [0xFF00] : [0x00],
            _ => data.ToBoolean() ? [0xFF00] : [0x00],
        };
    }

    /// <summary>原始数据转寄存器数组</summary>
    /// <param name="data"></param>
    /// <param name="point"></param>
    /// <param name="spec"></param>
    /// <returns></returns>
    protected virtual UInt16[] ConvertToRegister(Object? data, IPoint point, ThingSpec? spec)
    {
        var type = TypeHelper.GetNetType(point);
        if (type == null)
        {
            // 找到物属性定义
            var pi = spec?.Properties?.FirstOrDefault(e => e.Id.EqualIgnoreCase(point.Name));
            type = TypeHelper.GetNetType(pi?.DataType?.Type);
        }
        if (type == null) return [];

        switch (type.GetTypeCode())
        {
            case TypeCode.Boolean:
            case TypeCode.Byte:
            case TypeCode.SByte:
                return data.ToBoolean() ? [0xFF00] : [0x00];
            case TypeCode.Int16:
            case TypeCode.UInt16:
                return [(UInt16)data.ToInt()];
            case TypeCode.Int32:
            case TypeCode.UInt32:
                {
                    var n = data.ToInt();
                    return [(UInt16)(n >> 16), (UInt16)(n & 0xFFFF)];
                }
            case TypeCode.Int64:
            case TypeCode.UInt64:
                {
                    var n = data.ToLong();
                    return [(UInt16)(n >> 48), (UInt16)(n >> 32), (UInt16)(n >> 16), (UInt16)(n & 0xFFFF)];
                }
            case TypeCode.Single:
                {
                    var d = (Single)data.ToDouble();
                    //var n = BitConverter.SingleToInt32Bits(d);
                    var n = (UInt32)d;
                    return [(UInt16)(n >> 16), (UInt16)(n & 0xFFFF)];
                }
            case TypeCode.Double:
                {
                    var d = (Double)data.ToDouble();
                    //var n = BitConverter.DoubleToInt64Bits(d);
                    var n = (UInt64)d;
                    return [(UInt16)(n >> 48), (UInt16)(n >> 32), (UInt16)(n >> 16), (UInt16)(n & 0xFFFF)];
                }
            case TypeCode.Decimal:
                {
                    var d = data.ToDecimal();
                    var n = (UInt64)d;
                    return [(UInt16)(n >> 48), (UInt16)(n >> 32), (UInt16)(n >> 16), (UInt16)(n & 0xFFFF)];
                }
            //case TypeCode.String:
            //    break;
            default:
                return [];
        }
    }

    ///// <summary>
    ///// 控制设备，特殊功能使用
    ///// </summary>
    ///// <param name="node"></param>
    ///// <param name="parameters"></param>
    ///// <exception cref="NotImplementedException"></exception>
    //public override void Control(INode node, IDictionary<String, Object> parameters) => throw new NotImplementedException();
    #endregion
}