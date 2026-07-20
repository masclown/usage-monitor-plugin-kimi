using System.Collections.Concurrent;
using UsageMonitor.Core.Models;

// req-086-3.4：Core 项目内部作为兼容层继续使用旧字段，抑制 CS0618 警告
#pragma warning disable CS0618

namespace UsageMonitor.Core.Services;

/// <summary>
/// 单个用量历史点记录（用于折线图绘制）
/// </summary>
public class HistoryPoint
{
    /// <summary>已用百分比（0-100）</summary>
    public double UsagePercent { get; set; }

    /// <summary>记录时间</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>是否为失败/数据缺失点（刷新失败时记录，供图表呈现断点/缺口）。</summary>
    public bool IsError { get; set; }
}

/// <summary>
/// 用量历史数据存储服务
/// - 为每个 Provider 维护最近 N 个已用百分比数据点
/// - 默认 60 个点（约 5 分钟间隔下覆盖 5 小时）
/// - 可在 AppSettings.HistoryPointCount 中调整为 30/60/120
/// - 线程安全（UI 与刷新服务并发访问）
/// - 可选注入 UsageHistoryRepository，AddPoint 后 fire-and-forget 异步写 SQLite
/// </summary>
public class UsageHistoryStore
{
    private readonly ConcurrentDictionary<string, Queue<HistoryPoint>> _histories = new();
    private readonly UsageHistoryRepository? _repository;
    private int _maxPoints = 60;

    /// <summary>
    /// 每个 Provider 最多保留的历史点数
    /// 修改后会自动裁剪已有数据并触发 HistoryChanged
    /// </summary>
    public int MaxPoints
    {
        get => _maxPoints;
        set
        {
            if (value < 1) value = 1;
            if (value == _maxPoints) return;

            _maxPoints = value;
            // 裁剪所有已存在数据
            foreach (var key in _histories.Keys.ToList())
            {
                if (_histories.TryGetValue(key, out var queue))
                {
                    lock (queue)
                    {
                        while (queue.Count > _maxPoints)
                            queue.Dequeue();
                    }
                }
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>历史数据变更事件（添加点或修改 MaxPoints 时触发）</summary>
    public event EventHandler? HistoryChanged;

    /// <summary>指定 Provider 的历史数据变更事件（payload 为 providerId）</summary>
    public event EventHandler<string>? ProviderHistoryChanged;

    /// <summary>
    /// 创建内存历史仓库。可选传入持久化仓库，传入后会自动调择写 SQLite。
    /// </summary>
    public UsageHistoryStore(UsageHistoryRepository? repository = null)
    {
        _repository = repository;
    }

    /// <summary>
    /// 添加一个用量历史点（线程安全）
    /// - 同步：内存队列 + MaxPoints 裁剪 + 触发事件
    /// - 异步（可选）：fire-and-forget 写 SQLite，供历史窗口回看
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="usagePercent">已用百分比（0-100）</param>
    public void AddPoint(string providerId, double usagePercent)
    {
        if (string.IsNullOrEmpty(providerId)) return;

        // 钳制范围
        if (usagePercent < 0) usagePercent = 0;
        if (usagePercent > 100) usagePercent = 100;

        EnqueueMemoryPoint(providerId, usagePercent, isError: false);

        // 异步写库（fire-and-forget）。仓库内部已 try/catch + FileLogger，这里不需要再包装。
        if (_repository != null)
        {
            // 构造最小可用的 UsageInfo，避免引入插件路径所产生的额外查询。
            // 历史窗口仅需 (providerId, used_percent, recorded_at)，仓库 UpsertPoint 仅读取这些字段。
            //
            // 注意：为了让 GetUsagePercentage() 返回我们手头这个 usagePercent，
            // 必须让 UsedAmount / TotalAmount 形成比例 (usagePercent / 100)，
            // 然后由 Repository 中的钳制（0-100）保证范围。
            // 不要设 UsedAmount = 0 / TotalAmount = 100，那样 percent 永远是 0。
            var dummy = new UsageInfo
            {
                ProviderId = providerId,
                ProviderName = providerId, // 持久层不使用 name，仅占位
                UsedAmount = (decimal)usagePercent,
                TotalAmount = 100m,
                UsedTokens = 0,
                TotalTokens = -1,
                LastUpdated = DateTime.Now,
                IsSuccess = true,
                ErrorMessage = null
            };
            _ = Task.Run(() => _repository.UpsertPoint(dummy));
        }
    }

    /// <summary>
    /// req-015：添加一个包含完整 extras 的用量历史点。
    /// 内存入队走与 <see cref="AddPoint(string, double)"/> 相同的路径；
    /// 写库走 <see cref="UsageHistoryRepository.InsertUsagePointIfChangedAsync"/>：
    /// 先与上次同 Provider 同日采样点比对业务指纹，一致则跳过写入（仅日志）。
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="usage">本次刷新的完整用量（含 extras）</param>
    public void AddPoint(string providerId, UsageInfo usage)
    {
        if (string.IsNullOrEmpty(providerId) || usage == null) return;

        var usagePercent = usage.GetUsagePercentage();
        if (usagePercent < 0) usagePercent = 0;
        if (usagePercent > 100) usagePercent = 100;

        EnqueueMemoryPoint(providerId, usagePercent, isError: false);

        if (_repository != null)
        {
            _ = Task.Run(() => _repository.InsertUsagePointIfChangedAsync(usage));
        }
    }

    /// <summary>
    /// req-015 + 通用：把内存点压入队列 + 触发事件（线程安全）。
    /// </summary>
    private void EnqueueMemoryPoint(string providerId, double usagePercent, bool isError)
    {
        var queue = _histories.GetOrAdd(providerId, _ => new Queue<HistoryPoint>());
        lock (queue)
        {
            queue.Enqueue(new HistoryPoint
            {
                UsagePercent = usagePercent,
                Timestamp = DateTime.Now,
                IsError = isError
            });
            while (queue.Count > _maxPoints)
                queue.Dequeue();
        }

        ProviderHistoryChanged?.Invoke(this, providerId);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 记录一个“失败/数据缺失”历史点（IsError=true）。仅写内存序列并触发变更事件，不写 SQLite。
    /// UsagePercent 沿用该 provider 上一个有效值（无则 0），供图表在阶段三呈现断点/缺口。
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    public void AddErrorPoint(string providerId)
    {
        if (string.IsNullOrEmpty(providerId)) return;

        var queue = _histories.GetOrAdd(providerId, _ => new Queue<HistoryPoint>());
        lock (queue)
        {
            var last = queue.Count > 0 ? queue.Last().UsagePercent : 0;
            queue.Enqueue(new HistoryPoint
            {
                UsagePercent = last,
                Timestamp = DateTime.Now,
                IsError = true
            });
            while (queue.Count > _maxPoints)
                queue.Dequeue();
        }

        ProviderHistoryChanged?.Invoke(this, providerId);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 从持久化仓库加载并填充指定 provider 的初始内存数据。
    /// 启动时一次性调用，避免重启后历史曲线从头画起。
    /// </summary>
    public async Task LoadFromRepositoryAsync(UsageHistoryRepository repository,
                                              IEnumerable<string> providerIds,
                                              int pointsPerProvider)
    {
        if (repository == null || pointsPerProvider <= 0) return;
        foreach (var pid in providerIds)
        {
            if (string.IsNullOrEmpty(pid)) continue;
            try
            {
                var records = await repository.LoadLatestPointsAsync(pid, pointsPerProvider);
                if (records.Count == 0) continue;

                var queue = _histories.GetOrAdd(pid, _ => new Queue<HistoryPoint>());
                lock (queue)
                {
                    // 先清空避免与遗留数据混合
                    queue.Clear();
                    foreach (var r in records)
                    {
                        queue.Enqueue(UsageHistoryRepository.ToInMemoryPoint(r));
                    }
                    while (queue.Count > _maxPoints)
                        queue.Dequeue();
                }
                ProviderHistoryChanged?.Invoke(this, pid);
            }
            catch (Exception ex)
            {
                FileLogger.Error("UsageHistoryStore",
                    $"LoadFromRepositoryAsync({pid}) failed", ex);
            }
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 获取指定 Provider 的历史数据（返回快照）
    /// </summary>
    public IReadOnlyList<HistoryPoint> GetHistory(string providerId)
    {
        if (_histories.TryGetValue(providerId, out var queue))
        {
            lock (queue)
            {
                return queue.ToArray();
            }
        }
        return Array.Empty<HistoryPoint>();
    }

    /// <summary>
    /// 获取指定 Provider 的历史百分比值列表（仅数值，便于图表绑定）
    /// </summary>
    public IReadOnlyList<double> GetHistoryValues(string providerId)
    {
        var history = GetHistory(providerId);
        var result = new double[history.Count];
        for (int i = 0; i < history.Count; i++)
            result[i] = history[i].UsagePercent;
        return result;
    }

    /// <summary>
    /// 清空所有历史数据
    /// </summary>
    public void Clear()
    {
        _histories.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
