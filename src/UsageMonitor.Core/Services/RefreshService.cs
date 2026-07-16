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
    private readonly UsageHistoryStore _historyStore;
    private Timer? _timer;
    private bool _isRefreshing;

    /// <summary>
    /// 用量数据更新事件
    /// </summary>
    public event EventHandler<UsageRefreshedEventArgs>? UsageRefreshed;

    /// <summary>刷新开始事件</summary>
    public event EventHandler? RefreshStarted;

    /// <summary>
    /// 创建刷新服务实例
    /// </summary>
    /// <param name="pluginManager">插件管理器</param>
    /// <param name="configService">配置服务</param>
    /// <param name="historyStore">
    /// 历史仓库（可选）。传入后刷新过程中会同时将数据点入 SQLite（需在 historyStore 内部持有 Repository）。
    /// </param>
    public RefreshService(PluginManager pluginManager, ConfigService configService, UsageHistoryStore? historyStore = null)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _historyStore = historyStore ?? new UsageHistoryStore();
        _historyStore.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);
    }

    /// <summary>
    /// 启动定时刷新
    /// </summary>
    public void Start()
    {
        Stop();
        // 同步历史点数设置
        _historyStore.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);
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

            // 记录历史点（仅成功且有额度数据时）
            if (usage.IsSuccess)
            {
                _historyStore.AddPoint(plugin.Provider.ProviderId, usage.GetUsagePercentage());
            }
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
    /// 立即刷新指定服务商（供主窗口卡片右上角"刷新本卡片"按钮调用）。
    /// 仅刷新单个 Provider，并复用 UsageRefreshed 事件让卡片 UI 与托盘文本自动更新。
    /// </summary>
    /// <param name="providerId">要刷新的服务商 Id</param>
    public async Task RefreshProviderAsync(string providerId)
    {
        // 按 Id 取插件；卡片存在即插件存在，理论不会为 null，仍做空判防御。
        var plugin = _pluginManager.GetPlugin(providerId);
        if (plugin == null) return;

        // 复用单插件刷新逻辑（内部会写 LastUsage 并在成功时记录历史点）。
        await RefreshPluginAsync(plugin);

        // 触发与全量刷新相同的事件，App.OnUsageRefreshed 据此更新卡片 UI 与托盘提示。
        if (plugin.LastUsage != null)
            UsageRefreshed?.Invoke(this, new UsageRefreshedEventArgs(new[] { plugin.LastUsage }));
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
