using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 迷你折线图控件
/// - 展示用量历史趋势（X 轴：时间，Y 轴：已用百分比 0-100）
/// - 自动根据最新数据点的百分比切换颜色：低（绿）→ 中（黄）→ 高（红）
/// - 数据源为 IReadOnlyList&lt;double&gt;，通过 Values 依赖属性传入
/// </summary>
public class MiniLineChartControl : FrameworkElement
{
    /// <summary>数据点集合依赖属性</summary>
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(),
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnValuesChanged));

    /// <summary>Y 轴最大值依赖属性（默认 100）</summary>
    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>线宽（像素）</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>低用量颜色（&lt; 60%）</summary>
    public static readonly DependencyProperty LowBrushProperty = DependencyProperty.Register(
        nameof(LowBrush), typeof(Brush), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>中用量颜色（60-85%）</summary>
    public static readonly DependencyProperty MidBrushProperty = DependencyProperty.Register(
        nameof(MidBrush), typeof(Brush), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(Brushes.Goldenrod, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>高用量颜色（&gt; 85%）</summary>
    public static readonly DependencyProperty HighBrushProperty = DependencyProperty.Register(
        nameof(HighBrush), typeof(Brush), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>数据点集合</summary>
    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>Y 轴最大值</summary>
    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary>线宽</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>低用量颜色</summary>
    public Brush LowBrush
    {
        get => (Brush)GetValue(LowBrushProperty);
        set => SetValue(LowBrushProperty, value);
    }

    /// <summary>中用量颜色</summary>
    public Brush MidBrush
    {
        get => (Brush)GetValue(MidBrushProperty);
        set => SetValue(MidBrushProperty, value);
    }

    /// <summary>高用量颜色</summary>
    public Brush HighBrush
    {
        get => (Brush)GetValue(HighBrushProperty);
        set => SetValue(HighBrushProperty, value);
    }

    /// <summary>
    /// 监听集合变化（支持 ObservableCollection 等实现 INotifyCollectionChanged 的源）
    /// </summary>
    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MiniLineChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldIncc)
            oldIncc.CollectionChanged -= control.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newIncc)
            newIncc.CollectionChanged += control.OnCollectionChanged;
        control.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    /// <summary>
    /// 绘制折线图
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var values = Values;
        if (values == null || values.Count < 2) return;

        var max = MaxValue <= 0 ? 100 : MaxValue;
        var padding = StrokeThickness / 2.0;
        var plotWidth = Math.Max(0, width - padding * 2);
        var plotHeight = Math.Max(0, height - padding * 2);

        // X 步长
        var stepX = plotWidth / (values.Count - 1);

        // 计算点坐标
        var figure = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = figure.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                var v = values[i];
                if (v < 0) v = 0;
                if (v > max) v = max;
                var x = padding + i * stepX;
                var y = padding + plotHeight * (1.0 - v / max);
                if (i == 0)
                    ctx.BeginFigure(new Point(x, y), false, false);
                else
                    ctx.LineTo(new Point(x, y), true, false);
            }
        }
        figure.Freeze();

        // 根据最新点确定颜色
        var brush = SelectBrush(values[values.Count - 1]);
        var pen = new Pen(brush, StrokeThickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (pen.CanFreeze) pen.Freeze();

        dc.DrawGeometry(null, pen, figure);

        // 在最新点绘制一个小圆点强调
        var lastV = values[values.Count - 1];
        if (lastV < 0) lastV = 0;
        if (lastV > max) lastV = max;
        var lastX = padding + (values.Count - 1) * stepX;
        var lastY = padding + plotHeight * (1.0 - lastV / max);
        var dotRadius = Math.Max(1.5, StrokeThickness);
        dc.DrawEllipse(brush, null, new Point(lastX, lastY), dotRadius, dotRadius);
    }

    /// <summary>
    /// 根据当前百分比选择画笔
    /// </summary>
    private Brush SelectBrush(double percent)
    {
        if (percent >= 85) return HighBrush;
        if (percent >= 60) return MidBrush;
        return LowBrush;
    }
}
