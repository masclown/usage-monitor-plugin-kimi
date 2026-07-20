using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsageMonitor.Core.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
// ★ WPF/WinForms 命名冲突 alias
using Control = System.Windows.Controls.Control;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 度量数字网格控件（REQ-082 SDK v2）。动态 N 个并排独立数字。
/// <para>
/// 取代主窗口 XAML 中硬编码的余额快照 4 项（累计/峰值/活跃/积分余额），
/// 接收 <see cref="MetricGridData"/>，渲染为均分宽度的并排数字网格：
/// 每项 = 标签 + 主数字（大字号）+ 辅助行（小字号）。
/// </para>
/// <para>
/// 颜色策略：默认主题 AccentBrush；当 <see cref="Item.ColorHint"/> 不为空时按提示色覆盖。
/// </para>
/// </summary>
public class MetricGridControl : Control
{
    static MetricGridControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MetricGridControl),
            new FrameworkPropertyMetadata(typeof(MetricGridControl)));
    }

    /// <summary>数字项集合。</summary>
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<MetricGridItem>), typeof(MetricGridControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsChanged));

    /// <summary>数据源：度量数字项集合（IsVisible=false 的项不渲染）。</summary>
    public IReadOnlyList<MetricGridItem>? Items
    {
        get => (IReadOnlyList<MetricGridItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>色板（可空）。</summary>
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(UsageMonitor.Core.Plugins.IChartPalette), typeof(MetricGridControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>当前色板。</summary>
    public UsageMonitor.Core.Plugins.IChartPalette? Palette
    {
        get => (UsageMonitor.Core.Plugins.IChartPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>主数字字号（默认 22，与现有 CardBalanceTemplate 一致）。</summary>
    public static readonly DependencyProperty ValueFontSizeProperty = DependencyProperty.Register(
        nameof(ValueFontSize), typeof(double), typeof(MetricGridControl),
        new FrameworkPropertyMetadata(22.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>主数字字号。</summary>
    public double ValueFontSize
    {
        get => (double)GetValue(ValueFontSizeProperty);
        set => SetValue(ValueFontSizeProperty, value);
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MetricGridControl c) c.InvalidateVisual();
    }

    /// <summary>根据 ColorHint / Palette / 主题综合解析该项主数字的画笔。</summary>
    public Brush ResolveValueBrush(MetricGridItem item)
    {
        if (!string.IsNullOrEmpty(item.ColorHint))
        {
            var b = TryParseBrush(item.ColorHint);
            if (b != null) return b;
        }
        return System.Windows.Media.Brushes.SteelBlue;
    }

    /// <summary>把 "#RRGGBB" / "ARGB" 字符串解析为 SolidColorBrush；失败时返回 null。</summary>
    public static Brush? TryParseBrush(string color)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(color);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch
        {
            return null;
        }
    }
}