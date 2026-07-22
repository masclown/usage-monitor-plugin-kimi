using System.Collections.Concurrent;
using System.Globalization;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Modules;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services.Auth;
using UsageMonitor.Core.Services.Data;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 定时刷新服务 - 按设定间隔自动查询各AI服务商的用量信息
/// </summary>
public class RefreshService : IRefreshService
{
    /// <summary>
    /// req-091-002：单 Provider 刷新失败事件（携带 <see cref="FailureKind"/> 分类）。
    /// <para>
    /// App 层订阅此事件：<see cref="FailureKind.LoginExpired"/> 时触发 Cookie 失效确认弹窗，
    /// <see cref="FailureKind.NetworkError"/> / <see cref="FailureKind.Unknown"/> 仅日志记录。
    /// </para>
    /// </summary>
    public event EventHandler<RefreshFailedEventArgs>? RefreshFailed;

    private readonly PluginManager _pluginManager;
    private readonly ConfigService _configService;
    // req-099 B2：数据访问统一走 IDataModule，不再直接持有 Store / Repository（数据刷新保存模块）。
    private readonly IDataModule _dataModule;
    private readonly CookieHealthDetector _cookieHealthDetector;
    /// <summary>req-096 接线：统一鉴权管理器（可空）。用于刷新成功后记录登录态计时，使第四大模块真正生效。</summary>
    private readonly AuthManager? _authManager;
    private Timer? _timer;
    /// <summary>req-057：刷新中标记（0=空闲，1=刷新中），使用 Interlocked 保证线程安全。</summary>
    private int _isRefreshing;

    /// <summary>每个 provider 的刷新互斥锁：同一 provider 的全量刷新与单卡片刷新互斥，不同 provider 仍可并行。</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _providerLocks = new();

/// <summary>req-058：每个 Provider 的连续失败计数（用于熔断器）。</summary>
private readonly ConcurrentDictionary<string, int> _consecutiveFailures = new();

    /// <summary>req-058：每个 Provider 的熔断到期时间（UTC），在此之前跳过刷新。</summary>
    private readonly ConcurrentDictionary<string, DateTime> _circuitOpenUntil = new();

    /// <summary>req-058：连续失败多少次后触发熔断。</summary>
    private const int CircuitBreakerThreshold = 5;

    /// <summary>req-058：熔断时长（分钟）。</summary>
    private const int CircuitBreakerDurationMinutes = 5;

    /// <summary>req-058：单个 Provider 刷新超时（秒）。</summary>
    private const int PerProviderTimeoutSeconds = 60;

    /// <summary>req-058：全量刷新整体超时（秒）。</summary>
    private const int OverallTimeoutSeconds = 120;

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
    /// <param name="dataModule">
    /// req-099 B2：数据模块（可选）。刷新成功/失败通过它保存历史点、记错点、写刷新聚合，
    /// 不再直接操作 Store / Repository。为 null 时使用内存模式的默认实现。
    /// </param>
    /// <param name="cookieHealthDetector">Cookie 健康探测器（可选）。</param>
    /// <param name="authManager">req-096：统一鉴权管理器（可选）。传入后刷新成功会记录登录态计时。</param>
    public RefreshService(
        PluginManager pluginManager,
        ConfigService configService,
        IDataModule? dataModule = null,
        CookieHealthDetector? cookieHealthDetector = null,
        AuthManager? authManager = null)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _dataModule = dataModule ?? new DataModule();
        _cookieHealthDetector = cookieHealthDetector ?? new CookieHealthDetector();
        _authManager = authManager;
        _dataModule.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);
    }

    /// <summary>
    /// 启动定时刷新
    /// </summary>
    public void Start()
    {
        Stop();
        // 同步历史点数设置
        _dataModule.MaxPoints = Math.Max(1, _configService.Settings.HistoryPointCount);
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
    /// 立即刷新所有已启用的插件。
    /// req-057：使用 Interlocked.CompareExchange 保证多线程下仅一个线程进入刷新逻辑。
    /// req-058：加入整体超时 + 单 Provider 超时 + 熔断器。
    /// </summary>
    /// <param name="triggerKind">触发类型（"manual" / "auto"），传给 req-013 刷新聚合</param>
    public async Task RefreshAllAsync(string triggerKind = "manual")
    {
        // req-057 P0：原子 CAS 替代普通 bool，避免 ThreadPool 与 UI 线程同时进入刷新
        if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0) return;

        RefreshStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            // req-058：整体超时 CTS
            using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(OverallTimeoutSeconds));

            var enabledPlugins = _pluginManager.GetEnabledPlugins()
                .Where(p => p.IsEnabled && _configService.Settings.PluginEnabled.GetValueOrDefault(p.Provider.ProviderId, true))
                .ToList();

            var tasks = enabledPlugins.Select(p => RefreshPluginWithTimeoutAsync(p, triggerKind, overallCts.Token));
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
            Interlocked.Exchange(ref _isRefreshing, 0);
        }
    }

    /// <summary>
    /// req-058：带单 Provider 超时和熔断器的刷新包装。
    /// 单个 Provider 超时不影响其他 Provider。
    /// </summary>
    private async Task RefreshPluginWithTimeoutAsync(LoadedPlugin plugin, string triggerKind, CancellationToken overallToken)
    {
        var providerId = plugin.Provider.ProviderId;

        // req-058：熔断器检查——连续失败超过阈值后临时跳过
        if (_circuitOpenUntil.TryGetValue(providerId, out var openUntil))
        {
            if (DateTime.UtcNow < openUntil)
            {
                FileLogger.Info("RefreshService",
                    $"Circuit breaker OPEN for {providerId}, skipping until {openUntil:HH:mm:ss} UTC");
                return;
            }
            // 熔断到期，半开状态——允许一次尝试
            _circuitOpenUntil.TryRemove(providerId, out _);
        }

        // req-058：单 Provider 超时（与整体超时取较小值）
        using var perCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
        perCts.CancelAfter(TimeSpan.FromSeconds(PerProviderTimeoutSeconds));

        try
        {
            await RefreshPluginAsync(plugin, triggerKind, perCts.Token);

            // 成功：重置连续失败计数
            _consecutiveFailures[providerId] = 0;
        }
        catch (OperationCanceledException) when (!overallToken.IsCancellationRequested)
        {
            // 单 Provider 超时（不是整体取消）
            FileLogger.Warn("RefreshService",
                $"Provider {providerId} timed out after {PerProviderTimeoutSeconds}s");
            RecordFailure(providerId);
        }
        catch (Exception ex)
        {
            FileLogger.Error("RefreshService",
                $"Provider {providerId} refresh failed: {ex.Message}", ex);
            RecordFailure(providerId);
        }
    }

    /// <summary>
    /// req-091-002：根据 ErrorMessage 关键字兜底判定失败原因（用于 usage.IsSuccess=false 但无异常的场景）。
    /// <para>
    /// 关键字集合（中文 + 英文）：覆盖各 Provider 错误消息中常见的"未登录 / cookie 失效 / 401 / 403"等。
    /// 命中任一关键字 → LoginExpired；否则按字符串特征回退到 NetworkError / Unknown。
    /// </para>
    /// </summary>
    private static FailureKind ClassifyByErrorMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage)) return FailureKind.Unknown;

        var msg = errorMessage.ToLowerInvariant();

        // 登录态关键字（中英文 + 常见错误码）
        string[] loginKeywords = {
            "login", "登录", "未登录", "重新登录", "未授权", "cookie", "会话",
            "401", "403", "unauthorized", "forbidden", "expired", "过期", "失效", "凭证"
        };
        foreach (var kw in loginKeywords)
        {
            if (msg.Contains(kw)) return FailureKind.LoginExpired;
        }

        // 网络关键字
        string[] networkKeywords = {
            "network", "网络", "timeout", "超时", "unreachable", "无法连接",
            "dns", "connection", "连接", "abort", "中断"
        };
        foreach (var kw in networkKeywords)
        {
            if (msg.Contains(kw)) return FailureKind.NetworkError;
        }

        return FailureKind.Unknown;
    }

    /// <summary>
    /// req-058：记录失败并检查是否触发熔断。
    /// </summary>
    private void RecordFailure(string providerId)
    {
        var count = _consecutiveFailures.AddOrUpdate(providerId, 1, (_, c) => c + 1);
        if (count >= CircuitBreakerThreshold)
        {
            var openUntil = DateTime.UtcNow.AddMinutes(CircuitBreakerDurationMinutes);
            _circuitOpenUntil[providerId] = openUntil;
            FileLogger.Warn("RefreshService",
                $"Circuit breaker OPEN for {providerId} after {count} consecutive failures. " +
                $"Will retry after {CircuitBreakerDurationMinutes} min.");
        }
    }

    /// <summary>
    /// 刷新指定服务商的用量
    /// </summary>
    /// <param name="plugin">插件包装</param>
    /// <param name="triggerKind">触发类型（"manual" / "auto"），传给 req-013 刷新聚合</param>
    /// <param name="ct">取消令牌，用于区分用户主动取消与网络超时</param>
    public async Task RefreshPluginAsync(LoadedPlugin plugin, string triggerKind = "manual", CancellationToken ct = default)
    {
        var providerId = plugin.Provider.ProviderId;
        // per-provider 锁：同一 provider 的全量刷新与单卡片刷新互斥，不同 provider 仍可并行。
        var gate = _providerLocks.GetOrAdd(providerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            try
            {
                var config = _configService.GetProviderConfig(providerId, plugin.Provider);
                var usage = await plugin.Provider.GetUsageAsync(config, ct);

                plugin.LastUsage = usage;
                plugin.LastQueryTime = DateTime.Now;
                plugin.LastQuerySuccess = usage.IsSuccess;

                // 成功记有效历史点；失败记一个 IsError 点（避免折线无痕断裂）。
                // req-015：传完整 usage，Store 内部走 InsertUsagePointIfChangedAsync（业务指纹比对去重）。
                if (usage.IsSuccess)
                {
                    // req-092 B3 接线：传入插件 MapToStandardFields 映射结果（默认 null 时 DataModule 回退 ExtractStandardFields），
                    // 使字段级差异增量持久化生效。
                    _dataModule.SaveUsage(usage, plugin.Provider.MapToStandardFields(usage));
                    // req-096 接线：首次成功刷新时记录登录态获取时间（幂等，不覆盖已有 AcquiredAt），
                    // 使 AuthManager 的“登录态计时”真正生效；轻量记录，不触发浏览器登录。
                    _authManager?.EnsureLoginStateRecorded(providerId);
                }
                else
                {
                    _dataModule.AddErrorPoint(providerId);

                    // req-091-002：usage.IsSuccess=false 但无异常的场景
                    // （Web 插件 DOM 提取失败 / API 鉴权失效等）
                    // 按 ErrorMessage 关键字兜底判定（避免漏掉 LoginExpired）
                    var fallbackKind = ClassifyByErrorMessage(usage.ErrorMessage);
                    FileLogger.Warn("RefreshService",
                        $"[req-091] Provider {providerId} returned IsSuccess=false, kind={fallbackKind}: {usage.ErrorMessage}");
                    RefreshFailed?.Invoke(this, new RefreshFailedEventArgs(
                        providerId,
                        plugin.Provider.DisplayName,
                        fallbackKind,
                        null,
                        usage.ErrorMessage ?? "未知错误"));
                }

                // req-013：成功刷新后异步写入“刷新聚合”记录，供历史窗口展示每次刷新。
                if (usage.IsSuccess)
                    _dataModule.RecordRefreshAggregate(providerId, triggerKind);
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
                _dataModule.AddErrorPoint(providerId);

                // req-091-002：使用 CookieHealthDetector 判定失败原因，触发 RefreshFailed 事件
                var failureKind = _cookieHealthDetector.Classify(ex);
                FileLogger.Warn("RefreshService",
                    $"[req-091] Provider {providerId} refresh failed, kind={failureKind}: {ex.Message}");
                RefreshFailed?.Invoke(this, new RefreshFailedEventArgs(
                    providerId,
                    plugin.Provider.DisplayName,
                    failureKind,
                    ex,
                    ex.Message));

                // req-058-004：重新抛出以让 RefreshPluginWithTimeoutAsync 调用 RecordFailure（CircuitBreaker 计费）。
                // 不重新抛出会导致熔断器永远不触发（5 次连续失败也不会进入熔断）。
                throw;
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
    /// <param name="ct">取消令牌，用于区分用户主动取消与网络超时</param>
    public async Task RefreshProviderAsync(string providerId, CancellationToken ct = default)
    {
        // 按 Id 取插件；卡片存在即插件存在，理论不会为 null，仍做空判防御。
        var plugin = _pluginManager.GetPlugin(providerId);
        if (plugin == null) return;

        // 复用单插件刷新逻辑（内部会写 LastUsage 并在成功时记录历史点）。
        await RefreshPluginAsync(plugin, "manual", ct);

        // 触发与全量刷新相同的事件，App.OnUsageRefreshed 据此更新卡片 UI 与托盘提示。
        if (plugin.LastUsage != null)
            UsageRefreshed?.Invoke(this, new UsageRefreshedEventArgs(new[] { plugin.LastUsage }));
    }

    /// <summary>
    /// 定时器回调。req-058：加 faulted 回调避免 unobserved task exception。
    /// </summary>
    private void OnTimerTick(object? state)
    {
        _ = RefreshAllAsync("auto").ContinueWith(
            t => FileLogger.Error("RefreshService",
                $"Auto refresh unhandled exception: {t.Exception?.GetBaseException().Message}",
                t.Exception?.GetBaseException()),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    // req-099 B2：RecordRefreshAggregateAsync 已迁移至 DataModule.RecordRefreshAggregate（数据聚合/持久化集中在数据模块）。

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

/// <summary>
/// req-091-002：单 Provider 刷新失败事件参数。
/// <para>App 层通过 <see cref="FailureKind"/> 判定是否触发自动重新登录弹窗。</para>
/// </summary>
public sealed class RefreshFailedEventArgs : EventArgs
{
    /// <summary>失败的 Provider ID（如 "MiniMax"）。</summary>
    public string ProviderId { get; }

    /// <summary>失败的 Provider 显示名称。</summary>
    public string ProviderDisplayName { get; }

    /// <summary>失败原因分类（Cookie 失效 / 网络错误 / 未知错误）。</summary>
    public FailureKind FailureKind { get; }

    /// <summary>触发的异常（成功时为 null，Cookie 失效但无异常时为 null）。</summary>
    public Exception? Exception { get; }

    /// <summary>错误消息摘要（用于日志 / 弹窗展示）。</summary>
    public string ErrorMessage { get; }

    public RefreshFailedEventArgs(string providerId, string providerDisplayName,
        FailureKind failureKind, Exception? exception, string errorMessage)
    {
        ProviderId = providerId;
        ProviderDisplayName = providerDisplayName;
        FailureKind = failureKind;
        Exception = exception;
        ErrorMessage = errorMessage;
    }
}
