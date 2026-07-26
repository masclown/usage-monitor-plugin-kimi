namespace UsageMonitor.Core.Plugins.MiniChart;

/// <summary>
/// req-088 B1：迷你图类型枚举。
/// <para>
/// 定义 Taskbar 内可渲染的迷你图种类。SDK 内置 5 种类型，第三方插件可基于现有类型渲染数据，
/// 或新增枚举值扩展（需配合 ITaskbarMiniChartRegistry 注册 DataTemplateSelector）。
/// </para>
/// <para>设计动机：TaskbarWindow 当前硬编码 3 种模板（Text/LineChart/RingChart），新增图类型必须改
/// XAML 主文件。把类型抽象成枚举 + 注册中心后，插件可独立提供任意图类型的描述符。</para>
/// </summary>
public enum MiniChartKind
{
    /// <summary>迷你文字（Provider 名称 + 剩余百分比）—— req-029 已实现。</summary>
    MiniText = 0,

    /// <summary>迷你圆环/半圆环图（UsagePercentage 0-100%）—— req-051 重构中。</summary>
    MiniRingChart = 1,

    /// <summary>迷你折线图（历史用量百分比序列）—— req-029 已实现。</summary>
    MiniLineChart = 2,

    /// <summary>迷你柱状图（每日用量）—— 未来扩展，预留枚举值。</summary>
    MiniBarChart = 3,

    /// <summary>迷你热力图（每日 Token 用量日历）—— 未来扩展，预留枚举值。</summary>
    MiniHeatMap = 4,

    /// <summary>迷你面积图（历史用量时间序列，折线 + 渐变填充）。</summary>
    MiniAreaChart = 5,
}