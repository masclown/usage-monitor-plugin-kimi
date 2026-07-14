using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Typeface = System.Windows.Media.Typeface;
using FlowDirection = System.Windows.FlowDirection;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 圆环进度图控件
/// - 展示当前用量百分比（0-100）
/// - 视觉风格：暗色 UI 中的"挖空"效果，浅色背景环 + 深色进度条
/// - 中心用 FormattedText 绘制纯数字百分比（不含 % 符号，整数省略小数）
/// - 颜色根据 WarningThreshold / DangerThreshold 切换（默认 60/85）
/// </summary>
public class RingChartControl : FrameworkElement
{
    /// <summary>百分比（0-100）</summary>
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>控件尺寸（直径，默认 44）</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(44.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>环线粗细（默认 5）</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>背景轨道颜色（浅色）</summary>
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3F)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>进度条颜色（深色主色，低于警告阈值时使用）</summary>
    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>警告色（达到 WarningThreshold 时使用）</summary>
    public static readonly DependencyProperty WarningBrushProperty = DependencyProperty.Register(
        nameof(WarningBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>危险色（达到 DangerThreshold 时使用）</summary>
    public static readonly DependencyProperty DangerBrushProperty = DependencyProperty.Register(
        nameof(DangerBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>警告阈值（默认 60）</summary>
    public static readonly DependencyProperty WarningThresholdProperty = DependencyProperty.Register(
        nameof(WarningThreshold), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>危险阈值（默认 85）</summary>
    public static readonly DependencyProperty DangerThresholdProperty = DependencyProperty.Register(
        nameof(DangerThreshold), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(85.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>百分比</summary>
    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>直径</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>环线粗细</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>背景轨道颜色</summary>
    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>进度条颜色（主色）</summary>
    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    /// <summary>警告色</summary>
    public Brush WarningBrush
    {
        get => (Brush)GetValue(WarningBrushProperty);
        set => SetValue(WarningBrushProperty, value);
    }

    /// <summary>危险色</summary>
    public Brush DangerBrush
    {
        get => (Brush)GetValue(DangerBrushProperty);
        set => SetValue(DangerBrushProperty, value);
    }

    /// <summary>警告阈值</summary>
    public double WarningThreshold
    {
        get => (double)GetValue(WarningThresholdProperty);
        set => SetValue(WarningThresholdProperty, value);
    }

    /// <summary>危险阈值</summary>
    public double DangerThreshold
    {
        get => (double)GetValue(DangerThresholdProperty);
        set => SetValue(DangerThresholdProperty, value);
    }

    /// <summary>
    /// 绘制圆环进度图：背景轨道 + 前景进度弧 + 中心百分比文字
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        var size = Size <= 0 ? 44 : Size;
        var stroke = StrokeThickness <= 0 ? 5 : StrokeThickness;
        var center = new Point(size / 2.0, size / 2.0);
        var radius = size / 2.0 - stroke / 2.0;

        if (radius <= 0) return;

        // 1. 绘制整圆背景轨道（浅色）
        dc.DrawEllipse(null, new Pen(TrackBrush, stroke), center, radius, radius);

        // 2. 进度百分比（0-1）
        var percent = Percent;
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        if (percent <= 0)
        {
            // 仅 0% 时只画中心文字
            DrawCenterText(dc, size, 0, ProgressBrush);
            return;
        }

        // 3. 进度弧（从 12 点钟方向开始，顺时针）
        var progressGeometry = CreateArcGeometry(center, radius, percent / 100.0);
        var progressBrush = SelectBrush(percent);
        var pen = new Pen(progressBrush, stroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (pen.CanFreeze) pen.Freeze();
        dc.DrawGeometry(null, pen, progressGeometry);

        // 4. 中心文字
        DrawCenterText(dc, size, percent, progressBrush);
    }

    /// <summary>
    /// 创建从顶部（-90°）开始、按百分比顺时针的圆弧
    /// </summary>
    private static PathGeometry CreateArcGeometry(Point center, double radius, double fraction)
    {
        var angle = Math.Min(360.0, fraction * 360.0);
        // 起点：12 点钟方向
        var start = new Point(center.X, center.Y - radius);

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };

        if (angle >= 360.0)
        {
            // 完整圆：拆为两段大弧（>180°）
            var mid = new Point(center.X, center.Y + radius);
            figure.Segments.Add(new ArcSegment(mid, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
            figure.Segments.Add(new ArcSegment(start, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
        }
        else
        {
            var radians = (angle - 90) * Math.PI / 180.0;
            var end = new Point(
                center.X + radius * Math.Cos(radians),
                center.Y + radius * Math.Sin(radians));
            var isLargeArc = angle > 180.0;
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// 在圆心绘制纯数字百分比（不含 % 符号）
    /// </summary>
    private void DrawCenterText(DrawingContext dc, double size, double percent, Brush brush)
    {
        var text = percent == Math.Floor(percent)
            ? percent.ToString("0", CultureInfo.InvariantCulture)
            : percent.ToString("0.#", CultureInfo.InvariantCulture);

        var fontSize = Math.Max(8.0, size * 0.36);
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var origin = new Point((size - formatted.Width) / 2.0, (size - formatted.Height) / 2.0);
        dc.DrawText(formatted, origin);
    }

    /// <summary>
    /// 根据百分比选择画笔
    /// </summary>
    private Brush SelectBrush(double percent)
    {
        if (percent >= DangerThreshold) return DangerBrush;
        if (percent >= WarningThreshold) return WarningBrush;
        return ProgressBrush;
    }
}
