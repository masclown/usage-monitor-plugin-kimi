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
/// 迷你折线图控件（任务栏 / 卡片使用）。
/// - 展示用量历史趋势（X 轴：时间，Y 轴：已用百分比 0-100）
/// - 自动根据最新数据点的百分比切换颜色：低绿 → 注意金(#facd14) → 中橙 → 高红，
///   档位与配色统一取自 <see cref="UsageMonitor.App.Helpers.UsageTierScale"/>（单一数据源）。
/// - 现代化：折线下方绘制同色低透明渐变面积填充，最新点带柔和发光圆点
/// - 数据源为 IReadOnlyList&lt;double&gt;，通过 Values 依赖属性传入
/// 说明：下方 Low/Mid/High 画笔依赖属性已由 UsageTierScale 接管选色，保留仅为兼容既有 XAML 绑定，不再参与实际取色。
/// </summary>
public class MiniLineChartControl : FrameworkElement, IHoverTooltipProvider
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
        new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Provider 短名称，用于 tooltip 标题</summary>
    public static readonly DependencyProperty ProviderNameProperty = DependencyProperty.Register(
        nameof(ProviderName), typeof(string), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(string.Empty));

    /// <summary>数据单位，例如 %、tokens</summary>
    public static readonly DependencyProperty ValueUnitProperty = DependencyProperty.Register(
        nameof(ValueUnit), typeof(string), typeof(MiniLineChartControl),
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

    /// <summary>当前 hover 数据点索引</summary>
    private int _hoverIndex = -1;
    private double _plotLeft;
    private double _plotWidth;

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
    /// 订阅全局用量色阶变更事件，色阶变了就重绘。
    /// 只订阅一次（静态事件 / -= 后 +=），避免在多窗口场景中重复回调。
    /// </summary>
    private static int _tierChangedSubscribed;

    /// <summary>控件构造：启用鼠标与键盘焦点，以便通过 Tab 和方向键浏览数据点。</summary>
    public MiniLineChartControl()
    {
        Focusable = true;
        if (System.Threading.Interlocked.Exchange(ref _tierChangedSubscribed, 1) == 0)
        {
            UsageMonitor.App.Helpers.UsageTierScale.TierChanged += OnTierChangedStatic;
        }
    }

    /// <summary>档位表刷新回调（静态）：色阶变了就重绘所有迷你折线图实例。</summary>
    private static void OnTierChangedStatic(object? sender, EventArgs e)
    {
        // 静态回调只通知一次；具体实例通过遍历无效化（在 WinForms/WPF 混合项目中无需精细化，触发 Application 重绘即可）。
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var w in System.Windows.Application.Current.Windows)
            {
                if (w is System.Windows.Window win)
                    win.InvalidateVisual();
            }
        });
    }

    /// <summary>
    /// 绘制折线图：渐变面积填充 + 折线 + 最新点发光圆点
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var values = Values;
        if (values == null || values.Count < 2) return;

        var max = MaxValue <= 0 ? 100 : MaxValue;
        var padding = StrokeThickness / 2.0 + 1.0;
        var plotWidth = Math.Max(0, width - padding * 2);
        var plotHeight = Math.Max(0, height - padding * 2);
        var baseline = padding + plotHeight;

        // X 步长
        var stepX = plotWidth / (values.Count - 1);

        _plotLeft = padding;
        _plotWidth = plotWidth;

        // 计算所有点坐标
        var points = new Point[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            var v = values[i];
            if (v < 0) v = 0;
            if (v > max) v = max;
            points[i] = new Point(padding + i * stepX, padding + plotHeight * (1.0 - v / max));
        }

        var brush = SelectBrush(values[values.Count - 1]);

        // 1) 面积填充（折线 → 右下 → 左下 闭合），同色低透明
        var areaFill = MakeTranslucent(brush, 0x33);
        var area = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, baseline), true, true);
            ctx.LineTo(points[0], true, false);
            for (int i = 1; i < points.Length; i++)
                ctx.LineTo(points[i], true, false);
            ctx.LineTo(new Point(points[^1].X, baseline), true, false);
        }
        area.Freeze();
        dc.DrawGeometry(areaFill, null, area);

        // 2) 折线
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            for (int i = 1; i < points.Length; i++)
                ctx.LineTo(points[i], true, false);
        }
        line.Freeze();
        var pen = new Pen(brush, StrokeThickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (pen.CanFreeze) pen.Freeze();
        dc.DrawGeometry(null, pen, line);

        // 3) 最新点发光圆点：外圈低透明 + 内圈实心
        var last = points[^1];
        var dot = Math.Max(1.6, StrokeThickness);
        dc.DrawEllipse(MakeTranslucent(brush, 0x40), null, last, dot * 2.2, dot * 2.2);
        dc.DrawEllipse(brush, null, last, dot, dot);
    }

    /// <summary>鼠标移动时命中最近数据点并显示统一 tooltip。</summary>
    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (TryGetTooltip(e.GetPosition(this), out var data))
        {
            var index = GetIndex(e.GetPosition(this).X);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                InvalidateVisual();
            }
            HoverTooltipPresenter.Show(this, data);
        }
    }

    /// <summary>鼠标离开图表区域后关闭 tooltip 并清除高亮点。</summary>
    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        HoverTooltipPresenter.Hide(this);
        InvalidateVisual();
    }

    /// <summary>键盘方向键浏览折线数据点，Enter 重显当前点提示。</summary>
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
        if (TryGetTooltip(new Point(_plotLeft + (_plotWidth <= 0 ? 0 : next * _plotWidth / Math.Max(1, Values.Count - 1)), 0), out var data))
            HoverTooltipPresenter.Show(this, data);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>将控件内部坐标映射到最近的折线数据点。</summary>
    public bool TryGetTooltip(Point position, out HoverTooltipData data)
    {
        data = default!;
        if (Values == null || Values.Count == 0) return false;
        var index = GetIndex(position.X);
        var value = Values[index];
        var unit = string.IsNullOrWhiteSpace(ValueUnit) ? string.Empty : $" {ValueUnit}";
        var provider = string.IsNullOrWhiteSpace(ProviderName) ? "用量" : ProviderName;
        data = new HoverTooltipData($"{provider} · 第 {index + 1} 点", $"{value:0.##}{unit}", "时间标签：第 " + (index + 1) + " 个数据点");
        return true;
    }

    /// <summary>根据 X 坐标计算最近数据点索引。</summary>
    private int GetIndex(double x)
    {
        if (Values == null || Values.Count <= 1 || _plotWidth <= 0) return 0;
        var ratio = Math.Clamp((x - _plotLeft) / _plotWidth, 0, 1);
        return Math.Clamp((int)Math.Round(ratio * (Values.Count - 1)), 0, Values.Count - 1);
    }

    /// <summary>
    /// 根据当前百分比选择画笔：档位与颜色统一由 <see cref="UsageMonitor.App.Helpers.UsageTierScale"/>
    /// 定义（低绿 / 注意金 #facd14 / 中橙 / 高红），与主界面进度条保持一致。
    /// </summary>
    private Brush SelectBrush(double percent)
        => UsageMonitor.App.Helpers.UsageTierScale.ResolveBrush(percent);

    /// <summary>
    /// 从一个 Brush 派生出指定透明度的同色画笔（用于面积填充/发光）。
    /// 非 SolidColorBrush 时回退为半透明灰。
    /// </summary>
    private static Brush MakeTranslucent(Brush source, byte alpha)
    {
        if (source is SolidColorBrush scb)
        {
            var c = scb.Color;
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        var fallback = new SolidColorBrush(Color.FromArgb(alpha, 0x94, 0xA3, 0xB8));
        fallback.Freeze();
        return fallback;
    }
}
