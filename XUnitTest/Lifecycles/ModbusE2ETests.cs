using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NewLife;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.IoT.ThingModels;
using Xunit;

namespace XUnitTest.Lifecycles;

/// <summary>E2E 集成测试：ModbusTcpDriver + ModbusSlave 端到端读写</summary>
/// <remarks>
/// 测试驱动层（ModbusTcpDriver.Read/Write）与真实从机（ModbusSlave）之间的完整链路。
/// 使用随机端口，每个测试类共享一个从机实例（IClassFixture）。
/// </remarks>
public class ModbusE2EFixture : IDisposable
{
    public ModbusSlave Slave { get; }
    public Int32 Port { get; }

    public ModbusE2EFixture()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Slave = new ModbusSlave
        {
            Port = Port,
            Host = 1,
            Registers = Enumerable.Range(0, 30)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(1000 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 32)
                .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                .ToList(),
        };
        Slave.Start();
        Thread.Sleep(300);
    }

    public void Dispose() => Slave.Dispose();
}

public class ModbusE2ETests : IClassFixture<ModbusE2EFixture>
{
    private readonly ModbusE2EFixture _fixture;

    public ModbusE2ETests(ModbusE2EFixture fixture) => _fixture = fixture;

    private (ModbusTcpDriver driver, INode node) CreateDriverNode(
        FunctionCodes readCode = FunctionCodes.ReadRegister,
        FunctionCodes writeCode = FunctionCodes.WriteRegister)
    {
        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = $"tcp://localhost:{_fixture.Port}",
            Host = 1,
            ReadCode = readCode,
            WriteCode = writeCode,
            Timeout = 3000,
        };
        var node = driver.Open(null, p);
        return (driver, node);
    }

    private static E2ETestPoint MakePoint(String name, UInt16 address, Int32 length = 2, String? type = null)
        => new() { Name = name, Address = address.ToString(), Length = length, Type = type };

    #region 单点读取

    [Fact]
    [System.ComponentModel.DisplayName("E2E 读单寄存器，值与从机一致")]
    public void E2E_ReadSingleRegister_MatchesSlave()
    {
        var (driver, node) = CreateDriverNode();
        try
        {
            var points = new IPoint[] { MakePoint("reg0", 0) };
            var rs = driver.Read(node, points);

            Assert.True(rs.IsSuccess);
            var bytes = rs.GetValue("reg0") as Byte[];
            Assert.NotNull(bytes);
            var val = bytes.ToUInt16(0, false);
            Assert.Equal((UInt16)1000, val);
        }
        finally
        {
            driver.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("E2E 读多个相邻寄存器，值与从机一致")]
    public void E2E_ReadMultipleRegisters_MatchesSlave()
    {
        var (driver, node) = CreateDriverNode();
        try
        {
            var points = new IPoint[]
            {
                MakePoint("r0", 0),
                MakePoint("r1", 1),
                MakePoint("r2", 2),
                MakePoint("r3", 3),
                MakePoint("r4", 4),
            };
            var rs = driver.Read(node, points);

            for (var i = 0; i < 5; i++)
            {
                var key = $"r{i}";
                var bytes = rs.GetValue(key) as Byte[];
                Assert.NotNull(bytes);
                var val = bytes.ToUInt16(0, false);
                Assert.Equal((UInt16)(1000 + i), val);
            }
        }
        finally
        {
            driver.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("E2E 读线圈，值与从机一致")]
    public void E2E_ReadCoils_MatchesSlave()
    {
        var (driver, node) = CreateDriverNode(FunctionCodes.ReadCoil, FunctionCodes.WriteCoil);
        try
        {
            var points = new IPoint[]
            {
                MakePoint("c0", 0, 0),
                MakePoint("c1", 1, 0),
                MakePoint("c2", 2, 0),
            };
            var rs = driver.Read(node, points);

            // 线圈初始值：偶数 OFF(0)，奇数 ON(1)
            Assert.Equal(0, rs.GetValue("c0"));
            Assert.Equal(1, rs.GetValue("c1"));
            Assert.Equal(0, rs.GetValue("c2"));
        }
        finally
        {
            driver.Dispose();
        }
    }

    #endregion

    #region 写后读验证

    [Fact]
    [System.ComponentModel.DisplayName("E2E 写单寄存器后读回，值一致")]
    public void E2E_WriteThenReadRegister_RoundTrip()
    {
        var (driver, node) = CreateDriverNode();
        try
        {
            // 写入寄存器 20 = 0x5A5A（传 Byte[] 绕过类型推断）
            var writePoint = MakePoint("rw20", 20);
            driver.Write(node, writePoint, ((UInt16)0x5A5A).GetBytes(false));

            // 读回
            var rs = driver.Read(node, [MakePoint("rw20", 20)]);
            Assert.True(rs.IsSuccess);
            var bytes = rs.GetValue("rw20") as Byte[];
            Assert.NotNull(bytes);
            Assert.Equal((UInt16)0x5A5A, bytes.ToUInt16(0, false));
        }
        finally
        {
            driver.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("E2E 写多寄存器后读回，值一致")]
    public void E2E_WriteMultipleRegisters_RoundTrip()
    {
        var (driver, node) = CreateDriverNode();
        try
        {
            // 写寄存器 21-24（各自不同的值，传 Byte[] 绕过类型推断）
            for (var i = 0; i < 4; i++)
            {
                var p = MakePoint($"rw{21 + i}", (UInt16)(21 + i));
                driver.Write(node, p, ((UInt16)(0xA000 + i)).GetBytes(false));
            }

            // 批量读回
            var points = Enumerable.Range(21, 4)
                .Select(i => (IPoint)MakePoint($"rw{i}", (UInt16)i))
                .ToArray();
            var rs = driver.Read(node, points);

            for (var i = 0; i < 4; i++)
            {
                var bytes = rs.GetValue($"rw{21 + i}") as Byte[];
                Assert.NotNull(bytes);
                Assert.Equal((UInt16)(0xA000 + i), bytes.ToUInt16(0, false));
            }
        }
        finally
        {
            driver.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("E2E 写线圈后读回，值一致")]
    public void E2E_WriteCoil_ThenReadBack()
    {
        var (driver, node) = CreateDriverNode(FunctionCodes.ReadCoil, FunctionCodes.WriteCoil);
        try
        {
            // 线圈 16 初始为 OFF（偶数），写 ON（传 Byte[] = 0xFF00 大端）
            var wp = MakePoint("c16", 16, 0);
            driver.Write(node, wp, new Byte[] { 0xFF, 0x00 });

            var rs = driver.Read(node, [MakePoint("c16", 16, 0)]);
            Assert.Equal(1, rs.GetValue("c16"));

            // 再写 OFF（传 Byte[] = 0x0000）
            driver.Write(node, wp, new Byte[] { 0x00, 0x00 });
            rs = driver.Read(node, [MakePoint("c16", 16, 0)]);
            Assert.Equal(0, rs.GetValue("c16"));
        }
        finally
        {
            driver.Dispose();
        }
    }

    #endregion

    #region 分段合并优化

    [Fact]
    [System.ComponentModel.DisplayName("E2E 相邻点位合并为单次请求")]
    public void E2E_AdjacentPoints_MergedIntoSingleRequest()
    {
        var (driver, node) = CreateDriverNode();
        try
        {
            // 请求 10 个相邻点位，驱动应合并成一次读取
            var points = Enumerable.Range(0, 10)
                .Select(i => (IPoint)MakePoint($"seg{i}", (UInt16)i))
                .ToArray();

            var rs = driver.Read(node, points);

            Assert.Equal(10, rs.Points.Length);
            for (var i = 0; i < 10; i++)
            {
                var bytes = rs.GetValue($"seg{i}") as Byte[];
                Assert.NotNull(bytes);
                Assert.Equal((UInt16)(1000 + i), bytes.ToUInt16(0, false));
            }
        }
        finally
        {
            driver.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("E2E 空点位集合，返回空字典")]
    public void E2E_EmptyPoints_ReturnsEmptyDict()
    {
        var (driver, node) = CreateDriverNode();
        try
        {
            var rs = driver.Read(node, []);
            Assert.Equal(0, rs.Points.Length);
        }
        finally
        {
            driver.Dispose();
        }
    }

    #endregion

    #region 多节点共用连接

    [Fact]
    [System.ComponentModel.DisplayName("E2E 同一驱动多节点共用 Modbus 连接")]
    public void E2E_MultipleNodes_SharedConnection()
    {
        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = $"tcp://localhost:{_fixture.Port}",
            Host = 1,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            Timeout = 3000,
        };

        try
        {
            var node1 = driver.Open(null, p);
            var node2 = driver.Open(null, p);

            // 两个节点应共用同一个 Modbus 实例
            Assert.Same(driver.Modbus, driver.Modbus);

            var rs1 = driver.Read(node1, [MakePoint("r0", 0)]);
            var rs2 = driver.Read(node2, [MakePoint("r0", 0)]);

            Assert.True(rs1.IsSuccess);
            Assert.True(rs2.IsSuccess);

            driver.Close(node1);
            driver.Close(node2);
        }
        finally
        {
            driver.Dispose();
        }
    }

    #endregion
}

/// <summary>测试用点位模型</summary>
internal class E2ETestPoint : IPoint
{
    public String? Name { get; set; }
    public String? Address { get; set; }
    public String? Type { get; set; }
    public Int32 Length { get; set; }
    public String? Description { get; set; }
}
