using System.Globalization;
using System.Windows;
using System.Windows.Media;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.MiniChart;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 迷你时序图表统一控件（任务栏用）：通过 <see cref="ChartMode"/> 区分柱状 / 折线 / 面积三种渲染模式。
/// <para>
/// 设计动机：三种迷你时序图共享数据归一化、hover 命中、气泡绘制逻辑，仅绘制路径不同，
/// 合并为单一控件避免三套重复代码，且方便 DataTemplate 间统一绑定。
/// </para>
/// <para>
/// 渲染规格：
/// <list type="bullet">
/// <item>Bar：等宽圆角柱体（间距 1px），hover 柱高亮 + 气泡</item>
/// <item>Line：1.5px 折线 + 最新点发光圆点，hover 点高亮 + 气泡</item>
/// <item>Area：折线 + 底部渐变填充（画刷 30% → 透明），hover 点高亮 + 气泡</item>
/// <item>空数据：居中显示 "--" 占位</item>
/// </list>
/// </para>
/// <para>
/// 数据契约：X 轴为日期/时间（<see cref="Labels"/>），Y 轴为数值/百分比（<see cref="Values"/> + <see cref="ValueKind"/>）。
/// 滚轮切换数据组由宿主模板（TaskbarWindow PreviewMouseWheel）驱动，本控件不处理滚轮。
/// </para>
/// </summary>
public class MiniSeriesChartControl : FrameworkElement
{
    // req-067 B23：Typeface 缓存，避免每次 OnRender 重复创建
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // =====================================================================
    // 依赖属性
    // =====================================================================

    /// <summary>渲染模式（MiniBarChart / MiniLineChart / MiniAreaChart）。</summary>
    public static readonly DependencyProperty ChartModeProperty = DependencyProperty.Register(
        nameof(ChartMode), typeof(MiniChartKind), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(MiniChartKind.MiniLineChart, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Y 轴数值序列。</summary>
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>X 轴标签（日期短格式，供 hover 气泡显示；与 Values 等长）。</summary>
    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(Array.Empty<string>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Y 轴上限（&lt;=0 时按数据最大值自适应）。</summary>
    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Y 轴数值类型（百分比 / 绝对数值），影响气泡格式化。</summary>
    public static readonly DependencyProperty ValueKindProperty = DependencyProperty.Register(
        nameof(ValueKind), typeof(MiniSeriesValueKind), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(MiniSeriesValueKind.Percent, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>数值单位（如 "credits"、"次"、"万tokens"；Percent 模式自动补 %）。</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>柱体/折线/面积主画刷（主题感知）。</summary>
    public static readonly DependencyProperty SeriesBrushProperty = DependencyProperty.Register(
        nameof(SeriesBrush), typeof(Brush), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>hover 高亮点索引（-1 = 无高亮）。</summary>
    public static readonly DependencyProperty HighlightIndexProperty = DependencyProperty.Register(
        nameof(HighlightIndex), typeof(int), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>占位文本画刷（空数据时 "--" 的颜色）。</summary>
    public static readonly DependencyProperty PlaceholderBrushProperty = DependencyProperty.Register(
        nameof(PlaceholderBrush), typeof(Brush), typeof(MiniSeriesChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    // =====================================================================
    // CLR 包装
    // =====================================================================

    /// <summary>渲染模式（MiniBarChart / MiniLineChart / MiniAreaChart）。</summary>
    public MiniChartKind ChartMode
    {
        get => (MiniChartKind)GetValue(ChartModeProperty);
        set => SetValue(ChartModeProperty, value);
    }

    /// <summary>Y 轴数值序列。</summary>
    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>X 轴标签（日期短格式，供 hover 气泡显示）。</summary>
    public IReadOnlyList<string> Labels
    {
        get => (IReadOnlyList<string>)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>Y 轴上限（&lt;=0 时按数据最大值自适应）。</summary>
    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary>Y 轴数值类型（百分比 / 绝对数值）。</summary>
    public MiniSeriesValueKind ValueKind
    {
        get => (MiniSeriesValueKind)GetValue(ValueKindProperty);
        set => SetValue(ValueKindProperty, value);
    }

    /// <summary>数值单位。</summary>
    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>柱体/折线/面积主画刷（主题感知）。</summary>
    public Brush SeriesBrush
    {
        get => (Brush)GetValue(SeriesBrushProperty);
        set => SetValue(SeriesBrushProperty, value);
    }

    /// <summary>hover 高亮点索引（-1 = 无高亮）。</summary>
    public int HighlightIndex
    {
        get => (int)GetValue(HighlightIndexProperty);
        set => SetValue(HighlightIndexProperty, value);
    }

    /// <summary>占位文本画刷。</summary>
    public Brush PlaceholderBrush
    {
        get => (Brush)GetValue(PlaceholderBrushProperty);
        set => SetValue(PlaceholderBrushProperty, value);
    }

    // =====================================================================
    // 内部状态
    // =====================================================================

    /// <summary>当前 hover 命中的点索引（-1 = 未命中）。</summary>
    private int _hoverIndex = -1;

    /// <summary>绘图区布局缓存（供 OnMouseMove 命中计算）。</summary>
    private double _plotLeft, _plotWidth;
    private int _pointCount;

    /// <summary>
    /// 构造函数：从主题资源解析画刷默认值。
    /// </summary>
    public MiniSeriesChartControl()
    {
        // 主题感知画刷默认值（模板未绑定时兜底）
        if (SeriesBrush == null)
            SetValue(SeriesBrushProperty, TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue);
        if (PlaceholderBrush == null)
            SetValue(PlaceholderBrushProperty, TryFindResource("TextTertiaryBrush") as Brush ?? Brushes.Gray);
    }

    // =====================================================================
    // 鼠标交互
    // =====================================================================

    /// <summary>鼠标移动：命中最近数据点 → 高亮 + 气泡。</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pointCount < 1 || _plotWidth <= 0) return;
        var x = e.GetPosition(this).X;
        var idx = HitTestIndex(x);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            InvalidateVisual();
        }
    }

    /// <summary>鼠标移出：清除高亮与气泡。</summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    /// <summary>将 X 坐标映射为最近数据点索引（柱状按槽位，折线/面积按最近点）。</summary>
    private int HitTestIndex(double x)
    {
        if (_pointCount <= 0 || _plotWidth <= 0) return -1;
        if (ChartMode == MiniChartKind.MiniBarChart)
        {
            // 柱状：按槽位命中
            var slot = _plotWidth / _pointCount;
            var idx = (int)((x - _plotLeft) / slot);
            return Math.Clamp(idx, 0, _pointCount - 1);
        }
        // 折线/面积：按最近点命中
        var step = _pointCount > 1 ? _plotWidth / (_pointCount - 1) : _plotWidth;
        var nearest = (int)Math.Round((x - _plotLeft) / step);
        return Math.Clamp(nearest, 0, _pointCount - 1);
    }

    // =====================================================================
    // 渲染
    // =====================================================================

    /// <summary>
    /// 渲染入口：按 <see cref="ChartMode"/> 分发绘制柱状 / 折线 / 面积图。
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        // 透明底捕获 hit-test
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var values = Values;
        if (values == null || values.Count == 0)
        {
            DrawPlaceholder(dc, width, height);
            _pointCount = 0;
            return;
        }

        // 布局：迷你图紧凑边距（无坐标轴，气泡承载数值信息）
        const double padTop = 2, padBottom = 2, padLeft = 1, padRight = 1;
        var plotW = Math.Max(0, width - padLeft - padRight);
        var plotH = Math.Max(0, height - padTop - padBottom);
        var baseline = padTop + plotH;

        // Y 轴归一化上限
        double max = MaxValue > 0 ? MaxValue : values.Max();
        if (max <= 0) max = 1;

        _plotLeft = padLeft;
        _plotWidth = plotW;
        _pointCount = values.Count;

        var brush = SeriesBrush ?? Brushes.DodgerBlue;

        switch (ChartMode)
        {
            case MiniChartKind.MiniBarChart:
                DrawBars(dc, values, brush, padLeft, padTop, plotW, plotH, baseline, max);
                break;
            case MiniChartKind.MiniAreaChart:
                DrawLineOrArea(dc, values, brush, padLeft, padTop, plotW, plotH, baseline, max, fillArea: true);
                break;
            default: // MiniLineChart 及其它回退折线
                DrawLineOrArea(dc, values, brush, padLeft, padTop, plotW, plotH, baseline, max, fillArea: false);
                break;
        }
    }

    /// <summary>绘制柱状图模式：等宽圆角柱体，hover/HighlightIndex 柱高亮。
    /// <para>不再画独立 hover 气泡，由迷你图自身 ToolTip（XAML 绑定 CompositeTooltipText）统一承载；
    /// 仅保留高亮反馈指明当前指向。</para></summary>
    private void DrawBars(DrawingContext dc, IReadOnlyList<double> values, Brush brush,
        double padLeft, double padTop, double plotW, double plotH, double baseline, double max)
    {
        var count = values.Count;
        double slot = plotW / count;
        // 间距 1px（槽位过窄时缩减间距保证柱体可见）
        double gap = slot > 4 ? 1.0 : 0.5;
        double barW = Math.Max(1.5, slot - gap);
        double radius = Math.Min(barW / 2.0, 2.5);

        var dimFill = MakeTranslucent(brush, 0x88);
        for (int i = 0; i < count; i++)
        {
            var v = Math.Max(0, values[i]);
            double h = plotH * (v / max);
            double cx = padLeft + slot * i + slot / 2.0;
            var rect = new Rect(cx - barW / 2.0, baseline - Math.Max(1, h), barW, Math.Max(1, h));

            bool emphasized = i == _hoverIndex || i == HighlightIndex;
            dc.DrawRoundedRectangle(emphasized ? brush : dimFill, null, rect, radius, radius);
        }
        // 注：柱状图不调用 DrawHoverBubble——hover 信息由迷你图模板里的 ToolTip 元素统一显示。
    }

    /// <summary>绘制折线/面积图模式：折线路径 + 可选渐变填充 + 最新点发光 + hover 高亮。</summary>
    private void DrawLineOrArea(DrawingContext dc, IReadOnlyList<double> values, Brush brush,
        double padLeft, double padTop, double plotW, double plotH, double baseline, double max, bool fillArea)
    {
        var count = values.Count;

        // 单点特殊处理：画一个圆点
        if (count == 1)
        {
            var y = baseline - plotH * (Math.Max(0, values[0]) / max);
            var pt = new Point(padLeft + plotW / 2.0, y);
            dc.DrawEllipse(brush, null, pt, 2.5, 2.5);
            if (_hoverIndex == 0) DrawHoverBubble(dc, 0, pt.X, padTop, padLeft + plotW);
            return;
        }

        // 构建折线几何
        double step = plotW / (count - 1);
        var points = new Point[count];
        for (int i = 0; i < count; i++)
        {
            var v = Math.Max(0, values[i]);
            points[i] = new Point(padLeft + step * i, baseline - plotH * (v / max));
        }

        var figure = new PathFigure { StartPoint = points[0], IsClosed = false };
        for (int i = 1; i < count; i++)
            figure.Segments.Add(new LineSegment(points[i], true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        // 面积模式：渐变填充（画刷 30% → 透明）
        if (fillArea)
        {
            var areaFigure = new PathFigure { StartPoint = new Point(points[0].X, baseline), IsClosed = true };
            foreach (var pt in points)
                areaFigure.Segments.Add(new LineSegment(pt, true));
            areaFigure.Segments.Add(new LineSegment(new Point(points[^1].X, baseline), true));

            var areaGeometry = new PathGeometry();
            areaGeometry.Figures.Add(areaFigure);

            var gradient = new LinearGradientBrush(
                ExtractColor(brush, 0x4D), ExtractColor(brush, 0x00),
                new Point(0, 0), new Point(0, 1));
            gradient.Freeze();
            dc.DrawGeometry(gradient, null, areaGeometry);
        }

        // 折线描边
        var pen = new Pen(brush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        // 最新点发光圆点
        var lastPt = points[^1];
        var glowFill = MakeTranslucent(brush, 0x44);
        dc.DrawEllipse(glowFill, null, lastPt, 4, 4);
        dc.DrawEllipse(brush, null, lastPt, 2, 2);

        // hover 高亮点 + 气泡
        if (_hoverIndex >= 0 && _hoverIndex < count)
        {
            var hp = points[_hoverIndex];
            dc.DrawEllipse(Brushes.White, new Pen(brush, 1.5), hp, 3, 3);
            DrawHoverBubble(dc, _hoverIndex, hp.X, padTop, padLeft + plotW);
        }
    }

    /// <summary>绘制空数据占位文本 "--"。</summary>
    private void DrawPlaceholder(DrawingContext dc, double width, double height)
    {
        var brush = PlaceholderBrush ?? Brushes.Gray;
        var text = MakeText("--", brush, 11);
        dc.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
    }

    /// <summary>绘制 hover 气泡（标签行 + 数值行，自绘圆角矩形）。</summary>
    private void DrawHoverBubble(DrawingContext dc, int index, double cx, double top, double rightEdge)
    {
        var values = Values;
        if (values == null || index >= values.Count) return;

        var tipBg = FindBrush("TooltipBackgroundBrush", Color.FromRgb(0x1F, 0x24, 0x30));
        var tipFg = FindBrush("TooltipForegroundBrush", Color.FromRgb(0xF1, 0xF5, 0xF9));

        // 标签行（日期）+ 数值行
        var label = Labels != null && index < Labels.Count && !string.IsNullOrEmpty(Labels[index])
            ? Labels[index]
            : $"#{index + 1}";
        var valueText = FormatValue(values[index]);

        var labelText = MakeText(label, MakeTranslucent(tipFg, 0xB0), 9.5);
        var valueTextFmt = MakeText(valueText, tipFg, 11);

        double padX = 8, padY = 4, lineGap = 2;
        double bw = Math.Max(labelText.Width, valueTextFmt.Width) + padX * 2;
        double bh = labelText.Height + valueTextFmt.Height + lineGap + padY * 2;

        // 气泡水平定位（边界钳位）
        double bx = cx - bw / 2.0;
        if (bx < 1) bx = 1;
        if (bx + bw > rightEdge + 1) bx = rightEdge + 1 - bw;
        double by = top;

        dc.DrawRoundedRectangle(tipBg, null, new Rect(bx, by, bw, bh), 5, 5);
        dc.DrawText(labelText, new Point(bx + (bw - labelText.Width) / 2, by + padY));
        dc.DrawText(valueTextFmt, new Point(bx + (bw - valueTextFmt.Width) / 2, by + padY + labelText.Height + lineGap));
    }

    // =====================================================================
    // 工具方法
    // =====================================================================

    /// <summary>按 <see cref="ValueKind"/> 格式化数值（Percent → "42%"；Number → 大数简写 + 单位）。</summary>
    private string FormatValue(double v)
    {
        if (ValueKind == MiniSeriesValueKind.Percent)
            return $"{v:0.#}%";
        var unit = string.IsNullOrWhiteSpace(Unit) ? string.Empty : $" {Unit}";
        if (v >= 1_000_000) return $"{v / 1_000_000.0:0.#}M{unit}";
        if (v >= 1_000) return $"{v / 1_000.0:0.#}K{unit}";
        return $"{v:0.#}{unit}";
    }

    /// <summary>创建格式化文本（DPI 感知）。</summary>
    private FormattedText MakeText(string text, Brush brush, double size)
        => new(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            LabelTypeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>从主题资源查找画刷，未找到时回退固定色。</summary>
    private Brush FindBrush(string key, Color fallback)
    {
        if (TryFindResource(key) is Brush b) return b;
        var f = new SolidColorBrush(fallback);
        f.Freeze();
        return f;
    }

    /// <summary>将画刷转为指定透明度的冻结画刷。</summary>
    private static Brush MakeTranslucent(Brush source, byte alpha)
    {
        if (source is SolidColorBrush scb)
        {
            var c = scb.Color;
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        var fb = new SolidColorBrush(Color.FromArgb(alpha, 0x60, 0xA5, 0xFA));
        fb.Freeze();
        return fb;
    }

    /// <summary>从画刷提取颜色并生成指定透明度的颜色值（渐变填充用）。</summary>
    private static Color ExtractColor(Brush source, byte alpha)
    {
        var color = source is SolidColorBrush scb ? scb.Color : Color.FromRgb(0x60, 0xA5, 0xFA);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
