using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsageMonitor.Core.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
using Control = System.Windows.Controls.Control;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 度量进度条组控件（REQ-082 SDK v2）。动态 N 条带标签的进度条。
/// <para>
/// 取代主窗口 XAML 中硬编码的"5h 限额 / 本周限额 / 视频赠送"进度条，
/// 接收 <see cref="MetricBarData"/>，渲染为垂直堆叠的进度条组：
/// 左侧标签 + 中间进度条 + 右侧文本 + 底部可选文本。
/// </para>
/// <para>
/// 颜色策略：未设置 <see cref="Palette"/> 时使用主题资源 AccentBrush；
/// 设置 <see cref="Palette"/> 后调用 <c>Palette.GetMetricColor(percent)</c> 按百分比取色。
/// </para>
/// </summary>
public class MetricBarControl : Control
{
    static MetricBarControl()
    {
        // 允许模板参与样式系统
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MetricBarControl),
            new FrameworkPropertyMetadata(typeof(MetricBarControl)));
    }

    /// <summary>度量进度条组数据（REQ-082 SDK v2）。</summary>
    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(IReadOnlyList<MetricBarItem>), typeof(MetricBarControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnBarsChanged));

    /// <summary>数据源：度量进度条项集合（IsVisible=false 的项不渲染）。</summary>
    public IReadOnlyList<MetricBarItem>? Bars
    {
        get => (IReadOnlyList<MetricBarItem>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    /// <summary>色板（可空，null 时控件使用主题 AccentBrush）。</summary>
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(UsageMonitor.Core.Plugins.IChartPalette), typeof(MetricBarControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>当前色板。</summary>
    public UsageMonitor.Core.Plugins.IChartPalette? Palette
    {
        get => (UsageMonitor.Core.Plugins.IChartPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    private static void OnBarsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 触发重渲，控件本身负责根据 Bars 数据可视化
        if (d is MetricBarControl c) c.InvalidateVisual();
    }
}

/// <summary>
/// 单条进度条的用户控件（REQ-082 SDK v2）。由 <see cref="MetricBarControl"/> 内部使用。
/// </summary>
public class MetricBarRowControl : Control
{
    static MetricBarRowControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MetricBarRowControl),
            new FrameworkPropertyMetadata(typeof(MetricBarRowControl)));
    }

    /// <summary>左侧标签。</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(MetricBarRowControl),
        new FrameworkPropertyMetadata(string.Empty));

    /// <summary>左侧标签文本。</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>0-100 进度百分比。</summary>
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(MetricBarRowControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>进度百分比（0-100）。</summary>
    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>右侧文本（重置时间等，可空）。</summary>
    public static readonly DependencyProperty RightTextProperty = DependencyProperty.Register(
        nameof(RightText), typeof(string), typeof(MetricBarRowControl),
        new FrameworkPropertyMetadata(null));

    /// <summary>右侧文本。</summary>
    public string? RightText
    {
        get => (string?)GetValue(RightTextProperty);
        set => SetValue(RightTextProperty, value);
    }

    /// <summary>底部文本（"已用 43%"，可空）。</summary>
    public static readonly DependencyProperty FooterTextProperty = DependencyProperty.Register(
        nameof(FooterText), typeof(string), typeof(MetricBarRowControl),
        new FrameworkPropertyMetadata(null));

    /// <summary>底部文本。</summary>
    public string? FooterText
    {
        get => (string?)GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    /// <summary>颜色提示（null = 由主题/色阶决定）。</summary>
    public static readonly DependencyProperty ColorHintProperty = DependencyProperty.Register(
        nameof(ColorHint), typeof(string), typeof(MetricBarRowControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>颜色提示。</summary>
    public string? ColorHint
    {
        get => (string?)GetValue(ColorHintProperty);
        set => SetValue(ColorHintProperty, value);
    }

    /// <summary>色板（可空）。</summary>
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(UsageMonitor.Core.Plugins.IChartPalette), typeof(MetricBarRowControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>色板。</summary>
    public UsageMonitor.Core.Plugins.IChartPalette? Palette
    {
        get => (UsageMonitor.Core.Plugins.IChartPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>当前进度条颜色（由 ColorHint / Palette / 主题综合决定）。</summary>
    public Brush GetProgressBrush()
    {
        if (!string.IsNullOrEmpty(ColorHint))
        {
            return TryParseBrush(ColorHint) ?? System.Windows.Media.Brushes.SteelBlue;
        }
        if (Palette != null)
        {
            return TryParseBrush(Palette.GetMetricColor(Percent, null)) ?? System.Windows.Media.Brushes.SteelBlue;
        }
        return System.Windows.Media.Brushes.SteelBlue;
    }

    /// <summary>把 "#RRGGBB" / "ARGB" 字符串解析为 SolidColorBrush；失败时返回 null。</summary>
    private static Brush? TryParseBrush(string color)
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