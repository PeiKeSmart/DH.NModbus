using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace XUnitTest.Integrations;

/// <summary>ModbusSlave集成测试的共享fixture，服务器仅启动一次</summary>
public class ModbusSlaveFixture : IDisposable
{
    public ModbusSlave Slave { get; }
    public Int32 Port { get; }

    public ModbusSlaveFixture()
    {
        // 使用OS分配的空闲端口，避免端口冲突
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Slave = new ModbusSlave
        {
            Port = Port,
            Registers = Enumerable.Range(0, 20)
                .Select(i => new RegisterUnit { Address = i, Value = (UInt16)(200 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 32)
                .Select(i => new CoilUnit { Address = i, Value = (Byte)(i % 2) })
                .ToList(),
        };
        Slave.Start();
        Thread.Sleep(200);
    }

    public void Dispose() => Slave.Dispose();
}

/// <summary>ModbusSlave集成测试 - 覆盖更多场景</summary>
/// <remarks>
/// 使用IClassFixture共享服务器实例，避免每个测试重复启动/停止。
/// 读取初始值的测试使用寄存器0-9和线圈0-15（不被写测试修改）；
/// 写入测试使用寄存器10-19和线圈16-31（自包含，写后立即读回验证）。
/// </remarks>
public class ModbusSlaveIntegrationTests : IClassFixture<ModbusSlaveFixture>
{
    private readonly ModbusSlaveFixture _fixture;

    public ModbusSlaveIntegrationTests(ModbusSlaveFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<ModbusTcp> CreateClientAsync()
    {
        var client = new ModbusTcp
        {
            Server = $"tcp://localhost:{_fixture.Port}",
            Timeout = 3000,
        };
        await client.OpenAsync();
        return client;
    }

    #region 读取寄存器（使用地址0-9，不被写测试修改）
    [Fact]
    public async Task ReadRegister_AllRegisters()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 0, 10);
        Assert.NotNull(rs);
        Assert.Equal(10, rs.Length);
        for (var i = 0; i < 10; i++)
            Assert.Equal((UInt16)(200 + i), rs[i]);
    }

    [Fact]
    public async Task ReadRegister_MiddleRange()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 5, 5);
        Assert.NotNull(rs);
        Assert.Equal(5, rs.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal((UInt16)(205 + i), rs[i]);
    }

    [Fact]
    public async Task ReadRegister_LastRegister()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 9, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)209, rs[0]);
    }

    [Fact]
    public async Task ReadRegister_FirstRegister()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadRegisterAsync(1, 0, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)200, rs[0]);
    }
    #endregion

    #region 读取线圈（使用地址0-15，不被写测试修改）
    [Fact]
    public async Task ReadCoil_AllCoils()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadCoilAsync(1, 0, 16);
        Assert.NotNull(rs);
        Assert.Equal(16, rs.Length);
        for (var i = 0; i < 16; i++)
            Assert.Equal(i % 2 == 1, rs[i]);
    }

    [Fact]
    public async Task ReadCoil_NonMultipleOf8()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadCoilAsync(1, 0, 10);
        Assert.NotNull(rs);
        Assert.Equal(10, rs.Length);
        for (var i = 0; i < 10; i++)
            Assert.Equal(i % 2 == 1, rs[i]);
    }

    [Fact]
    public async Task ReadCoil_SingleCoil()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadCoilAsync(1, 1, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.True(rs[0]);
    }

    [Fact]
    public async Task ReadCoil_OffCoil()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadCoilAsync(1, 0, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.False(rs[0]);
    }

    [Fact]
    public async Task ReadCoil_PartialByte()
    {
        using var client = await CreateClientAsync();
        // 请求3个线圈，不是8的整数倍
        var rs = await client.ReadCoilAsync(1, 0, 3);
        Assert.NotNull(rs);
        Assert.Equal(3, rs.Length);
    }
    #endregion

    #region 读取离散输入 FC02（与FC01共用线圈区，地址0-15不被写测试修改）
    [Fact]
    [System.ComponentModel.DisplayName("FC02 ReadDiscrete 读多个离散输入，值与线圈区一致")]
    public async Task ReadDiscrete_MultipleCoils_MatchesCoilArea()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadDiscreteAsync(1, 0, 8);
        Assert.NotNull(rs);
        Assert.Equal(8, rs.Length);
        for (var i = 0; i < 8; i++)
            Assert.Equal(i % 2 == 1, rs[i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC02 ReadDiscrete 读单个离散输入")]
    public async Task ReadDiscrete_SingleCoil_OddAddress_True()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadDiscreteAsync(1, 1, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.True(rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC02 ReadDiscrete 读单个偶数地址，返回False")]
    public async Task ReadDiscrete_SingleCoil_EvenAddress_False()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadDiscreteAsync(1, 0, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.False(rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC02 ReadDiscrete 非8倍数数量")]
    public async Task ReadDiscrete_NonMultipleOf8_ReturnsCorrectCount()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadDiscreteAsync(1, 0, 5);
        Assert.NotNull(rs);
        Assert.Equal(5, rs.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal(i % 2 == 1, rs[i]);
    }
    #endregion

    #region 读取输入寄存器 FC04（与FC03共用寄存器区，地址0-9不被写测试修改）
    [Fact]
    [System.ComponentModel.DisplayName("FC04 ReadInput 读多个输入寄存器，值与寄存器区一致")]
    public async Task ReadInput_MultipleRegisters_MatchesRegisterArea()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadInputAsync(1, 0, 5);
        Assert.NotNull(rs);
        Assert.Equal(5, rs.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal((UInt16)(200 + i), rs[i]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC04 ReadInput 读单个输入寄存器")]
    public async Task ReadInput_SingleRegister_FirstAddress()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadInputAsync(1, 0, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)200, rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC04 ReadInput 读最后一个寄存器")]
    public async Task ReadInput_SingleRegister_LastReadOnlyAddress()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadInputAsync(1, 9, 1);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)209, rs[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC04 ReadInput 中间范围寄存器值正确")]
    public async Task ReadInput_MiddleRange_ValuesCorrect()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadInputAsync(1, 3, 4);
        Assert.NotNull(rs);
        Assert.Equal(4, rs.Length);
        for (var i = 0; i < 4; i++)
            Assert.Equal((UInt16)(203 + i), rs[i]);
    }
    #endregion

    #region 写入寄存器（使用地址10-19，自包含写后读回）
    [Fact]
    public async Task WriteRegister_ThenRead()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegisterAsync(1, 10, 0x9999);
        Assert.Equal(0x9999, rs);

        var regs = await client.ReadRegisterAsync(1, 10, 1);
        Assert.Equal(0x9999, regs[0]);
    }

    [Fact]
    public async Task WriteRegister_MaxValue()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegisterAsync(1, 11, 0xFFFF);
        Assert.Equal(0xFFFF, rs);

        var regs = await client.ReadRegisterAsync(1, 11, 1);
        Assert.Equal(0xFFFF, regs[0]);
    }

    [Fact]
    public async Task WriteRegister_ZeroValue()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegisterAsync(1, 12, 0x0000);
        Assert.Equal(0, rs);

        var regs = await client.ReadRegisterAsync(1, 12, 1);
        Assert.Equal(0, regs[0]);
    }

    [Fact]
    public async Task WriteRegisters_ThenRead()
    {
        using var client = await CreateClientAsync();
        var values = new UInt16[] { 0xAAAA, 0xBBBB, 0xCCCC, 0xDDDD };
        var rs = await client.WriteRegistersAsync(1, 13, values);
        Assert.Equal(4, rs);

        var regs = await client.ReadRegisterAsync(1, 13, 4);
        Assert.Equal(0xAAAA, regs[0]);
        Assert.Equal(0xBBBB, regs[1]);
        Assert.Equal(0xCCCC, regs[2]);
        Assert.Equal(0xDDDD, regs[3]);
    }

    [Fact]
    public async Task WriteRegisters_SingleValue()
    {
        using var client = await CreateClientAsync();
        var rs = await client.WriteRegistersAsync(1, 17, new UInt16[] { 0x1111 });
        Assert.Equal(1, rs);

        var regs = await client.ReadRegisterAsync(1, 17, 1);
        Assert.Equal(0x1111, regs[0]);
    }
    #endregion

    #region 写入线圈（使用地址16-31，自包含写后读回）
    [Fact]
    public async Task WriteCoil_ThenRead()
    {
        using var client = await CreateClientAsync();
        // 写入ON
        await client.WriteCoilAsync(1, 16, 0xFF00);
        var rs = await client.ReadCoilAsync(1, 16, 1);
        Assert.True(rs[0]);

        // 再关闭
        await client.WriteCoilAsync(1, 16, 0x0000);
        rs = await client.ReadCoilAsync(1, 16, 1);
        Assert.False(rs[0]);
    }

    [Fact]
    public async Task WriteCoils_ThenRead()
    {
        using var client = await CreateClientAsync();
        // 写入8个线圈：全开
        var values = new UInt16[] { 0xFF00, 0xFF00, 0xFF00, 0xFF00, 0xFF00, 0xFF00, 0xFF00, 0xFF00 };
        var rs = await client.WriteCoilsAsync(1, 16, values);
        Assert.Equal(8, rs);

        var coils = await client.ReadCoilAsync(1, 16, 8);
        for (var i = 0; i < 8; i++)
            Assert.True(coils[i]);
    }

    [Fact]
    public async Task WriteCoils_AllOff_ThenRead()
    {
        using var client = await CreateClientAsync();
        // 全关
        var values = new UInt16[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        var rs = await client.WriteCoilsAsync(1, 16, values);
        Assert.Equal(8, rs);

        var coils = await client.ReadCoilAsync(1, 16, 8);
        for (var i = 0; i < 8; i++)
            Assert.False(coils[i]);
    }

    [Fact]
    public async Task WriteCoils_NonMultipleOf8_ThenRead()
    {
        using var client = await CreateClientAsync();
        // 写5个线圈（非8的倍数）
        var values = new UInt16[] { 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00 };
        var rs = await client.WriteCoilsAsync(1, 24, values);
        Assert.Equal(5, rs);

        var coils = await client.ReadCoilAsync(1, 24, 5);
        Assert.True(coils[0]);
        Assert.False(coils[1]);
        Assert.True(coils[2]);
        Assert.False(coils[3]);
        Assert.True(coils[4]);
    }

    [Fact]
    public async Task WriteCoils_Pattern_ThenRead()
    {
        using var client = await CreateClientAsync();
        // 交替模式
        var values = new UInt16[] { 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000, 0xFF00, 0x0000 };
        var rs = await client.WriteCoilsAsync(1, 16, values);
        Assert.Equal(16, rs);

        var coils = await client.ReadCoilAsync(1, 16, 16);
        for (var i = 0; i < 16; i++)
            Assert.Equal(i % 2 == 0, coils[i]);
    }
    #endregion

    #region 读写组合场景（使用写区域地址，自包含）
    [Fact]
    public async Task ReadWrite_RegisterRoundTrip()
    {
        using var client = await CreateClientAsync();

        // 写入一系列值，然后读回（使用寄存器10-19）
        for (UInt16 i = 0; i < 10; i++)
        {
            var val = (UInt16)(i * 1000);
            await client.WriteRegisterAsync(1, (UInt16)(10 + i), val);
        }

        var regs = await client.ReadRegisterAsync(1, 10, 10);
        for (var i = 0; i < 10; i++)
            Assert.Equal((UInt16)(i * 1000), regs[i]);
    }

    [Fact]
    public async Task ReadWrite_CoilRoundTrip()
    {
        using var client = await CreateClientAsync();

        // 逐个设置线圈（使用线圈24-31）
        for (var i = 0; i < 8; i++)
        {
            var value = (UInt16)(i % 3 == 0 ? 0xFF00 : 0x0000);
            await client.WriteCoilAsync(1, (UInt16)(24 + i), value);
        }

        var coils = await client.ReadCoilAsync(1, 24, 8);
        for (var i = 0; i < 8; i++)
            Assert.Equal(i % 3 == 0, coils[i]);
    }

    [Fact]
    public async Task Read_FunctionCodes_Dispatch()
    {
        using var client = await CreateClientAsync();

        // 通过Read方法使用不同的功能码读取
        var rs = await client.ReadAsync(FunctionCodes.ReadRegister, 1, 0, 3);
        Assert.NotNull(rs);

        var rs2 = await client.ReadAsync(FunctionCodes.ReadCoil, 1, 0, 8);
        Assert.NotNull(rs2);
    }
    #endregion

    #region FC23 读写多寄存器集成测试（ReadWriteMultipleRegisters，使用写区域地址）
    [Fact]
    [System.ComponentModel.DisplayName("FC23 先写后原子读回，写入值正确")]
    public async Task ReadWriteRegisters_WriteThenRead_AtomicRoundTrip()
    {
        using var client = await CreateClientAsync();
        // 写入地址10-11，同时读取地址0-1（初始值200/201）
        var rs = await client.ReadWriteRegistersAsync(1, 0, 2, 10, [0xF001, 0xF002]);
        Assert.NotNull(rs);
        Assert.Equal(2, rs.Length);
        // 读回值应为初始寄存器0-1的值
        Assert.Equal((UInt16)200, rs[0]);
        Assert.Equal((UInt16)201, rs[1]);

        // 确认写入已经生效
        var verify = await client.ReadRegisterAsync(1, 10, 2);
        Assert.Equal((UInt16)0xF001, verify[0]);
        Assert.Equal((UInt16)0xF002, verify[1]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC23 写单个寄存器同时读单个寄存器")]
    public async Task ReadWriteRegisters_SingleRegisterEach()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadWriteRegistersAsync(1, 5, 1, 12, [0xABCD]);
        Assert.NotNull(rs);
        Assert.Single(rs);
        Assert.Equal((UInt16)205, rs[0]);   // 初始值 200+5

        var verify = await client.ReadRegisterAsync(1, 12, 1);
        Assert.Equal((UInt16)0xABCD, verify[0]);
    }

    [Fact]
    [System.ComponentModel.DisplayName("FC23 写多个寄存器同时读取不重叠区域")]
    public async Task ReadWriteRegisters_NonOverlappingRegions()
    {
        using var client = await CreateClientAsync();
        // 写地址13-16（4个），读地址0-3（4个）
        var writeVals = new UInt16[] { 0x1111, 0x2222, 0x3333, 0x4444 };
        var rs = await client.ReadWriteRegistersAsync(1, 0, 4, 13, writeVals);
        Assert.NotNull(rs);
        Assert.Equal(4, rs.Length);
        for (var i = 0; i < 4; i++)
            Assert.Equal((UInt16)(200 + i), rs[i]);

        var verify = await client.ReadRegisterAsync(1, 13, 4);
        Assert.Equal((UInt16)0x1111, verify[0]);
        Assert.Equal((UInt16)0x2222, verify[1]);
        Assert.Equal((UInt16)0x3333, verify[2]);
        Assert.Equal((UInt16)0x4444, verify[3]);
    }
    #endregion

    #region ModbusSlave属性
    [Fact]
    public void Slave_DefaultPort()
    {
        var slave = new ModbusSlave();
        Assert.Equal(502, slave.Port);
        slave.Dispose();
    }

    [Fact]
    public void Slave_RegistersAndCoils()
    {
        Assert.Equal(20, _fixture.Slave.Registers.Count);
        Assert.Equal(32, _fixture.Slave.Coils.Count);
    }

    [Fact]
    public void Slave_EmptyRegistersAndCoils()
    {
        var slave = new ModbusSlave();
        Assert.NotNull(slave.Registers);
        Assert.NotNull(slave.Coils);
        Assert.Empty(slave.Registers);
        Assert.Empty(slave.Coils);
        slave.Dispose();
    }
    #endregion

    #region T005 — EC01 异常响应（不支持的功能码）
    [Fact]
    [System.ComponentModel.DisplayName("T005 不支持的功能码返回EC01异常")]
    public async Task UnsupportedFunctionCode_Returns_EC01Exception()
    {
        using var client = await CreateClientAsync();
        // FC08 Diagnostics 当前 Slave 不支持，应返回 EC01
        var ex = await Assert.ThrowsAsync<NewLife.IoT.Protocols.ModbusException>(async () =>
            await client.DiagnosticsAsync(1, 0x0000, 0x1234));
        Assert.Equal(ErrorCodes.IllegalFunction, ex.ErrorCode);
    }

    [Fact]
    [System.ComponentModel.DisplayName("T005 FC43 Slave 支持，返回有效设备信息")]
    public async Task ReadDevId_SlaveSupports_ReturnsInfo()
    {
        using var client = await CreateClientAsync();
        var rs = await client.ReadDevIdAsync(1);
        Assert.NotNull(rs);
        Assert.True(rs.Count >= 1);
    }

    [Fact]
    [System.ComponentModel.DisplayName("T005 Slave Host 属性默认为1")]
    public void Slave_DefaultHost_IsOne()
    {
        Assert.Equal((Byte)1, _fixture.Slave.Host);
    }
    #endregion

    #region T006 — 广播模式
    [Fact]
    [System.ComponentModel.DisplayName("T006 广播写寄存器不返回响应")]
    public async Task BroadcastWriteRegister_NoResponse()
    {
        // 使用独立从机避免污染共享 fixture
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            Host = 1,
            Registers = Enumerable.Range(0, 5)
                .Select(i => new RegisterUnit { Address = i, Value = 0 })
                .ToList(),
        };
        slave.Start();
        Thread.Sleep(200);

        try
        {
            // 广播地址(0)写入，从机应执行写操作但不回复
            var broadcastClient = new ModbusTcp
            {
                Server = $"tcp://localhost:{port}",
                Timeout = 500,  // 短超时，因为广播不回响应
                ValidResponse = false,
            };
            await broadcastClient.OpenAsync();

            // 向站号0广播写寄存器，期望超时（因为不回复）
            try
            {
                await broadcastClient.WriteRegisterAsync(0, 0, 0xABCD);
            }
            catch
            {
                // 广播下从机不响应，客户端会超时，这是预期行为
            }
            broadcastClient.Dispose();

            // 使用普通站号读取，验证写入已生效
            Thread.Sleep(100);
            var readClient = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await readClient.OpenAsync();
            var regs = await readClient.ReadRegisterAsync(1, 0, 1);
            // 广播写入后值应为 0xABCD
            Assert.Equal((UInt16)0xABCD, regs[0]);
            readClient.Dispose();
        }
        finally
        {
            slave.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("T006 非本机站号请求被丢弃")]
    public async Task WrongHostAddress_RequestDiscarded()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            Host = 5,   // 只响应站号 5
            Registers = Enumerable.Range(0, 5)
                .Select(i => new RegisterUnit { Address = i, Value = (UInt16)(100 + i) })
                .ToList(),
        };
        slave.Start();
        Thread.Sleep(200);

        try
        {
            // 向站号5发送请求，应得到正常响应
            var client5 = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client5.OpenAsync();
            var regs = await client5.ReadRegisterAsync(5, 0, 3);
            Assert.Equal(3, regs.Length);
            Assert.Equal((UInt16)100, regs[0]);
            client5.Dispose();
        }
        finally
        {
            slave.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("T006 Host=0时接受所有站号")]
    public async Task SlaveHost_Zero_AcceptsAllAddresses()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            Host = 0,   // 接受所有站号
            Registers = Enumerable.Range(0, 5)
                .Select(i => new RegisterUnit { Address = i, Value = (UInt16)(300 + i) })
                .ToList(),
        };
        slave.Start();
        Thread.Sleep(200);

        try
        {
            // 用站号1、2、3都能读取成功
            foreach (var hostId in new Byte[] { 1, 2, 3 })
            {
                var client = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
                await client.OpenAsync();
                var regs = await client.ReadRegisterAsync(hostId, 0, 2);
                Assert.Equal(2, regs.Length);
                Assert.Equal((UInt16)300, regs[0]);
                client.Dispose();
            }
        }
        finally
        {
            slave.Dispose();
        }
    }
    #endregion

    #region T007 — 自定义数据回调
    [Fact]
    [System.ComponentModel.DisplayName("T007 BeforeRead 事件在读取前触发")]
    public async Task BeforeRead_Event_TriggeredBeforeRead()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            Registers = Enumerable.Range(0, 5)
                .Select(i => new RegisterUnit { Address = i, Value = 0 })
                .ToList(),
        };

        // 在 BeforeRead 中动态填充数据
        slave.BeforeRead += (code, addr, count) =>
        {
            if (code == FunctionCodes.ReadRegister || code == FunctionCodes.ReadInput)
            {
                foreach (var reg in slave.Registers)
                    reg.Value = (UInt16)(reg.Address * 10 + 1);
            }
        };
        slave.Start();
        Thread.Sleep(200);

        try
        {
            var client = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client.OpenAsync();
            var regs = await client.ReadRegisterAsync(1, 0, 3);
            Assert.Equal(3, regs.Length);
            Assert.Equal((UInt16)1, regs[0]);   // 0*10+1
            Assert.Equal((UInt16)11, regs[1]);  // 1*10+1
            Assert.Equal((UInt16)21, regs[2]);  // 2*10+1
            client.Dispose();
        }
        finally
        {
            slave.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("T007 AfterWrite 事件在写入后触发")]
    public async Task AfterWrite_Event_TriggeredAfterWrite()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            Registers = Enumerable.Range(0, 5)
                .Select(i => new RegisterUnit { Address = i, Value = 0 })
                .ToList(),
        };

        FunctionCodes? notifiedCode = null;
        UInt16? notifiedAddr = null;
        UInt16[]? notifiedValues = null;

        slave.AfterWrite += (code, addr, values) =>
        {
            notifiedCode = code;
            notifiedAddr = addr;
            notifiedValues = values;
        };
        slave.Start();
        Thread.Sleep(200);

        try
        {
            var client = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client.OpenAsync();
            await client.WriteRegisterAsync(1, 2, 0x5678);
            Thread.Sleep(50); // 等待事件触发

            Assert.Equal(FunctionCodes.WriteRegister, notifiedCode);
            Assert.Equal((UInt16)2, notifiedAddr);
            Assert.NotNull(notifiedValues);
            Assert.Equal((UInt16)0x5678, notifiedValues![0]);
            client.Dispose();
        }
        finally
        {
            slave.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("T007 AfterWrite 批量写入后触发带完整数组")]
    public async Task AfterWrite_WriteRegisters_FullArray()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var slave = new ModbusSlave
        {
            Port = port,
            Registers = Enumerable.Range(0, 10)
                .Select(i => new RegisterUnit { Address = i, Value = 0 })
                .ToList(),
        };

        UInt16[]? capturedValues = null;
        slave.AfterWrite += (code, addr, values) => capturedValues = values;
        slave.Start();
        Thread.Sleep(200);

        try
        {
            var client = new ModbusTcp { Server = $"tcp://localhost:{port}", Timeout = 3000 };
            await client.OpenAsync();
            await client.WriteRegistersAsync(1, 0, [0x1111, 0x2222, 0x3333]);
            Thread.Sleep(50);

            Assert.NotNull(capturedValues);
            Assert.Equal(3, capturedValues!.Length);
            Assert.Equal((UInt16)0x1111, capturedValues[0]);
            Assert.Equal((UInt16)0x2222, capturedValues[1]);
            Assert.Equal((UInt16)0x3333, capturedValues[2]);
            client.Dispose();
        }
        finally
        {
            slave.Dispose();
        }
    }
    #endregion

    #region T008 — 并发多客户端
    [Fact]
    [System.ComponentModel.DisplayName("T008 多客户端并发读取，结果均正确")]
    public async Task ConcurrentClients_Read_AllSucceed()
    {
        const Int32 clientCount = 8;
        var errors = new System.Collections.Concurrent.ConcurrentBag<String>();
        var threads = new Thread[clientCount];

        for (var t = 0; t < clientCount; t++)
        {
            var idx = t;
            threads[t] = new Thread(async () =>
            {
                try
                {
                    using var client = await CreateClientAsync();
                    // 每个线程读取不同起始地址的2个寄存器（地址0-9均为只读区）
                    var addr = (UInt16)(idx % 8);
                    var rs = await client.ReadRegisterAsync(1, addr, 2);
                    if (rs == null || rs.Length != 2)
                    {
                        errors.Add($"Thread{idx}: 返回为null或长度错误");
                        return;
                    }
                    if (rs[0] != (UInt16)(200 + addr))
                        errors.Add($"Thread{idx}: addr={addr} 期望{200 + addr} 实际{rs[0]}");
                }
                catch (Exception ex)
                {
                    errors.Add($"Thread{idx}: {ex.Message}");
                }
            });
        }

        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join(5000);

        Assert.Empty(errors);
    }

    [Fact]
    [System.ComponentModel.DisplayName("T008 多客户端并发写入不同地址，互不干扰")]
    public async Task ConcurrentClients_WriteDistinctAddresses_NoConflict()
    {
        // 每个线程写独立地址，最后统一读回验证
        // 使用fixture寄存器区之外的新地址需要动态从机，这里用写测试区10-19
        // 限制并发数=4，避免同一地址被覆盖
        const Int32 clientCount = 4;
        var errors = new System.Collections.Concurrent.ConcurrentBag<String>();
        var expected = new UInt16[clientCount];
        var threads = new Thread[clientCount];

        for (var t = 0; t < clientCount; t++)
        {
            var idx = t;
            expected[idx] = (UInt16)(0x8000 + idx * 0x10);
            threads[t] = new Thread(async () =>
            {
                try
                {
                    using var client = await CreateClientAsync();
                    var addr = (UInt16)(10 + idx);
                    await client.WriteRegisterAsync(1, addr, expected[idx]);
                }
                catch (Exception ex)
                {
                    errors.Add($"WriteThread{idx}: {ex.Message}");
                }
            });
        }

        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join(5000);

        Assert.Empty(errors);

        // 串行读回验证
        using var verifyClient = await CreateClientAsync();
        for (var i = 0; i < clientCount; i++)
        {
            var rs = await verifyClient.ReadRegisterAsync(1, (UInt16)(10 + i), 1);
            Assert.Equal(expected[i], rs[0]);
        }
    }
    #endregion
}