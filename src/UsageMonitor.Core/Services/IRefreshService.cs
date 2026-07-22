using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-069 F-10：刷新服务接口——为 DI 容器做准备。
/// 定义定时刷新、手动刷新、刷新事件等核心契约。
/// </summary>
public interface IRefreshService : IDisposable
{
    /// <summary>单 Provider 刷新失败事件（携带 FailureKind 分类）</summary>
    event EventHandler<RefreshFailedEventArgs>? RefreshFailed;

    /// <summary>刷新完成事件（全部 Provider 刷新完成后触发）</summary>
    event EventHandler<UsageRefreshedEventArgs>? UsageRefreshed;

    /// <summary>刷新开始事件</summary>
    event EventHandler? RefreshStarted;

    /// <summary>启动定时刷新</summary>
    void Start();

    /// <summary>停止定时刷新</summary>
    void Stop();

    /// <summary>立即刷新所有启用的 Provider</summary>
    /// <param name="triggerKind">触发类型（"manual" / "auto"）</param>
    Task RefreshAllAsync(string triggerKind = "manual");

    /// <summary>刷新单个 Provider</summary>
    /// <param name="providerId">Provider 唯一标识</param>
    /// <param name="ct">取消令牌</param>
    Task RefreshProviderAsync(string providerId, CancellationToken ct = default);
}
