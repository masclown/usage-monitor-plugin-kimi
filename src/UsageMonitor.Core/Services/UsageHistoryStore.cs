using System.Collections.Concurrent;

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
}

/// <summary>
/// 用量历史数据存储服务
/// - 为每个 Provider 维护最近 N 个已用百分比数据点
/// - 默认 60 个点（约 5 分钟间隔下覆盖 5 小时）
/// - 可在 AppSettings.HistoryPointCount 中调整为 30/60/120
/// - 线程安全（UI 与刷新服务并发访问）
/// </summary>
public class UsageHistoryStore
{
    private readonly ConcurrentDictionary<string, Queue<HistoryPoint>> _histories = new();
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
    /// 添加一个用量历史点（线程安全）
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="usagePercent">已用百分比（0-100）</param>
    public void AddPoint(string providerId, double usagePercent)
    {
        if (string.IsNullOrEmpty(providerId)) return;

        // 钳制范围
        if (usagePercent < 0) usagePercent = 0;
        if (usagePercent > 100) usagePercent = 100;

        var queue = _histories.GetOrAdd(providerId, _ => new Queue<HistoryPoint>());
        lock (queue)
        {
            queue.Enqueue(new HistoryPoint
            {
                UsagePercent = usagePercent,
                Timestamp = DateTime.Now
            });
            while (queue.Count > _maxPoints)
                queue.Dequeue();
        }

        ProviderHistoryChanged?.Invoke(this, providerId);
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
