using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104 / CS0234）
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using FontFamily = System.Windows.Media.FontFamily;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 历史窗口大尺寸多线折线图中一条数据序列的描述。用于多 Provider 曲线对比。
/// </summary>
public class HistorySeries
{
    /// <summary>Provider 唯一标识</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>显示名称</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>折线颜色</summary>
    public Brush LineBrush { get; set; } = Brushes.SteelBlue;

    /// <summary>已用百分比采样点（0-100，按时间升序）</summary>
    public IReadOnlyList<double> Values { get; set; } = Array.Empty<double>();
}

/// <summary>
/// 历史窗口大尺寸多线折线图。
/// <para>
/// - 接收 Series（<see cref="HistorySeries"/> 集合），可同时绘制多条线
/// - 单序列时在折线下方绘制渐变面积填充（AreaBrush），最新点带发光圆点
/// - 支持鼠标 hover：显示竖向游标 + 数值气泡（自绘，主题感知）
/// - Y 轴 0/25/50/75/100 网格刻度，底部图例区
/// </para>
/// </summary>
public class HistoryLineChartControl : FrameworkElement
{
    /// <summary>多线数据序列集合依赖属性</summary>
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IEnumerable), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(Array.Empty<HistorySeries>(),
            FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    /// <summary>Y 轴最大值（默认 100）</summary>
    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>折线粗细</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(2.4, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>轴线颜色</summary>
    public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
        nameof(AxisBrush), typeof(Brush), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>网格线颜色</summary>
    public static readonly DependencyProperty GridLineBrushProperty = DependencyProperty.Register(
        nameof(GridLineBrush), typeof(Brush), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>轴文本颜色</summary>
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>单序列面积填充画笔（一般传主题 ChartAreaGradientBrush）</summary>
    public static readonly DependencyProperty AreaBrushProperty = DependencyProperty.Register(
        nameof(AreaBrush), typeof(Brush), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>图例条目高度</summary>
    public static readonly DependencyProperty LegendItemHeightProperty = DependencyProperty.Register(
        nameof(LegendItemHeight), typeof(double), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>req-040：横坐标日期标签集合（与 Values 等长，格式如"7/11"）</summary>
    public static readonly DependencyProperty DatesProperty = DependencyProperty.Register(
        nameof(Dates), typeof(IReadOnlyList<string>), typeof(HistoryLineChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable Series
    {
        get => (IEnumerable)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush AxisBrush
    {
        get => (Brush)GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
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

    public Brush? AreaBrush
    {
        get => (Brush?)GetValue(AreaBrushProperty);
        set => SetValue(AreaBrushProperty, value);
    }

    public double LegendItemHeight
    {
        get => (double)GetValue(LegendItemHeightProperty);
        set => SetValue(LegendItemHeightProperty, value);
    }

    /// <summary>req-040：横坐标日期标签集合。</summary>
    public IReadOnlyList<string>? Dates
    {
        get => (IReadOnlyList<string>?)GetValue(DatesProperty);
        set => SetValue(DatesProperty, value);
    }

    // hover 状态与最近一次布局（供 OnMouseMove 把鼠标 X 映射为数据索引）
    private int _hoverIndex = -1;
    private double _left, _top, _plotW, _plotH;
    private int _maxCount;

    public HistoryLineChartControl()
    {
        MinHeight = 200;
        MinWidth = 300;
    }

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (HistoryLineChartControl)d;
        if (e.OldValue is INotifyCollectionChanged oldIncc)
            oldIncc.CollectionChanged -= control.OnSeriesCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newIncc)
            newIncc.CollectionChanged += control.OnSeriesCollectionChanged;
        control._hoverIndex = -1;
        control.InvalidateVisual();
    }

    private void OnSeriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    /// <summary>鼠标移动：把 X 坐标映射为最近的数据索引，触发重绘显示游标/气泡。</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_maxCount < 2 || _plotW <= 0) { return; }
        var x = e.GetPosition(this).X;
        var rel = (x - _left) / _plotW;
        var idx = (int)Math.Round(rel * (_maxCount - 1));
        idx = Math.Max(0, Math.Min(_maxCount - 1, idx));
        if (idx != _hoverIndex) { _hoverIndex = idx; InvalidateVisual(); }
    }

    /// <summary>鼠标移出：清除 hover 游标。</summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1) { _hoverIndex = -1; InvalidateVisual(); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var max = MaxValue <= 0 ? 100 : MaxValue;
        var paddingLeft = 36.0;
        var paddingRight = 12.0;
        var paddingTop = 12.0;
        // req-040：底部预留 20px 给横坐标日期标签
        var paddingBottom = 32.0 + LegendItemHeight;
        var plotWidth = Math.Max(0, width - paddingLeft - paddingRight);
        var plotHeight = Math.Max(0, height - paddingTop - paddingBottom);

        // 透明底捕获 hit-test，使 hover 覆盖整个绘图区
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        // 收集可绘制序列
        var seriesList = new List<HistorySeries>();
        if (Series != null)
        {
            foreach (var item in Series)
                if (item is HistorySeries s && s.Values != null && s.Values.Count >= 2)
                    seriesList.Add(s);
        }

        // 记录布局供 hover 使用
        _left = paddingLeft; _top = paddingTop; _plotW = plotWidth; _plotH = plotHeight;
        _maxCount = seriesList.Count == 0 ? 0 : seriesList.Max(s => s.Values.Count);

        DrawGrid(dc, paddingLeft, paddingTop, plotWidth, plotHeight, max);

        // req-040：横坐标日期标签
        DrawXAxisLabels(dc, paddingLeft, paddingTop, plotWidth, plotHeight);

        if (seriesList.Count > 0)
        {
            bool single = seriesList.Count == 1;
            foreach (var s in seriesList)
                DrawSeries(dc, s, paddingLeft, paddingTop, plotWidth, plotHeight, max, single);
        }
        else
        {
            DrawCenterText(dc, paddingLeft, paddingTop, plotWidth, plotHeight, "暂无数据");
        }

        DrawLegend(dc, seriesList, paddingLeft, height - LegendItemHeight, plotWidth);

        if (_hoverIndex >= 0 && seriesList.Count > 0)
            DrawHover(dc, seriesList, paddingLeft, paddingTop, plotWidth, plotHeight, max);
    }

    /// <summary>绘制网格线 + Y 轴 0/25/50/75/100 刻度文字</summary>
    private void DrawGrid(DrawingContext dc, double left, double top, double plotWidth, double plotHeight, double max)
    {
        var gridPen = new Pen(GridLineBrush, 0.6) { DashStyle = new DashStyle(new[] { 2.0, 3.0 }, 0) };
        if (gridPen.CanFreeze) gridPen.Freeze();
        var axisPen = new Pen(AxisBrush, 1.0);
        if (axisPen.CanFreeze) axisPen.Freeze();

        dc.DrawLine(axisPen, new Point(left, top + plotHeight), new Point(left + plotWidth, top + plotHeight));
        for (int pct = 0; pct <= 100; pct += 25)
        {
            var y = top + plotHeight * (1.0 - pct / max);
            dc.DrawLine(gridPen, new Point(left, y), new Point(left + plotWidth, y));
            DrawTickText(dc, pct.ToString(CultureInfo.InvariantCulture), left - 6, y,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }
    }

    /// <summary>req-040：绘制横坐标日期标签（等间距 4~5 个）。</summary>
    private void DrawXAxisLabels(DrawingContext dc, double left, double top, double plotWidth, double plotHeight)
    {
        var dates = Dates;
        if (dates == null || dates.Count == 0 || _maxCount < 2) return;
        // req-040：横坐标标签数量必须与数据点数一致，否则位置计算会错位
        if (dates.Count != _maxCount) return;

        // 等间距 4~5 个标签
        var labelCount = Math.Min(5, dates.Count);
        var step = Math.Max(1, dates.Count / labelCount);
        var baselineY = top + plotHeight + 4; // 图表底部下方 4px

        for (int i = 0; i < dates.Count; i += step)
        {
            var x = left + plotWidth * i / (_maxCount - 1);
            DrawTickText(dc, dates[i], x, baselineY, HorizontalAlignment.Center, VerticalAlignment.Top);
        }
        // 确保最后一个标签始终显示
        var lastIdx = dates.Count - 1;
        if (lastIdx % step != 0)
        {
            var x = left + plotWidth * lastIdx / (_maxCount - 1);
            DrawTickText(dc, dates[lastIdx], x, baselineY, HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }

    /// <summary>绘制单条折线（可选面积填充 + 发光末点）</summary>
    private void DrawSeries(DrawingContext dc, HistorySeries s, double left, double top,
        double plotWidth, double plotHeight, double max, bool fillArea)
    {
        var values = s.Values;
        var stepX = values.Count > 1 ? plotWidth / (values.Count - 1) : 0;
        var baseline = top + plotHeight;

        var points = new Point[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            var v = Math.Max(0, Math.Min(max, values[i]));
            points[i] = new Point(left + i * stepX, top + plotHeight * (1.0 - v / max));
        }

        // 面积填充（仅单序列，用 AreaBrush 或同色低透明）
        if (fillArea)
        {
            var fill = AreaBrush ?? MakeTranslucent(s.LineBrush, 0x33);
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, baseline), true, true);
                ctx.LineTo(points[0], true, false);
                for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
                ctx.LineTo(new Point(points[^1].X, baseline), true, false);
            }
            area.Freeze();
            dc.DrawGeometry(fill, null, area);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
        }
        line.Freeze();
        var pen = new Pen(s.LineBrush, StrokeThickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (pen.CanFreeze) pen.Freeze();
        dc.DrawGeometry(null, pen, line);

        var last = points[^1];
        var dot = Math.Max(2.5, StrokeThickness);
        dc.DrawEllipse(MakeTranslucent(s.LineBrush, 0x40), null, last, dot * 2.2, dot * 2.2);
        dc.DrawEllipse(s.LineBrush, null, last, dot, dot);
    }

    /// <summary>绘制 hover 游标：竖线 + 各序列圆点 + 数值气泡</summary>
    private void DrawHover(DrawingContext dc, List<HistorySeries> series, double left, double top,
        double plotWidth, double plotHeight, double max)
    {
        int idx = _hoverIndex;
        double x = left + (_maxCount > 1 ? plotWidth * idx / (_maxCount - 1) : 0);

        var guidePen = new Pen(MakeTranslucent(AxisBrush, 0x80), 1.0);
        if (guidePen.CanFreeze) guidePen.Freeze();
        dc.DrawLine(guidePen, new Point(x, top), new Point(x, top + plotHeight));

        // 组织气泡文本行：每序列 "名称 值%"
        var lines = new List<(string text, Brush color)>();
        foreach (var s in series)
        {
            if (idx >= s.Values.Count) continue;
            var v = Math.Max(0, Math.Min(max, s.Values[idx]));
            var y = top + plotHeight * (1.0 - v / max);
            dc.DrawEllipse(s.LineBrush, null, new Point(x, y), 3.5, 3.5);
            lines.Add(($"{s.DisplayName} {s.Values[idx]:0.#}%", s.LineBrush));
        }
        if (lines.Count == 0) return;

        // 气泡尺寸
        var tipBg = FindBrush("TooltipBackgroundBrush", Color.FromRgb(0x1F, 0x24, 0x30));
        var tipFg = FindBrush("TooltipForegroundBrush", Color.FromRgb(0xF1, 0xF5, 0xF9));
        double padX = 9, padY = 6, lineH = 16, dotGap = 12;
        var texts = lines.Select(l => MakeText(l.text, tipFg, 11.5)).ToList();
        double bw = texts.Max(t => t.Width) + padX * 2 + dotGap;
        double bh = texts.Count * lineH + padY * 2;

        double bx = x + 10;
        if (bx + bw > left + plotWidth) bx = x - bw - 10;
        double by = top + 4;

        var rect = new Rect(bx, by, bw, bh);
        dc.DrawRoundedRectangle(tipBg, null, rect, 8, 8);
        for (int i = 0; i < texts.Count; i++)
        {
            double ly = by + padY + i * lineH;
            dc.DrawEllipse(lines[i].color, null, new Point(bx + padX + 3, ly + lineH / 2 - 1), 3, 3);
            dc.DrawText(texts[i], new Point(bx + padX + dotGap, ly + (lineH - texts[i].Height) / 2));
        }
    }

    /// <summary>图例（圆点 + Provider 名 + 当前最新百分比），自动换行</summary>
    private void DrawLegend(DrawingContext dc, List<HistorySeries> series, double left, double top, double width)
    {
        if (series.Count == 0) return;
        var rowHeight = LegendItemHeight;
        var x = left; var y = top;
        foreach (var s in series)
        {
            var label = $"{s.DisplayName} {s.Values[^1]:0.#}%";
            dc.DrawEllipse(s.LineBrush, null, new Point(x + 5, y + rowHeight / 2.0), 4, 4);
            var t = MakeText(label, TextBrush, 11, FontWeights.SemiBold);
            dc.DrawText(t, new Point(x + 14, y + (rowHeight - t.Height) / 2.0));
            x += label.Length * 7.0 + 24;
            if (x > left + width - 50) { x = left; y += rowHeight; }
        }
    }

    private void DrawCenterText(DrawingContext dc, double left, double top, double w, double h, string text)
    {
        var t = MakeText(text, Brushes.Gray, 14);
        dc.DrawText(t, new Point(left + (w - t.Width) / 2.0, top + (h - t.Height) / 2.0));
    }

    private void DrawTickText(DrawingContext dc, string text, double x, double y,
        HorizontalAlignment hAlign, VerticalAlignment vAlign)
    {
        var t = MakeText(text, TextBrush, 10);
        double ox = hAlign switch
        {
            HorizontalAlignment.Right => x - t.Width,
            HorizontalAlignment.Center => x - t.Width / 2.0,
            _ => x
        };
        double oy = vAlign switch
        {
            VerticalAlignment.Top => y,
            VerticalAlignment.Bottom => y - t.Height,
            _ => y - t.Height / 2.0
        };
        dc.DrawText(t, new Point(ox, oy));
    }

    /// <summary>构造 FormattedText（中文优先 YaHei）。</summary>
    private FormattedText MakeText(string text, Brush brush, double size, FontWeight? weight = null)
    {
        return new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal,
                weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    /// <summary>从应用资源取画笔（主题感知），缺失时用回退色。</summary>
    private Brush FindBrush(string key, Color fallback)
    {
        if (TryFindResource(key) is Brush b) return b;
        var f = new SolidColorBrush(fallback); f.Freeze(); return f;
    }

    /// <summary>派生同色半透明画笔。</summary>
    private static Brush MakeTranslucent(Brush source, byte alpha)
    {
        if (source is SolidColorBrush scb)
        {
            var c = scb.Color;
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        var fb = new SolidColorBrush(Color.FromArgb(alpha, 0x94, 0xA3, 0xB8));
        fb.Freeze();
        return fb;
    }
}
