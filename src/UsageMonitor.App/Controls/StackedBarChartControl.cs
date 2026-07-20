using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsageMonitor.Core.Models;
using Brush = System.Windows.Media.Brush;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 多系列堆叠柱状图控件（REQ-082 SDK v2）。动态 N 系列叠加。
/// <para>
/// 用于按类别拆分的多系列叠加展示（如按日期 × 模型维度的消费金额堆叠柱）。
/// </para>
/// </summary>
public class StackedBarChartControl : FrameworkElement
{
    /// <summary>X 轴类别标签（如日期）。</summary>
    public static readonly DependencyProperty CategoriesProperty = DependencyProperty.Register(
        nameof(Categories), typeof(IReadOnlyList<string>), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>X 轴类别标签集合。</summary>
    public IReadOnlyList<string>? Categories
    {
        get => (IReadOnlyList<string>?)GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }

    /// <summary>堆叠系列集合（数量动态）。</summary>
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>堆叠系列集合。</summary>
    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>数值单位提示（"¥"/"tokens"/"次"）。</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>数值单位提示。</summary>
    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>图表标题。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>图表标题。</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>色板（可空，null 时使用主题资源）。</summary>
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(UsageMonitor.Core.Plugins.IChartPalette), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>色板。</summary>
    public UsageMonitor.Core.Plugins.IChartPalette? Palette
    {
        get => (UsageMonitor.Core.Plugins.IChartPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>柱体画笔（默认主题 AccentBrush）。</summary>
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>柱体画笔（无 Palette 时 fallback 使用）。</summary>
    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>网格线画笔。</summary>
    public static readonly DependencyProperty GridLineBrushProperty = DependencyProperty.Register(
        nameof(GridLineBrush), typeof(Brush), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>网格线画笔。</summary>
    public Brush? GridLineBrush
    {
        get => (Brush?)GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
    }

    /// <summary>文字画笔。</summary>
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>文字画笔。</summary>
    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>
    /// 渲染入口：根据 Categories × Series 自绘堆叠柱。
    /// <para>本控件仅占位实现，完整渲染留给后续 sprint 完善。</para>
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // 占位：渲染一个空白面板 + 标题，确保控件可被实例化和显示。
        var bg = BarBrush ?? System.Windows.Media.Brushes.Transparent;
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));
    }
}