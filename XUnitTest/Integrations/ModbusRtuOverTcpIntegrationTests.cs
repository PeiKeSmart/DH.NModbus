using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest.Integrations;

/// <summary>
/// RtuOverTcp 软件回环集成测试
/// <para>
/// 无串口硬件下测试 ModbusRtuOverTcp 的方法：
/// 在本地启动一个原始 TCP 服务器，每次收到字节后调用 ModbusRtuSlave.ProcessRequest()
/// 处理 RTU 格式帧，再把 RTU 格式响应写回。客户端用 ModbusRtuOverTcp 连接，
/// 完整覆盖 RTU 帧编码→TCP 传输→RTU 帧解码→业务处理→RTU 响应编码的全链路。
/// </para>
/// </summary>
public class ModbusRtuOverTcpFixture : IDisposable
{
    public Int32 Port { get; }

    private readonly TcpListener _listener;
    private readonly ModbusRtuSlave _slave;
    private volatile Boolean _running = true;
    private readonly Thread _acceptThread;

    public ModbusRtuOverTcpFixture()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _slave = new ModbusRtuSlave
        {
            Host = 1,
            Registers = Enumerable.Range(0, 20)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(500 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 32)
                .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                .ToList(),
        };

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "RtuOverTcp-Accept" };
        _acceptThread.Start();

        // 等待监听线程就绪
        Thread.Sleep(100);
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                var client = _listener.AcceptTcpClient();
                new Thread(() => HandleClient(client)) { IsBackground = true }.Start();
            }
            catch (SocketException)
            {
                // 监听器已停止，退出循环
                break;
            }
            catch
            {
                // 其他异常继续循环
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var buf = new Byte[256];
        stream.ReadTimeout = 3000;

        try
        {
            while (client.Connected && _running)
            {
                Int32 n;
                try
                {
                    n = stream.Read(buf, 0, buf.Length);
                }
                catch (System.IO.IOException)
                {
                    break;
                }

                if (n == 0) break;

                var frame = buf[..n];
                var response = _slave.ProcessRequest(frame);
                if (response != null)
                    stream.Write(response, 0, response.Length);
            }
        }
        catch
        {
            // 客户端断开，忽略
        }
    }

    public void Dispose()
    {
        _running = false;
        _listener.Stop();
    }
}

/// <summary>ModbusRtuOverTcp 端到端集成测试（无串口硬件）</summary>
/// <remarks>
/// 使用 IClassFixture 共享 TCP 服务器，每个测试共用一个从机实例。
/// 地址分区规则（避免并发写冲突）：
///   读测试 → 地址 0-9（只读，不被写测试修改）
///   写测试 → 地址 10-19（写后立即读回验证，测试之间可能互相覆盖但不影响断言）
/// </remarks>
public class ModbusRtuOverTcpIntegrationTests : IClassFixture<ModbusRtuOverTcpFixture>
{
    private readonly ModbusRtuOverTcpFixture _fixture;

    public ModbusRtuOverTcpIntegrationTests(ModbusRtuOverTcpFixture fixture) => _fixture = fixture;

    private async Task<ModbusRtuOverTcp> CreateClientAsync()
    {
        var client = new ModbusRtuOverTcp
        {
            Server = $"tcp://127.0.0.1:{_fixture.Port}",
            Timeout = 3000,
        };
        await client.OpenAsync();
        return client;
    }

    #region 读取寄存器（FC03，地址 0-9）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC03 读多个保持寄存器，值与从机一致")]
    public async Task ReadRegister_Multiple_MatchesSlave()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 0, 5);
        Assert.NotNull(rs);
        Assert.Equal(5, rs.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal((UInt16)(500 + i), rs[i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC03 读单个寄存器")]
    public async Task ReadRegister_Single_FirstAddress()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 0, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)500, rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC03 读中间范围寄存器")]
    public async Task ReadRegister_MiddleRange()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 3, 3);
        Assert.NotNull(rs);
        Assert.Equal(3, rs.Length);
        for (var i = 0; i < 3; i++)
            Assert.Equal((UInt16)(503 + i), rs[i]);
    }

    #endregion

    #region 读取输入寄存器（FC04，地址 0-9）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC04 读输入寄存器，值与寄存器区一致")]
    public async Task ReadInput_Multiple_MatchesSlave()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadInputAsync(1, 0, 4);
        Assert.NotNull(rs);
        Assert.Equal(4, rs.Length);
        for (var i = 0; i < 4; i++)
            Assert.Equal((UInt16)(500 + i), rs[i]);
    }

    #endregion

    #region 读取线圈（FC01，地址 0-15）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC01 读线圈，奇偶交替模式正确")]
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
    [System.ComponentModel.DisplayName("RtuOverTcp FC01 读单个线圈（奇数地址为 ON）")]
    public async Task ReadCoil_OddAddress_True()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadCoilAsync(1, 1, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.True(rs[0]);
    }

    #endregion

    #region 读取离散输入（FC02，地址 0-15）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC02 读离散输入，与线圈区一致")]
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

    #region 写入寄存器（地址 10-19，自包含写后读回）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC06 写单个寄存器后读回一致")]
    public async Task WriteRegister_ThenRead_RoundTrip()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegisterAsync(1, 10, 0x7E7E);
        Assert.Equal(0x7E7E, rs);

        var regs = await client.ReadRegisterAsync(1, 10, 1);
        Assert.Equal((UInt16)0x7E7E, regs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC16 写多个寄存器后批量读回")]
    public async Task WriteRegisters_ThenRead_AllMatch()
    {
        using var client = await CreateClientAsync();
        var values = new UInt16[] { 0xAABB, 0xCCDD, 0xEEFF };
        var rs = await client.WriteRegistersAsync(1, 11, values);
        Assert.Equal(3, rs);

        var regs = await client.ReadRegisterAsync(1, 11, 3);
        Assert.Equal((UInt16)0xAABB, regs[0]);
        Assert.Equal((UInt16)0xCCDD, regs[1]);
        Assert.Equal((UInt16)0xEEFF, regs[2]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC06 写零值后读回为零")]
    public async Task WriteRegister_ZeroValue_RoundTrip()
    {
        using var client = await CreateClientAsync();
        await client.WriteRegisterAsync(1, 14, 0x0000);
        var regs = await client.ReadRegisterAsync(1, 14, 1);
        Assert.Equal((UInt16)0, regs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC06 写最大值后读回")]
    public async Task WriteRegister_MaxValue_RoundTrip()
    {
        using var client = await CreateClientAsync();
        await client.WriteRegisterAsync(1, 15, 0xFFFF);
        var regs = await client.ReadRegisterAsync(1, 15, 1);
        Assert.Equal((UInt16)0xFFFF, regs[0]);
    }

    #endregion

    #region 写入线圈（地址 16-31，自包含写后读回）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC05 写单个线圈 ON 后读回 True")]
    public async Task WriteCoil_On_ThenRead_True()
    {
        using var client = await CreateClientAsync();
        await client.WriteCoilAsync(1, 16, 0xFF00);
        var rs = await client.ReadCoilAsync(1, 16, 1);
        Assert.NotNull(rs);
        Assert.True(rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC05 写单个线圈 OFF 后读回 False")]
    public async Task WriteCoil_Off_ThenRead_False()
    {
        using var client = await CreateClientAsync();
        await client.WriteCoilAsync(1, 17, 0x0000);
        var rs = await client.ReadCoilAsync(1, 17, 1);
        Assert.NotNull(rs);
        Assert.False(rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp FC15 批量写线圈后批量读回")]
    public async Task WriteCoils_Pattern_ThenRead()
    {
        using var client = await CreateClientAsync();
        // ON/OFF 交替写入 6 个线圈
        var values = new UInt16[] { 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000 };
        var rs = await client.WriteCoilsAsync(1, 20, values);
        Assert.Equal(6, rs);

        var coils = await client.ReadCoilAsync(1, 20, 6);
        Assert.True(coils[0]);
        Assert.False(coils[1]);
        Assert.True(coils[2]);
        Assert.False(coils[3]);
        Assert.True(coils[4]);
        Assert.False(coils[5]);
    }

    #endregion

    #region 错误响应（EC01 / EC02）

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp 不支持的功能码返回 EC01 异常")]
    public async Task UnsupportedFunctionCode_Returns_EC01()
    {
        using var client = await CreateClientAsync();
        var ex = await Assert.ThrowsAsync<ModbusException>(async () =>
            await client.DiagnosticsAsync(1, 0x0000, 0x1234));
        Assert.Equal(ErrorCodes.IllegalFunction, ex.ErrorCode);
    }

    #endregion

    #region 多次请求连续性

    [Fact]
    [System.ComponentModel.DisplayName("RtuOverTcp 同一连接多次读写，结果始终正确")]
    public async Task MultipleRequests_SameConnection_ConsistentResults()
    {
        using var client = await CreateClientAsync();

        // 连续执行多次读写，验证连接无状态污染
        for (var i = 0; i < 5; i++)
        {
            var val = (UInt16)(0x1000 + i);
            await client.WriteRegisterAsync(1, (UInt16)(10 + i), val);
            var rs = await client.ReadRegisterAsync(1, (UInt16)(10 + i), 1);
            Assert.Equal(val, rs[0]);
        }
    }

    #endregion
}
