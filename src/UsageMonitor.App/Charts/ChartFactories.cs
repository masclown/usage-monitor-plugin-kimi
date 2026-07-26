using System;
using System.Collections.Generic;
using System.Windows.Media;
using UsageMonitor.App.Controls;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using Brush = System.Windows.Media.Brush;

namespace UsageMonitor.App.Charts;

/// <summary>
/// 把宿主主题的 object 颜色转换为 WPF <see cref="Brush"/> 的小工具（REQ-005 SDK）。
/// <para>
/// Core SDK 中的 <see cref="IChartTheme"/> 用 object 携带颜色（避免 Core 强绑 WPF），
/// WPF 控件在 Bind 时通过本适配器把 object 还原为 Brush；null 或类型不匹配时回退到
/// App.xaml 中的同名 DynamicResource，确保插件未提供主题时也能正常渲染。
/// </para>
/// </summary>
internal static class ThemeBrushAdapter
{
    /// <summary>把主题 object 颜色转换为 Brush；无法识别时回退到 key 对应的 DynamicResource。</summary>
    public static Brush AsBrush(object? value, string fallbackResourceKey)
    {
        if (value is Brush b) return b;
        if (System.Windows.Application.Current?.TryFindResource(fallbackResourceKey) is Brush dyn)
            return dyn;
        return System.Windows.Media.Brushes.Gray;
    }

    /// <summary>保留占位字段：当前实现走 <see cref="System.Windows.Application.Current"/>，无需额外缓存。</summary>
    private static System.Windows.Application? _ => null;
}

/// <summary>
/// 折线图工厂（REQ-005 SDK）—— 包装 <c>MiniLineChartControl</c>。
/// <para>
/// 一期 k1：保留现有 <c>MiniLineChartControl</c> 的所有依赖属性与渲染行为，
/// 仅新增 <see cref="MiniLineChartAdapter.Bind"/> 接收 <see cref="LineChartData"/>；
/// 新旧 API 完全兼容，未走 SDK 的旧调用代码不受影响。
/// </para>
/// </summary>
public sealed class MiniLineChartFactory : IUsageChartFactory
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Line;

    /// <inheritdoc />
    public IUsageChart Create() => new MiniLineChartAdapter();
}

/// <summary>折线图适配器：把 <see cref="LineChartData"/> 注入 <c>MiniLineChartControl</c>。</summary>
public sealed class MiniLineChartAdapter : IUsageChart
{
    /// <summary>WPF 控件实例。</summary>
    public MiniLineChartControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Line;

    /// <inheritdoc />
    public Type ControlType => typeof(MiniLineChartControl);

    /// <inheritdoc />
    public string DisplayName => "折线图 / Line Chart";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is LineChartData line)
        {
            Control.Values = line.Values ?? Array.Empty<double>();
            if (line.MaxValue.HasValue) Control.MaxValue = line.MaxValue.Value;
        }
        if (theme != null)
        {
            Control.LowBrush = ThemeBrushAdapter.AsBrush(theme.LowBrush, "UsageLowBrush");
            Control.MidBrush = ThemeBrushAdapter.AsBrush(theme.MidBrush, "UsageMidBrush");
            Control.HighBrush = ThemeBrushAdapter.AsBrush(theme.HighBrush, "UsageHighBrush");
        }
    }
}

/// <summary>
/// 柱状图工厂（REQ-005 SDK）—— 包装 <c>BarChartControl</c>。
/// </summary>
public sealed class BarChartFactory : IUsageChartFactory
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Bar;

    /// <inheritdoc />
    public IUsageChart Create() => new BarChartAdapter();
}

/// <summary>柱状图适配器：把 <see cref="BarChartData"/> 注入 <c>BarChartControl</c>。</summary>
public sealed class BarChartAdapter : IUsageChart
{
    /// <summary>WPF 控件实例。</summary>
    public BarChartControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Bar;

    /// <inheritdoc />
    public Type ControlType => typeof(BarChartControl);

    /// <inheritdoc />
    public string DisplayName => "柱状图 / Bar Chart";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is BarChartData bar)
        {
            Control.Values = bar.Values ?? Array.Empty<double>();
            if (bar.MaxValue.HasValue) Control.MaxValue = bar.MaxValue.Value;
        }
        if (theme != null)
        {
            Control.BarBrush = ThemeBrushAdapter.AsBrush(theme.MidBrush, "AccentBrush");
            Control.GridLineBrush = ThemeBrushAdapter.AsBrush(theme.TrackBrush, "ChartAxisBrush");
            Control.TextBrush = ThemeBrushAdapter.AsBrush(theme.TextBrush, "TextSecondaryBrush");
        }
    }
}

/// <summary>
/// 圆环图工厂（REQ-005 SDK）—— 包装 <c>RingChartControl</c>。
/// </summary>
public sealed class RingChartFactory : IUsageChartFactory
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Ring;

    /// <inheritdoc />
    public IUsageChart Create() => new RingChartAdapter();
}

/// <summary>圆环图适配器：把 <see cref="RingChartData"/> 注入 <c>RingChartControl</c>。</summary>
public sealed class RingChartAdapter : IUsageChart
{
    /// <summary>WPF 控件实例。</summary>
    public RingChartControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Ring;

    /// <inheritdoc />
    public Type ControlType => typeof(RingChartControl);

    /// <inheritdoc />
    public string DisplayName => "圆环图 / Ring Chart";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is RingChartData ring)
        {
            Control.Percent = ring.Percent;
            if (ring.WarningThreshold.HasValue) Control.WarningThreshold = ring.WarningThreshold.Value;
            if (ring.DangerThreshold.HasValue) Control.DangerThreshold = ring.DangerThreshold.Value;
            if (!string.IsNullOrEmpty(ring.CenterLabel)) Control.CenterText = ring.CenterLabel;
        }
        if (theme != null)
        {
            Control.TrackBrush = ThemeBrushAdapter.AsBrush(theme.TrackBrush, "TrackBrush");
            Control.ProgressBrush = ThemeBrushAdapter.AsBrush(theme.LowBrush, "AccentBrush");
            Control.WarningBrush = ThemeBrushAdapter.AsBrush(theme.MidBrush, "WarningBrush");
            Control.DangerBrush = ThemeBrushAdapter.AsBrush(theme.HighBrush, "DangerBrush");
        }
    }
}

/// <summary>
/// 年热力图工厂（REQ-005 SDK）—— 包装 <c>YearHeatMapControl</c>。
/// </summary>
public sealed class YearHeatMapFactory : IUsageChartFactory
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.HeatMap;

    /// <inheritdoc />
    public IUsageChart Create() => new YearHeatMapAdapter();
}

/// <summary>热力图适配器：把 <see cref="HeatMapData"/> 注入 <c>YearHeatMapControl</c>。</summary>
public sealed class YearHeatMapAdapter : IUsageChart
{
    /// <summary>WPF 控件实例。</summary>
    public YearHeatMapControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.HeatMap;

    /// <inheritdoc />
    public Type ControlType => typeof(YearHeatMapControl);

    /// <inheritdoc />
    public string DisplayName => "年热力图 / Year Heatmap";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is HeatMapData heat)
        {
            // YearHeatMapControl.Cells 是 IEnumerable，内部自己 double -> HeatMapCell 转换。
            Control.Cells = heat.Cells ?? Array.Empty<double>();
        }
        if (theme != null)
        {
            Control.EmptyCellBrush = ThemeBrushAdapter.AsBrush(theme.TrackBrush, "TrackBrush");
            Control.TextBrush = ThemeBrushAdapter.AsBrush(theme.TextBrush, "TextSecondaryBrush");
        }
    }
}

/// <summary>
/// 日月编程时段弧线图工厂（REQ-005 SDK）—— 包装 <c>DayNightArcControl</c>。
/// </summary>
public sealed class DayNightArcFactory : IUsageChartFactory
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.DayNightArc;

    /// <inheritdoc />
    public IUsageChart Create() => new DayNightArcAdapter();
}

/// <summary>编程时段图适配器：把 <see cref="DayNightArcData"/> 注入 <c>DayNightArcControl</c>。</summary>
public sealed class DayNightArcAdapter : IUsageChart
{
    /// <summary>WPF 控件实例。</summary>
    public DayNightArcControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.DayNightArc;

    /// <inheritdoc />
    public Type ControlType => typeof(DayNightArcControl);

    /// <inheritdoc />
    public string DisplayName => "编程时段 / Day-Night Arc";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is DayNightArcData arc)
        {
            Control.HourlyActivity = arc.HourlyActivity ?? Array.Empty<double>();
        }
        if (theme != null)
        {
            Control.TrackBrush = ThemeBrushAdapter.AsBrush(theme.TrackBrush, "TextTertiaryBrush");
            Control.AccentBrush = ThemeBrushAdapter.AsBrush(theme.LowBrush, "AccentBrush");
            Control.TextBrush = ThemeBrushAdapter.AsBrush(theme.TextBrush, "TextSecondaryBrush");
        }
    }
}

// ============== REQ-082 SDK v2 新增工厂 ==============

/// <summary>
/// 堆叠柱状图工厂（REQ-082 SDK v2）—— 包装 <c>StackedBarChartControl</c>。
/// </summary>
public sealed class StackedBarChartFactory : IUsageChartFactory2
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.StackedBar;

    /// <inheritdoc />
    public IUsageChart Create() => new StackedBarChartAdapter();

    /// <inheritdoc />
    public IUsageChart Create(ChartContext context) => new StackedBarChartAdapter(context);
}

/// <summary>堆叠柱状图适配器：把 <see cref="StackedBarChartData"/> 注入控件。</summary>
public sealed class StackedBarChartAdapter : IUsageChart
{
    private readonly ChartContext? _context;

    /// <summary>无参构造（兼容 V1 调用）。</summary>
    public StackedBarChartAdapter() { }

    /// <summary>接收 <see cref="ChartContext"/> 的构造。</summary>
    public StackedBarChartAdapter(ChartContext context) { _context = context; }

    /// <summary>WPF 控件实例。</summary>
    public StackedBarChartControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.StackedBar;

    /// <inheritdoc />
    public Type ControlType => typeof(StackedBarChartControl);

    /// <inheritdoc />
    public string DisplayName => "堆叠柱状图 / Stacked Bar Chart";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is StackedBarChartData stacked)
        {
            Control.Categories = stacked.Categories;
            Control.Series = stacked.Series;
            Control.Unit = stacked.Unit;
            Control.Title = stacked.Title;
        }
        if (theme != null)
        {
            Control.BarBrush = ThemeBrushAdapter.AsBrush(theme.MidBrush, "AccentBrush");
            Control.GridLineBrush = ThemeBrushAdapter.AsBrush(theme.TrackBrush, "ChartAxisBrush");
            Control.TextBrush = ThemeBrushAdapter.AsBrush(theme.TextBrush, "TextSecondaryBrush");
        }
    }
}

/// <summary>
/// 面积图工厂（REQ-082 SDK v2）—— 包装 <c>AreaChartControl</c>。
/// </summary>
public sealed class AreaChartFactory : IUsageChartFactory2
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Area;

    /// <inheritdoc />
    public IUsageChart Create() => new AreaChartAdapter();

    /// <inheritdoc />
    public IUsageChart Create(ChartContext context) => new AreaChartAdapter(context);
}

/// <summary>面积图适配器：把 <see cref="AreaChartData"/> 注入控件。</summary>
public sealed class AreaChartAdapter : IUsageChart
{
    private readonly ChartContext? _context;

    /// <summary>无参构造。</summary>
    public AreaChartAdapter() { }

    /// <summary>接收 <see cref="ChartContext"/> 的构造。</summary>
    public AreaChartAdapter(ChartContext context) { _context = context; }

    /// <summary>WPF 控件实例。</summary>
    public AreaChartControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Area;

    /// <inheritdoc />
    public Type ControlType => typeof(AreaChartControl);

    /// <inheritdoc />
    public string DisplayName => "面积图 / Area Chart";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is AreaChartData area)
        {
            Control.Values = area.Values ?? Array.Empty<double>();
            Control.Categories = area.Categories;
            if (area.MaxValue.HasValue) Control.MaxValue = area.MaxValue.Value;
            Control.Unit = area.Unit;
            Control.SeriesName = area.SeriesName;
            // 问题7：传递填充/平滑曲线开关。
            Control.FillBelowLine = area.FillBelowLine;
            Control.SmoothCurve = area.SmoothCurve;
        }
        if (theme != null)
        {
            Control.AreaBrush = ThemeBrushAdapter.AsBrush(theme.LowBrush, "AccentBrush");
            Control.StrokeBrush = ThemeBrushAdapter.AsBrush(theme.MidBrush, "AccentBrush");
        }
    }
}

/// <summary>
/// 分组容器工厂（REQ-082 SDK v2）—— 包装 <c>GroupedChartControl</c>。
/// </summary>
public sealed class GroupedChartFactory : IUsageChartFactory2
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Grouped;

    /// <inheritdoc />
    public IUsageChart Create() => new GroupedChartAdapter();

    /// <inheritdoc />
    public IUsageChart Create(ChartContext context) => new GroupedChartAdapter(context);
}

/// <summary>分组容器适配器：把 <see cref="GroupedChartData"/> 注入控件。</summary>
public sealed class GroupedChartAdapter : IUsageChart
{
    private readonly ChartContext? _context;

    /// <summary>无参构造。</summary>
    public GroupedChartAdapter() { }

    /// <summary>接收 <see cref="ChartContext"/> 的构造。</summary>
    public GroupedChartAdapter(ChartContext context) { _context = context; }

    /// <summary>WPF 控件实例。</summary>
    public GroupedChartControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.Grouped;

    /// <inheritdoc />
    public Type ControlType => typeof(GroupedChartControl);

    /// <inheritdoc />
    public string DisplayName => "分组容器 / Grouped Chart";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is GroupedChartData grouped)
        {
            Control.Groups = grouped.Groups;
        }
    }
}

/// <summary>
/// 度量进度条工厂（REQ-082 SDK v2）—— 包装 <c>MetricBarControl</c>。
/// </summary>
public sealed class MetricBarChartFactory : IUsageChartFactory2
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.MetricBar;

    /// <inheritdoc />
    public IUsageChart Create() => new MetricBarChartAdapter();

    /// <inheritdoc />
    public IUsageChart Create(ChartContext context) => new MetricBarChartAdapter(context);
}

/// <summary>度量进度条适配器：把 <see cref="MetricBarData"/> 注入控件。</summary>
public sealed class MetricBarChartAdapter : IUsageChart
{
    private readonly ChartContext? _context;

    /// <summary>无参构造。</summary>
    public MetricBarChartAdapter() { }

    /// <summary>接收 <see cref="ChartContext"/> 的构造。</summary>
    public MetricBarChartAdapter(ChartContext context) { _context = context; }

    /// <summary>WPF 控件实例。</summary>
    public MetricBarControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.MetricBar;

    /// <inheritdoc />
    public Type ControlType => typeof(MetricBarControl);

    /// <inheritdoc />
    public string DisplayName => "度量进度条 / Metric Bar";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is MetricBarData metric)
        {
            Control.Bars = metric.Bars;
        }
    }
}

/// <summary>
/// 度量数字网格工厂（REQ-082 SDK v2）—— 包装 <c>MetricGridControl</c>。
/// </summary>
public sealed class MetricGridChartFactory : IUsageChartFactory2
{
    /// <inheritdoc />
    public ChartKind Kind => ChartKind.MetricGrid;

    /// <inheritdoc />
    public IUsageChart Create() => new MetricGridChartAdapter();

    /// <inheritdoc />
    public IUsageChart Create(ChartContext context) => new MetricGridChartAdapter(context);
}

/// <summary>度量数字网格适配器：把 <see cref="MetricGridData"/> 注入控件。</summary>
public sealed class MetricGridChartAdapter : IUsageChart
{
    private readonly ChartContext? _context;

    /// <summary>无参构造。</summary>
    public MetricGridChartAdapter() { }

    /// <summary>接收 <see cref="ChartContext"/> 的构造。</summary>
    public MetricGridChartAdapter(ChartContext context) { _context = context; }

    /// <summary>WPF 控件实例。</summary>
    public MetricGridControl Control { get; } = new();

    /// <inheritdoc />
    public ChartKind Kind => ChartKind.MetricGrid;

    /// <inheritdoc />
    public Type ControlType => typeof(MetricGridControl);

    /// <inheritdoc />
    public string DisplayName => "度量数字网格 / Metric Grid";

    /// <inheritdoc />
    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is MetricGridData metric)
        {
            Control.Items = metric.Items;
        }
    }
}