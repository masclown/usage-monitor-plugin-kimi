using System.Windows;
using System.Windows.Controls;
using UsageMonitor.Core.Plugins.MiniChart;

namespace UsageMonitor.App.Views;

/// <summary>
/// req-088 B5：Taskbar 迷你图模板选择器——按 <see cref="MiniChartDescriptor.Kind"/> 选择 DataTemplate。
/// <para>
/// 模板资源需要在本类初始化时由外部 XAML 注入（与现有 <see cref="TaskbarItemTemplateSelector"/> 风格一致）。
/// 未注册的 Kind 会回退到 <see cref="TextTemplate"/>，避免新增枚举值时 Taskbar 完全空渲染。
/// </para>
/// </summary>
public class MiniChartTemplateSelector : DataTemplateSelector
{
    /// <summary>MiniText 模板（文字模式：Logo + Provider 名 + 剩余百分比）。</summary>
    public DataTemplate? TextTemplate { get; set; }

    /// <summary>MiniRingChart 模板（半圆环图：复用现有 RingChartControl）。</summary>
    public DataTemplate? RingChartTemplate { get; set; }

    /// <summary>MiniLineChart 模板（迷你折线图：复用现有 MiniLineChartControl）。</summary>
    public DataTemplate? LineChartTemplate { get; set; }

    /// <summary>MiniBarChart 模板（迷你柱状图：占位 —— 后续 BarChartControl 可直接复用）。</summary>
    public DataTemplate? BarChartTemplate { get; set; }

    /// <summary>MiniHeatMap 模板（迷你热力图：占位 —— 后续 YearHeatMapControl 可直接复用）。</summary>
    public DataTemplate? HeatMapTemplate { get; set; }

    /// <summary>MiniAreaChart 模板（迷你面积图：MiniSeriesChartControl Area 模式）。</summary>
    public DataTemplate? AreaChartTemplate { get; set; }

    /// <summary>
    /// req-088 B5：根据数据项的 Kind 选择模板。
    /// </summary>
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        // 非 MiniChartItemViewModel 一律回退到 Text 模板（兜底，避免 NRE）
        if (item is not MiniChartItemViewModel vm) return TextTemplate;

        return vm.Kind switch
        {
            MiniChartKind.MiniRingChart => RingChartTemplate ?? TextTemplate,
            MiniChartKind.MiniLineChart => LineChartTemplate ?? TextTemplate,
            MiniChartKind.MiniBarChart => BarChartTemplate ?? TextTemplate,
            MiniChartKind.MiniHeatMap => HeatMapTemplate ?? TextTemplate,
            MiniChartKind.MiniAreaChart => AreaChartTemplate ?? TextTemplate,
            MiniChartKind.MiniText => TextTemplate,
            _ => TextTemplate
        };
    }
}
