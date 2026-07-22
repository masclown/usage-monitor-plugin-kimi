namespace UsageMonitor.Core.Plugins.MiniChart;

/// <summary>
/// req-098：迷你图表的"内容类型"枚举，决定迷你图主显示/副显示数据来源。
/// <para>
/// 与 <see cref="MiniChartKind"/>（决定图类型：RingChart / Text / LineChart）的区别：
/// <list type="bullet">
///   <item><description><see cref="MiniChartKind"/> = "用什么画"（半圆环 / 文字 / 折线）。</description></item>
///   <item><description><see cref="MiniChartContentKind"/> = "画什么"（已用百分比 / Credits / 重置时间）。</description></item>
/// </list>
/// 解耦后，插件可以声明"我能产哪种内容"，宿主按用户配置的内容去拉对应数据。
/// </para>
/// <para>设计动机：req-088 B5 只暴露 5 个内置 MiniChartKind，但用户希望按 Provider 配置
/// 显示"百分比 / Credits / 重置时间"中的任一组合。本枚举让 MiniChartItemViewModel 知道
/// "现在该显示哪种数据"，与图类型正交。</para>
/// </summary>
public enum MiniChartContentKind
{
    /// <summary>主指标（如已用百分比 / 5h 用量）。默认。</summary>
    PrimaryMetric = 0,

    /// <summary>副指标（如周用量）。</summary>
    SecondaryMetric = 1,

    /// <summary>积分 / Credits 余额（文本形式）。</summary>
    Credits = 2,

    /// <summary>重置时间文本（如 "2 小时 21 分钟后重置"）。</summary>
    ResetTime = 3,

    /// <summary>仅 Provider Logo，不显示数字。</summary>
    Logo = 4
}