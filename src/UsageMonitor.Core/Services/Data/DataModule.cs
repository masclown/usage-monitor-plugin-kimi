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
    public void SaveUsage(UsageInfo usage)
    {
        if (usage == null) return;
        // Store.AddPoint(providerId, usage) 内部走 InsertUsagePointIfChangedAsync（字段级指纹去重）+ 持久化。
        _store.AddPoint(usage.ProviderId, usage);
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
