using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsageMonitor.Core.Models;
using Brush = System.Windows.Media.Brush;
// ★ WPF/WinForms 命名冲突 alias
using Control = System.Windows.Controls.Control;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 分组容器控件（REQ-082 SDK v2）。按维度分组展示嵌套子图表。
/// <para>
/// 每个分组：标题 + 副标题 + 多个指标（名称 + 汇总值 + 嵌套图表）。
/// 占位实现，完整渲染留给后续 sprint 完善。
/// </para>
/// </summary>
public class GroupedChartControl : Control
{
    static GroupedChartControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GroupedChartControl),
            new FrameworkPropertyMetadata(typeof(GroupedChartControl)));
    }

    /// <summary>分组集合。</summary>
    public static readonly DependencyProperty GroupsProperty = DependencyProperty.Register(
        nameof(Groups), typeof(IReadOnlyList<ChartGroup>), typeof(GroupedChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGroupsChanged));

    /// <summary>分组集合。</summary>
    public IReadOnlyList<ChartGroup>? Groups
    {
        get => (IReadOnlyList<ChartGroup>?)GetValue(GroupsProperty);
        set => SetValue(GroupsProperty, value);
    }

    /// <summary>色板（可空）。</summary>
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(UsageMonitor.Core.Plugins.IChartPalette), typeof(GroupedChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>色板。</summary>
    public UsageMonitor.Core.Plugins.IChartPalette? Palette
    {
        get => (UsageMonitor.Core.Plugins.IChartPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    private static void OnGroupsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GroupedChartControl c) c.InvalidateVisual();
    }
}