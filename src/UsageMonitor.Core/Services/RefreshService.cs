using System.Collections.Concurrent;
using System.Globalization;
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
    private readonly UsageHistoryRepository? _historyRepository;
    private Timer? _timer;
    private bool _isRefreshing;

    /// <summary>每个 provider 的刷新互斥锁：同一 provider 的全量刷新与单卡片刷新互斥，不同 provider 仍可并行。</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _providerLocks = new();

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
    /// <param name="historyRepository">
    /// req-013：历史仓库（Repository，可选）。传入后会在每次刷新后异步写入 <c>usage_refresh_aggregates</c>。
    /// </param>
    public RefreshService(
        PluginManager pluginManager,
        ConfigService configService,
        UsageHistoryStore? historyStore = null,
        UsageHistoryRepository? historyRepository = null)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _historyStore = historyStore ?? new UsageHistoryStore();
        _historyRepository = historyRepository;
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
    /// <param name="triggerKind">触发类型（"manual" / "auto"），传给 req-013 刷新聚合</param>
    public async Task RefreshAllAsync(string triggerKind = "manual")
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        RefreshStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            var enabledPlugins = _pluginManager.GetEnabledPlugins()
                .Where(p => p.IsEnabled && _configService.Settings.PluginEnabled.GetValueOrDefault(p.Provider.ProviderId, true))
                .ToList();

            var tasks = enabledPlugins.Select(p => RefreshPluginAsync(p, triggerKind));
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
    /// <param name="plugin">插件包装</param>
    /// <param name="triggerKind">触发类型（"manual" / "auto"），传给 req-013 刷新聚合</param>
    public async Task RefreshPluginAsync(LoadedPlugin plugin, string triggerKind = "manual")
    {
        var providerId = plugin.Provider.ProviderId;
        // per-provider 锁：同一 provider 的全量刷新与单卡片刷新互斥，不同 provider 仍可并行。
        var gate = _providerLocks.GetOrAdd(providerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            try
            {
                var config = _configService.GetProviderConfig(providerId, plugin.Provider);
                var usage = await plugin.Provider.GetUsageAsync(config);

                plugin.LastUsage = usage;
                plugin.LastQueryTime = DateTime.Now;
                plugin.LastQuerySuccess = usage.IsSuccess;

                // 成功记有效历史点；失败记一个 IsError 点（避免折线无痕断裂）。
                // req-015：传完整 usage，Store 内部走 InsertUsagePointIfChangedAsync（业务指纹比对去重）。
                if (usage.IsSuccess)
                    _historyStore.AddPoint(providerId, usage);
                else
                    _historyStore.AddErrorPoint(providerId);

                // req-013：成功刷新后异步写入“刷新聚合”记录，供历史窗口展示每次刷新。
                if (usage.IsSuccess)
                    RecordRefreshAggregateAsync(providerId, triggerKind);
            }
            catch (Exception ex)
            {
                plugin.LastUsage = UsageInfo.CreateError(
                    providerId,
                    plugin.Provider.DisplayName,
                    ex.Message);
                plugin.LastQueryTime = DateTime.Now;
                plugin.LastQuerySuccess = false;
                // 异常同样记失败点。
                _historyStore.AddErrorPoint(providerId);
            }
        }
        finally
        {
            gate.Release();
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
        _ = RefreshAllAsync("auto");
    }

    /// <summary>
    /// req-013：在一次成功刷新后，把本次刷新区间的最大 / 最小 / 末尾 / 平均值写入
    /// <c>usage_refresh_aggregates</c>，供历史窗口 DataGrid 展示。区间来源为 <see cref="UsageHistoryStore"/>
    /// 的当前内存快照（最近 <c>MaxPoints</c> 个点）。写入是 fire-and-forget，失败仅日志。
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="triggerKind">触发类型（"manual" / "auto"）</param>
    private void RecordRefreshAggregateAsync(string providerId, string triggerKind)
    {
        if (_historyRepository == null)
        {
            FileLogger.Warn("RefreshService", $"RecordRefreshAggregateAsync({providerId}): _historyRepository is null");
            return;
        }
        var points = _historyStore.GetHistory(providerId);
        if (points == null || points.Count == 0)
        {
            FileLogger.Warn("RefreshService", $"RecordRefreshAggregateAsync({providerId}): no history points");
            return;
        }

        // 过滤错误点（IsError=true）；错误点不参与"用量"的聚合统计。
        var valid = new List<HistoryPoint>(points.Count);
        foreach (var p in points)
        {
            if (!p.IsError) valid.Add(p);
        }
        if (valid.Count == 0)
        {
            FileLogger.Warn("RefreshService", $"RecordRefreshAggregateAsync({providerId}): all points are errors");
            return;
        }

        var now = DateTime.Now;
        var agg = new RefreshAggregate(
            Id: 0,
            ProviderId: providerId,
            RefreshAt: now,
            BusinessDay: now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MaxUsedPercent: valid.Max(p => p.UsagePercent),
            MinUsedPercent: valid.Min(p => p.UsagePercent),
            EndUsedPercent: valid[^1].UsagePercent,
            AvgUsedPercent: valid.Average(p => p.UsagePercent),
            SnapshotCount: valid.Count,
            TriggerKind: triggerKind);

        _ = _historyRepository.InsertRefreshAggregateAsync(agg);
        FileLogger.Info("RefreshService", $"RecordRefreshAggregateAsync({providerId}): inserted aggregate with {valid.Count} points");
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
