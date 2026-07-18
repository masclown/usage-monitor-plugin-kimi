using System.Globalization;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-013：单次主窗口刷新产生的"刷新聚合"记录。
/// <para>
/// 与 <c>usage_daily</c>（按天聚合）的区别：本记录按"刷新事件"聚合——主窗口每次刷新（包括手动 + 定时）
/// 写入一行；用户在历史窗口可以直观看到"我点了几次刷新 / 自动刷新了几次"。
/// </para>
/// <para>
/// 存储表：<c>usage_refresh_aggregates</c>。详见 <see cref="UsageMonitor.Core.Services.UsageHistoryRepository"/>。
/// </para>
/// </summary>
/// <param name="Id">自增主键（新增时传 0）</param>
/// <param name="ProviderId">服务商唯一标识</param>
/// <param name="RefreshAt">实际刷新时间（秒级精度）</param>
/// <param name="BusinessDay">业务日期 yyyy-MM-dd（按 Local Time 计算）</param>
/// <param name="MaxUsedPercent">本次刷新区间内最高已用百分比</param>
/// <param name="MinUsedPercent">本次刷新区间内最低已用百分比</param>
/// <param name="EndUsedPercent">本次刷新区间内最后采样点的已用百分比</param>
/// <param name="AvgUsedPercent">本次刷新区间内平均已用百分比</param>
/// <param name="SnapshotCount">本次刷新区间内的采样点数</param>
/// <param name="TriggerKind">触发类型："manual"（手动） / "auto"（定时）</param>
public sealed record RefreshAggregate(
    long Id,
    string ProviderId,
    DateTime RefreshAt,
    string BusinessDay,
    double MaxUsedPercent,
    double MinUsedPercent,
    double EndUsedPercent,
    double AvgUsedPercent,
    int SnapshotCount,
    string TriggerKind)
{
    /// <summary>
    /// 把时间格式化为 SQLite 文本字段（yyyy-MM-dd HH:mm:ss）。
    /// </summary>
    public string RefreshAtText =>
        RefreshAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}