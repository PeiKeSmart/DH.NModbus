using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace XUnitTest.Slaves;

/// <summary>Modbus从机集成测试</summary>
public class ModbusSlaveTests : IDisposable
{
    private readonly ModbusSlave _slave;

    public ModbusSlaveTests()
    {
        _slave = new ModbusSlave
        {
            Port = 1506,
            Registers = Enumerable.Range(0, 10)
                .Select(i => new RegisterUnit { Address = i, Value = (UInt16)(100 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 16)
                .Select(i => new CoilUnit { Address = i, Value = (Byte)(i % 2) })
                .ToList(),
        };
        _slave.Start();
        Thread.Sleep(200);
    }

    public void Dispose() => _slave.Dispose();

    private async Task<ModbusTcp> CreateClientAsync()
    {
        var client = new ModbusTcp
        {
            Server = "tcp://localhost:1506",
            Timeout = 3000,
        };
        await client.OpenAsync();
        return client;
    }

    [Fact]
    public async Task ReadRegister()
    {
        using var client = await CreateClientAsync();

        var rs = await client.ReadRegisterAsync(1, 0, 5);
        Assert.NotNull(rs);
        Assert.Equal(5, rs.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal((UInt16)(100 + i), rs[i]);
    }

    [Fact]
    public async Task ReadRegister_Single()
    {
        using var client = await CreateClientAsync();

        var rs = await client.ReadRegisterAsync(1, 7, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)107, rs[0]);
    }

    [Fact]
    public async Task ReadCoil()
    {
        using var client = await CreateClientAsync();

        // coils 0..7: value = i % 2, so odd indices are ON
        var rs = await client.ReadCoilAsync(1, 0, 8);
        Assert.NotNull(rs);
        Assert.Equal(8, rs.Length);
        for (var i = 0; i < 8; i++)
            Assert.Equal(i % 2 == 1, rs[i]);
    }

    [Fact]
    public async Task WriteRegister_EchosAddressAndValue()
    {
        using var client = await CreateClientAsync();

        var rs = await client.WriteRegisterAsync(1, 3, 0xABCD);
        Assert.Equal(0xABCD, rs);

        // Verify the register was updated
        var regs = await client.ReadRegisterAsync(1, 3, 1);
        Assert.Equal(0xABCD, regs[0]);
    }

    [Fact]
    public async Task WriteCoil_TurnOn()
    {
        using var client = await CreateClientAsync();

        // Coil 0 starts as 0 (off)
        var before = await client.ReadCoilAsync(1, 0, 1);
        Assert.False(before[0]);

        var rs = await client.WriteCoilAsync(1, 0, 0xFF00);
        Assert.Equal(0xFF00, rs);

        var after = await client.ReadCoilAsync(1, 0, 1);
        Assert.True(after[0]);
    }

    [Fact]
    public async Task WriteCoil_TurnOff()
    {
        using var client = await CreateClientAsync();

        // Coil 1 starts as 1 (on)
        var before = await client.ReadCoilAsync(1, 1, 1);
        Assert.True(before[0]);

        var rs = await client.WriteCoilAsync(1, 1, 0x0000);
        Assert.Equal(0x0000, rs);

        var after = await client.ReadCoilAsync(1, 1, 1);
        Assert.False(after[0]);
    }

    [Fact]
    public async Task WriteRegisters_MultipleValues()
    {
        using var client = await CreateClientAsync();

        var values = new UInt16[] { 0x1234, 0x5678, 0x9ABC };
        var rs = await client.WriteRegistersAsync(1, 5, values);
        Assert.Equal(3, rs);

        // Verify
        var regs = await client.ReadRegisterAsync(1, 5, 3);
        Assert.Equal(0x1234, regs[0]);
        Assert.Equal(0x5678, regs[1]);
        Assert.Equal(0x9ABC, regs[2]);
    }

    [Fact]
    public async Task WriteCoils_MultipleValues()
    {
        using var client = await CreateClientAsync();

        // Write 8 coils: ON, OFF, ON, OFF, ON, OFF, ON, OFF
        var values = new UInt16[] { 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000 };
        var rs = await client.WriteCoilsAsync(1, 0, values);
        Assert.Equal(8, rs);

        // Verify
        var coils = await client.ReadCoilAsync(1, 0, 8);
        for (var i = 0; i < 8; i++)
            Assert.Equal(i % 2 == 0, coils[i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC22 屏蔽写寄存器集成测试")]
    public async Task MaskWriteRegister_Integration()
    {
        using var client = await CreateClientAsync();

        // Register 3 initial value = 103 (0x0067)
        var before = await client.ReadRegisterAsync(1, 3, 1);
        Assert.Equal((UInt16)103, before[0]);

        // Set bit 0 using mask: AND=0xFFFE, OR=0x0001
        // Result = (0x0067 & 0xFFFE) | (0x0001 & ~0xFFFE)
        //        = 0x0066 | 0x0001 = 0x0067 (bit0 already set, stays set)
        var rs = await client.MaskWriteRegisterAsync(1, 3, 0xFFFE, 0x0001);
        Assert.NotEqual((UInt16)0, rs);

        // Clear bit 0 using mask: AND=0xFFFE, OR=0x0000
        // Result = (0x0067 & 0xFFFE) | (0x0000 & ~0xFFFE)
        //        = 0x0066 | 0x0000 = 0x0066 = 102
        await client.MaskWriteRegisterAsync(1, 3, 0xFFFE, 0x0000);
        var after = await client.ReadRegisterAsync(1, 3, 1);
        Assert.Equal((UInt16)102, after[0]);
    }

    #region 多从站路由（SlaveInstances）

    [Fact]
    [System.ComponentModel.DisplayName("ENH-4 SlaveInstances 多个从站实例按站号路由")]
    public async Task SlaveInstances_MultipleInstances_RouteByHost()
    {
        // 使用随机端口避免冲突
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            // 主实例站号 0 = 接受所有站号
            Host = 0,
            Registers = Enumerable.Range(0, 5)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(100 + i) })
                .ToList(),
        };

        // 添加从站实例：站号 10 和 20
        var slave10 = new ModbusSlave
        {
            Host = 10,
            Registers = Enumerable.Range(0, 5)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(1000 + i) })
                .ToList(),
        };
        var slave20 = new ModbusSlave
        {
            Host = 20,
            Registers = Enumerable.Range(0, 5)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(2000 + i) })
                .ToList(),
        };

        slave.SlaveInstances[10] = slave10;
        slave.SlaveInstances[20] = slave20;

        slave.Start();
        Thread.Sleep(200);

        try
        {
            // 读取站号 10 → 应返回 slave10 的数据（1000~1004）
            var client10 = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client10.OpenAsync();
            var regs10 = await client10.ReadRegisterAsync(10, 0, 3);
            Assert.Equal(3, regs10.Length);
            Assert.Equal((UInt16)1000, regs10[0]);
            Assert.Equal((UInt16)1001, regs10[1]);
            Assert.Equal((UInt16)1002, regs10[2]);
            client10.Dispose();

            // 读取站号 20 → 应返回 slave20 的数据（2000~2004）
            var client20 = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client20.OpenAsync();
            var regs20 = await client20.ReadRegisterAsync(20, 0, 2);
            Assert.Equal(2, regs20.Length);
            Assert.Equal((UInt16)2000, regs20[0]);
            Assert.Equal((UInt16)2001, regs20[1]);
            client20.Dispose();

            // 站号 10 写入后，站号 20 不受影响
            var client10w = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client10w.OpenAsync();
            await client10w.WriteRegisterAsync(10, 0, 9999);
            client10w.Dispose();

            // 验证站号 10 已更新
            var client10r = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client10r.OpenAsync();
            var regs10after = await client10r.ReadRegisterAsync(10, 0, 1);
            Assert.Equal((UInt16)9999, regs10after[0]);
            client10r.Dispose();

            // 验证站号 20 未受影响
            var client20r = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client20r.OpenAsync();
            var regs20after = await client20r.ReadRegisterAsync(20, 0, 1);
            Assert.Equal((UInt16)2000, regs20after[0]);
            client20r.Dispose();
        }
        finally
        {
            slave.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("ENH-4 SlaveInstances 未匹配站号回退主实例")]
    public async Task SlaveInstances_FallbackToMain_WhenNoMatch()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            // Host = 0 接受所有站号，让未匹配的站号回退到主实例
            Host = 0,
            Registers = Enumerable.Range(0, 3)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(300 + i) })
                .ToList(),
        };

        // 添加站号 10 的实例，但不覆盖站号 5
        var slave10 = new ModbusSlave
        {
            Host = 10,
            Registers = Enumerable.Range(0, 3)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(1000 + i) })
                .ToList(),
        };
        slave.SlaveInstances[10] = slave10;

        slave.Start();
        Thread.Sleep(200);

        try
        {
            // 站号 5 未注册 → 应回退主实例
            var client = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client.OpenAsync();
            var regs = await client.ReadRegisterAsync(5, 0, 2);
            Assert.Equal(2, regs.Length);
            Assert.Equal((UInt16)300, regs[0]);
            Assert.Equal((UInt16)301, regs[1]);
            client.Dispose();
        }
        finally
        {
            slave.Dispose();
        }
    }

    #endregion
}
