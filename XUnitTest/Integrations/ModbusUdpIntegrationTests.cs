using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Integrations;

/// <summary>
/// ModbusUdp UDP 回环集成测试夹具
/// <para>
/// 无需硬件，使用 UdpClient 回环模拟 Modbus/UDP 从机：
/// 收到 MBAP 帧后解析 ModbusIpMessage，按 FC 处理寄存器/线圈数据，
/// 将响应封装为 ModbusIpMessage 后通过 UDP 返回。
/// </para>
/// </summary>
public class ModbusUdpFixture : IDisposable
{
    /// <summary>服务器监听端口（随机分配）</summary>
    public Int32 Port { get; }

    private readonly UdpClient _server;
    private volatile Boolean _running = true;
    private readonly Thread _thread;

    // 从机站号
    private readonly Byte _host = 1;

    // 寄存器区：地址 0-19，初始值 400+i
    internal readonly List<RegisterUnit> Registers;

    // 线圈区：地址 0-31，奇数地址为 ON
    internal readonly List<CoilUnit> Coils;

    public ModbusUdpFixture()
    {
        _server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_server.Client.LocalEndPoint!).Port;

        Registers = Enumerable.Range(0, 20)
            .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(400 + i) })
            .ToList();

        Coils = Enumerable.Range(0, 32)
            .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
            .ToList();

        _thread = new Thread(Loop) { IsBackground = true, Name = "ModbusUdp-Server" };
        _thread.Start();

        // 等待服务线程就绪
        Thread.Sleep(50);
    }

    private void Loop()
    {
        _server.Client.ReceiveTimeout = 500;

        while (_running)
        {
            try
            {
                var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                var data = _server.Receive(ref remoteEP);
                if (data == null || data.Length == 0) continue;

                var response = HandleRequest(data);
                if (response != null && response.Length > 0)
                    _server.Send(response, response.Length, remoteEP);
            }
            catch (SocketException)
            {
                // 接收超时或服务器已停止
            }
            catch
            {
                // 忽略其他异常
            }
        }
    }

    /// <summary>处理一个 MBAP 帧，返回 MBAP 响应字节；帧非法返回 null</summary>
    private Byte[]? HandleRequest(Byte[] data)
    {
        var req = ModbusIpMessage.Read(data.AsSpan());
        if (req == null) return null;

        // 站号过滤
        if (_host != 0 && req.Host != _host && req.Host != 0) return null;

        var rs = req.CreateReply();
        IPacket? payload = null;

        switch (req.Code)
        {
            case FunctionCodes.ReadRegister:
            case FunctionCodes.ReadInput:
                payload = BuildRegisterPayload(req);
                break;

            case FunctionCodes.ReadCoil:
            case FunctionCodes.ReadDiscrete:
                payload = BuildCoilPayload(req);
                break;

            case FunctionCodes.WriteRegister:
                payload = ProcessWriteRegister(req);
                break;

            case FunctionCodes.WriteCoil:
                payload = ProcessWriteCoil(req);
                break;

            default:
                // EC01 IllegalFunction
                rs.Code = (FunctionCodes)((Byte)req.Code | 0x80);
                rs.ErrorCode = ErrorCodes.IllegalFunction;
                payload = (ArrayPacket)new Byte[] { (Byte)ErrorCodes.IllegalFunction };
                break;
        }

        if (payload == null) return null;

        rs.Payload = payload;
        return rs.ToPacket().ReadBytes();
    }

    private IPacket? BuildRegisterPayload(ModbusMessage msg)
    {
        if (Registers.Count == 0) return null;

        var (reqAddr, reqCount) = msg.GetRequest();
        var offset = reqAddr - Registers[0].Address;
        if (offset < 0 || offset + reqCount > Registers.Count) return null;

        var values = Registers.Skip(offset).Take(reqCount).SelectMany(r => r.GetData()).ToArray();
        var buf = new Byte[1 + values.Length];
        buf[0] = (Byte)values.Length;
        Array.Copy(values, 0, buf, 1, values.Length);
        return (ArrayPacket)buf;
    }

    private IPacket? BuildCoilPayload(ModbusMessage msg)
    {
        if (Coils.Count == 0) return null;

        var (reqAddr, reqCount) = msg.GetRequest();
        var offset = reqAddr - Coils[0].Address;
        if (offset < 0 || offset + reqCount > Coils.Count) return null;

        var cs = Coils.Skip(offset).Take(reqCount).ToList();
        var byteCount = (Int32)Math.Ceiling(reqCount / 8.0);
        var buf = new Byte[1 + byteCount];
        buf[0] = (Byte)byteCount;
        for (var i = 0; i < byteCount; i++)
        {
            var b = 0;
            var max = Math.Min(8, reqCount - i * 8);
            for (var j = 0; j < max; j++)
                if (cs[i * 8 + j].Value > 0) b |= 1 << j;
            buf[1 + i] = (Byte)b;
        }
        return (ArrayPacket)buf;
    }

    private IPacket? ProcessWriteRegister(ModbusMessage msg)
    {
        if (msg.Payload == null || msg.Payload.Total < 4) return null;

        var reqAddr = msg.GetAddress();
        var value = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);
        var offset = reqAddr - Registers[0].Address;
        if (offset >= 0 && offset < Registers.Count)
            Registers[offset].Value = value;

        return msg.Payload;
    }

    private IPacket? ProcessWriteCoil(ModbusMessage msg)
    {
        if (msg.Payload == null || msg.Payload.Total < 4) return null;

        var reqAddr = msg.GetAddress();
        var value = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);
        var offset = reqAddr - Coils[0].Address;
        if (offset >= 0 && offset < Coils.Count)
            Coils[offset].Value = value == 0xFF00 ? (Byte)1 : (Byte)0;

        return msg.Payload;
    }

    public void Dispose()
    {
        _running = false;
        _server.Close();
    }
}

/// <summary>ModbusUdp 端到端集成测试（MBAP over UDP，无需硬件）</summary>
/// <remarks>
/// 使用 IClassFixture 共享 UDP 服务器。地址分区规则：
///   读测试 → 地址 0-9（只读，不被写测试修改）
///   写测试 → 地址 10-19（写后立即读回验证）
/// </remarks>
public class ModbusUdpIntegrationTests : IClassFixture<ModbusUdpFixture>
{
    private readonly ModbusUdpFixture _fixture;

    public ModbusUdpIntegrationTests(ModbusUdpFixture fixture) => _fixture = fixture;

    private async Task<ModbusUdp> CreateClientAsync()
    {
        var client = new ModbusUdp
        {
            Server = $"udp://127.0.0.1:{_fixture.Port}",
            Timeout = 3000,
        };
        await client.OpenAsync();
        return client;
    }

    #region 读取保持寄存器（FC03）

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC03 读多个保持寄存器值与从机一致")]
    public async Task ReadRegister_Multiple_MatchesSlave()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 0, 5);
        Assert.NotNull(rs);
        Assert.Equal(5, rs.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal((UInt16)(400 + i), rs[i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC03 读单个寄存器")]
    public async Task ReadRegister_Single_FirstAddress()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 0, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)400, rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC03 读中间范围寄存器值正确")]
    public async Task ReadRegister_MiddleRange_Correct()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 3, 4);
        Assert.NotNull(rs);
        Assert.Equal(4, rs.Length);
        for (var i = 0; i < 4; i++)
            Assert.Equal((UInt16)(403 + i), rs[i]);
    }

    #endregion

    #region 读取输入寄存器（FC04）

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC04 读输入寄存器与寄存器区一致")]
    public async Task ReadInput_MatchesSlave()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadInputAsync(1, 0, 3);
        Assert.NotNull(rs);
        Assert.Equal(3, rs.Length);
        for (var i = 0; i < 3; i++)
            Assert.Equal((UInt16)(400 + i), rs[i]);
    }

    #endregion

    #region 读取线圈（FC01）

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC01 读线圈，奇偶交替模式正确")]
    public async Task ReadCoil_AlternatingPattern()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadCoilAsync(1, 0, 8);
        Assert.NotNull(rs);
        Assert.Equal(8, rs.Length);
        for (var i = 0; i < 8; i++)
            Assert.Equal(i % 2 == 1, rs[i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC01 读单个线圈（偶数地址为 OFF）")]
    public async Task ReadCoil_EvenAddress_IsFalse()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadCoilAsync(1, 0, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.False(rs[0]);
    }

    #endregion

    #region 读取离散输入（FC02）

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC02 读离散输入与线圈区一致")]
    public async Task ReadDiscrete_AlternatingPattern()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadDiscreteAsync(1, 0, 4);
        Assert.NotNull(rs);
        Assert.Equal(4, rs.Length);
        for (var i = 0; i < 4; i++)
            Assert.Equal(i % 2 == 1, rs[i]);
    }

    #endregion

    #region 写入保持寄存器（FC06，地址 10-19）

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC06 写单寄存器后读回一致")]
    public async Task WriteRegister_ThenRead_RoundTrip()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegisterAsync(1, 10, 0x1234);
        Assert.Equal(0x1234, rs);

        var regs = await client.ReadRegisterAsync(1, 10, 1);
        Assert.NotNull(regs);
        Assert.Equal((UInt16)0x1234, regs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC06 写最大值 0xFFFF 后读回一致")]
    public async Task WriteRegister_MaxValue_RoundTrip()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegisterAsync(1, 11, 0xFFFF);
        Assert.Equal(0xFFFF, rs);

        var regs = await client.ReadRegisterAsync(1, 11, 1);
        Assert.NotNull(regs);
        Assert.Equal((UInt16)0xFFFF, regs[0]);
    }

    #endregion

    #region 写入线圈（FC05）

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC05 写线圈 ON 后读回为 true")]
    public async Task WriteCoil_On_ThenReadBack_True()
    {
        using var client = await CreateClientAsync();
        // 地址 16 默认 OFF（16%2==0），写 0xFF00 表示 ON
        await client.WriteCoilAsync(1, 16, 0xFF00);
        var rs = await client.ReadCoilAsync(1, 16, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.True(rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusUdp FC05 写线圈 OFF 后读回为 false")]
    public async Task WriteCoil_Off_ThenReadBack_False()
    {
        using var client = await CreateClientAsync();
        // 地址 17 默认 ON（17%2==1），写 0x0000 表示 OFF
        await client.WriteCoilAsync(1, 17, 0x0000);
        var rs = await client.ReadCoilAsync(1, 17, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.False(rs[0]);
    }

    #endregion
}
