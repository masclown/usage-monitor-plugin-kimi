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
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 柱状图控件（对应参考图"消费金额 / Tokens / 请求量"形态）。
/// <para>
/// - 接收 <see cref="Values"/> 数值序列，自动或按 <see cref="MaxValue"/> 归一化
/// - 渐变柱体（BarBrush，一般传主题 AccentGradientBrush）+ 圆角顶 + 轻网格
/// - 支持 hover：高亮所在柱并显示数值气泡（自绘，主题感知）
/// - 数据经依赖属性传入，真实数据接入后无需改动本控件
/// </para>
/// </summary>
public class BarChartControl : FrameworkElement, IHoverTooltipProvider
{
    // req-067 B23：Typeface 缓存，避免每次 OnRender 重复创建
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Provider 短名称，用于 tooltip 标题</summary>
    public static readonly DependencyProperty ProviderNameProperty = DependencyProperty.Register(
        nameof(ProviderName), typeof(string), typeof(BarChartControl),
        new FrameworkPropertyMetadata(string.Empty));

    /// <summary>数据单位，例如 %、tokens</summary>
    public static readonly DependencyProperty ValueUnitProperty = DependencyProperty.Register(
        nameof(ValueUnit), typeof(string), typeof(BarChartControl),
        new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Provider 短名称</summary>
    public string ProviderName
    {
        get => (string)GetValue(ProviderNameProperty);
        set => SetValue(ProviderNameProperty, value);
    }

    /// <summary>数据单位</summary>
    public string ValueUnit
    {
        get => (string)GetValue(ValueUnitProperty);
        set => SetValue(ValueUnitProperty, value);
    }

    /// <summary>柱状数据序列</summary>
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(BarChartControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(),
            FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    /// <summary>Y 轴最大值（&lt;=0 时按数据最大值自动放大 15%）</summary>
    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(BarChartControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>柱体画笔。req-074：默认改主题资源 AccentBrush。</summary>
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(BarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>强调高亮的柱索引（-1 表示无）</summary>
    public static readonly DependencyProperty HighlightIndexProperty = DependencyProperty.Register(
        nameof(HighlightIndex), typeof(int), typeof(BarChartControl),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>网格线颜色。req-074：默认改主题资源 ChartGridBrush。</summary>
    public static readonly DependencyProperty GridLineBrushProperty = DependencyProperty.Register(
        nameof(GridLineBrush), typeof(Brush), typeof(BarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>轴/文本颜色。req-074：默认改主题资源 TextSecondaryBrush。</summary>
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(BarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public int HighlightIndex
    {
        get => (int)GetValue(HighlightIndexProperty);
        set => SetValue(HighlightIndexProperty, value);
    }

    public Brush GridLineBrush
    {
        get => (Brush)GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    private int _hoverIndex = -1;
    private double _left, _top, _plotW, _plotH;
    private int _count;

    /// <summary>req-063 B9：跟踪当前订阅的集合，用于 OnUnloaded 时解绑。</summary>
    private INotifyCollectionChanged? _subscribed;

    /// <summary>
    /// 构造函数。req-074：从主题资源解析 Brush 默认值。
    /// </summary>
    public BarChartControl()
    {
        MinHeight = 90;
        MinWidth = 160;
        Focusable = true;

        // req-074：从主题资源解析 Brush 默认值
        if (BarBrush == null)
            SetValue(BarBrushProperty, TryFindResource("AccentBrush") as Brush ?? Brushes.OrangeRed);
        if (GridLineBrush == null)
            SetValue(GridLineBrushProperty, TryFindResource("ChartGridBrush") as Brush ?? Brushes.DimGray);
        if (TextBrush == null)
            SetValue(TextBrushProperty, TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);

        // req-063 B9：订阅 Unloaded 事件，控件卸载时解绑 CollectionChanged
        Unloaded += OnControlUnloaded;
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (BarChartControl)d;
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
        c._hoverIndex = -1;
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

    /// <summary>鼠标移动：把 X 映射为柱索引并高亮。</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_count < 1 || _plotW <= 0) return;
        var x = e.GetPosition(this).X;
        var idx = (int)((x - _left) / (_plotW / _count));
        idx = Math.Max(0, Math.Min(_count - 1, idx));
        if (idx != _hoverIndex) { _hoverIndex = idx; InvalidateVisual(); }
        if (TryGetTooltip(e.GetPosition(this), out var data))
            HoverTooltipPresenter.Show(this, data);
    }

    /// <summary>鼠标移出：清除高亮。</summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1) { _hoverIndex = -1; InvalidateVisual(); }
        HoverTooltipPresenter.Hide(this);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        // 透明底捕获 hit-test
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var values = Values;
        if (values == null || values.Count == 0)
        {
            var t0 = MakeText("暂无数据", TextBrush, 13);
            dc.DrawText(t0, new Point((width - t0.Width) / 2, (height - t0.Height) / 2));
            return;
        }

        double padTop = 10, padBottom = 8, padLeft = 6, padRight = 6;
        var plotW = Math.Max(0, width - padLeft - padRight);
        var plotH = Math.Max(0, height - padTop - padBottom);
        var baseline = padTop + plotH;

        double max = MaxValue > 0 ? MaxValue : values.Max();
        if (max <= 0) max = 1;
        if (MaxValue <= 0) max *= 1.15;

        _left = padLeft; _top = padTop; _plotW = plotW; _plotH = plotH; _count = values.Count;

        // 基线
        var axisPen = new Pen(GridLineBrush, 1.0);
        if (axisPen.CanFreeze) axisPen.Freeze();
        dc.DrawLine(axisPen, new Point(padLeft, baseline), new Point(padLeft + plotW, baseline));

        double slot = plotW / values.Count;
        double barW = Math.Max(2, slot * 0.6);
        double radius = Math.Min(barW / 2.0, 4.0);

        var dimFill = MakeTranslucent(BarBrush, 0x66);
        for (int i = 0; i < values.Count; i++)
        {
            var v = Math.Max(0, values[i]);
            double h = plotH * (v / max);
            double cx = padLeft + slot * i + slot / 2.0;
            double bx = cx - barW / 2.0;
            double by = baseline - h;
            var rect = new Rect(bx, by, barW, Math.Max(1, h));

            bool emphasized = i == HighlightIndex || i == _hoverIndex;
            Brush fill = emphasized ? BarBrush : dimFill;
            dc.DrawRoundedRectangle(fill, null, rect, radius, radius);
        }

        if (_hoverIndex >= 0 && _hoverIndex < values.Count)
            DrawHoverBubble(dc, values[_hoverIndex], padLeft + slot * _hoverIndex + slot / 2.0, padTop, plotW + padLeft);
    }

    /// <summary>绘制 hover 数值气泡。</summary>
    private void DrawHoverBubble(DrawingContext dc, double value, double cx, double top, double rightEdge)
    {
        var tipBg = FindBrush("TooltipBackgroundBrush", Color.FromRgb(0x1F, 0x24, 0x30));
        var tipFg = FindBrush("TooltipForegroundBrush", Color.FromRgb(0xF1, 0xF5, 0xF9));
        var t = MakeText(FormatValue(value), tipFg, 11.5);
        double padX = 9, padY = 5;
        double bw = t.Width + padX * 2, bh = t.Height + padY * 2;
        double bx = cx - bw / 2.0;
        if (bx < 2) bx = 2;
        if (bx + bw > rightEdge) bx = rightEdge - bw;
        double by = top;
        dc.DrawRoundedRectangle(tipBg, null, new Rect(bx, by, bw, bh), 7, 7);
        dc.DrawText(t, new Point(bx + padX, by + padY));
    }

    /// <summary>键盘方向键浏览柱状数据，Enter 重显当前柱提示。</summary>
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Values == null || Values.Count == 0) return;
        var next = _hoverIndex < 0 ? 0 : _hoverIndex;
        next = e.Key switch
        {
            System.Windows.Input.Key.Left or System.Windows.Input.Key.Up => Math.Max(0, next - 1),
            System.Windows.Input.Key.Right or System.Windows.Input.Key.Down => Math.Min(Values.Count - 1, next + 1),
            System.Windows.Input.Key.Home => 0,
            System.Windows.Input.Key.End => Values.Count - 1,
            System.Windows.Input.Key.Enter => next,
            _ => -1
        };
        if (next < 0) return;
        _hoverIndex = next;
        if (TryGetTooltip(new Point(_left + (_plotW <= 0 ? 0 : (next + 0.5) * _plotW / Values.Count), 0), out var data))
            HoverTooltipPresenter.Show(this, data);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>将控件内部坐标映射为命中的柱状数据点。</summary>
    public bool TryGetTooltip(Point position, out HoverTooltipData data)
    {
        data = default!;
        if (Values == null || Values.Count == 0 || _plotW <= 0) return false;
        var index = Math.Clamp((int)((position.X - _left) / (_plotW / Values.Count)), 0, Values.Count - 1);
        var unit = string.IsNullOrWhiteSpace(ValueUnit) ? string.Empty : $" {ValueUnit}";
        var provider = string.IsNullOrWhiteSpace(ProviderName) ? "用量" : ProviderName;
        data = new HoverTooltipData($"{provider} · 第 {index + 1} 柱", $"{Values[index]:0.##}{unit}", "时间标签：第 " + (index + 1) + " 个数据点");
        return true;
    }

    /// <summary>数值格式化（大数用 K/M 简写）。</summary>
    private static string FormatValue(double v)
    {
        if (v >= 1_000_000) return $"{v / 1_000_000.0:0.#}M";
        if (v >= 1_000) return $"{v / 1_000.0:0.#}K";
        return v.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private FormattedText MakeText(string text, Brush brush, double size)
        => new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            LabelTypeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush FindBrush(string key, Color fallback)
    {
        if (TryFindResource(key) is Brush b) return b;
        var f = new SolidColorBrush(fallback); f.Freeze(); return f;
    }

    private static Brush MakeTranslucent(Brush source, byte alpha)
    {
        if (source is SolidColorBrush scb)
        {
            var c = scb.Color;
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); b.Freeze(); return b;
        }
        if (source is LinearGradientBrush lg)
        {
            var clone = lg.Clone();
            clone.Opacity = alpha / 255.0;
            clone.Freeze();
            return clone;
        }
        var fb = new SolidColorBrush(Color.FromArgb(alpha, 0x94, 0xA3, 0xB8)); fb.Freeze(); return fb;
    }
}
