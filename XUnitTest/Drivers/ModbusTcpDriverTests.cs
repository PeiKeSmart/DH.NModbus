using System;
using System.ComponentModel;
using System.Security.Authentication;
using NewLife;
using NewLife.IoT.Drivers;
using NewLife.IoT.Protocols;
using NewLife.Log;
using NewLife.Net;
using Xunit;

namespace XUnitTest.Drivers;

public class ModbusTcpDriverTests : DisposeBase
{
    private readonly NetServer _server;

    public ModbusTcpDriverTests()
    {
        _server = new NetServer(1502)
        {
            Log = XTrace.Log
        };
        _server.Start();
    }

    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);

        _server.Dispose();
    }

    [Fact]
    public void Test1()
    {
        var driver = new ModbusTcpDriver();

        var p = new ModbusTcpParameter
        {
            Host = 3,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            Server = "tcp://localhost:1502",
        };
        //var dic = p.ToDictionary();

        var node = driver.Open(null, p);

        var node2 = node as ModbusNode;
        Assert.NotNull(node2);

        Assert.Equal(p.Host, node2.Host);
        Assert.Equal(p.ReadCode, node2.ReadCode);
        Assert.Equal(p.WriteCode, node2.WriteCode);
        //Assert.NotNull(node2.Device);

        var modbus = driver.Modbus as ModbusTcp;
        Assert.NotNull(modbus);
        Assert.Equal(p.Server, modbus.Server);
    }

    [Fact]
    public void Test2()
    {
        var driver = new ModbusTcpDriver();

        var p = new ModbusTcpParameter
        {
            Host = 3,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            Server = "tcp://localhost:1502",
        };

        var node = driver.Open(null, p);

        var node2 = node as ModbusNode;
        Assert.NotNull(node2);

        Assert.Equal(p.Host, node2.Host);
        Assert.Equal(p.ReadCode, node2.ReadCode);
        Assert.Equal(p.WriteCode, node2.WriteCode);
        //Assert.NotNull(node2.Device);

        var modbus = driver.Modbus as ModbusTcp;
        Assert.NotNull(modbus);
        Assert.Equal(p.Server, modbus.Server);
    }

    // ────────────────────────────────────────────────────────────────────────
    #region Enron 模式

    [Fact]
    [DisplayName("CreateModbus IsEnron=true → ModbusEnron")]
    public void CreateModbus_EnronMode_CreatesModbusEnron()
    {
        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = "tcp://127.0.0.1:1",
            Host = 1,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            IsEnron = true,
        };

        var node = driver.Open(null, p);

        Assert.NotNull(node);
        var modbus = Assert.IsType<ModbusEnron>(driver.Modbus);
        Assert.Equal("tcp://127.0.0.1:1", modbus.Server);
        Assert.Equal(0, modbus.ProtocolId);
    }

    [Fact]
    [DisplayName("CreateModbus IsEnron=false → ModbusTcp")]
    public void CreateModbus_NonEnron_CreatesModbusTcp()
    {
        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = "tcp://127.0.0.1:1",
            Host = 1,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            IsEnron = false,
        };

        var node = driver.Open(null, p);

        Assert.NotNull(node);
        Assert.IsType<ModbusTcp>(driver.Modbus);
        Assert.IsNotType<ModbusEnron>(driver.Modbus);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region TLS 配置

    [Fact]
    [DisplayName("CreateModbus SslProtocol 传参正确")]
    public void CreateModbus_SslProtocol_Propagates()
    {
        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = "tcp://127.0.0.1:1",
            Host = 1,
            ReadCode = FunctionCodes.ReadRegister,
            WriteCode = FunctionCodes.WriteRegister,
            SslProtocol = SslProtocols.Tls12,
        };

        driver.Open(null, p);

        var modbus = (ModbusTcp)driver.Modbus;
        Assert.Equal(SslProtocols.Tls12, modbus.SslProtocol);
    }

    [Fact]
    [DisplayName("CreateModbus 默认 SslProtocol 为 None")]
    public void CreateModbus_DefaultSslProtocol_None()
    {
        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = "tcp://127.0.0.1:1",
            Host = 1,
        };

        driver.Open(null, p);

        var modbus = (ModbusTcp)driver.Modbus;
        Assert.Equal(SslProtocols.None, modbus.SslProtocol);
    }

    [Fact]
    [DisplayName("CreateModbus 带 ProtocolId 传参")]
    public void CreateModbus_ProtocolId_Propagates()
    {
        var driver = new ModbusTcpDriver();
        var p = new ModbusTcpParameter
        {
            Server = "tcp://127.0.0.1:1",
            Host = 1,
            ProtocolId = 7,
        };

        driver.Open(null, p);

        var modbus = (ModbusTcp)driver.Modbus;
        Assert.Equal((UInt16)7, modbus.ProtocolId);
    }

    #endregion
}