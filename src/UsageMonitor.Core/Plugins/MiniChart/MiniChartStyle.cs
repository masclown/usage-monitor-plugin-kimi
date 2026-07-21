namespace UsageMonitor.Core.Plugins.MiniChart;

/// <summary>
/// req-088 B3：迷你图视觉样式枚举。
/// <para>
/// 控制迷你图在 Taskbar 内的视觉密度：文字大小、间距、是否显示副标题等。
/// 不同样式共享同一套数据源，仅展示层不同——切换样式不重新查询 Provider 数据。
/// </para>
/// </summary>
public enum MiniChartStyle
{
    /// <summary>浅色样式（亮色背景 + 深色文字）—— 默认。</summary>
    Light = 0,

    /// <summary>深色样式（深色背景 + 浅色文字）—— 与 Light 通过 DynamicResource 主题切换。</summary>
    Dark = 1,

    /// <summary>紧凑样式（最小字号 + 无副标题，适合空间紧张场景）。</summary>
    Compact = 2,

    /// <summary>详细样式（显示完整 Provider 名称 + 副标题 + 倒计时）。</summary>
    Detailed = 3,
}