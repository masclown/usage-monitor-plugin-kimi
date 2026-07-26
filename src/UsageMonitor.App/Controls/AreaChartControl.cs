using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using UsageMonitor.Core.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using FlowDirection = System.Windows.FlowDirection;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 面积图控件（REQ-082 SDK v2）。独立控件，非折线变体。
/// <para>
/// 单系列面积填充展示（如单模型请求量随时间变化）。自绘实现：
/// 渐变填充面积 + 顶边线 + 类别标签 + hover 竖向参考线与 tooltip。
/// 参考 DeepSeek 用量页面积图样式（蓝色渐变填充 #70B2FE @ 0.7 → 透明）。
/// </para>
/// </summary>
public class AreaChartControl : FrameworkElement, IHoverTooltipProvider
{
    // Typeface 缓存，避免每次 OnRender 重复创建。
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>默认面积填充色（DeepSeek 风格蓝）。</summary>
    private static readonly Color DefaultAreaColor = Color.FromRgb(0x70, 0xB2, 0xFE);

    /// <summary>Y 轴数值序列。</summary>
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Y 轴数值序列。</summary>
    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>X 轴标签。</summary>
    public static readonly DependencyProperty CategoriesProperty = DependencyProperty.Register(
        nameof(Categories), typeof(IReadOnlyList<string>), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>X 轴标签。</summary>
    public IReadOnlyList<string>? Categories
    {
        get => (IReadOnlyList<string>?)GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }

    /// <summary>Y 轴上限。</summary>
    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Y 轴上限。</summary>
    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary>数值单位。</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>数值单位。</summary>
    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>系列名称（tooltip / 图例显示）。</summary>
    public static readonly DependencyProperty SeriesNameProperty = DependencyProperty.Register(
        nameof(SeriesName), typeof(string), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>系列名称。</summary>
    public string? SeriesName
    {
        get => (string?)GetValue(SeriesNameProperty);
        set => SetValue(SeriesNameProperty, value);
    }

    /// <summary>面积填充画笔。</summary>
    public static readonly DependencyProperty AreaBrushProperty = DependencyProperty.Register(
        nameof(AreaBrush), typeof(Brush), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>面积填充画笔。</summary>
    public Brush? AreaBrush
    {
        get => (Brush?)GetValue(AreaBrushProperty);
        set => SetValue(AreaBrushProperty, value);
    }

    /// <summary>边框画笔（顶边线）。</summary>
    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(Brush), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>边框画笔。</summary>
    public Brush? StrokeBrush
    {
        get => (Brush?)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    /// <summary>问题7：是否填充曲线下方区域（面积效果），默认 true；关闭后仅显示平滑曲线。</summary>
    public static readonly DependencyProperty FillBelowLineProperty = DependencyProperty.Register(
        nameof(FillBelowLine), typeof(bool), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>问题7：是否填充曲线下方区域。</summary>
    public bool FillBelowLine
    {
        get => (bool)GetValue(FillBelowLineProperty);
        set => SetValue(FillBelowLineProperty, value);
    }

    /// <summary>问题7：是否使用平滑曲线（样条插值），默认 true（对齐 DeepSeek 用量页曲线风格）。</summary>
    public static readonly DependencyProperty SmoothCurveProperty = DependencyProperty.Register(
        nameof(SmoothCurve), typeof(bool), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>问题7：是否使用平滑曲线。</summary>
    public bool SmoothCurve
    {
        get => (bool)GetValue(SmoothCurveProperty);
        set => SetValue(SmoothCurveProperty, value);
    }

    /// <summary>文字画笔。</summary>
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(AreaChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>文字画笔。</summary>
    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>当前 hover 的数据点索引（-1 = 无）。</summary>
    private int _hoverIndex = -1;
    /// <summary>绘图区布局缓存（供 hit-test 使用）。</summary>
    private double _plotLeft, _plotW, _plotTop, _plotH;
    private int _count;
    private double _max;

    /// <summary>构造函数：设置最小尺寸与主题默认画笔。</summary>
    public AreaChartControl()
    {
        MinHeight = 80;
        MinWidth = 120;
        ClipToBounds = true;
        if (TextBrush == null)
            SetValue(TextBrushProperty, TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);
    }

    /// <summary>鼠标移动：映射 X 坐标到数据点索引，显示参考线与 tooltip。</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_count < 1 || _plotW <= 0) return;
        var pos = e.GetPosition(this);
        var idx = (int)Math.Round((pos.X - _plotLeft) / (_plotW / Math.Max(1, _count - 1)));
        idx = Math.Max(0, Math.Min(_count - 1, idx));
        if (idx != _hoverIndex) { _hoverIndex = idx; InvalidateVisual(); }
        if (TryGetTooltip(pos, out var data))
            HoverTooltipPresenter.Show(this, data);
    }

    /// <summary>鼠标移出：清除参考线与 tooltip。</summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1) { _hoverIndex = -1; InvalidateVisual(); }
        HoverTooltipPresenter.Hide(this);
    }

    /// <summary>
    /// 渲染入口：自绘面积图（渐变填充 + 顶边线 + 类别标签 + hover 参考线）。
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 10 || height < 10) return;

        var values = Values;
        var textBrush = TextBrush ?? Brushes.Gray;

        // 无数据时显示空态提示。
        if (values == null || values.Count == 0)
        {
            // 问题8：空态文案走 i18n。
            var empty = MakeText(UsageMonitor.Core.Services.I18n.T("chart.empty"), textBrush, 13);
            dc.DrawText(empty, new Point((width - empty.Width) / 2, (height - empty.Height) / 2));
            _count = 0;
            return;
        }

        int count = values.Count;
        _count = count;

        double max = MaxValue > 0 ? MaxValue : values.Max();
        if (max <= 0) max = 1;
        if (MaxValue <= 0) max *= 1.15;
        _max = max;

        // 布局。
        double padLeft = 6, padRight = 6, padTop = 6, padBottom = 16;
        double plotW = Math.Max(10, width - padLeft - padRight);
        double plotH = Math.Max(10, height - padTop - padBottom);
        double baseline = padTop + plotH;
        _plotLeft = padLeft;
        _plotW = plotW;
        _plotTop = padTop;
        _plotH = plotH;

        // 计算数据点坐标。
        var points = new Point[count];
        for (int i = 0; i < count; i++)
        {
            double x = count > 1 ? padLeft + plotW * i / (count - 1) : padLeft + plotW / 2;
            double y = baseline - plotH * (Math.Max(0, values[i]) / max);
            points[i] = new Point(x, y);
        }

        // 问题7：面积填充几何（平滑曲线/折线 + 底边封闭），仅在 FillBelowLine 开启时绘制。
        if (FillBelowLine)
        {
            var areaGeo = new StreamGeometry();
            using (var ctx = areaGeo.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, baseline), true, true);
                BuildCurveSegments(ctx, points, SmoothCurve);
                ctx.LineTo(new Point(points[count - 1].X, baseline), true, true);
            }
            areaGeo.Freeze();
            // 渐变填充画笔（AreaBrush 优先；缺省用主题色渐变）。
            Brush fill = AreaBrush ?? BuildDefaultGradient();
            dc.DrawGeometry(fill, null, areaGeo);
        }

        // 顶边线（问题7：平滑曲线/折线可配）。
        var strokeColor = (StrokeBrush as SolidColorBrush)?.Color ?? DefaultAreaColor;
        var strokePen = new Pen(StrokeBrush ?? new SolidColorBrush(DefaultAreaColor), 1.8);
        if (strokePen.CanFreeze) strokePen.Freeze();
        var lineGeo = new StreamGeometry();
        using (var ctx = lineGeo.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            BuildCurveSegments(ctx, points, SmoothCurve);
        }
        lineGeo.Freeze();
        dc.DrawGeometry(null, strokePen, lineGeo);

        // hover 参考线 + 数据点圆点。
        if (_hoverIndex >= 0 && _hoverIndex < count)
        {
            var hp = points[_hoverIndex];
            var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x66, strokeColor.R, strokeColor.G, strokeColor.B)), 1.0);
            if (guidePen.CanFreeze) guidePen.Freeze();
            dc.DrawLine(guidePen, new Point(hp.X, _plotTop), new Point(hp.X, baseline));

            var dotFill = new SolidColorBrush(strokeColor);
            if (dotFill.CanFreeze) dotFill.Freeze();
            dc.DrawEllipse(dotFill, null, hp, 3.5, 3.5);
        }

        // 类别标签（稀疏化）。
        var categories = Categories;
        if (categories != null && categories.Count > 0)
        {
            int labelCount = Math.Min(count, categories.Count);
            int labelStep = Math.Max(1, (int)Math.Ceiling(labelCount / Math.Max(1, plotW / 42)));
            for (int i = 0; i < labelCount; i += labelStep)
            {
                var label = MakeText(ShortenDateLabel(categories[i]), textBrush, 9.5);
                double lx = (count > 1 ? padLeft + plotW * i / (count - 1) : padLeft + plotW / 2) - label.Width / 2;
                dc.DrawText(label, new Point(Math.Max(0, lx), baseline + 3));
            }
        }
    }

    /// <summary>构建默认渐变填充（主题色 0.7 透明度 → 透明）。</summary>
    private LinearGradientBrush BuildDefaultGradient()
    {
        var c = (AreaBrush as SolidColorBrush)?.Color ?? DefaultAreaColor;
        var gradient = new LinearGradientBrush(
            Color.FromArgb(0xB3, c.R, c.G, c.B),
            Color.FromArgb(0x0D, c.R, c.G, c.B),
            new Point(0, 0), new Point(0, 1));
        if (gradient.CanFreeze) gradient.Freeze();
        return gradient;
    }

    /// <summary>将控件内部坐标映射为命中的面积图数据点（tooltip 显示日期 + 数值）。</summary>
    public bool TryGetTooltip(Point position, out HoverTooltipData data)
    {
        data = default!;
        var values = Values;
        if (values == null || values.Count == 0 || _plotW <= 0) return false;

        int count = values.Count;
        int index = (int)Math.Round((position.X - _plotLeft) / (_plotW / Math.Max(1, count - 1)));
        index = Math.Max(0, Math.Min(count - 1, index));

        var unit = string.IsNullOrWhiteSpace(Unit) ? string.Empty : $" {Unit}";
        var name = string.IsNullOrWhiteSpace(SeriesName) ? "数值" : SeriesName;
        var categories = Categories;
        var title = categories != null && index < categories.Count ? categories[index] : $"#{index + 1}";
        data = new HoverTooltipData(title, $"{FormatValue(values[index])}{unit}", name);
        return true;
    }

    /// <summary>数值格式化（大数用 K/M 简写）。</summary>
    private static string FormatValue(double v)
    {
        if (v >= 1_000_000_000) return (v / 1_000_000_000).ToString("0.##B", CultureInfo.InvariantCulture);
        if (v >= 1_000_000) return (v / 1_000_000).ToString("0.##M", CultureInfo.InvariantCulture);
        if (v >= 10_000) return (v / 1_000).ToString("0.##K", CultureInfo.InvariantCulture);
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>创建格式化文本对象。</summary>
    private static FormattedText MakeText(string text, Brush brush, double size) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface,
        size, brush, VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

    /// <summary>问题9：X 轴日期标签缩短——yyyy-MM-dd 取 MM-dd，其余原样返回（tooltip 仍用完整类别值）。</summary>
    private static string ShortenDateLabel(string label)
        => label.Length == 10 && label[4] == '-' && label[7] == '-' ? label.Substring(5) : label;

    /// <summary>问题7：向 StreamGeometryContext 追加数据点间的线段——
    /// smooth=true 时用 Catmull-Rom 样条转 Bezier 生成平滑曲线（对齐 DeepSeek 用量页），否则直线连接。</summary>
    private static void BuildCurveSegments(StreamGeometryContext ctx, Point[] points, bool smooth)
    {
        int count = points.Length;
        if (count < 2) return;
        if (!smooth)
        {
            for (int i = 1; i < count; i++)
                ctx.LineTo(points[i], true, true);
            return;
        }
        // Catmull-Rom 转三次 Bezier：张力 1/6，端点重复取首/末点。
        for (int i = 0; i < count - 1; i++)
        {
            var p0 = points[Math.Max(0, i - 1)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(count - 1, i + 2)];
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
            ctx.BezierTo(c1, c2, p2, true, true);
        }
    }
}
