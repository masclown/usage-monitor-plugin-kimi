namespace UsageMonitor.Core.Models;

/// <summary>
/// 图表展示位置枚举（REQ-082 SDK v2）。
/// <para>
/// 同一份 <see cref="IChartData"/> 在不同位置应呈现不同细节程度，由宿主在
/// 构造 <see cref="ChartContext"/> 时填入，控件据此自动适配（迷你版/完整版）。
/// </para>
/// </summary>
public enum ChartPlacement
{
    /// <summary>主窗口卡片（迷你版，高度受限）。</summary>
    Card,

    /// <summary>历史窗口（完整版，可滚动）。</summary>
    History,

    /// <summary>任务栏嵌入窗口（极简，单色）。</summary>
    Taskbar,

    /// <summary>托盘悬浮窗（无 X 轴标签）。</summary>
    TrayTooltip,

    /// <summary>独立面板（完整 + 筛选/导出）。</summary>
    Panel
}

/// <summary>
/// 图表渲染上下文（REQ-082 SDK v2）。宿主在创建图表时构造并传入。
/// <para>
/// 控件据此决定：
/// <list type="bullet">
/// <item>展示位置（迷你/完整/极简）。</item>
/// <item>可用宽高（自绘时计算布局）。</item>
/// <item>当前主题与色板。</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Placement">展示位置。</param>
/// <param name="AvailableWidth">可用宽度（像素，运行时由宿主测量）。</param>
/// <param name="AvailableHeight">可用高度（像素，运行时由宿主测量）。</param>
/// <param name="Theme">当前主题（可空，null 时控件沿用最近一次主题）。</param>
/// <param name="Palette">当前色板（可空，null 时控件使用默认色）。</param>
public sealed record ChartContext(
    ChartPlacement Placement,
    double AvailableWidth,
    double AvailableHeight,
    Plugins.IChartTheme? Theme,
    Plugins.IChartPalette? Palette);