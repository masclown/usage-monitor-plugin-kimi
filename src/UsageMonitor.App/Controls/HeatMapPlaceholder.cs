using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 热力图占位控件。Stage-7 / Stage-8 才填充真实数据，期间该控件
/// 以“热力图占位”文字 + 168 个均匀灰格作为视觉占位，不影响卡片布局。
/// </summary>
public class HeatMapPlaceholder : System.Windows.Controls.UserControl
{
    static HeatMapPlaceholder()
    {
        // 让默认 Style 可以从 Generic.xaml 拉取，确保加入可视树后能正确初始化。
        DefaultStyleKeyProperty.OverrideMetadata(typeof(HeatMapPlaceholder),
            new FrameworkPropertyMetadata(typeof(HeatMapPlaceholder)));
    }

    /// <summary>热力图模拟数据点（0-100），默认 0，控件按 168 个 0 均分演示样式。</summary>
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(HeatMapPlaceholder),
        new FrameworkPropertyMetadata(null));

    /// <summary>行数（默认按用量页约定显示 7 行 × 24 列 = 168 天）。</summary>
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(HeatMapPlaceholder),
        new FrameworkPropertyMetadata(7, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>列数，默认 24。</summary>
    public static readonly DependencyProperty ColsProperty = DependencyProperty.Register(
        nameof(Cols), typeof(int), typeof(HeatMapPlaceholder),
        new FrameworkPropertyMetadata(24, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public int Cols
    {
        get => (int)GetValue(ColsProperty);
        set => SetValue(ColsProperty, value);
    }

    public HeatMapPlaceholder()
    {
        MinHeight = 80;
        MinWidth = 240;
    }
}
