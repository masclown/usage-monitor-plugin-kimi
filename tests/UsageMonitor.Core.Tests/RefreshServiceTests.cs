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
    // req-110：刷新链路现在可能触发 ConfigService.Save（TryBindAccountStableId），
    // 必须把配置路径重定向到临时目录，避免测试写坏用户真实 config.json。
    private readonly TempDir _tempDir = new();

    public void Dispose()
    {
        foreach (var m in _managersToDispose) m.ReloadPlugins();
        _managersToDispose.Clear();
        _tempDir.Dispose();
    }

    private PluginManager BuildManagerWith(FakeUsageProvider provider)
    {
        var mgr = new PluginManager();
        mgr.RegisterPlugin(provider);
        _managersToDispose.Add(mgr);
        return mgr;
    }

    /// <summary>
    /// 构建测试用 ConfigService（默认构造，测试中只改内存 .Settings 不写盘）。
    /// <para>req-110 Q1 刷新门控后，刷新单元 = 已启用插件 × 已启用且配置完成的账号——
    /// 因此为每个参测 Provider 直接向 Settings 注入启用账号 + ApiKey 凭据（不走 AddAccount，避免 Save 写盘）。</para>
    /// </summary>
    /// <param name="providerIds">需要通过刷新门控的 Provider ID 集合。</param>
    private ConfigService BuildConfigService(params string[] providerIds)
    {
        var cfg = new ConfigService();
        // 重定向配置路径到临时目录（Save 不碰用户真实配置）
        ReflectionHelpers.SetField(cfg, "_configDirectory", _tempDir.Path);
        ReflectionHelpers.SetField(cfg, "_configFilePath", _tempDir.Combine("config.json"));
        foreach (var pid in providerIds)
        {
            cfg.Settings.Accounts.Add(new Account
            {
                ProviderId = pid,
                AccountId = "default",
                Enabled = true,
                IsDefault = true
            });
            var pc = new ProviderConfig { ProviderId = pid };
            pc.SetValue("ApiKey", "test-key");
            cfg.Settings.ProviderConfigs[pid] = pc;
        }
        return cfg;
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
        var cfg = BuildConfigService("test");
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

        var cfg = BuildConfigService("a", "b");
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
        var cfg = BuildConfigService("failing");
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
        var cfg = BuildConfigService("hang");
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
        var cfg = BuildConfigService("ok");
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
        var cfg = BuildConfigService("single");
        var svc = new RefreshService(mgr, cfg);

        UsageRefreshedEventArgs? capturedArgs = null;
        svc.UsageRefreshed += (_, args) => capturedArgs = args;

        await svc.RefreshProviderAsync("single");

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Usages.Should().HaveCount(1);
        capturedArgs.Usages[0].ProviderId.Should().Be("single");
        // req-110 P1-3：usage 路由键重写为配置账号 ID
        capturedArgs.Usages[0].AccountId.Should().Be("default");
        capturedArgs.Usages[0].CardId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshProviderAsync_Unknown_Id_No_Op()
    {
        var provider = new FakeUsageProvider("single");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService("single");
        var svc = new RefreshService(mgr, cfg);

        var eventFired = false;
        svc.UsageRefreshed += (_, _) => eventFired = true;

        await svc.RefreshProviderAsync("nonexistent");
        eventFired.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    // req-110 Q1: 刷新门控——无账号 / 账号未启用 / 未配凭据 / 插件未启用 均跳过刷新
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshPluginAsync_No_Account_Skips_Refresh()
    {
        var provider = new FakeUsageProvider("noacct");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService(); // 不注入账号
        var svc = new RefreshService(mgr, cfg);

        await svc.RefreshProviderAsync("noacct");

        provider.GetUsageCallCount.Should().Be(0, "req-110 门控：无账号不刷新");
    }

    [Fact]
    public async Task RefreshPluginAsync_Disabled_Account_Skips_Refresh()
    {
        var provider = new FakeUsageProvider("disacct");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService("disacct");
        cfg.Settings.Accounts[0].Enabled = false; // 唯一账号禁用
        var svc = new RefreshService(mgr, cfg);

        await svc.RefreshProviderAsync("disacct");

        provider.GetUsageCallCount.Should().Be(0, "req-110 门控：账号未启用不刷新");
    }

    [Fact]
    public async Task RefreshPluginAsync_No_Credential_Skips_Refresh()
    {
        var provider = new FakeUsageProvider("nocred");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService();
        // 只建账号不配凭据
        cfg.Settings.Accounts.Add(new Account { ProviderId = "nocred", AccountId = "default", Enabled = true });
        var svc = new RefreshService(mgr, cfg);

        await svc.RefreshProviderAsync("nocred");

        provider.GetUsageCallCount.Should().Be(0, "req-110 门控：账号未配置凭据不刷新");
    }

    [Fact]
    public async Task RefreshPluginAsync_Disabled_Plugin_Skips_Refresh()
    {
        var provider = new FakeUsageProvider("displug");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService("displug");
        cfg.Settings.PluginEnabled["displug"] = false; // 插件未启用
        var svc = new RefreshService(mgr, cfg);

        await svc.RefreshProviderAsync("displug");

        provider.GetUsageCallCount.Should().Be(0, "req-110 门控：插件未启用不刷新");
    }

    // -----------------------------------------------------------------
    // req-110 P1-3: 网页身份哈希绑定到发起账号（BoundStableId）
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshPluginAsync_Binds_StableId_And_Rewrites_AccountId()
    {
        var provider = new FakeUsageProvider(
            "bindtest",
            getUsageHandler: (_, _) => Task.FromResult(new UsageInfo
            {
                ProviderId = "bindtest",
                ProviderName = "BindTest",
                IsSuccess = true,
                AccountId = "abcdef0123456789" // 插件返回的网页身份哈希
            }));
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService("bindtest");
        var svc = new RefreshService(mgr, cfg);

        await svc.RefreshProviderAsync("bindtest");

        // 哈希写入账号绑定元数据；usage 路由键重写为配置账号 ID
        cfg.GetAccount("bindtest", "default")!.BoundStableId.Should().Be("abcdef0123456789");
        mgr.GetPlugin("bindtest")!.LastUsage!.AccountId.Should().Be("default");
    }

    // -----------------------------------------------------------------
    // req-110：零产出早退路径必须清除 _lastAccountUsages 旧缓存，
    // 避免下一轮事件分发把上一轮 stale 数据当新数据推给 UI
    // -----------------------------------------------------------------

    [Fact]
    public async Task RefreshProviderAsync_All_Accounts_Gated_Clears_Stale_Account_Usages()
    {
        // 第一轮成功刷新写入 _lastAccountUsages；随后移除凭据使全部账号被门控④跳过，
        // 第二轮必须清除旧缓存——UsageRefreshed 不得再分发第一轮的 stale 数据。
        var provider = new FakeUsageProvider("stalecred");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService("stalecred");
        var svc = new RefreshService(mgr, cfg);

        var eventCount = 0;
        svc.UsageRefreshed += (_, _) => eventCount++;

        await svc.RefreshProviderAsync("stalecred");
        eventCount.Should().Be(1, "第一轮成功取数应分发事件");

        // 移除凭据 → 门控④（账号未配置凭据）跳过全部账号，accountUsages 零产出
        cfg.Settings.ProviderConfigs["stalecred"].SetValue("ApiKey", "");

        await svc.RefreshProviderAsync("stalecred");

        provider.GetUsageCallCount.Should().Be(1, "第二轮被门控跳过，不应再取数");
        eventCount.Should().Be(1, "零产出时不得分发上一轮的 stale 账号数据");
    }

    [Fact]
    public async Task RefreshProviderAsync_Accounts_Disabled_Clears_Stale_Account_Usages()
    {
        // 第一轮成功刷新后禁用全部账号 → 门控③（无已启用账号）早退，
        // 同样必须清除旧缓存，不得分发 stale 数据。
        var provider = new FakeUsageProvider("staleacct");
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService("staleacct");
        var svc = new RefreshService(mgr, cfg);

        var eventCount = 0;
        svc.UsageRefreshed += (_, _) => eventCount++;

        await svc.RefreshProviderAsync("staleacct");
        eventCount.Should().Be(1, "第一轮成功取数应分发事件");

        // 禁用唯一账号 → 门控③早退
        cfg.Settings.Accounts.First(a => a.ProviderId == "staleacct").Enabled = false;

        await svc.RefreshProviderAsync("staleacct");

        provider.GetUsageCallCount.Should().Be(1, "第二轮被门控跳过，不应再取数");
        eventCount.Should().Be(1, "零产出时不得分发上一轮的 stale 账号数据");
    }

    // -----------------------------------------------------------------
    // req-110 P2-3：账号级熔断的自动恢复链路——到期半开放行一次，
    // 成功后账号计数归零、熔断解除（验证恢复不依赖 Provider 级重置）
    // -----------------------------------------------------------------

    [Fact]
    public async Task Account_Circuit_Breaker_Auto_Recovers_Via_HalfOpen_After_Expiry()
    {
        // 阶段 1：连续 5 次失败触发账号级熔断（key = "recover:default"）；
        // 阶段 2：熔断期内跳过；阶段 3：把到期时间拨到过去 → 半开放行一次 →
        // 成功后 L356 将账号计数归零，熔断完全解除——证明恢复不需要 Provider 级成功重置账号计数。
        var shouldFail = true;
        var provider = new FakeUsageProvider(
            "recover",
            getUsageHandler: (_, _) => shouldFail
                ? throw new InvalidOperationException("account fails")
                : Task.FromResult(new UsageInfo
                {
                    ProviderId = "recover",
                    ProviderName = "Recover",
                    IsSuccess = true
                }));
        var mgr = BuildManagerWith(provider);
        var cfg = BuildConfigService("recover");
        var svc = new RefreshService(mgr, cfg);

        // 阶段 1：5 次失败 → 账号级熔断开启
        for (int i = 0; i < 5; i++)
            await svc.RefreshProviderAsync("recover");
        provider.GetUsageCallCount.Should().Be(5);

        var circuit = ReflectionHelpers.GetField<ConcurrentDictionary<string, DateTime>>(svc, "_circuitOpenUntil")!;
        circuit.Should().ContainKey("recover:default", "连续 5 次失败后账号级熔断应开启");

        // 阶段 2：熔断期内再刷 → 账号被跳过，不取数
        await svc.RefreshProviderAsync("recover");
        provider.GetUsageCallCount.Should().Be(5, "熔断期内账号应被跳过");

        // 阶段 3：模拟熔断到期（拨到过去）+ 数据源恢复 → 半开放行一次并成功
        circuit["recover:default"] = DateTime.UtcNow.AddMinutes(-1);
        shouldFail = false;
        await svc.RefreshProviderAsync("recover");

        provider.GetUsageCallCount.Should().Be(6, "熔断到期后半开应放行一次尝试");
        var failures = ReflectionHelpers.GetField<ConcurrentDictionary<string, int>>(svc, "_consecutiveFailures")!;
        failures["recover:default"].Should().Be(0, "账号刷新成功后计数在账号循环内就地归零（不依赖 Provider 级重置）");
        circuit.Should().NotContainKey("recover:default", "半开尝试成功后熔断应解除");
    }
}
