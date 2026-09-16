using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using NewLife;
using NewLife.Data;
using NewLife.IoT.Protocols;
using Xunit;

namespace XUnitTest.Protocols;

/// <summary>ModbusIp 基类单元测试</summary>
/// <remarks>
/// 覆盖 CloseAsync（null 安全）、Dispose、ReconnectAsync、Init（Server/Address 回退）。
/// Open/SendCommand/ReceiveCommand 需要真实网络，此处不测。
/// </remarks>
public class ModbusIpTests
{
    // ────────────────────────────────────────────────────────────────────────
    #region 辅助：可测试子类

    /// <summary>暴露 ModbusIp 受保护成员的可测试子类</summary>
    private sealed class TestableModbusIp : ModbusIp
    {
        /// <summary>记录 CloseAsync 调用次数</summary>
        public Int32 CloseCallCount { get; private set; }

        /// <summary>记录 OpenAsync 调用次数</summary>
        public Int32 OpenCallCount { get; private set; }

        /// <summary>公开暴露 CloseAsync</summary>
        public override async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            CloseCallCount++;
            await base.CloseAsync(cancellationToken);
        }

        /// <summary>公开暴露 OpenAsync（跳过真实网络）</summary>
        public override async Task OpenAsync(CancellationToken cancellationToken = default)
        {
            OpenCallCount++;
            // 不调用 base.OpenAsync，避免真实网络连接
            await Task.CompletedTask;
        }

        /// <summary>公开暴露 ReconnectAsync</summary>
        public new async Task ReconnectAsync(CancellationToken cancellationToken)
            => await base.ReconnectAsync(cancellationToken);

        /// <summary>实现 ReadMessage 抽象方法（测试用）</summary>
        protected override ModbusMessage? ReadMessage(ModbusMessage request, IPacket data, out Boolean match)
        {
            match = true;
            return null;
        }
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region CloseAsync

    [Fact]
    [DisplayName("CloseAsync _client 为 null 时不抛异常")]
    public async Task CloseAsync_NullClient_NoThrow()
    {
        var mb = new TestableModbusIp();
        // _client 尚未初始化
        await mb.CloseAsync();
        Assert.Equal(1, mb.CloseCallCount);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Dispose

    [Fact]
    [DisplayName("Dispose 调用 CloseAsync")]
    public void Dispose_CallsCloseAsync()
    {
        var mb = new TestableModbusIp();
        // 设置 Server 避免 Open 抛异常
        mb.GetType().GetProperty("Server")?.SetValue(mb, "tcp://127.0.0.1:502");

        mb.Dispose();
        // CloseAsync 应该被调用（在 Dispose 内部）
        Assert.True(mb.CloseCallCount >= 1);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region ReconnectAsync

    [Fact]
    [DisplayName("ReconnectAsync 调用 CloseAsync 再调用 OpenAsync")]
    public async Task ReconnectAsync_CallsCloseThenOpen()
    {
        var mb = new TestableModbusIp();
        await mb.ReconnectAsync(CancellationToken.None);

        Assert.Equal(1, mb.CloseCallCount);
        Assert.Equal(1, mb.OpenCallCount);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Init

    [Fact]
    [DisplayName("Init 从字典中解析 Server")]
    public void Init_ParsesServer()
    {
        var mb = new TestableModbusIp();
        mb.Init(new System.Collections.Generic.Dictionary<String, Object>
        {
            ["Server"] = "tcp://192.168.1.100:502"
        });

        Assert.Equal("tcp://192.168.1.100:502", mb.Server);
    }

    [Fact]
    [DisplayName("Init 无 Server 时回退至 Address")]
    public void Init_FallsBackToAddress()
    {
        var mb = new TestableModbusIp();
        mb.Init(new System.Collections.Generic.Dictionary<String, Object>
        {
            ["Address"] = "tcp://10.0.0.1:502"
        });

        Assert.Equal("tcp://10.0.0.1:502", mb.Server);
    }

    [Fact]
    [DisplayName("Init Server 优先于 Address")]
    public void Init_ServerTakesPriority()
    {
        var mb = new TestableModbusIp();
        mb.Init(new System.Collections.Generic.Dictionary<String, Object>
        {
            ["Server"] = "tcp://192.168.1.1:502",
            ["Address"] = "tcp://10.0.0.1:502"
        });

        Assert.Equal("tcp://192.168.1.1:502", mb.Server);
    }

    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Dispose 双重安全

    [Fact]
    [DisplayName("Dispose 多次调用不抛异常")]
    public void Dispose_MultipleCalls_NoThrow()
    {
        var mb = new TestableModbusIp();
        mb.Dispose();
        mb.Dispose(); // 第二次不应抛异常
        Assert.True(true);
    }

    #endregion
}
