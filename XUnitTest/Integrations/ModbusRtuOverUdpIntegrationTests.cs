using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest.Integrations;

/// <summary>
/// ModbusRtuOverUdp UDP 回环集成测试夹具
/// <para>
/// 无串口/无特定硬件下测试 ModbusRtuOverUdp 的方法：
/// 在本地启动一个原始 UDP 服务器，收到 RTU 帧后调用 ModbusRtuSlave.ProcessRequest()
/// 处理，再把 RTU 响应帧通过 UDP 回发给客户端。
/// 客户端使用 ModbusRtuOverUdp 连接，完整覆盖 RTU 帧编码→UDP 传输→RTU 帧解码的全链路。
/// </para>
/// </summary>
public class ModbusRtuOverUdpFixture : IDisposable
{
    /// <summary>服务器监听端口（随机分配）</summary>
    public Int32 Port { get; }

    private readonly UdpClient _server;
    private readonly ModbusRtuSlave _slave;
    private volatile Boolean _running = true;
    private readonly Thread _thread;

    public ModbusRtuOverUdpFixture()
    {
        _server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_server.Client.LocalEndPoint!).Port;

        _slave = new ModbusRtuSlave
        {
            Host = 1,
            Registers = Enumerable.Range(0, 20)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(300 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 32)
                .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                .ToList(),
        };

        _thread = new Thread(Loop) { IsBackground = true, Name = "RtuOverUdp-Server" };
        _thread.Start();

        // 等待服务线程就绪
        Thread.Sleep(50);
    }

    private void Loop()
    {
        // 设置接收超时，使线程在 Dispose 后能退出
        _server.Client.ReceiveTimeout = 500;

        while (_running)
        {
            try
            {
                var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                var data = _server.Receive(ref remoteEP);
                if (data == null || data.Length == 0) continue;

                var response = _slave.ProcessRequest(data);
                if (response != null && response.Length > 0)
                    _server.Send(response, response.Length, remoteEP);
            }
            catch (SocketException)
            {
                // 接收超时或服务器已停止，继续循环或退出
            }
            catch
            {
                // 忽略其他异常，保持循环
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _server.Close();
    }
}

/// <summary>ModbusRtuOverUdp 端到端集成测试（无串口/无特定硬件）</summary>
/// <remarks>
/// 使用 IClassFixture 共享 UDP 服务器。地址分区规则：
///   读测试 → 地址 0-9（只读，不被写测试修改）
///   写测试 → 地址 10-19（写后立即读回验证）
/// </remarks>
public class ModbusRtuOverUdpIntegrationTests : IClassFixture<ModbusRtuOverUdpFixture>
{
    private readonly ModbusRtuOverUdpFixture _fixture;

    public ModbusRtuOverUdpIntegrationTests(ModbusRtuOverUdpFixture fixture) => _fixture = fixture;

    private async Task<ModbusRtuOverUdp> CreateClientAsync()
    {
        var client = new ModbusRtuOverUdp
        {
            Server = $"udp://127.0.0.1:{_fixture.Port}",
            Timeout = 3000,
        };
        await client.OpenAsync();
        return client;
    }

    #region 读取保持寄存器（FC03）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC03 读多个保持寄存器值与从机一致")]
    public async Task ReadRegister_Multiple_MatchesSlave()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 0, 5);
        Assert.NotNull(rs);
        Assert.Equal(5, rs.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal((UInt16)(300 + i), rs[i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC03 读单个寄存器")]
    public async Task ReadRegister_Single_FirstAddress()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 0, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)300, rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC03 读中间范围寄存器值正确")]
    public async Task ReadRegister_MiddleRange_Correct()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 5, 3);
        Assert.NotNull(rs);
        Assert.Equal(3, rs.Length);
        for (var i = 0; i < 3; i++)
            Assert.Equal((UInt16)(305 + i), rs[i]);
    }

    #endregion

    #region 读取输入寄存器（FC04）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC04 读输入寄存器与寄存器区一致")]
    public async Task ReadInput_MatchesSlave()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadInputAsync(1, 0, 3);
        Assert.NotNull(rs);
        Assert.Equal(3, rs.Length);
        for (var i = 0; i < 3; i++)
            Assert.Equal((UInt16)(300 + i), rs[i]);
    }

    #endregion

    #region 读取线圈（FC01）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC01 读线圈，奇偶交替模式正确")]
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
    [System.ComponentModel.DisplayName("RtuOverUdp FC01 读单个线圈（奇数地址为 ON）")]
    public async Task ReadCoil_OddAddress_IsTrue()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadCoilAsync(1, 1, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.True(rs[0]);
    }

    #endregion

    #region 读取离散输入（FC02）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC02 读离散输入与线圈区一致")]
    public async Task ReadDiscrete_AlternatingPattern()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadDiscreteAsync(1, 0, 6);
        Assert.NotNull(rs);
        Assert.Equal(6, rs.Length);
        for (var i = 0; i < 6; i++)
            Assert.Equal(i % 2 == 1, rs[i]);
    }

    #endregion

    #region 写入保持寄存器（FC06，地址 10-19）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC06 写单寄存器后读回一致")]
    public async Task WriteRegister_ThenRead_RoundTrip()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegisterAsync(1, 10, 0xABCD);
        Assert.Equal(0xABCD, rs);

        // 读回验证
        var regs = await client.ReadRegisterAsync(1, 10, 1);
        Assert.NotNull(regs);
        Assert.Equal((UInt16)0xABCD, regs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC06 写寄存器边界值 0x0000")]
    public async Task WriteRegister_ZeroValue_Roundtrip()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegisterAsync(1, 11, 0x0000);
        Assert.Equal(0x0000, rs);

        var regs = await client.ReadRegisterAsync(1, 11, 1);
        Assert.NotNull(regs);
        Assert.Equal((UInt16)0, regs[0]);
    }

    #endregion

    #region 写入线圈（FC05，地址 0-15）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverUdp FC05 写线圈 ON 后读回为 true")]
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
    [System.ComponentModel.DisplayName("RtuOverUdp FC05 写线圈 OFF 后读回为 false")]
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
