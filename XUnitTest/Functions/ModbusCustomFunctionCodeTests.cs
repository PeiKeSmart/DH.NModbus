using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NewLife;
using NewLife.Data;
using NewLife.IoT;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Functions;

/// <summary>自定义功能码注册测试</summary>
public class ModbusCustomFunctionCodeTests
{
    #region Master 端测试
    [Fact]
    [System.ComponentModel.DisplayName("RegisterFunctionCode 注册成功")]
    public void RegisterFunctionCode_Success()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        // 注册自定义功能码 0x41 (65)
        modbus.RegisterFunctionCode(0x41, (host, data, ct) => Task.FromResult<IPacket?>(data));

        Assert.True(modbus.CustomFunctionCodes.ContainsKey(0x41));
    }

    [Fact]
    [System.ComponentModel.DisplayName("RegisterFunctionCode 标准功能码冲突抛出 ArgumentException")]
    public void RegisterFunctionCode_StandardConflict_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        // FC03 是标准功能码，应拒绝注册
        var ex = Assert.Throws<ArgumentException>(() =>
            modbus.RegisterFunctionCode(0x03, (host, data, ct) => Task.FromResult<IPacket?>(data)));

        Assert.Contains("0x03", ex.Message);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RegisterFunctionCode handler 为 null 抛出 ArgumentNullException")]
    public void RegisterFunctionCode_NullHandler_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        Assert.Throws<ArgumentNullException>(() =>
            modbus.RegisterFunctionCode(0x41, null!));
    }

    [Fact]
    [System.ComponentModel.DisplayName("SendCustomCommandAsync 未注册功能码抛出 NotSupportedException")]
    public async Task SendCustomCommandAsync_NotRegistered_Throws()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            modbus.SendCustomCommandAsync(0x41, 1, new ArrayPacket([])));
    }

    [Fact]
    [System.ComponentModel.DisplayName("SendCustomCommandAsync 正常收发")]
    public async Task SendCustomCommandAsync_Success()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var requestData = (ArrayPacket)new Byte[] { 0x01, 0x02, 0x03, 0x04 };
        var responseData = (ArrayPacket)new Byte[] { 0xAA, 0xBB };

        // 模拟公共 SendCommandAsync 重载返回响应负载
        mb.Setup(e => e.SendCommandAsync(
                It.IsAny<FunctionCodes>(), It.IsAny<Byte>(), It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseData);

        var modbus = mb.Object;

        // 注册处理器：直接透传响应数据
        modbus.RegisterFunctionCode(0x41, (host, data, ct) =>
            Task.FromResult<IPacket?>(data));

        var rs = await modbus.SendCustomCommandAsync(0x41, 1, requestData);

        Assert.NotNull(rs);
        Assert.Equal(responseData.ReadBytes(), rs!.ReadBytes());
    }

    [Fact]
    [System.ComponentModel.DisplayName("SendCustomCommandAsync 异常响应抛出 ModbusException")]
    public async Task SendCustomCommandAsync_ErrorResponse_ThrowsModbusException()
    {
        var mb = new Mock<Modbus>() { CallBase = true };

        // 模拟 SendCommandAsync 抛出 ModbusException（网络层检测到错误响应）
        mb.Setup(e => e.SendCommandAsync(
                It.IsAny<FunctionCodes>(), It.IsAny<Byte>(), It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ModbusException(ErrorCodes.IllegalFunction, ErrorCodes.IllegalFunction.GetDescription()!));

        var modbus = mb.Object;
        modbus.RegisterFunctionCode(0x41, (host, data, ct) =>
            Task.FromResult<IPacket?>(data));

        var ex = await Assert.ThrowsAsync<ModbusException>(() =>
            modbus.SendCustomCommandAsync(0x41, 1, new ArrayPacket([])));

        Assert.Equal(ErrorCodes.IllegalFunction, ex.ErrorCode);
    }

    [Fact]
    [System.ComponentModel.DisplayName("RegisterFunctionCode 用户区段 65~72 可注册")]
    public void RegisterFunctionCode_UserDefinedRange65to72()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        for (var code = 65; code <= 72; code++)
        {
            modbus.RegisterFunctionCode((Byte)code, (host, data, ct) => Task.FromResult<IPacket?>(data));
            Assert.True(modbus.CustomFunctionCodes.ContainsKey((Byte)code));
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("RegisterFunctionCode 用户区段 100~110 可注册")]
    public void RegisterFunctionCode_UserDefinedRange100to110()
    {
        var mb = new Mock<Modbus>() { CallBase = true };
        var modbus = mb.Object;

        for (var code = 100; code <= 110; code++)
        {
            modbus.RegisterFunctionCode((Byte)code, (host, data, ct) => Task.FromResult<IPacket?>(data));
            Assert.True(modbus.CustomFunctionCodes.ContainsKey((Byte)code));
        }
    }
    #endregion

    #region Slave 端测试
    [Fact]
    [System.ComponentModel.DisplayName("ModbusSlave RegisterFunctionHandler 注册成功")]
    public void Slave_RegisterFunctionHandler_Success()
    {
        var slave = new ModbusSlave();
        slave.RegisterFunctionHandler(0x41, msg => new ArrayPacket(new Byte[] { 0x01 }));

        Assert.True(slave.CustomFunctionHandlers.ContainsKey(0x41));
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusSlave RegisterFunctionHandler handler 为 null 抛出 ArgumentNullException")]
    public void Slave_RegisterFunctionHandler_NullHandler_Throws()
    {
        var slave = new ModbusSlave();

        Assert.Throws<ArgumentNullException>(() =>
            slave.RegisterFunctionHandler(0x41, null!));
    }

    [Fact]
    [System.ComponentModel.DisplayName("ModbusSlave CustomFunctionHandlers 默认空字典")]
    public void Slave_CustomFunctionHandlers_DefaultEmpty()
    {
        var slave = new ModbusSlave();

        Assert.NotNull(slave.CustomFunctionHandlers);
        Assert.Empty(slave.CustomFunctionHandlers);
    }
    #endregion

    #region 集成测试
    [Fact]
    [System.ComponentModel.DisplayName("集成：Master 自定义功能码 → Slave 自定义处理器")]
    public async Task Integration_MasterCustomCommand_SlaveCustomHandler()
    {
        var slave = new ModbusSlave
        {
            Port = 1507,
        };

        // 在从机注册自定义功能码 0x41 处理器：将请求数据的每个字节加 1
        slave.RegisterFunctionHandler(0x41, msg =>
        {
            var data = msg.Payload?.ReadBytes() ?? [];
            var response = new Byte[data.Length];
            for (var i = 0; i < data.Length; i++)
                response[i] = (Byte)(data[i] + 1);
            return (ArrayPacket)response;
        });

        slave.Start();
        Thread.Sleep(200);

        try
        {
            var client = new ModbusTcp
            {
                Server = "tcp://localhost:1507",
                Timeout = 3000,
            };
            await client.OpenAsync();

            try
            {
                // 在客户端注册自定义功能码 0x41 处理器
                client.RegisterFunctionCode(0x41, (host, data, ct) =>
                    Task.FromResult<IPacket?>(data));

                var requestData = (ArrayPacket)new Byte[] { 0x10, 0x20, 0x30 };
                var rs = await client.SendCustomCommandAsync(0x41, 1, requestData);

                Assert.NotNull(rs);
                var result = rs!.ReadBytes();
                Assert.Equal(new Byte[] { 0x11, 0x21, 0x31 }, result);
            }
            finally
            {
                await client.CloseAsync();
            }
        }
        finally
        {
            slave.Dispose();
        }
    }

    [Fact]
    [System.ComponentModel.DisplayName("集成：Slave 未注册的功能码返回 IllegalFunction")]
    public async Task Integration_SlaveUnregisteredCode_ReturnsIllegalFunction()
    {
        var slave = new ModbusSlave
        {
            Port = 1508,
        };
        slave.Start();
        Thread.Sleep(200);

        try
        {
            var client = new ModbusTcp
            {
                Server = "tcp://localhost:1508",
                Timeout = 3000,
            };
            await client.OpenAsync();

            try
            {
                client.RegisterFunctionCode(0x42, (host, data, ct) =>
                    Task.FromResult<IPacket?>(data));

                // Slave 未注册 0x42，应返回 EC01 IllegalFunction
                var ex = await Assert.ThrowsAsync<ModbusException>(() =>
                    client.SendCustomCommandAsync(0x42, 1, new ArrayPacket(new Byte[] { 0x01 })));

                Assert.Equal(ErrorCodes.IllegalFunction, ex.ErrorCode);
            }
            finally
            {
                await client.CloseAsync();
            }
        }
        finally
        {
            slave.Dispose();
        }
    }
    #endregion
}
