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

    /// <summary>req-110 P2-3：每个 Provider 最近一轮按账号刷新的 usage 列表（供 UsageRefreshed 事件按账号分发）。</summary>
    private readonly ConcurrentDictionary<string, List<UsageInfo>> _lastAccountUsages = new();

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
            // req-110 P2-3：按 _lastAccountUsages 组装——每账号 usage 本体 + 该账号其余卡片克隆，
            // DisplayModule 按 (Provider, Account, Card) 三段路由到每张卡片。
            var allUsages = new List<UsageInfo>();
            foreach (var p in enabledPlugins)
            {
                allUsages.AddRange(BuildAccountUsageList(p.Provider.ProviderId));
            }

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

            // 成功：重置 Provider 级连续失败计数（仅对应本方法 catch 中的超时/异常计数）。
            // 注意：账号级计数（key="Provider:Account"）有意不在此重置——
            // RefreshPluginAsync 就地消化单账号异常，本方法的"成功"不代表各账号取数成功，
            // 若在此重置账号计数会使账号级熔断永远无法触发；
            // 账号级计数由账号循环自治：成功归零，熔断到期后半开放行一次自动恢复。
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
    /// 刷新指定服务商的用量（req-110 P2-3：按账号循环）。
    /// <para>req-110 Q1 刷新门控：刷新单元 = 已启用插件 × 已启用且配置完成的账号——
    /// 插件未启用 / 无启用账号 / 账号未配置凭据均跳过（Info 日志记录原因），不空跑浏览器/API。</para>
    /// <para>req-110 P2-3：每个启用账号独立取数（账号生效配置 = Provider 级基底 + 账号级凭据覆盖）、
    /// 独立熔断计数（key = Provider:Account）——单账号失效/熔断不影响同 Provider 其他账号。</para>
    /// <para>req-110 P1-3：每账号刷新成功后把插件返回的网页身份哈希绑定到该账号（BoundStableId），
    /// 并把 usage.AccountId/CardId 重写为配置账号 ID，使落库与卡片路由都以用户创建的账号为准。</para>
    /// </summary>
    /// <param name="plugin">插件包装</param>
    /// <param name="triggerKind">触发类型（"manual" / "auto"），传给 req-013 刷新聚合</param>
    /// <param name="ct">取消令牌，用于区分用户主动取消与网络超时</param>
    public async Task RefreshPluginAsync(LoadedPlugin plugin, string triggerKind = "manual", CancellationToken ct = default)
    {
        var providerId = plugin.Provider.ProviderId;

        // req-110 Q1 门控①：插件未启用不刷新（RefreshAllAsync 已过滤，单卡刷新入口在此统一把关）。
        if (!plugin.IsEnabled || !_configService.Settings.PluginEnabled.GetValueOrDefault(providerId, true))
        {
            FileLogger.Info("RefreshService", $"req-110 门控：插件 {providerId} 未启用，跳过刷新");
            return;
        }

        // req-110 Q1 门控②③：无账号 / 无启用账号不刷新。
        var enabledAccounts = _configService.GetAccounts(providerId).Where(a => a.Enabled).ToList();
        if (enabledAccounts.Count == 0)
        {
            FileLogger.Info("RefreshService", $"req-110 门控：{providerId} 无已启用账号，跳过刷新");
            // 本轮零产出：清除上一轮账号数据缓存，避免 BuildAccountUsageList 把旧数据当新数据分发。
            _lastAccountUsages.TryRemove(providerId, out _);
            return;
        }

        // per-provider 锁：同一 provider 的全量刷新与单卡片刷新互斥，不同 provider 仍可并行。
        var gate = _providerLocks.GetOrAdd(providerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var accountUsages = new List<UsageInfo>();
            foreach (var account in enabledAccounts)
            {
                // req-110 Q1 门控④：账号未配置凭据不刷新（账号生效配置 + 账号级 cookie 文件，判定与待命卡共用 CredentialProbe）。
                var config = _configService.GetEffectiveAccountConfig(providerId, account.AccountId, plugin.Provider);
                if (!CredentialProbe.HasConfiguredCredential(providerId, config, account.AccountId))
                {
                    FileLogger.Info("RefreshService", $"req-110 门控：{providerId} 账号 {account.AccountId} 未配置凭据，跳过刷新");
                    continue;
                }

                // req-110 P2-3：账号级熔断——单账号连续失败不影响同 Provider 其他账号。
                var unitKey = $"{providerId}:{account.AccountId}";
                if (_circuitOpenUntil.TryGetValue(unitKey, out var openUntil))
                {
                    if (DateTime.UtcNow < openUntil)
                    {
                        FileLogger.Info("RefreshService",
                            $"Circuit breaker OPEN for {unitKey}, skipping until {openUntil:HH:mm:ss} UTC");
                        continue;
                    }
                    _circuitOpenUntil.TryRemove(unitKey, out _); // 熔断到期，半开允许一次尝试
                }

                try
                {
                    var usage = await plugin.Provider.GetUsageAsync(config, ct);

                    // req-110 P1-3：账号身份绑定替代自动注册——插件返回的网页身份哈希写入本账号的
                    // BoundStableId（首次绑定时同步迁移哈希键历史数据）；不再调用 EnsureAccount 反向建号。
                    var stableHash = usage.AccountId;
                    if (usage.IsSuccess)
                    {
                        var firstBind = string.IsNullOrWhiteSpace(
                            _configService.GetAccount(providerId, account.AccountId)?.BoundStableId);
                        if (!_configService.TryBindAccountStableId(providerId, account.AccountId, stableHash))
                        {
                            FileLogger.Warn("RefreshService",
                                $"req-110：{providerId} 网页身份哈希 {stableHash} 与账号 {account.AccountId} 已绑定值不一致（网页侧可能更换了账号）");
                        }
                        else if (firstBind && !string.IsNullOrWhiteSpace(stableHash) &&
                                 !string.Equals(stableHash, "default", StringComparison.Ordinal) &&
                                 !string.Equals(stableHash, account.AccountId, StringComparison.Ordinal))
                        {
                            // 首次绑定且哈希 ≠ 账号 ID：把历史表哈希键旧行归属到配置账号（fire-and-forget）。
                            _ = _dataModule.MigrateAccountIdAsync(providerId, stableHash!, account.AccountId);
                        }
                    }
                    // 路由/落库键统一重写为配置账号：成功与失败态都重写，使错误文案也能精确路由到该账号卡片。
                    usage.AccountId = account.AccountId;
                    usage.CardId = _configService.GetCards(providerId, account.AccountId)
                        .FirstOrDefault()?.CardId ?? "default-card";

                    // 成功记有效历史点；失败记一个 IsError 点（避免折线无痕断裂）。
                    // req-015：传完整 usage，Store 内部走 InsertUsagePointIfChangedAsync（业务指纹比对去重）。
                    if (usage.IsSuccess)
                    {
                        // req-092：传 null 走 DataModule 内部 ExtractStandardFields 自动提取；
                        // req-110：usage.AccountId 已重写为配置账号 ID，落库按用户账号隔离。
                        _dataModule.SaveUsage(usage);
                        // req-096 接线：首次成功刷新时记录登录态获取时间（幂等，不覆盖已有 AcquiredAt）。
                        _authManager?.EnsureLoginStateRecorded(providerId);
                        // req-013：成功刷新后异步写入"刷新聚合"记录，供历史窗口展示每次刷新。
                        _dataModule.RecordRefreshAggregate(providerId, triggerKind);
                        _consecutiveFailures[unitKey] = 0;
                    }
                    else
                    {
                        _dataModule.AddErrorPoint(providerId);

                        // req-091-002：usage.IsSuccess=false 但无异常的场景，按 ErrorMessage 关键字兜底判定。
#pragma warning disable CS0618 // ErrorMessage 已过时，req-091 兜底分类向后兼容保留
                        var fallbackKind = ClassifyByErrorMessage(usage.ErrorMessage);
                        FileLogger.Warn("RefreshService",
                            $"[req-091] Provider {providerId} 账号 {account.AccountId} returned IsSuccess=false, kind={fallbackKind}: {usage.ErrorMessage}");
                        RefreshFailed?.Invoke(this, new RefreshFailedEventArgs(
                            providerId,
                            plugin.Provider.DisplayName,
                            fallbackKind,
                            null,
                            usage.ErrorMessage ?? "未知错误"));
#pragma warning restore CS0618
                        RecordFailure(unitKey);
                    }

                    accountUsages.Add(usage);
                }
                // 用户取消/整体超时：中断全部账号循环并上抛（由 RefreshPluginWithTimeoutAsync 处理）。
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // req-110 P2-3：单账号异常就地消化，不中断同 Provider 其他账号刷新。
                    var errorUsage = UsageInfo.CreateError(providerId, plugin.Provider.DisplayName, ex.Message);
                    errorUsage.AccountId = account.AccountId;
                    errorUsage.CardId = _configService.GetCards(providerId, account.AccountId)
                        .FirstOrDefault()?.CardId ?? "default-card";
                    accountUsages.Add(errorUsage);
                    _dataModule.AddErrorPoint(providerId);

                    // req-091-002：使用 CookieHealthDetector 判定失败原因，触发 RefreshFailed 事件
                    var failureKind = _cookieHealthDetector.Classify(ex);
                    FileLogger.Warn("RefreshService",
                        $"[req-091] Provider {providerId} 账号 {account.AccountId} refresh failed, kind={failureKind}: {ex.Message}");
                    RefreshFailed?.Invoke(this, new RefreshFailedEventArgs(
                        providerId,
                        plugin.Provider.DisplayName,
                        failureKind,
                        ex,
                        ex.Message));

                    // req-058 / req-110：熔断计费收敛到账号级（单账号连续失败触发该账号熔断）。
                    RecordFailure(unitKey);
                }
            }

            if (accountUsages.Count == 0)
            {
                FileLogger.Info("RefreshService", $"req-110：{providerId} 全部启用账号被门控/熔断跳过，本轮未取数");
                // 本轮零产出：清除上一轮账号数据缓存，避免后续 UsageRefreshed 事件分发 stale 数据。
                _lastAccountUsages.TryRemove(providerId, out _);
                return;
            }

            // 托盘/迷你图等 Provider 粒度消费方兼容：LastUsage = 首个成功账号的 usage（全失败时取首个）。
            plugin.LastUsage = accountUsages.FirstOrDefault(u => u.IsSuccess) ?? accountUsages[0];
            plugin.LastQueryTime = DateTime.Now;
            plugin.LastQuerySuccess = plugin.LastUsage.IsSuccess;
            // req-110 P2-3：保存本轮全部账号的 usage，供 UsageRefreshed 事件按账号精确分发。
            _lastAccountUsages[providerId] = accountUsages;
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

        // 复用单插件刷新逻辑（内部按账号循环并写 _lastAccountUsages）。
        await RefreshPluginAsync(plugin, "manual", ct);

        // req-110 P2-3：每账号 usage 直接分发 + 该账号多卡片克隆（不再跨账号共享同一份数据）。
        var usageList = BuildAccountUsageList(providerId);
        if (usageList.Count == 0) return;

        UsageRefreshed?.Invoke(this, new UsageRefreshedEventArgs(usageList));
    }

    /// <summary>
    /// req-110 P2-3：按 _lastAccountUsages 组装指定 Provider 的事件分发列表——
    /// 每账号 usage 本体 + 该账号其余卡片的克隆（同账号多卡共享同一份数据）。
    /// </summary>
    /// <param name="providerId">Provider ID。</param>
    private List<UsageInfo> BuildAccountUsageList(string providerId)
    {
        var result = new List<UsageInfo>();
        if (!_lastAccountUsages.TryGetValue(providerId, out var accountUsages)) return result;
        foreach (var usage in accountUsages)
        {
            result.Add(usage);
            if (usage.AccountId == null) continue;
            var cards = _configService.GetCards(providerId, usage.AccountId);
            foreach (var card in cards)
            {
                if (string.Equals(card.CardId, usage.CardId, StringComparison.Ordinal)) continue;
                result.Add(CloneUsageForCard(usage, usage.AccountId, card.CardId));
            }
        }
        return result;
    }

    /// <summary>
    /// req-109：克隆一份 UsageInfo 并设置 (AccountId, CardId) 路由信息。
    /// 浅拷贝 Extra 字典，避免跨卡片数据共享导致 INPC 误触发。
    /// </summary>
    private static UsageInfo CloneUsageForCard(UsageInfo source, string accountId, string cardId)
    {
        return new UsageInfo
        {
            ProviderId = source.ProviderId,
            ProviderName = source.ProviderName,
            AccountId = accountId,
            CardId = cardId,
            Quantity = source.Quantity,
            Error = source.Error,
#pragma warning disable CS0618
            UsedAmount = source.UsedAmount,
            TotalAmount = source.TotalAmount,
            Unit = source.Unit,
            UsedTokens = source.UsedTokens,
            TotalTokens = source.TotalTokens,
            ErrorMessage = source.ErrorMessage,
#pragma warning restore CS0618
            ExpireDate = source.ExpireDate,
            Extra = source.Extra != null ? new System.Collections.Generic.Dictionary<string, object>(source.Extra) : new(),
            LastUpdated = source.LastUpdated,
            IsSuccess = source.IsSuccess,
        };
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
