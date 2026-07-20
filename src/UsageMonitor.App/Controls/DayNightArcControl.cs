using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 日月"编程时段"弧线图（对应参考图"编程时段"）。
/// <para>
/// 以正弦日照曲线表现 24 小时活跃分布：06:00 日出（横线）→ 12:00 正午（最高）→
/// 18:00 日落（横线）→ 24:00 午夜（最低），太阳在顶、月亮在底。
/// 24 个小时点沿曲线分布，点的大小/颜色由 <see cref="HourlyActivity"/>（24 个 0-1 值）表示活跃度。
/// </para>
/// </summary>
public class DayNightArcControl : FrameworkElement
{
    // req-067 B23：Typeface 缓存，避免每次 OnRender 重复创建
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>24 小时活跃度（每项 0-1，index=0 表示 0 点）</summary>
    public static readonly DependencyProperty HourlyActivityProperty = DependencyProperty.Register(
        nameof(HourlyActivity), typeof(IReadOnlyList<double>), typeof(DayNightArcControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(),
            FrameworkPropertyMetadataOptions.AffectsRender, OnActivityChanged));

    /// <summary>曲线 / 非活跃点颜色（一般传主题 TextTertiaryBrush / DividerBrush）</summary>
    /// <summary>req-074：轨道色，默认主题 TrackBrush。</summary>
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(DayNightArcControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>强调色（太阳/高活跃点，一般传主题 AccentBrush）</summary>
    /// <summary>req-074：强调色，默认主题 AccentBrush。</summary>
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(DayNightArcControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>刻度文本颜色</summary>
    /// <summary>req-074：文本色，默认主题 TextSecondaryBrush。</summary>
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(DayNightArcControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> HourlyActivity
    {
        get => (IReadOnlyList<double>)GetValue(HourlyActivityProperty);
        set => SetValue(HourlyActivityProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>
    /// 构造函数。req-074：从主题资源解析 Brush 默认值。
    /// </summary>
    public DayNightArcControl()
    {
        MinHeight = 120;
        MinWidth = 240;

        // req-074：从主题资源解析 Brush 默认值
        if (TrackBrush == null)
            SetValue(TrackBrushProperty, TryFindResource("TrackBrush") as Brush ?? Brushes.Gray);
        if (AccentBrush == null)
            SetValue(AccentBrushProperty, TryFindResource("AccentBrush") as Brush ?? Brushes.OrangeRed);
        if (TextBrush == null)
            SetValue(TextBrushProperty, TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);

        // req-063 B9：订阅 Unloaded 事件，控件卸载时解绑 CollectionChanged
        Unloaded += OnControlUnloaded;
    }

    /// <summary>req-063 B9：跟踪当前订阅的集合，用于 OnUnloaded 时解绑。</summary>
    private INotifyCollectionChanged? _subscribed;

    private static void OnActivityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (DayNightArcControl)d;
        if (e.OldValue is INotifyCollectionChanged oldIncc) oldIncc.CollectionChanged -= c.OnItemsChanged;
        if (e.NewValue is INotifyCollectionChanged newIncc)
        {
            newIncc.CollectionChanged += c.OnItemsChanged;
            c._subscribed = newIncc;
        }
        else
        {
            c._subscribed = null;
        }
        c.InvalidateVisual();
    }

    /// <summary>req-063 B9：控件卸载时解绑 CollectionChanged，防止内存泄漏。</summary>
    private void OnControlUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed != null)
        {
            _subscribed.CollectionChanged -= OnItemsChanged;
            _subscribed = null;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double padX = 18, padTop = 12, padBottom = 24;
        double plotW = Math.Max(0, width - padX * 2);
        double plotH = Math.Max(0, height - padTop - padBottom);
        double midY = padTop + plotH * 0.52;
        double amp = plotH * 0.40;

        // t 为从 06:00 起算的小时（0..24），x 线性映射；elevation = sin(t/24*2π)
        double X(double t) => padX + (t / 24.0) * plotW;
        double Y(double t) => midY - Math.Sin(t / 24.0 * 2 * Math.PI) * amp;

        // 1) 地平线（虚线）
        var horizonPen = new Pen(MakeTranslucent(TrackBrush, 0x66), 1.0)
        { DashStyle = new DashStyle(new[] { 2.0, 3.0 }, 0) };
        if (horizonPen.CanFreeze) horizonPen.Freeze();
        dc.DrawLine(horizonPen, new Point(padX, midY), new Point(padX + plotW, midY));

        // 2) 日照正弦曲线
        var curve = new StreamGeometry();
        using (var ctx = curve.Open())
        {
            ctx.BeginFigure(new Point(X(0), Y(0)), false, false);
            for (double t = 0.5; t <= 24.0001; t += 0.5)
                ctx.LineTo(new Point(X(t), Y(t)), true, true);
        }
        curve.Freeze();
        var curvePen = new Pen(MakeTranslucent(TrackBrush, 0xAA), 1.6)
        { LineJoin = PenLineJoin.Round };
        if (curvePen.CanFreeze) curvePen.Freeze();
        dc.DrawGeometry(null, curvePen, curve);

        // 3) 24 个小时点（clock hour h → t=(h-6+24)%24）
        var activity = HourlyActivity;
        var mutedDot = MakeTranslucent(TrackBrush, 0x99);
        for (int h = 0; h < 24; h++)
        {
            double t = ((h - 6) % 24 + 24) % 24;
            double a = (activity != null && h < activity.Count) ? Clamp01(activity[h]) : 0;
            double r = 2.2 + a * 4.0;
            var p = new Point(X(t), Y(t));
            bool active = a >= 0.55;
            if (active)
            {
                dc.DrawEllipse(MakeTranslucent(AccentBrush, 0x40), null, p, r + 2.5, r + 2.5);
                dc.DrawEllipse(AccentBrush, null, p, r, r);
            }
            else
            {
                dc.DrawEllipse(mutedDot, null, p, r, r);
            }
        }

        // 4) 太阳（正午 t=6，顶部）：发光实心圆 + 短射线
        var sun = new Point(X(6), Y(6));
        dc.DrawEllipse(MakeTranslucent(AccentBrush, 0x33), null, sun, 9, 9);
        dc.DrawEllipse(AccentBrush, null, sun, 4.5, 4.5);
        var rayPen = new Pen(AccentBrush, 1.4);
        if (rayPen.CanFreeze) rayPen.Freeze();
        for (int k = 0; k < 8; k++)
        {
            double ang = k * Math.PI / 4;
            var d1 = new Point(sun.X + Math.Cos(ang) * 7, sun.Y + Math.Sin(ang) * 7);
            var d2 = new Point(sun.X + Math.Cos(ang) * 10, sun.Y + Math.Sin(ang) * 10);
            dc.DrawLine(rayPen, d1, d2);
        }

        // 5) 月亮（午夜 t=18，底部）：环形轮廓
        var moon = new Point(X(18), Y(18));
        var moonPen = new Pen(MakeTranslucent(TrackBrush, 0xCC), 1.6);
        if (moonPen.CanFreeze) moonPen.Freeze();
        dc.DrawEllipse(null, moonPen, moon, 4.5, 4.5);

        // 6) 时刻标签：06:00 / 12:00 / 18:00 / 24:00 / 06:00
        DrawLabel(dc, "06:00", X(0), height - padBottom + 4, true);
        DrawLabel(dc, "12:00", X(6), height - padBottom + 4, false);
        DrawLabel(dc, "18:00", X(12), height - padBottom + 4, false);
        DrawLabel(dc, "24:00", X(18), height - padBottom + 4, false);
        DrawLabel(dc, "06:00", X(24), height - padBottom + 4, false);
    }

    /// <summary>在指定 x 下方居中绘制刻度文本（首个左对齐避免越界）。</summary>
    private void DrawLabel(DrawingContext dc, string text, double x, double y, bool leftAlign)
    {
        var t = new FormattedText(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            LabelTypeface,
            10, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double ox = leftAlign ? x : x - t.Width / 2.0;
        dc.DrawText(t, new Point(ox, y));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static Brush MakeTranslucent(Brush source, byte alpha)
    {
        if (source is SolidColorBrush scb)
        {
            var c = scb.Color;
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); b.Freeze(); return b;
        }
        var fb = new SolidColorBrush(Color.FromArgb(alpha, 0x94, 0xA3, 0xB8)); fb.Freeze(); return fb;
    }
}
