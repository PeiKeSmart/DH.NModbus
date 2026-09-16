using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewLife;
using NewLife.Data;
using NewLife.IoT;
using NewLife.IoT.Drivers;
using NewLife.IoT.Models;
using NewLife.IoT.Protocols;
using NewLife.IoT.ThingModels;
using NewLife.Serial.Drivers;
using NewLife.Serial.Protocols;
using Xunit;

namespace XUnitTest.Lifecycles;

/// <summary>
/// 全通道生命周期集成测试
/// <para>
/// 每个测试方法独立验证一种传输变体的完整通信全程：
///   启动服务端 → 客户端连接（Open） → 读寄存器/线圈 → 写寄存器/线圈 → 读回验证 → 断开连接（Dispose） → 停止服务端
/// 涵盖：ModbusTCP、ModbusUDP、ModbusRtuOverTCP、ModbusRtuOverUDP 四种网络传输，
/// 以及对应驱动层（ModbusTcpDriver、ModbusUdpDriver、ModbusRtuOverTcpDriver、ModbusRtuOverUdpDriver）。
/// </para>
/// </summary>
public class ModbusLifecycleTests
{
    #region 辅助：分配随机端口

    private static Int32 AllocPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static Int32 AllocUdpPort()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    private static IPoint MakePoint(String name, UInt16 address, Int32 length = 2, String? type = null)
        => new LifecyclePoint { Name = name, Address = address.ToString(), Length = length, Type = type };

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusTCP 全程生命周期

    /// <summary>ModbusTCP 完整生命周期：启动从机 → Open → 读 → 写 → 验证 → Dispose → 停止</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusTCP：连接→读→写→验证→断开→停止")]
    public async Task ModbusTcp_FullLifecycle_ConnectReadWriteVerifyClose()
    {
        var port = AllocPort();

        // 1. 启动从机
        var slave = new ModbusSlave
        {
            Port = port,
            Host = 1,
            Registers = Enumerable.Range(0, 10)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(100 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 16)
                .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                .ToList(),
        };
        slave.Start();
        Thread.Sleep(200);

        // 2. 客户端 Open 连接
        var client = new ModbusTcp
        {
            Server = $"tcp://127.0.0.1:{port}",
            Timeout = 3000,
        };
        await client.OpenAsync();

        try
        {
            // 3. 读取寄存器（FC03）
            var regs = await client.ReadRegisterAsync(1, 0, 5);
            Assert.NotNull(regs);
            Assert.Equal(5, regs.Length);
            for (var i = 0; i < 5; i++)
                Assert.Equal((UInt16)(100 + i), regs[i]);

            // 4. 读取线圈（FC01）
            var coils = await client.ReadCoilAsync(1, 0, 8);
            Assert.NotNull(coils);
            for (var i = 0; i < 8; i++)
                Assert.Equal(i % 2 == 1, coils[i]);

            // 5. 写单寄存器后读回（FC06 → FC03）
            var wr = await client.WriteRegisterAsync(1, 5, 0x1234);
            Assert.Equal(0x1234, wr);
            var verify = await client.ReadRegisterAsync(1, 5, 1);
            Assert.Equal((UInt16)0x1234, verify![0]);

            // 6. 写多寄存器后读回（FC16 → FC03）
            var wrs = await client.WriteRegistersAsync(1, 6, [0xAAAA, 0xBBBB, 0xCCCC]);
            Assert.Equal(3, wrs);
            var verify2 = await client.ReadRegisterAsync(1, 6, 3);
            Assert.Equal((UInt16)0xAAAA, verify2![0]);
            Assert.Equal((UInt16)0xBBBB, verify2[1]);
            Assert.Equal((UInt16)0xCCCC, verify2[2]);

            // 7. 写线圈后读回（FC05 → FC01）
            await client.WriteCoilAsync(1, 0, 0xFF00);   // 地址0 写 ON
            var coil0 = await client.ReadCoilAsync(1, 0, 1);
            Assert.True(coil0![0]);

            await client.WriteCoilAsync(1, 1, 0x0000);   // 地址1 写 OFF
            var coil1 = await client.ReadCoilAsync(1, 1, 1);
            Assert.False(coil1![0]);

            // 8. 读离散输入（FC02）
            var disc = await client.ReadDiscreteAsync(1, 0, 4);
            Assert.NotNull(disc);
            Assert.Equal(4, disc.Length);

            // 9. 读输入寄存器（FC04）
            var inp = await client.ReadInputAsync(1, 0, 3);
            Assert.NotNull(inp);
            Assert.Equal(3, inp.Length);
        }
        finally
        {
            // 10. 断开客户端连接
            client.Dispose();

            // 11. 停止从机
            slave.Dispose();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusUDP 全程生命周期

    /// <summary>ModbusUDP 完整生命周期：启动 UDP 从机 → Open → 读 → 写 → 验证 → Dispose → 停止</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusUDP：连接→读→写→验证→断开→停止")]
    public async Task ModbusUdp_FullLifecycle_ConnectReadWriteVerifyClose()
    {
        var port = AllocUdpPort();

        // 1. 启动 UDP 模拟从机（MBAP over UDP）
        using var fixture = new InlineUdpSlaveServer(port);

        // 2. 客户端 Open 连接
        var client = new ModbusUdp
        {
            Server = $"udp://127.0.0.1:{port}",
            Timeout = 3000,
        };
        await client.OpenAsync();

        try
        {
            // 3. 读取寄存器（FC03）
            var regs = await client.ReadRegisterAsync(1, 0, 5);
            Assert.NotNull(regs);
            Assert.Equal(5, regs.Length);
            for (var i = 0; i < 5; i++)
                Assert.Equal((UInt16)(200 + i), regs[i]);

            // 4. 读取线圈（FC01）
            var coils = await client.ReadCoilAsync(1, 0, 8);
            Assert.NotNull(coils);
            for (var i = 0; i < 8; i++)
                Assert.Equal(i % 2 == 1, coils[i]);

            // 5. 写单寄存器后读回（FC06 → FC03）
            await client.WriteRegisterAsync(1, 5, 0xABCD);
            var verify = await client.ReadRegisterAsync(1, 5, 1);
            Assert.Equal((UInt16)0xABCD, verify![0]);

            // 6. 写线圈后读回（FC05 → FC01）
            await client.WriteCoilAsync(1, 0, 0xFF00);
            var coil0 = await client.ReadCoilAsync(1, 0, 1);
            Assert.True(coil0![0]);
        }
        finally
        {
            // 7. 断开客户端
            client.Dispose();
            // fixture 通过 using 自动 Dispose
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverTCP 全程生命周期

    /// <summary>ModbusRtuOverTCP 完整生命周期</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusRtuOverTCP：连接→读→写→验证→断开→停止")]
    public async Task ModbusRtuOverTcp_FullLifecycle_ConnectReadWriteVerifyClose()
    {
        var port = AllocPort();

        // 1. 启动 RTU over TCP 模拟从机
        using var fixture = new InlineRtuTcpSlaveServer(port);

        // 2. 客户端 Open 连接
        var client = new ModbusRtuOverTcp
        {
            Server = $"tcp://127.0.0.1:{port}",
            Timeout = 3000,
        };
        await client.OpenAsync();

        try
        {
            // 3. 读取寄存器（FC03）
            var regs = await client.ReadRegisterAsync(1, 0, 5);
            Assert.NotNull(regs);
            Assert.Equal(5, regs.Length);
            for (var i = 0; i < 5; i++)
                Assert.Equal((UInt16)(300 + i), regs[i]);

            // 4. 写单寄存器后读回
            await client.WriteRegisterAsync(1, 5, 0x5678);
            var verify = await client.ReadRegisterAsync(1, 5, 1);
            Assert.Equal((UInt16)0x5678, verify![0]);

            // 5. 读线圈
            var coils = await client.ReadCoilAsync(1, 0, 4);
            Assert.NotNull(coils);
            Assert.Equal(4, coils.Length);

            // 6. 写线圈后读回
            await client.WriteCoilAsync(1, 2, 0xFF00);
            var coil2 = await client.ReadCoilAsync(1, 2, 1);
            Assert.True(coil2![0]);
        }
        finally
        {
            // 7. 断开客户端
            client.Dispose();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverUDP 全程生命周期

    /// <summary>ModbusRtuOverUDP 完整生命周期</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusRtuOverUDP：连接→读→写→验证→断开→停止")]
    public async Task ModbusRtuOverUdp_FullLifecycle_ConnectReadWriteVerifyClose()
    {
        var port = AllocUdpPort();

        // 1. 启动 RTU over UDP 模拟从机
        using var fixture = new InlineRtuUdpSlaveServer(port);

        // 2. 客户端 Open 连接
        var client = new ModbusRtuOverUdp
        {
            Server = $"udp://127.0.0.1:{port}",
            Timeout = 3000,
        };
        await client.OpenAsync();

        try
        {
            // 3. 读取寄存器（FC03）
            var regs = await client.ReadRegisterAsync(1, 0, 5);
            Assert.NotNull(regs);
            Assert.Equal(5, regs.Length);
            for (var i = 0; i < 5; i++)
                Assert.Equal((UInt16)(400 + i), regs[i]);

            // 4. 写单寄存器后读回
            await client.WriteRegisterAsync(1, 5, 0xDEAD);
            var verify = await client.ReadRegisterAsync(1, 5, 1);
            Assert.Equal((UInt16)0xDEAD, verify![0]);

            // 5. 读线圈
            var coils = await client.ReadCoilAsync(1, 0, 4);
            Assert.NotNull(coils);
            Assert.Equal(4, coils.Length);
        }
        finally
        {
            // 6. 断开客户端
            client.Dispose();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusTcpDriver 驱动层全程生命周期

    /// <summary>ModbusTcpDriver 驱动层完整生命周期</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusTcpDriver：Open→Read→Write→Close→Dispose")]
    public async Task ModbusTcpDriver_FullLifecycle_OpenReadWriteClose()
    {
        var port = AllocPort();

        var slave = new ModbusSlave
        {
            Port = port,
            Host = 1,
            Registers = Enumerable.Range(0, 30)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(1000 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 32)
                .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                .ToList(),
        };
        slave.Start();
        Thread.Sleep(200);

        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = $"tcp://127.0.0.1:{port}",
            Host = 1,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            Timeout = 3000,
        };

        INode? node = null;
        try
        {
            // Open
            node = driver.Open(null, p);
            Assert.NotNull(node);
            Assert.NotNull(driver.Modbus);

            // Read
            var readPoints = Enumerable.Range(0, 5)
                .Select(i => MakePoint($"r{i}", (UInt16)i))
                .ToArray();
            var rs = driver.Read(node, readPoints);
            Assert.Equal(5, rs.Points.Length);
            for (var i = 0; i < 5; i++)
            {
                var bytes = rs.GetValue($"r{i}") as Byte[];
                Assert.NotNull(bytes);
                Assert.Equal((UInt16)(1000 + i), bytes.ToUInt16(0, false));
            }

            // Write（寄存器 20-22）
            var writePoint = MakePoint("rw20", 20);
            driver.Write(node, writePoint, ((UInt16)0x1A2B).GetBytes(false));

            // 读回验证
            var vrs = driver.Read(node, [MakePoint("rw20", 20)]);
            var vBytes = vrs.GetValue("rw20") as Byte[];
            Assert.NotNull(vBytes);
            Assert.Equal((UInt16)0x1A2B, vBytes.ToUInt16(0, false));

            // Close node
            driver.Close(node);
            node = null;
        }
        finally
        {
            if (node != null) driver.Close(node);
            driver.Dispose();
            slave.Dispose();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusUdpDriver 驱动层全程生命周期

    /// <summary>ModbusUdpDriver 驱动层完整生命周期</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusUdpDriver：Open→Read→Write→Close→Dispose")]
    public async Task ModbusUdpDriver_FullLifecycle_OpenReadWriteClose()
    {
        var port = AllocUdpPort();
        using var fixture = new InlineUdpSlaveServer(port);

        var driver = new ModbusUdpDriver();
        var p = new ModbusUdpParameter
        {
            Server = $"udp://127.0.0.1:{port}",
            Host = 1,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            Timeout = 3000,
        };

        INode? node = null;
        try
        {
            // Open
            node = driver.Open(null, p);
            Assert.NotNull(node);
            Assert.NotNull(driver.Modbus);

            // Read 5 个相邻寄存器（触发 BatchSegments 合并）
            var readPoints = Enumerable.Range(0, 5)
                .Select(i => MakePoint($"ur{i}", (UInt16)i))
                .ToArray();
            var rs = driver.Read(node, readPoints);
            Assert.Equal(5, rs.Points.Length);
            for (var i = 0; i < 5; i++)
            {
                var bytes = rs.GetValue($"ur{i}") as Byte[];
                Assert.NotNull(bytes);
                Assert.Equal((UInt16)(200 + i), bytes.ToUInt16(0, false));
            }

            // Write 寄存器后读回
            var writePoint = MakePoint("uw5", 5);
            driver.Write(node, writePoint, ((UInt16)0xCAFE).GetBytes(false));

            var vrs = driver.Read(node, [MakePoint("uw5", 5)]);
            var vBytes = vrs.GetValue("uw5") as Byte[];
            Assert.NotNull(vBytes);
            Assert.Equal((UInt16)0xCAFE, vBytes.ToUInt16(0, false));

            // Close
            driver.Close(node);
            node = null;
        }
        finally
        {
            if (node != null) driver.Close(node);
            driver.Dispose();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverTcpDriver 驱动层全程生命周期

    /// <summary>ModbusRtuOverTcpDriver 驱动层完整生命周期</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusRtuOverTcpDriver：Open→Read→Write→Close→Dispose")]
    public async Task ModbusRtuOverTcpDriver_FullLifecycle_OpenReadWriteClose()
    {
        var port = AllocPort();
        using var fixture = new InlineRtuTcpSlaveServer(port);

        var driver = new ModbusRtuOverTcpDriver();
        var p = new ModbusIpParameter
        {
            Server = $"tcp://127.0.0.1:{port}",
            Host = 1,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            Timeout = 3000,
        };

        INode? node = null;
        try
        {
            node = driver.Open(null, p);
            Assert.NotNull(node);
            Assert.NotNull(driver.Modbus);

            // Read
            var readPoints = Enumerable.Range(0, 3)
                .Select(i => MakePoint($"rr{i}", (UInt16)i))
                .ToArray();
            var rs = driver.Read(node, readPoints);
            Assert.Equal(3, rs.Points.Length);
            for (var i = 0; i < 3; i++)
            {
                var bytes = rs.GetValue($"rr{i}") as Byte[];
                Assert.NotNull(bytes);
                Assert.Equal((UInt16)(300 + i), bytes.ToUInt16(0, false));
            }

            // Write 后读回
            driver.Write(node, MakePoint("rr5", 5), ((UInt16)0xBEEF).GetBytes(false));
            var vrs = driver.Read(node, [MakePoint("rr5", 5)]);
            var vBytes = vrs.GetValue("rr5") as Byte[];
            Assert.NotNull(vBytes);
            Assert.Equal((UInt16)0xBEEF, vBytes.ToUInt16(0, false));

            driver.Close(node);
            node = null;
        }
        finally
        {
            if (node != null) driver.Close(node);
            driver.Dispose();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusRtuOverUdpDriver 驱动层全程生命周期

    /// <summary>ModbusRtuOverUdpDriver 驱动层完整生命周期</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusRtuOverUdpDriver：Open→Read→Write→Close→Dispose")]
    public async Task ModbusRtuOverUdpDriver_FullLifecycle_OpenReadWriteClose()
    {
        var port = AllocUdpPort();
        using var fixture = new InlineRtuUdpSlaveServer(port);

        var driver = new ModbusRtuOverUdpDriver();
        var p = new ModbusIpParameter
        {
            Server = $"udp://127.0.0.1:{port}",
            Host = 1,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            Timeout = 3000,
        };

        INode? node = null;
        try
        {
            node = driver.Open(null, p);
            Assert.NotNull(node);
            Assert.NotNull(driver.Modbus);

            // Read
            var readPoints = Enumerable.Range(0, 3)
                .Select(i => MakePoint($"rru{i}", (UInt16)i))
                .ToArray();
            var rs = driver.Read(node, readPoints);
            Assert.Equal(3, rs.Points.Length);
            for (var i = 0; i < 3; i++)
            {
                var bytes = rs.GetValue($"rru{i}") as Byte[];
                Assert.NotNull(bytes);
                Assert.Equal((UInt16)(400 + i), bytes.ToUInt16(0, false));
            }

            // Write 后读回
            driver.Write(node, MakePoint("rru5", 5), ((UInt16)0xF00D).GetBytes(false));
            var vrs = driver.Read(node, [MakePoint("rru5", 5)]);
            var vBytes = vrs.GetValue("rru5") as Byte[];
            Assert.NotNull(vBytes);
            Assert.Equal((UInt16)0xF00D, vBytes.ToUInt16(0, false));

            driver.Close(node);
            node = null;
        }
        finally
        {
            if (node != null) driver.Close(node);
            driver.Dispose();
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusAscii 生命周期（via ProcessRequest + 参数/属性验证）

    /// <summary>ModbusAscii 从机全功能码生命周期（无串口，直接调用 ProcessRequest）</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusAsciiSlave：ProcessRequest全覆盖然后销毁")]
    public async Task ModbusAsciiSlave_FullLifecycle_ProcessRequestThenDispose()
    {
        // 1. 创建并初始化从机
        var slave = new ModbusAsciiSlave
        {
            Host = 1,
            Registers = Enumerable.Range(0, 10)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(500 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 16)
                .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                .ToList(),
        };

        // 2. 读取寄存器 FC03
        var readReq = BuildAsciiReadFrame(1, FunctionCodes.ReadRegister, 0, 5);
        var readRs = slave.ProcessRequest(readReq);
        Assert.NotNull(readRs);
        var readMsg = ModbusAsciiMessage.Read((ReadOnlySpan<Byte>)readRs, reply: true);
        Assert.NotNull(readMsg);
        Assert.Equal(FunctionCodes.ReadRegister, readMsg.Code);

        // 3. 读取线圈 FC01
        var coilReq = BuildAsciiReadFrame(1, FunctionCodes.ReadCoil, 0, 8);
        var coilRs = slave.ProcessRequest(coilReq);
        Assert.NotNull(coilRs);

        // 4. 写单寄存器 FC06
        var writeReq = BuildAsciiWriteRegFrame(1, 3, 0xABCD);
        var writeRs = slave.ProcessRequest(writeReq);
        Assert.NotNull(writeRs);

        // 5. 验证写入生效（再次读取）
        var verifyReq = BuildAsciiReadFrame(1, FunctionCodes.ReadRegister, 3, 1);
        var verifyRs = slave.ProcessRequest(verifyReq);
        Assert.NotNull(verifyRs);
        var verifyMsg = ModbusAsciiMessage.Read((ReadOnlySpan<Byte>)verifyRs, reply: true);
        Assert.NotNull(verifyMsg);
        var payload = verifyMsg.Payload!.ReadBytes(0, verifyMsg.Payload.Total);
        // ByteCount(1) + value(2)
        Assert.True(payload.Length >= 3);
        var readBackVal = (UInt16)((payload[1] << 8) | payload[2]);
        Assert.Equal((UInt16)0xABCD, readBackVal);

        // 6. 写多线圈 FC15
        var writeCoilsReq = BuildAsciiWriteCoilsFrame(1, 0, [true, false, true, false]);
        var writeCoilsRs = slave.ProcessRequest(writeCoilsReq);
        Assert.NotNull(writeCoilsRs);

        // 7. 销毁从机
        slave.Dispose();
        Assert.True(slave.Disposed);
    }

    /// <summary>ModbusAsciiDriver 驱动属性验证生命周期（Open → CreateParameter → Close → Dispose）</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusAsciiDriver：Open→属性验证→Close→Dispose")]
    public async Task ModbusAsciiDriver_LifecyclePropertyValidation()
    {
        var driver = new ModbusAsciiDriver();
        var p = new ModbusRtuParameter
        {
            Host = 2,
            PortName = "COM99",      // 不会实际打开
            Baudrate = 19200,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
        };

        // Open 只创建 Modbus 实例，不打开串口
        var node = driver.Open(null, p);
        Assert.NotNull(node);

        var mn = node as ModbusNode;
        Assert.NotNull(mn);
        Assert.Equal(2, mn.Host);
        Assert.Equal(FunctionCodes.ReadRegister, mn.ReadCode);

        var modbus = driver.Modbus as ModbusAscii;
        Assert.NotNull(modbus);
        Assert.Equal("COM99", modbus.PortName);
        Assert.Equal(19200, modbus.Baudrate);

        // Close 节点（不应抛异常）
        driver.Close(node);

        // Dispose 驱动
        driver.Dispose();
    }

    /// <summary>ModbusRtuDriver 驱动属性验证生命周期</summary>
    [Fact]
    [System.ComponentModel.DisplayName("生命周期 ModbusRtuDriver：Open→属性验证→Close→Dispose")]
    public async Task ModbusRtuDriver_LifecyclePropertyValidation()
    {
        var driver = new ModbusRtuDriver();
        var p = new ModbusRtuParameter
        {
            Host = 3,
            PortName = "COM98",
            Baudrate = 115200,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
        };

        var node = driver.Open(null, p);
        Assert.NotNull(node);

        var mn = node as ModbusNode;
        Assert.NotNull(mn);
        Assert.Equal(3, mn.Host);

        var modbus = driver.Modbus as ModbusRtu;
        Assert.NotNull(modbus);
        Assert.Equal("COM98", modbus.PortName);
        Assert.Equal(115200, modbus.Baudrate);

        driver.Close(node);
        driver.Dispose();
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ModbusAscii 帧构造辅助

    private static Byte[] BuildAsciiReadFrame(Byte host, FunctionCodes code, UInt16 address, UInt16 count)
    {
        var pdu = new Byte[]
        {
            host, (Byte)code,
            (Byte)(address >> 8), (Byte)(address & 0xFF),
            (Byte)(count >> 8), (Byte)(count & 0xFF),
        };
        return WrapAsciiFrame(pdu);
    }

    private static Byte[] BuildAsciiWriteRegFrame(Byte host, UInt16 address, UInt16 value)
    {
        var pdu = new Byte[]
        {
            host, (Byte)FunctionCodes.WriteRegister,
            (Byte)(address >> 8), (Byte)(address & 0xFF),
            (Byte)(value >> 8), (Byte)(value & 0xFF),
        };
        return WrapAsciiFrame(pdu);
    }

    private static Byte[] BuildAsciiWriteCoilsFrame(Byte host, UInt16 startAddr, Boolean[] coils)
    {
        var byteCount = (Byte)Math.Ceiling(coils.Length / 8.0);
        var coilBytes = new Byte[byteCount];
        for (var i = 0; i < coils.Length; i++)
            if (coils[i]) coilBytes[i / 8] |= (Byte)(1 << (i % 8));

        var pdu = new Byte[7 + byteCount];
        pdu[0] = host;
        pdu[1] = (Byte)FunctionCodes.WriteCoils;
        pdu[2] = (Byte)(startAddr >> 8);
        pdu[3] = (Byte)(startAddr & 0xFF);
        pdu[4] = (Byte)(coils.Length >> 8);
        pdu[5] = (Byte)(coils.Length & 0xFF);
        pdu[6] = byteCount;
        Array.Copy(coilBytes, 0, pdu, 7, byteCount);
        return WrapAsciiFrame(pdu);
    }

    private static Byte[] WrapAsciiFrame(Byte[] pdu)
    {
        var lrc = ModbusHelper.Lrc(pdu, 0, pdu.Length);
        var sb = new StringBuilder();
        sb.Append(':');
        foreach (var b in pdu) sb.Append(b.ToString("X2"));
        sb.Append(lrc.ToString("X2"));
        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    #endregion
}

// ============================================================================
#region 内联从机辅助类（用于生命周期测试，每个测试独立实例）

/// <summary>UDP MBAP 从机内联服务（用于生命周期测试）</summary>
internal sealed class InlineUdpSlaveServer : IDisposable
{
    private readonly UdpClient _server;
    private volatile Boolean _running = true;
    private readonly Thread _thread;
    internal readonly List<RegisterUnit> Registers;
    internal readonly List<CoilUnit> Coils;

    public InlineUdpSlaveServer(Int32 port)
    {
        Registers = Enumerable.Range(0, 20)
            .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(200 + i) })
            .ToList();
        Coils = Enumerable.Range(0, 32)
            .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
            .ToList();

        _server = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        _thread = new Thread(Loop) { IsBackground = true };
        _thread.Start();
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

                var rs = HandleMbap(data);
                if (rs != null)
                    _server.Send(rs, rs.Length, remoteEP);
            }
            catch (SocketException) { }
            catch { }
        }
    }

    private Byte[]? HandleMbap(Byte[] data)
    {
        var req = ModbusIpMessage.Read(data.AsSpan());
        if (req == null) return null;

        var rs = req.CreateReply();
        IPacket? payload = null;

        switch (req.Code)
        {
            case FunctionCodes.ReadRegister:
            case FunctionCodes.ReadInput:
                payload = BuildRegPayload(req);
                break;
            case FunctionCodes.ReadCoil:
            case FunctionCodes.ReadDiscrete:
                payload = BuildCoilPayload(req);
                break;
            case FunctionCodes.WriteRegister:
                payload = ProcessWriteReg(req);
                break;
            case FunctionCodes.WriteCoil:
                payload = ProcessWriteCoil(req);
                break;
            default:
                rs.Code = (FunctionCodes)((Byte)req.Code | 0x80);
                rs.ErrorCode = ErrorCodes.IllegalFunction;
                payload = (ArrayPacket)new Byte[] { (Byte)ErrorCodes.IllegalFunction };
                break;
        }

        if (payload == null) return null;
        rs.Payload = payload;
        return rs.ToPacket().ReadBytes();
    }

    private IPacket BuildRegPayload(ModbusMessage msg)
    {
        var (addr, cnt) = msg.GetRequest();
        var values = Registers.Skip(addr).Take(cnt).SelectMany(r => r.GetData()).ToArray();
        var buf = new Byte[1 + values.Length];
        buf[0] = (Byte)values.Length;
        Array.Copy(values, 0, buf, 1, values.Length);
        return (ArrayPacket)buf;
    }

    private IPacket BuildCoilPayload(ModbusMessage msg)
    {
        var (addr, cnt) = msg.GetRequest();
        var cs = Coils.Skip(addr).Take(cnt).ToList();
        var byteCount = (Int32)Math.Ceiling(cnt / 8.0);
        var buf = new Byte[1 + byteCount];
        buf[0] = (Byte)byteCount;
        for (var i = 0; i < byteCount; i++)
        {
            var b = 0;
            for (var j = 0; j < Math.Min(8, cnt - i * 8); j++)
                if (cs[i * 8 + j].Value > 0) b |= 1 << j;
            buf[1 + i] = (Byte)b;
        }
        return (ArrayPacket)buf;
    }

    private IPacket? ProcessWriteReg(ModbusMessage msg)
    {
        if (msg.Payload == null || msg.Payload.Total < 4) return null;
        var addr = msg.GetAddress();
        var value = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);
        if (addr < Registers.Count) Registers[addr].Value = value;
        return msg.Payload;
    }

    private IPacket? ProcessWriteCoil(ModbusMessage msg)
    {
        if (msg.Payload == null || msg.Payload.Total < 4) return null;
        var addr = msg.GetAddress();
        var value = msg.Payload.ReadBytes(2, 2).ToUInt16(0, false);
        if (addr < Coils.Count) Coils[addr].Value = value == 0xFF00 ? (Byte)1 : (Byte)0;
        return msg.Payload;
    }

    public void Dispose()
    {
        _running = false;
        _server.Close();
    }
}

/// <summary>RTU over TCP 从机内联服务（用于生命周期测试）</summary>
internal sealed class InlineRtuTcpSlaveServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ModbusRtuSlave _slave;
    private volatile Boolean _running = true;
    private readonly Thread _acceptThread;

    public InlineRtuTcpSlaveServer(Int32 port)
    {
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

        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        _acceptThread.Start();
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
            catch (SocketException) { break; }
            catch { }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var buf = new Byte[256];
        stream.ReadTimeout = 3000;

        while (client.Connected && _running)
        {
            Int32 n;
            try { n = stream.Read(buf, 0, buf.Length); }
            catch (System.IO.IOException) { break; }
            if (n == 0) break;

            var rs = _slave.ProcessRequest(buf[..n]);
            if (rs != null && rs.Length > 0)
                stream.Write(rs, 0, rs.Length);
        }
    }

    public void Dispose()
    {
        _running = false;
        _listener.Stop();
    }
}

/// <summary>RTU over UDP 从机内联服务（用于生命周期测试）</summary>
internal sealed class InlineRtuUdpSlaveServer : IDisposable
{
    private readonly UdpClient _server;
    private readonly ModbusRtuSlave _slave;
    private volatile Boolean _running = true;
    private readonly Thread _thread;

    public InlineRtuUdpSlaveServer(Int32 port)
    {
        _slave = new ModbusRtuSlave
        {
            Host = 1,
            Registers = Enumerable.Range(0, 20)
                .Select(i => new RegisterUnit { Address = (UInt16)i, Value = (UInt16)(400 + i) })
                .ToList(),
            Coils = Enumerable.Range(0, 32)
                .Select(i => new CoilUnit { Address = (UInt16)i, Value = (Byte)(i % 2) })
                .ToList(),
        };

        _server = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        _thread = new Thread(Loop) { IsBackground = true };
        _thread.Start();
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

                var rs = _slave.ProcessRequest(data);
                if (rs != null && rs.Length > 0)
                    _server.Send(rs, rs.Length, remoteEP);
            }
            catch (SocketException) { }
            catch { }
        }
    }

    public void Dispose()
    {
        _running = false;
        _server.Close();
    }
}

#endregion

/// <summary>生命周期测试点位模型</summary>
internal sealed class LifecyclePoint : IPoint
{
    public String? Name { get; set; }
    public String? Address { get; set; }
    public String? Type { get; set; }
    public Int32 Length { get; set; }
    public String? Description { get; set; }
}