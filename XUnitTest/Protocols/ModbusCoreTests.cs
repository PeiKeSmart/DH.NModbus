using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Protocols;

/// <summary>Modbus 基类核心机制单元测试</summary>
/// <remarks>
/// 覆盖 ExecuteWithRetryAsync、RegisterFunctionCode、SendCustomCommandAsync、
/// ReadAsync/WriteAsync 泛型重载等受保护/公开方法。
/// </remarks>
public class ModbusCoreTests
{
    // ────────────────────────────────────────────────────────────────────────
    #region 辅助：可测试子类

    /// <summary>暴露 Modbus 受保护成员的可测试子类</summary>
    private sealed class TestableModbus : Modbus
    {
        /// <summary>模拟 SendCommandAsync 返回指定结果</summary>
        public Func<ModbusMessage, CancellationToken, Task<ModbusMessage?>>? SendAsyncImpl { get; set; }

        /// <summary>记录最后一次 SendCommandAsync 调用</summary>
        public ModbusMessage? LastSentMessage { get; private set; }

        /// <summary>重写 SendCommandAsync，用于单元测试</summary>
        internal protected override async Task<ModbusMessage?> SendCommandAsync(ModbusMessage message, CancellationToken cancellationToken)
        {
            LastSentMessage = message;
            if (SendAsyncImpl != null)
                return await SendAsyncImpl(message, cancellationToken);
            return null;
        }

        /// <summary>公开暴露 ExecuteWithRetryAsync 供测试</summary>
        public async Task<TResult?> PublicExecuteWithRetryAsync<TResult>(Func<Task<TResult?>> func, CancellationToken cancellationToken) where TResult : class
            => await ExecuteWithRetryAsync(func, cancellationToken);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ExecuteWithRetryAsync

    [Fact]
    [DisplayName("ExecuteWithRetryAsync 首次成功直接返回")]
    public async Task ExecuteWithRetry_FirstTrySucceeds()
    {
        var mb = new TestableModbus { MaxRetry = 3 };
        var result = await mb.PublicExecuteWithRetryAsync(async () => "ok", CancellationToken.None);
        Assert.Equal("ok", result);
    }

    [Fact]
    [DisplayName("ExecuteWithRetryAsync 失败一次后重试成功")]
    public async Task ExecuteWithRetry_RetryThenSucceeds()
    {
        var mb = new TestableModbus { MaxRetry = 3, RetryInterval = 10 };
        var callCount = 0;

        var result = await mb.PublicExecuteWithRetryAsync<string>(async () =>
        {
            callCount++;
            if (callCount == 1) throw new InvalidOperationException("first fail");
            return "ok-after-retry";
        }, CancellationToken.None);

        Assert.Equal("ok-after-retry", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    [DisplayName("ExecuteWithRetryAsync 重试耗尽后抛出原始异常")]
    public async Task ExecuteWithRetry_RetryExhausted_Throws()
    {
        var mb = new TestableModbus { MaxRetry = 2, RetryInterval = 10 };
        var callCount = 0;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mb.PublicExecuteWithRetryAsync<string>(async () =>
            {
                callCount++;
                throw new InvalidOperationException("persistent fail");
            }, CancellationToken.None));

        Assert.Equal(3, callCount); // 1 original + 2 retries
    }

    [Fact]
    [DisplayName("ExecuteWithRetryAsync MaxRetry=0 时不重试")]
    public async Task ExecuteWithRetry_NoRetry_ThrowsImmediately()
    {
        var mb = new TestableModbus { MaxRetry = 0 };
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mb.PublicExecuteWithRetryAsync<string>(async () =>
            {
                callCount++;
                throw new InvalidOperationException("no retry");
            }, CancellationToken.None));

        Assert.Equal(1, callCount);
    }

    [Fact]
    [DisplayName("ExecuteWithRetryAsync 取消令牌触发时跳过重试")]
    public async Task ExecuteWithRetry_Cancelled_SkipsRetry()
    {
        var mb = new TestableModbus { MaxRetry = 3, RetryInterval = 100 };
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 预取消

        var callCount = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await mb.PublicExecuteWithRetryAsync<string>(async () =>
            {
                callCount++;
                cts.Token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("should not reach");
            }, cts.Token));
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region RegisterFunctionCode

    [Fact]
    [DisplayName("RegisterFunctionCode 正常注册自定义功能码")]
    public void RegisterFunctionCode_Normal()
    {
        var mb = new TestableModbus();
        mb.RegisterFunctionCode(65, (host, data, ct) => Task.FromResult<IPacket?>(null));
        Assert.True(mb.CustomFunctionCodes.ContainsKey(65));
    }

    [Fact]
    [DisplayName("RegisterFunctionCode null handler 抛 ArgumentNullException")]
    public void RegisterFunctionCode_NullHandler_Throws()
    {
        var mb = new TestableModbus();
        Assert.Throws<ArgumentNullException>(() => mb.RegisterFunctionCode(65, null!));
    }

    [Fact]
    [DisplayName("RegisterFunctionCode 标准功能码冲突抛 ArgumentException")]
    public void RegisterFunctionCode_StandardCodeConflict_Throws()
    {
        var mb = new TestableModbus();
        // FC03 = ReadRegister 是标准功能码
        var ex = Assert.Throws<ArgumentException>(() =>
            mb.RegisterFunctionCode(3, (host, data, ct) => Task.FromResult<IPacket?>(null)));
        Assert.Contains("已被标准功能码占用", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [DisplayName("RegisterFunctionCode 覆盖已有注册")]
    public void RegisterFunctionCode_Override()
    {
        var mb = new TestableModbus();
        mb.RegisterFunctionCode(100, (host, data, ct) => Task.FromResult<IPacket?>(new ArrayPacket([0x01])));
        mb.RegisterFunctionCode(100, (host, data, ct) => Task.FromResult<IPacket?>(new ArrayPacket([0x02])));
        Assert.True(mb.CustomFunctionCodes.ContainsKey(100));
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region SendCustomCommandAsync

    [Fact]
    [DisplayName("SendCustomCommandAsync 未注册功能码抛 NotSupportedException")]
    public async Task SendCustomCommandAsync_Unregistered_Throws()
    {
        var mb = new TestableModbus();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await mb.SendCustomCommandAsync(65, 1, new ArrayPacket([0x00])));
        Assert.Contains("未注册", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [DisplayName("SendCustomCommandAsync 注册后正常调用并返回")]
    public async Task SendCustomCommandAsync_Registered_Success()
    {
        var mb = new TestableModbus();
        mb.SendAsyncImpl = (msg, ct) =>
            Task.FromResult<ModbusMessage?>(new ModbusMessage { Reply = true, Host = 1, Code = (FunctionCodes)65, Payload = new ArrayPacket([0xAA, 0xBB]) });

        mb.RegisterFunctionCode(65, (host, data, ct) =>
        {
            Assert.Equal(0xAA, data.ReadBytes(0, 1)[0]);
            return Task.FromResult<IPacket?>(new ArrayPacket([0xCC]));
        });

        var rs = await mb.SendCustomCommandAsync(65, 1, new ArrayPacket([0x00]));
        Assert.NotNull(rs);
        Assert.Equal(0xCC, rs.ReadBytes(0, 1)[0]);
    }

    [Fact]
    [DisplayName("SendCustomCommandAsync 网络层返回 null → 结果为 null")]
    public async Task SendCustomCommandAsync_NullResponse_ReturnsNull()
    {
        var mb = new TestableModbus();
        mb.SendAsyncImpl = (msg, ct) => Task.FromResult<ModbusMessage?>(null);

        IPacket? handlerCalled = null;
        mb.RegisterFunctionCode(66, (host, data, ct) =>
        {
            handlerCalled = data;
            return Task.FromResult<IPacket?>(new ArrayPacket([0x01]));
        });

        var rs = await mb.SendCustomCommandAsync(66, 1, new ArrayPacket([0x00]));
        Assert.Null(rs);
        Assert.Null(handlerCalled); // 网络层 null → handler 不会被调用
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ReadAsync / WriteAsync 泛型重载（不支持的功能码）

    [Fact]
    [DisplayName("ReadAsync 不支持的功能码抛 NotSupportedException")]
    public async Task ReadAsync_UnsupportedCode_Throws()
    {
        var mb = new TestableModbus();
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await mb.ReadAsync(FunctionCodes.Diagnostics, 1, 0, 1));
    }

    [Fact]
    [DisplayName("WriteAsync 不支持的功能码抛 NotSupportedException")]
    public async Task WriteAsync_UnsupportedCode_Throws()
    {
        var mb = new TestableModbus();
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await mb.WriteAsync(FunctionCodes.Diagnostics, 1, 0, [0x0000]));
    }

    #endregion
}
