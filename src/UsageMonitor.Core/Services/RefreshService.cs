using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 定时刷新服务 - 按设定间隔自动查询各AI服务商的用量信息
/// </summary>
public class RefreshService : IDisposable
{
    private readonly PluginManager _pluginManager;
    private readonly ConfigService _configService;
    private Timer? _timer;
    private bool _isRefreshing;

    /// <summary>用量数据更新事件</summary>
    public event EventHandler<UsageRefreshedEventArgs>? UsageRefreshed;

    /// <summary>刷新开始事件</summary>
    public event EventHandler? RefreshStarted;

    /// <summary>
    /// 创建刷新服务实例
    /// </summary>
    public RefreshService(PluginManager pluginManager, ConfigService configService)
    {
        _pluginManager = pluginManager;
        _configService = configService;
    }

    /// <summary>
    /// 启动定时刷新
    /// </summary>
    public void Start()
    {
        Stop();
        var intervalMs = _configService.Settings.RefreshIntervalSeconds * 1000;
        _timer = new Timer(OnTimerTick, null, 0, intervalMs);
    }

    /// <summary>
    /// 停止定时刷新
    /// </summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// 立即刷新所有已启用的插件
    /// </summary>
    public async Task RefreshAllAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        RefreshStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            var enabledPlugins = _pluginManager.GetEnabledPlugins()
                .Where(p => p.IsEnabled && _configService.Settings.PluginEnabled.GetValueOrDefault(p.Provider.ProviderId, true))
                .ToList();

            var tasks = enabledPlugins.Select(RefreshPluginAsync);
            await Task.WhenAll(tasks);

            // 触发数据更新事件
            var allUsages = enabledPlugins
                .Where(p => p.LastUsage != null)
                .Select(p => p.LastUsage!)
                .ToList();

            UsageRefreshed?.Invoke(this, new UsageRefreshedEventArgs(allUsages));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// 刷新指定服务商的用量
    /// </summary>
    public async Task RefreshPluginAsync(LoadedPlugin plugin)
    {
        try
        {
            var config = _configService.GetProviderConfig(plugin.Provider.ProviderId, plugin.Provider);
            var usage = await plugin.Provider.GetUsageAsync(config);

            plugin.LastUsage = usage;
            plugin.LastQueryTime = DateTime.Now;
            plugin.LastQuerySuccess = usage.IsSuccess;
        }
        catch (Exception ex)
        {
            plugin.LastUsage = UsageInfo.CreateError(
                plugin.Provider.ProviderId,
                plugin.Provider.DisplayName,
                ex.Message);
            plugin.LastQueryTime = DateTime.Now;
            plugin.LastQuerySuccess = false;
        }
    }

    /// <summary>
    /// 定时器回调
    /// </summary>
    private void OnTimerTick(object? state)
    {
        _ = RefreshAllAsync();
    }

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// 用量刷新完成事件参数
/// </summary>
public class UsageRefreshedEventArgs : EventArgs
{
    /// <summary>所有服务商的最新用量信息</summary>
    public IReadOnlyList<UsageInfo> Usages { get; }

    public UsageRefreshedEventArgs(IReadOnlyList<UsageInfo> usages)
    {
        Usages = usages;
    }
}
