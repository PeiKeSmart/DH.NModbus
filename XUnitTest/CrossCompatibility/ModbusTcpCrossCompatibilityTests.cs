using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NewLife;
using NewLife.Data;
using NewLife.IoT;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NModbus;
using NModbus.Data;
using Xunit;

namespace XUnitTest.CrossCompatibility;

/// <summary>ModbusTCP 竞品交叉兼容测试</summary>
/// <remarks>
/// 交叉验证模式：
/// - N2（你→我）：NModbus Master → NewLife.Modbus Slave（核心：行业标准读取我方实现）
/// - N4（你→你）：NModbus → NModbus 自身一致性基线
///
/// 竞品：NModbus 3.x（NuGet 下载 2.4M，业界最主流 .NET Modbus 库）
/// </remarks>
public class ModbusTcpCrossCompatibilityTests
{
    #region NModbus ISlaveDataStore 辅助实现

    private sealed class SimpleSlaveDataStore : ISlaveDataStore
    {
        public IPointSource<Boolean> CoilDiscretes { get; } = new PointSource<Boolean>();
        public IPointSource<Boolean> CoilInputs { get; } = new PointSource<Boolean>();
        public IPointSource<UInt16> HoldingRegisters { get; } = new PointSource<UInt16>();
        public IPointSource<UInt16> InputRegisters { get; } = new PointSource<UInt16>();
    }

    #endregion

    #region N4 模式：NModbus → NModbus 自身一致性基线

    /// <summary>NModbus TCP 从机 Fixture（用于 N4 基线）</summary>
    public class NModbusSlaveFixture : IDisposable
    {
        public Int32 Port { get; }
        private readonly TcpListener _listener;
        private IModbusSlaveNetwork _slaveNetwork;
        private CancellationTokenSource _cts;
        private Task _listenTask;

        public NModbusSlaveFixture()
        {
            var factory = new NModbus.ModbusFactory();
            var store = new SimpleSlaveDataStore();

            store.HoldingRegisters.WritePoints(0, Enumerable.Range(0, 20).Select(i => (UInt16)(300 + i)).ToArray());
            store.CoilDiscretes.WritePoints(0, Enumerable.Range(0, 32).Select(i => i % 2 == 1).ToArray());

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _slaveNetwork = factory.CreateSlaveNetwork(_listener);
            _slaveNetwork.AddSlave(factory.CreateSlave(1, store));
            _cts = new CancellationTokenSource();
            _listenTask = _slaveNetwork.ListenAsync(_cts.Token);
            Thread.Sleep(200);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _listenTask?.Wait(1000); } catch { }
            _slaveNetwork?.Dispose();
            _listener?.Stop();
            _cts?.Dispose();
        }
    }

    /// <summary>N4 基线测试：确认竞品 NModbus 自身行为正常</summary>
    public class N4BaselineTests : IClassFixture<NModbusSlaveFixture>
    {
        private readonly NModbusSlaveFixture _fixture;
        public N4BaselineTests(NModbusSlaveFixture fixture) => _fixture = fixture;

        private IModbusMaster CreateNModbusMaster()
        {
            var factory = new NModbus.ModbusFactory();
            var client = new TcpClient("localhost", _fixture.Port);
            return factory.CreateMaster(client);
        }

        [Fact]
        [DisplayName("N4-基线 NModbus读取自身从机保持寄存器")]
        public async Task N4_ReadHoldingRegisters()
        {
            using var master = CreateNModbusMaster();
            var rs = await master.ReadHoldingRegistersAsync(1, 0, 10);
            Assert.Equal(10, rs.Length);
            for (var i = 0; i < 10; i++) Assert.Equal((UInt16)(300 + i), rs[i]);
        }

        [Fact]
        [DisplayName("N4-基线 NModbus读取自身从机线圈")]
        public async Task N4_ReadCoils()
        {
            using var master = CreateNModbusMaster();
            var rs = await master.ReadCoilsAsync(1, 0, 16);
            Assert.Equal(16, rs.Length);
            for (var i = 0; i < 16; i++) Assert.Equal(i % 2 == 1, rs[i]);
        }
    }

    #endregion

    #region N2 模式：NModbus Master → NewLife Slave（你写我读 / 你写我写 —— 核心交叉验证）

    /// <summary>NewLife ModbusSlave 共享夹具</summary>
    /// <remarks>
    /// 地址分区避免测试间共享状态干扰：
    /// - 读测试：寄存器 0-9（初始 400+i），线圈 0-15
    /// - 写测试：寄存器 10-19（自包含写后验证），线圈 16-31（自包含写后验证）
    /// </remarks>
    public class NewLifeSlaveFixture : IDisposable
    {
        public Int32 Port { get; }
        public ModbusSlave Slave { get; }

        public NewLifeSlaveFixture()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            Slave = new ModbusSlave
            {
                Port = Port, Host = 1,
                Registers = Enumerable.Range(0, 20)
                    .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(400 + i) })
                    .ToList(),
                Coils = Enumerable.Range(0, 32)
                    .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                    .ToList(),
            };
            Slave.Start();
            Thread.Sleep(200);
        }

        public void Dispose() => Slave.Dispose();
    }

    /// <summary>N2 全功能测试：NModbus Master 读写 NewLife Slave</summary>
    public class N2Tests : IClassFixture<NewLifeSlaveFixture>
    {
        private readonly NewLifeSlaveFixture _fixture;
        public N2Tests(NewLifeSlaveFixture fixture) => _fixture = fixture;

        private IModbusMaster CreateNModbusMaster()
        {
            var factory = new NModbus.ModbusFactory();
            var client = new TcpClient("localhost", _fixture.Port);
            return factory.CreateMaster(client);
        }

        // ====== 读测试（地址 0-9 / 线圈 0-15，不被写测试修改） ======

        [Fact]
        [DisplayName("N2-FC03 NModbus读取NewLife从机保持寄存器（全量）")]
        public async Task N2_ReadHoldingRegisters()
        {
            using var master = CreateNModbusMaster();
            var rs = await master.ReadHoldingRegistersAsync(1, 0, 10);
            Assert.Equal(10, rs.Length);
            for (var i = 0; i < 10; i++) Assert.Equal((UInt16)(400 + i), rs[i]);
        }

        [Fact]
        [DisplayName("N2-FC01 NModbus读取NewLife从机线圈（全量）")]
        public async Task N2_ReadCoils()
        {
            using var master = CreateNModbusMaster();
            var rs = await master.ReadCoilsAsync(1, 0, 16);
            Assert.Equal(16, rs.Length);
            for (var i = 0; i < 16; i++) Assert.Equal(i % 2 == 1, rs[i]);
        }

        [Fact]
        [DisplayName("N2-FC02 NModbus读取NewLife从机离散输入")]
        public async Task N2_ReadDiscreteInputs()
        {
            using var master = CreateNModbusMaster();
            var rs = await master.ReadInputsAsync(1, 0, 8);
            Assert.Equal(8, rs.Length);
            for (var i = 0; i < 8; i++) Assert.Equal(i % 2 == 1, rs[i]);
        }

        [Fact]
        [DisplayName("N2-FC04 NModbus读取NewLife从机输入寄存器")]
        public async Task N2_ReadInputRegisters()
        {
            using var master = CreateNModbusMaster();
            var rs = await master.ReadInputRegistersAsync(1, 0, 10);
            Assert.Equal(10, rs.Length);
            for (var i = 0; i < 10; i++) Assert.Equal((UInt16)(400 + i), rs[i]);
        }

        [Fact]
        [DisplayName("N2-FC01 NModbus读取单个线圈（边界验证）")]
        public async Task N2_ReadCoils_Single()
        {
            using var master = CreateNModbusMaster();
            var rs = await master.ReadCoilsAsync(1, 1, 1);
            Assert.Single(rs); Assert.True(rs[0]);
        }

        [Fact]
        [DisplayName("N2-FC03 NModbus读取单个寄存器（边界验证）")]
        public async Task N2_ReadHoldingRegisters_Single()
        {
            using var master = CreateNModbusMaster();
            var rs = await master.ReadHoldingRegistersAsync(1, 9, 1);
            Assert.Single(rs); Assert.Equal((UInt16)409, rs[0]);
        }

        // ====== 写测试（地址 10-19 / 线圈 16-31，自包含写后验证） ======

        [Fact]
        [DisplayName("N2-FC06 NModbus写入NewLife从机单个寄存器并验证")]
        public async Task N2_WriteSingleRegister()
        {
            using var master = CreateNModbusMaster();
            await master.WriteSingleRegisterAsync(1, 12, 0xBEEF);
            var reg = _fixture.Slave.Registers.FirstOrDefault(r => r.Address == 12);
            Assert.NotNull(reg); Assert.Equal(0xBEEF, reg!.Value);
        }

        [Fact]
        [DisplayName("N2-FC05 NModbus写入NewLife从机单个线圈并验证")]
        public async Task N2_WriteSingleCoil()
        {
            using var master = CreateNModbusMaster();
            await master.WriteSingleCoilAsync(1, 18, true);
            var coil = _fixture.Slave.Coils.FirstOrDefault(c => c.Address == 18);
            Assert.NotNull(coil); Assert.Equal(1, coil!.Value);
        }

        [Fact]
        [DisplayName("N2-FC16 NModbus写入NewLife从机多个寄存器并验证")]
        public async Task N2_WriteMultipleRegisters()
        {
            using var master = CreateNModbusMaster();
            await master.WriteMultipleRegistersAsync(1, 14, new UInt16[] { 0xAAAA, 0xBBBB, 0xCCCC });
            Assert.Equal(0xAAAA, _fixture.Slave.Registers.First(r => r.Address == 14).Value);
            Assert.Equal(0xBBBB, _fixture.Slave.Registers.First(r => r.Address == 15).Value);
            Assert.Equal(0xCCCC, _fixture.Slave.Registers.First(r => r.Address == 16).Value);
        }

        [Fact]
        [DisplayName("N2-FC15 NModbus写入NewLife从机多个线圈并验证")]
        public async Task N2_WriteMultipleCoils()
        {
            using var master = CreateNModbusMaster();
            await master.WriteMultipleCoilsAsync(1, 20, new[] { true, false, true, false });
            Assert.Equal(1, _fixture.Slave.Coils.First(c => c.Address == 20).Value);
            Assert.Equal(0, _fixture.Slave.Coils.First(c => c.Address == 21).Value);
            Assert.Equal(1, _fixture.Slave.Coils.First(c => c.Address == 22).Value);
            Assert.Equal(0, _fixture.Slave.Coils.First(c => c.Address == 23).Value);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region N1 模式：NewLife Client → NModbus Slave（我→你）

    /// <summary>NModbus TCP 从机夹具（用于 N1 模式）</summary>
    public class NModbusSlaveForN1Fixture : IDisposable
    {
        public Int32 Port { get; }
        private readonly TcpListener _listener;
        private IModbusSlaveNetwork _slaveNetwork;
        private CancellationTokenSource _cts;
        private Task _listenTask;

        /// <summary>暴露数据存储供验证</summary>
        public ISlaveDataStore Store { get; } = new SimpleSlaveDataStore();

        public NModbusSlaveForN1Fixture()
        {
            var factory = new NModbus.ModbusFactory();
            Store.HoldingRegisters.WritePoints(0, Enumerable.Range(0, 20).Select(i => (UInt16)(500 + i)).ToArray());
            Store.CoilDiscretes.WritePoints(0, Enumerable.Range(0, 32).Select(i => i % 2 == 1).ToArray());
            Store.InputRegisters.WritePoints(0, Enumerable.Range(0, 10).Select(i => (UInt16)(600 + i)).ToArray());
            Store.CoilInputs.WritePoints(0, Enumerable.Range(0, 16).Select(i => i % 2 == 0).ToArray());

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _slaveNetwork = factory.CreateSlaveNetwork(_listener);
            _slaveNetwork.AddSlave(factory.CreateSlave(1, Store));
            _cts = new CancellationTokenSource();
            _listenTask = _slaveNetwork.ListenAsync(_cts.Token);
            Thread.Sleep(200);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _listenTask?.Wait(1000); } catch { }
            _slaveNetwork?.Dispose();
            _listener?.Stop();
            _cts?.Dispose();
        }
    }

    /// <summary>N1 全功能测试：NewLife Client 读写 NModbus Slave</summary>
    public class N1Tests : IClassFixture<NModbusSlaveForN1Fixture>
    {
        private readonly NModbusSlaveForN1Fixture _fixture;
        public N1Tests(NModbusSlaveForN1Fixture fixture) => _fixture = fixture;

        private async Task<ModbusTcp> CreateClientAsync()
        {
            var client = new ModbusTcp
            {
                Server = $"tcp://127.0.0.1:{_fixture.Port}",
                Timeout = 3000,
            };
            await client.OpenAsync();
            return client;
        }

        [Fact]
        [DisplayName("N1-FC03 NewLife读取NModbus从机保持寄存器")]
        public async Task N1_ReadHoldingRegisters()
        {
            using var client = await CreateClientAsync();
            var rs = await client.ReadRegisterAsync(1, 0, 5);
            Assert.NotNull(rs);
            Assert.Equal(5, rs.Length);
            for (var i = 0; i < 5; i++)
                Assert.Equal((UInt16)(500 + i), rs[i]);
        }

        [Fact]
        [DisplayName("N1-FC01 NewLife读取NModbus从机线圈")]
        public async Task N1_ReadCoils()
        {
            using var client = await CreateClientAsync();
            var rs = await client.ReadCoilAsync(1, 0, 8);
            Assert.NotNull(rs);
            Assert.Equal(8, rs.Length);
            for (var i = 0; i < 8; i++)
                Assert.Equal(i % 2 == 1, rs[i]);
        }

        [Fact]
        [DisplayName("N1-FC04 NewLife读取NModbus从机输入寄存器")]
        public async Task N1_ReadInputRegisters()
        {
            using var client = await CreateClientAsync();
            var rs = await client.ReadInputAsync(1, 0, 5);
            Assert.NotNull(rs);
            Assert.Equal(5, rs.Length);
            for (var i = 0; i < 5; i++)
                Assert.Equal((UInt16)(600 + i), rs[i]);
        }

        [Fact]
        [DisplayName("N1-FC02 NewLife读取NModbus从机离散输入")]
        public async Task N1_ReadDiscreteInputs()
        {
            using var client = await CreateClientAsync();
            var rs = await client.ReadDiscreteAsync(1, 0, 8);
            Assert.NotNull(rs);
            Assert.Equal(8, rs.Length);
            for (var i = 0; i < 8; i++)
                Assert.Equal(i % 2 == 0, rs[i]);
        }

        [Fact]
        [DisplayName("N1-FC06 NewLife写入NModbus从机单个寄存器并读回验证")]
        public async Task N1_WriteSingleRegister()
        {
            using var client = await CreateClientAsync();
            var wr = await client.WriteRegisterAsync(1, 12, 0xCDCD);
            Assert.Equal(0xCDCD, wr);

            var verify = await client.ReadRegisterAsync(1, 12, 1);
            Assert.Equal((UInt16)0xCDCD, verify[0]);
        }

        [Fact]
        [DisplayName("N1-FC05 NewLife写入NModbus从机单个线圈并读回验证")]
        public async Task N1_WriteSingleCoil()
        {
            using var client = await CreateClientAsync();
            await client.WriteCoilAsync(1, 5, 0xFF00);
            var verify = await client.ReadCoilAsync(1, 5, 1);
            Assert.True(verify[0]);
        }

        [Fact]
        [DisplayName("N1-FC16 NewLife写入NModbus从机多个寄存器并验证")]
        public async Task N1_WriteMultipleRegisters()
        {
            using var client = await CreateClientAsync();
            var wr = await client.WriteRegistersAsync(1, 15, [0x1111, 0x2222, 0x3333]);
            Assert.Equal(3, wr);

            var verify = await client.ReadRegisterAsync(1, 15, 3);
            Assert.Equal((UInt16)0x1111, verify[0]);
            Assert.Equal((UInt16)0x3333, verify[2]);
        }

        [Fact]
        [DisplayName("N1-FC15 NewLife写入NModbus从机多个线圈并验证")]
        public async Task N1_WriteMultipleCoils()
        {
            using var client = await CreateClientAsync();
            var wr = await client.WriteCoilsAsync(1, 10, [0xFF00, 0x0000, 0xFF00]);
            Assert.Equal(3, wr);

            var verify = await client.ReadCoilAsync(1, 10, 3);
            Assert.True(verify[0]);
            Assert.False(verify[1]);
            Assert.True(verify[2]);
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region N3 模式：NewLife Client → NewLife Slave（我→我 基线）

    /// <summary>NewLife Slave 夹具（用于 N3 基线，使用自增端口避免冲突）</summary>
    public class NewLifeSlaveForN3Fixture : IDisposable
    {
        private static Int32 _portSeed = 17000;
        public Int32 Port { get; }
        public ModbusSlave Slave { get; }

        public NewLifeSlaveForN3Fixture()
        {
            Port = Interlocked.Increment(ref _portSeed);
            Slave = new ModbusSlave
            {
                Port = Port, Host = 1,
                Registers = Enumerable.Range(0, 20)
                    .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(700 + i) })
                    .ToList(),
                Coils = Enumerable.Range(0, 32)
                    .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                    .ToList(),
            };
            try
            {
                Slave.Start();
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"N3Fixture failed to start slave on port {Port}: {ex.Message}", ex);
            }
        }

        public void Dispose() => Slave.Dispose();
    }

    [Collection("Non-Parallel Integration")]
    public class N3Tests : IClassFixture<NewLifeSlaveForN3Fixture>
    {
        private readonly NewLifeSlaveForN3Fixture _fixture;
        public N3Tests(NewLifeSlaveForN3Fixture fixture) => _fixture = fixture;

        private async Task<ModbusTcp> CreateClientAsync()
        {
            var client = new ModbusTcp
            {
                Server = $"tcp://127.0.0.1:{_fixture.Port}",
                Timeout = 3000,
            };
            await client.OpenAsync();
            return client;
        }

        [Fact]
        [DisplayName("N3-FC03 NewLife→NewLife 读保持寄存器")]
        public async Task N3_ReadHoldingRegisters()
        {
            using var client = await CreateClientAsync();
            var rs = await client.ReadRegisterAsync(1, 0, 10);
            Assert.NotNull(rs);
            Assert.Equal(10, rs.Length);
            for (var i = 0; i < 10; i++) Assert.Equal((UInt16)(700 + i), rs[i]);
        }

        [Fact]
        [DisplayName("N3-FC01 NewLife→NewLife 读线圈")]
        public async Task N3_ReadCoils()
        {
            using var client = await CreateClientAsync();
            var rs = await client.ReadCoilAsync(1, 0, 16);
            Assert.NotNull(rs);
            Assert.Equal(16, rs.Length);
            for (var i = 0; i < 16; i++) Assert.Equal(i % 2 == 1, rs[i]);
        }

        [Fact]
        [DisplayName("N3-FC04 NewLife→NewLife 读输入寄存器")]
        public async Task N3_ReadInputRegisters()
        {
            using var client = await CreateClientAsync();
            var rs = await client.ReadInputAsync(1, 0, 5);
            Assert.NotNull(rs);
            Assert.Equal(5, rs.Length);
            for (var i = 0; i < 5; i++) Assert.Equal((UInt16)(700 + i), rs[i]);
        }

        [Fact]
        [DisplayName("N3-FC02 NewLife→NewLife 读离散输入")]
        public async Task N3_ReadDiscreteInputs()
        {
            using var client = await CreateClientAsync();
            var rs = await client.ReadDiscreteAsync(1, 0, 8);
            Assert.NotNull(rs);
            Assert.Equal(8, rs.Length);
            for (var i = 0; i < 8; i++) Assert.Equal(i % 2 == 1, rs[i]);
        }

        [Fact]
        [DisplayName("N3-FC06 NewLife→NewLife 写入单个寄存器（地址10）并验证")]
        public async Task N3_WriteSingleRegister_Verify()
        {
            using var client = await CreateClientAsync();
            await client.WriteRegisterAsync(1, 10, 0xABCD);
            var verify = await client.ReadRegisterAsync(1, 10, 1);
            Assert.Equal((UInt16)0xABCD, verify[0]);
        }

        [Fact]
        [DisplayName("N3-FC05 NewLife→NewLife 写入线圈（地址16）并验证")]
        public async Task N3_WriteSingleCoil_Verify()
        {
            using var client = await CreateClientAsync();
            await client.WriteCoilAsync(1, 16, 0xFF00);
            var verify = await client.ReadCoilAsync(1, 16, 1);
            Assert.True(verify[0]);
        }

        [Fact]
        [DisplayName("N3-FC16 NewLife→NewLife 写多个寄存器并验证")]
        public async Task N3_WriteMultipleRegisters_Verify()
        {
            using var client = await CreateClientAsync();
            await client.WriteRegistersAsync(1, 14, [0x1111, 0x2222]);
            var verify = await client.ReadRegisterAsync(1, 14, 2);
            Assert.Equal((UInt16)0x1111, verify[0]);
            Assert.Equal((UInt16)0x2222, verify[1]);
        }

        [Fact]
        [DisplayName("N3-FC15 NewLife→NewLife 写多个线圈并验证")]
        public async Task N3_WriteMultipleCoils_Verify()
        {
            using var client = await CreateClientAsync();
            await client.WriteCoilsAsync(1, 16, [0xFF00, 0x0000]);
            var verify = await client.ReadCoilAsync(1, 16, 2);
            Assert.True(verify[0]);
            Assert.False(verify[1]);
        }

        [Fact]
        [DisplayName("N3-FC22 NewLife→NewLife 屏蔽写寄存器")]
        public async Task N3_MaskWriteRegister()
        {
            using var client = await CreateClientAsync();
            var before = await client.ReadRegisterAsync(1, 10, 1);
            var orig = before[0];
            await client.MaskWriteRegisterAsync(1, 10, 0xFFFE, 0x0000);
            var after = await client.ReadRegisterAsync(1, 10, 1);
            Assert.Equal((UInt16)(orig & 0xFFFE), after[0]);
        }

        [Fact]
        [DisplayName("N3-FC23 NewLife→NewLife 读写多个寄存器（通过原始命令）")]
        public async Task N3_ReadWriteMultipleRegisters()
        {
            using var client = await CreateClientAsync();
            // 手动构造 FC23 请求并直接发送（通过 SendCommandAsync 公共重载）
            var writeData = new Byte[] { 0xAA, 0xAA, 0xBB, 0xBB };
            var payload = new Byte[9 + writeData.Length];
            payload.Write((UInt16)0, 0, false);    // ReadAddr
            payload.Write((UInt16)3, 2, false);    // ReadCount
            payload.Write((UInt16)18, 4, false);   // WriteAddr
            payload.Write((UInt16)2, 6, false);    // WriteCount
            payload[8] = (Byte)writeData.Length;    // ByteCount
            Array.Copy(writeData, 0, payload, 9, writeData.Length);

            // 使用公共 SendCommandAsync(code, host, data) 重载直接发送原始 PDU
            var rs = await client.SendCommandAsync((FunctionCodes)23, 1, new ArrayPacket(payload));
            Assert.NotNull(rs);
            // 响应: ByteCount(1) + RegisterData(N)
            var data = rs.ReadBytes(1, 6);
            Assert.Equal((UInt16)700, data.ToUInt16(0, false));

            // 验证写入
            var verify = await client.ReadRegisterAsync(1, 18, 2);
            Assert.Equal((UInt16)0xAAAA, verify[0]);
            Assert.Equal((UInt16)0xBBBB, verify[1]);
        }
    }

    #endregion
}
