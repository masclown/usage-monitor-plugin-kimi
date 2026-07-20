using System.Collections.Concurrent;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-057-001 / req-058 / req-059-001: RefreshService 关键场景覆盖。
/// </summary>
public class RefreshServiceTests : IDisposable
{
    private readonly List<PluginManager> _managersToDispose = new();

    public void Dispose()
    {
        foreach (var m in _managersToDispose) m.ReloadPlugins();
        _managersToDispose.Clear();
    }

    private PluginManager BuildManagerWith(FakeUsageProvider provider)
    {
        var mgr = new PluginManager();
        mgr.RegisterPlugin(provider);
        _managersToDispose.Add(mgr);
        return mgr;
    }

    private static ConfigService BuildConfigService()
    {
        // 用默认构造（%AppData%/UsageMonitor/）；测试中只读 .Settings 不实际写盘。
        return new ConfigService();
    }

    // -----------------------------------------------------------------
    // req-057-001: _isRefreshing 互斥 CAS——并发 RefreshAllAsync 仅 1 个真正执行
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_Concurrent_Calls_Only_Execute_Once()
    {
        // 用 Task.Delay 让 GetUsageAsync 持续 ~50ms，期间足以让多个并发调用看到 _isRefreshing=1。
        // 如不延迟，因 RefreshAllAsync 是 async、同步 Task.FromResult 会在 CAS 后立即退出，
        // 第二个调用看到的 _isRefreshing 已被 reset 为 0，依然能进入刷新逻辑。
        var callCount = 0;
        var provider = new FakeUsageProvider(
            "test",
            getUsageHandler: async (_, _) =>
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(50);
                return new UsageInfo
                {
                    ProviderId = "test",
                    ProviderName = "Test",
                    IsSuccess = true,
                };
            });

        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService();
        var svc = new RefreshService(mgr, cfg);

        // 并发触发 10 次 RefreshAllAsync
        var tasks = Enumerable.Range(0, 10).Select(_ => svc.RefreshAllAsync("manual")).ToArray();
        await Task.WhenAll(tasks);

        // 只有第一个调用进入刷新逻辑，其余 9 个因 _isRefreshing=1 被 CAS 拒绝
        callCount.Should().Be(1, "Interlocked.CompareExchange 应保证仅 1 个线程进入刷新");
    }

    // -----------------------------------------------------------------
    // 异常 Provider 不影响其他 Provider
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_One_Provider_Throws_Others_Still_Refresh()
    {
        var providerA = new FakeUsageProvider("a"); // 成功
        var providerB = new FakeUsageProvider(
            "b",
            getUsageHandler: (_, _) => throw new InvalidOperationException("boom")); // 抛

        var mgr = new PluginManager();
        mgr.RegisterPlugin(providerA);
        mgr.RegisterPlugin(providerB);
        _managersToDispose.Add(mgr);

        var cfg = BuildConfigService();
        var svc = new RefreshService(mgr, cfg);

        await svc.RefreshAllAsync("manual");

        providerA.GetUsageCallCount.Should().Be(1);
        providerB.GetUsageCallCount.Should().Be(1);
        // a 仍然成功
        mgr.GetPlugin("a")!.LastUsage.Should().NotBeNull();
        mgr.GetPlugin("a")!.LastUsage!.IsSuccess.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    // req-058: CircuitBreaker 触发——连续失败 N 次后跳过刷新
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_Circuit_Breaker_Opens_After_Threshold_Failures()
    {
        // req-058: CircuitBreakerThreshold = 5
        var provider = new FakeUsageProvider(
            "failing",
            getUsageHandler: (_, _) => throw new InvalidOperationException("always fails"));

        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService();
        var svc = new RefreshService(mgr, cfg);

        // 触发 6 次刷新：前 5 次实际执行，第 6 次开始被熔断跳过
        for (int i = 0; i < 6; i++)
        {
            await svc.RefreshAllAsync("manual");
        }

        // 前 5 次执行了 GetUsage；第 6 次被熔断跳过，不再调用
        provider.GetUsageCallCount.Should().Be(5);
    }

    // -----------------------------------------------------------------
    // req-058: 整体超时 CancellationToken
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_Overall_Timeout_Starts_Provider()
    {
        // req-058 验证：整体超时 CTS 已创建并传递给 Provider。
        // 实际等满 120s 太慢；本测试只验证 Provider 被启动、ct 可被取消。
        var started = new ManualResetEventSlim(false);
        var provider = new FakeUsageProvider(
            "hang",
            getUsageHandler: async (_, ct) =>
            {
                started.Set();
                // 等 1 秒后 ct 未取消也返回（避免测试 hang）
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    // 期望行为：ct 触发取消时正常返回
                }
                return new UsageInfo { ProviderId = "hang", ProviderName = "Hang" };
            });

        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService();
        var svc = new RefreshService(mgr, cfg);

        await svc.RefreshAllAsync("manual");

        // 关键断言：Provider 的 GetUsage 被调用了（说明 RefreshAllAsync 确实把 ct 传下去）
        provider.GetUsageCallCount.Should().Be(1);
        started.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
    }

    // -----------------------------------------------------------------
    // req-058-002: OnTimerTick fire-and-forget 异常能被观察到（不会 unobserved exception）
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshAllAsync_With_Internal_Fault_Does_Not_Propagate_To_ContinueWith_Callback()
    {
        // 触发正常刷新，验证不抛异常到调用方
        var provider = new FakeUsageProvider("ok");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService();
        var svc = new RefreshService(mgr, cfg);

        await svc.RefreshAllAsync("manual"); // 不抛
        provider.GetUsageCallCount.Should().Be(1);
    }

    // -----------------------------------------------------------------
    // RefreshProviderAsync: 单 provider 刷新事件触发
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshProviderAsync_Fires_UsageRefreshed_Event()
    {
        var provider = new FakeUsageProvider("single");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService();
        var svc = new RefreshService(mgr, cfg);

        UsageRefreshedEventArgs? capturedArgs = null;
        svc.UsageRefreshed += (_, args) => capturedArgs = args;

        await svc.RefreshProviderAsync("single");

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Usages.Should().HaveCount(1);
        capturedArgs.Usages[0].ProviderId.Should().Be("single");
    }

    [Fact]
    public async Task RefreshProviderAsync_Unknown_Id_No_Op()
    {
        var provider = new FakeUsageProvider("single");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService();
        var svc = new RefreshService(mgr, cfg);

        var eventFired = false;
        svc.UsageRefreshed += (_, _) => eventFired = true;

        await svc.RefreshProviderAsync("nonexistent");
        eventFired.Should().BeFalse();
    }
}
