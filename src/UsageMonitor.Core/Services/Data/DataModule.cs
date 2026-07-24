using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Modules;

namespace UsageMonitor.Core.Services.Data;

/// <summary>
/// req-099 B2：数据模块实现（Core 层，WPF-无关）。作为数据层的唯一协调者，
/// 内部持有并封装 <see cref="UsageHistoryStore"/>（内存历史 + 去重 + 持久化）与
/// <see cref="UsageHistoryRepository"/>（SQLite 仓库），对外只暴露 <see cref="IDataModule"/> 契约。
/// <para>
/// req-099 B2 验收：<c>RefreshService</c> 通过本模块保存/记错/记聚合，不再直接操作存储；
/// 刷新聚合（req-013）逻辑从 <c>RefreshService</c> 迁移至此集中管理。
/// </para>
/// </summary>
public sealed class DataModule : IDataModule
{
    private readonly UsageHistoryStore _store;
    private readonly UsageHistoryRepository? _repository;
    /// <summary>req-092 B3 接线：字段级差异检测引擎，用于提取标准字段与对比新旧值。</summary>
    private readonly IUsageDataDiffService _diffService = new UsageDataDiffService();
    /// <summary>req-107 B8：时序明细仓储（懒初始化，与历史库同文件），供声明式图表取数。</summary>
    private UsageDetailRepository? _detailRepository;

    /// <summary>
    /// 创建数据模块。传入仓库时启用 SQLite 持久化（内部据此创建 Store）。
    /// </summary>
    /// <param name="repository">历史数据仓库（可选）。为 null 时仅内存模式。</param>
    public DataModule(UsageHistoryRepository? repository = null)
    {
        _repository = repository;
        _store = repository != null ? new UsageHistoryStore(repository) : new UsageHistoryStore();
    }

    /// <summary>
    /// 用已有 Store 创建数据模块（用于测试注入或 App 已构造 Store 的场景）。
    /// </summary>
    /// <param name="store">已构造的历史存储。</param>
    /// <param name="repository">历史数据仓库（可选，用于刷新聚合与持久化加载）。</param>
    public DataModule(UsageHistoryStore store, UsageHistoryRepository? repository)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _repository = repository;
    }

    /// <inheritdoc/>
    public IUsageHistoryStore HistoryStore => _store;

    /// <inheritdoc/>
    public int MaxPoints
    {
        get => _store.MaxPoints;
        set => _store.MaxPoints = value;
    }

    /// <inheritdoc/>
    public event EventHandler? HistoryChanged
    {
        add => _store.HistoryChanged += value;
        remove => _store.HistoryChanged -= value;
    }

    /// <inheritdoc/>
    public event EventHandler<string>? ProviderHistoryChanged
    {
        add => _store.ProviderHistoryChanged += value;
        remove => _store.ProviderHistoryChanged -= value;
    }

    /// <inheritdoc/>
    public void SaveUsage(UsageInfo usage, IReadOnlyDictionary<string, object>? standardFields = null)
    {
        if (usage == null) return;
        // Store.AddPoint(providerId, usage) 内部走 InsertUsagePointIfChangedAsync（字段级指纹去重）+ 持久化。
        _store.AddPoint(usage.ProviderId, usage);

        // req-092 B3 接线：字段级差异持久化。与点级历史（AddPoint）并行，仅将变化的标准字段写入 usage_field_versions。
        // 优先用插件 MapToStandardFields 结果（standardFields），缺省回退 DiffService.ExtractStandardFields。
        // 需有 repository 才能持久化（纯内存模式无字段版本表）。
        var accountId = ResolveAccountId(usage);
        if (_repository != null)
        {
            var newFields = standardFields ?? _diffService.ExtractStandardFields(usage);
            _ = SaveIncrementalDiffAsync(usage.ProviderId, accountId, newFields);
        }

        // req-107 B8：把插件提供的每日时序数据（Extra 中 mm_dailyToken* ）写入 usage_daily_trend，供声明式折线/热力图取数。
        PopulateDailyTrendFromExtra(usage);

        // req-网页校准：把插件提供的模型×日明细（Extra 中 mm_modelDaily）写入 usage_model_daily（分模型维度）。
        PopulateModelDailyFromExtra(usage);
    }

    /// <summary>req-088 Phase1：解析落库用 account_id（usage.AccountId 为空时兜底 "default"，兼容无身份 Provider）。</summary>
    private static string ResolveAccountId(UsageInfo usage)
        => string.IsNullOrWhiteSpace(usage.AccountId) ? "default" : usage.AccountId!;

    /// <summary>
    /// req-107 B8：从 <see cref="UsageInfo.Extra"/> 的 <c>mm_dailyTokenDates</c> / <c>mm_dailyTokenValues</c> /
    /// <c>mm_dailyCacheHitPercents</c>（由 MiniMaxDomExtractor 从 date_model_usage 提取）写入 <c>usage_daily_trend</c>。
    /// <para>仅当日期与数值列表齐备且等长时写入（date_model_usage 路径）；daily_token_usage 回退路径（无日期）不写表。失败仅日志，不影响主流程。</para>
    /// </summary>
    private void PopulateDailyTrendFromExtra(UsageInfo usage)
    {
        if (_repository == null || usage.Extra == null) return;
        try
        {
            if (!usage.Extra.TryGetValue("mm_dailyTokenDates", out var datesObj) || datesObj is not List<string> dates || dates.Count == 0)
                return;
            if (!usage.Extra.TryGetValue("mm_dailyTokenValues", out var valuesObj) || valuesObj is not List<long> values || values.Count == 0)
                return;
            usage.Extra.TryGetValue("mm_dailyCacheHitPercents", out var cacheObj);
            var cacheList = cacheObj as List<double>;
            if (values.Count != dates.Count) return; // 数据不齐时不写，避免日期/数值错位

            var repo = EnsureDetailRepository();
            if (repo == null) return;
            var rows = new List<DailyTrendRow>(dates.Count);
            for (var i = 0; i < dates.Count; i++)
            {
                double? cache = (cacheList != null && i < cacheList.Count && cacheList[i] >= 0) ? cacheList[i] : null;
                rows.Add(new DailyTrendRow(dates[i], values[i], cache));
            }
            repo.UpsertDailyTrendBatch(usage.ProviderId, ResolveAccountId(usage), rows);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("DataModule", $"PopulateDailyTrendFromExtra({usage.ProviderId}) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// req-网页校准：从 <see cref="UsageInfo.Extra"/> 的 <c>mm_modelDaily</c>（MiniMaxDomExtractor 从 date_model_usage[].models[] 提取）
    /// 写入 <c>usage_model_daily</c>（分模型维度：输入/输出/缓存读取/总计/命中率）。
    /// <para>每项为 {date, model, input_token, output_token, cache_read_token, total_token, cache_hit_percent} 字典；
    /// cache_hit_percent 为 -1 时视为无数据存 null。失败仅日志，不影响主流程。</para>
    /// </summary>
    private void PopulateModelDailyFromExtra(UsageInfo usage)
    {
        if (_repository == null || usage.Extra == null) return;
        try
        {
            if (!usage.Extra.TryGetValue("mm_modelDaily", out var mdObj)
                || mdObj is not List<Dictionary<string, object>> modelRows || modelRows.Count == 0)
                return;

            var repo = EnsureDetailRepository();
            if (repo == null) return;

            var rows = new List<ModelDailyRow>(modelRows.Count);
            foreach (var r in modelRows)
            {
                var date = r.TryGetValue("date", out var dv) ? dv as string ?? "" : "";
                var model = r.TryGetValue("model", out var mv) ? mv as string ?? "" : "";
                if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(model)) continue;
                long GetL(string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToInt64(v) : 0L;
                double? cacheHit = null;
                if (r.TryGetValue("cache_hit_percent", out var cv) && cv != null)
                {
                    var d = Convert.ToDouble(cv);
                    if (d >= 0) cacheHit = d;
                }
                rows.Add(new ModelDailyRow(date, model,
                    GetL("input_token"), GetL("output_token"), GetL("cache_read_token"),
                    0, GetL("total_token"), cacheHit));
            }
            if (rows.Count > 0)
                repo.UpsertModelDailyBatch(usage.ProviderId, ResolveAccountId(usage), rows);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("DataModule", $"PopulateModelDailyFromExtra({usage.ProviderId}) failed: {ex.Message}");
        }
    }

    /// <summary>req-107 B8：懒初始化明细仓储（与历史库同一 SQLite 文件）并幂等建表。</summary>
    private UsageDetailRepository? EnsureDetailRepository()
    {
        if (_repository == null) return null;
        if (_detailRepository == null)
        {
            _detailRepository = new UsageDetailRepository(_repository.DbFilePath);
            _detailRepository.EnsureSchema();
        }
        return _detailRepository;
    }

    /// <summary>req-107 B8：声明式图表取数服务（按需创建）；无持久化仓库时返回 null。</summary>
    public ChartDataService? GetChartDataService()
    {
        var repo = EnsureDetailRepository();
        return repo != null ? new ChartDataService(repo) : null;
    }

    /// <summary>
    /// req-092 B3：异步执行字段级差异检测 + 增量保存（fire-and-forget，不阻塞刷新）。
    /// <para>从 <c>usage_field_versions</c> 读取上次各字段最新值作为旧值，与新值逐字段对比，
    /// 仅对有变化的字段调 <c>SaveIncrementalAsync</c>。相同数据重复刷新时无新记录（验收 req-092#3）。</para>
    /// </summary>
    private async Task SaveIncrementalDiffAsync(string providerId, string accountId, IReadOnlyDictionary<string, object> newFields)
    {
        try
        {
            var oldFields = await _repository!.GetLatestFieldsAsync(providerId, accountId);
            var changes = _diffService.DetectChanges(oldFields, newFields);
            if (changes.Length > 0)
            {
                await _repository.SaveIncrementalAsync(providerId, accountId, changes);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("DataModule", $"SaveIncrementalDiffAsync({providerId}) failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void AddErrorPoint(string providerId) => _store.AddErrorPoint(providerId);

    /// <inheritdoc/>
    public IReadOnlyList<double> GetHistoryValues(string providerId) => _store.GetHistoryValues(providerId);

    /// <inheritdoc/>
    public IReadOnlyList<HistoryPoint> GetHistory(string providerId) => _store.GetHistory(providerId);

    /// <inheritdoc/>
    public Task LoadPersistedHistoryAsync(IEnumerable<string> providerIds, int pointsPerProvider)
        => _repository != null
            ? _store.LoadFromRepositoryAsync(_repository, providerIds, pointsPerProvider)
            : Task.CompletedTask;

    /// <inheritdoc/>
    public void RecordRefreshAggregate(string providerId, string triggerKind)
    {
        // req-099 B2：本方法由 RefreshService.RecordRefreshAggregateAsync 迁移而来，数据聚合/持久化集中在数据模块。
        if (_repository == null)
        {
            FileLogger.Warn("DataModule", $"RecordRefreshAggregate({providerId}): repository is null");
            return;
        }
        var points = _store.GetHistory(providerId);
        if (points == null || points.Count == 0)
        {
            FileLogger.Warn("DataModule", $"RecordRefreshAggregate({providerId}): no history points");
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
            FileLogger.Warn("DataModule", $"RecordRefreshAggregate({providerId}): all points are errors");
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

        _ = _repository.InsertRefreshAggregateAsync(agg);
        FileLogger.Info("DataModule", $"RecordRefreshAggregate({providerId}): inserted aggregate with {valid.Count} points");
    }
}
