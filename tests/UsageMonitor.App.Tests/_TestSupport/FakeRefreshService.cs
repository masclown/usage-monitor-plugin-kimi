using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Tests._TestSupport;

/// <summary>
/// app-layer-zero-test: 测试用 <see cref="IRefreshService"/> 桩实现。
/// <para>
/// 记录 RefreshAllAsync / RefreshProviderAsync 的调用次数，
/// 并支持通过 <see cref="ThrowOnRefreshAll"/> 注入刷新失败场景，
/// 供 MainViewModel.RefreshCommand 的成功 / 失败路径断言。
/// </para>
/// </summary>
public sealed class FakeRefreshService : IRefreshService
{
    /// <summary>RefreshAllAsync 累计调用次数。</summary>
    public int RefreshAllCallCount { get; private set; }

    /// <summary>RefreshProviderAsync 累计调用次数。</summary>
    public int RefreshProviderCallCount { get; private set; }

    /// <summary>为 true 时 RefreshAllAsync 抛出 InvalidOperationException（模拟刷新失败）。</summary>
    public bool ThrowOnRefreshAll { get; set; }

    public event EventHandler<RefreshFailedEventArgs>? RefreshFailed;
    public event EventHandler<UsageRefreshedEventArgs>? UsageRefreshed;
    public event EventHandler? RefreshStarted;

    /// <summary>空实现：测试不启动定时器。</summary>
    public void Start() { }

    /// <summary>空实现：测试不启动定时器。</summary>
    public void Stop() { }

    /// <summary>记录调用并按 <see cref="ThrowOnRefreshAll"/> 决定成功或抛出。</summary>
    public Task RefreshAllAsync(string triggerKind = "manual")
    {
        RefreshAllCallCount++;
        if (ThrowOnRefreshAll)
            throw new InvalidOperationException("fake refresh failure");
        return Task.CompletedTask;
    }

    /// <summary>记录单 Provider 刷新调用。</summary>
    public Task RefreshProviderAsync(string providerId, CancellationToken ct = default)
    {
        RefreshProviderCallCount++;
        return Task.CompletedTask;
    }

    /// <summary>无资源需要释放；同时压制未使用事件的编译告警。</summary>
    public void Dispose()
    {
        _ = RefreshFailed;
        _ = UsageRefreshed;
        _ = RefreshStarted;
    }
}
