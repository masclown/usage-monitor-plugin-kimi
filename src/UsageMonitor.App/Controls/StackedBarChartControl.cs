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
using ColorConverter = System.Windows.Media.ColorConverter;
using FlowDirection = System.Windows.FlowDirection;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 多系列堆叠柱状图控件（REQ-082 SDK v2）。动态 N 系列叠加。
/// <para>
/// 用于按类别拆分的多系列叠加展示（如按日期 × 模型维度的消费金额堆叠柱）。
/// 自绘实现：堆叠柱体 + 网格线 + 类别标签 + hover 高亮与 tooltip（日期 + 各系列分项）。
/// </para>
/// </summary>
public class StackedBarChartControl : FrameworkElement, IHoverTooltipProvider
{
    // Typeface 缓存，避免每次 OnRender 重复创建。
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>默认色板（问题7修复：统一蓝色渐变系列，保证同一卡片内多个堆叠柱状图颜色风格一致）。</summary>
    private static readonly Color[] DefaultPalette =
    {
        Color.FromRgb(0x0C, 0x70, 0xF3), // 深蓝
        Color.FromRgb(0x60, 0xB3, 0xFE), // 中蓝
        Color.FromRgb(0xA0, 0xDC, 0xFD), // 浅蓝
        Color.FromRgb(0x38, 0x8E, 0xF7), // 蓝
        Color.FromRgb(0x82, 0xC4, 0xFA), // 淡蓝
        Color.FromRgb(0xC0, 0xE8, 0xFE), // 极浅蓝
    };

    /// <summary>X 轴类别标签（如日期）。</summary>
    public static readonly DependencyProperty CategoriesProperty = DependencyProperty.Register(
        nameof(Categories), typeof(IReadOnlyList<string>), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>X 轴类别标签集合。</summary>
    public IReadOnlyList<string>? Categories
    {
        get => (IReadOnlyList<string>?)GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }

    /// <summary>堆叠系列集合（数量动态）。</summary>
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>堆叠系列集合。</summary>
    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>数值单位提示（"¥"/"tokens"/"次"）。</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>数值单位提示。</summary>
    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>图表标题。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>图表标题。</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>色板（可空，null 时使用默认色板）。</summary>
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(UsageMonitor.Core.Plugins.IChartPalette), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>色板。</summary>
    public UsageMonitor.Core.Plugins.IChartPalette? Palette
    {
        get => (UsageMonitor.Core.Plugins.IChartPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>柱体画笔（默认主题 AccentBrush）。</summary>
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>柱体画笔（无 Palette 时 fallback 使用）。</summary>
    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>网格线画笔。</summary>
    public static readonly DependencyProperty GridLineBrushProperty = DependencyProperty.Register(
        nameof(GridLineBrush), typeof(Brush), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>网格线画笔。</summary>
    public Brush? GridLineBrush
    {
        get => (Brush?)GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
    }

    /// <summary>文字画笔。</summary>
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(StackedBarChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>文字画笔。</summary>
    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>当前 hover 的柱索引（-1 = 无）。</summary>
    private int _hoverIndex = -1;
    /// <summary>绘图区布局缓存（供 hit-test 使用）。</summary>
    private double _plotLeft, _plotW;
    private int _categoryCount;

    /// <summary>构造函数：设置最小尺寸与主题默认画笔。</summary>
    public StackedBarChartControl()
    {
        MinHeight = 100;
        MinWidth = 160;
        ClipToBounds = true;
        if (GridLineBrush == null)
            SetValue(GridLineBrushProperty, TryFindResource("ChartGridBrush") as Brush ?? Brushes.DimGray);
        if (TextBrush == null)
            SetValue(TextBrushProperty, TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);
    }

    /// <summary>鼠标移动：映射 X 坐标到柱索引并高亮，显示 tooltip。</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_categoryCount < 1 || _plotW <= 0) return;
        var x = e.GetPosition(this).X;
        var idx = (int)((x - _plotLeft) / (_plotW / _categoryCount));
        idx = Math.Max(0, Math.Min(_categoryCount - 1, idx));
        if (idx != _hoverIndex) { _hoverIndex = idx; InvalidateVisual(); }
        if (TryGetTooltip(e.GetPosition(this), out var data))
            HoverTooltipPresenter.Show(this, data);
    }

    /// <summary>鼠标移出：清除高亮与 tooltip。</summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1) { _hoverIndex = -1; InvalidateVisual(); }
        HoverTooltipPresenter.Hide(this);
    }

    /// <summary>
    /// 渲染入口：根据 Categories × Series 自绘堆叠柱状图。
    /// <para>含网格线、类别标签、堆叠柱体（圆角顶）、hover 高亮列。</para>
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 10 || height < 10) return;

        var categories = Categories;
        var series = Series;
        var textBrush = TextBrush ?? Brushes.Gray;
        var gridBrush = GridLineBrush ?? Brushes.DimGray;

        // 无数据时显示空态提示。
        if (categories == null || categories.Count == 0 || series == null || series.Count == 0)
        {
            var empty = MakeText("暂无数据", textBrush, 13);
            dc.DrawText(empty, new Point((width - empty.Width) / 2, (height - empty.Height) / 2));
            _categoryCount = 0;
            return;
        }

        int count = categories.Count;
        _categoryCount = count;

        // 计算每个类别的堆叠总和，确定 Y 轴上限。
        var totals = new double[count];
        for (int s = 0; s < series.Count; s++)
        {
            var values = series[s].Values;
            if (values == null) continue;
            for (int i = 0; i < Math.Min(count, values.Count); i++)
                totals[i] += Math.Max(0, values[i]);
        }
        double max = totals.Length > 0 ? totals.Max() : 0;
        if (max <= 0) max = 1;
        max *= 1.1; // 顶部留白

        // 布局：标题区 + 绘图区 + 标签区。
        double titleH = string.IsNullOrEmpty(Title) ? 0 : 20;
        double padLeft = 6, padRight = 6, padBottom = 18;
        double plotTop = titleH + 4;
        double plotH = Math.Max(10, height - plotTop - padBottom);
        double plotW = Math.Max(10, width - padLeft - padRight);
        double baseline = plotTop + plotH;
        _plotLeft = padLeft;
        _plotW = plotW;

        // 标题。
        if (!string.IsNullOrEmpty(Title))
            dc.DrawText(MakeText(Title!, textBrush, 12, bold: true), new Point(padLeft, 2));

        // 网格线（3 条水平线 + 基线）。
        var gridPen = new Pen(gridBrush, 0.5);
        if (gridPen.CanFreeze) gridPen.Freeze();
        for (int g = 0; g <= 3; g++)
        {
            double y = baseline - plotH * g / 3.0;
            dc.DrawLine(gridPen, new Point(padLeft, y), new Point(padLeft + plotW, y));
        }

        // 柱体绘制。
        double slot = plotW / count;
        double barW = Math.Max(2, Math.Min(slot * 0.6, 24));

        for (int i = 0; i < count; i++)
        {
            double cx = padLeft + slot * i + slot / 2.0;
            double x0 = cx - barW / 2.0;
            double accH = 0; // 累积高度（自底向上堆叠）

            for (int s = 0; s < series.Count; s++)
            {
                var values = series[s].Values;
                double v = values != null && i < values.Count ? Math.Max(0, values[i]) : 0;
                if (v <= 0) continue;

                double segH = plotH * (v / max);
                double y0 = baseline - accH - segH;
                var brush = ResolveSeriesBrush(s, series[s]);

                var rect = new Rect(x0, y0, barW, segH);
                // 最顶层段画圆角顶。
                bool isTop = IsTopSegment(s, i, series, count);
                if (isTop && segH > 1)
                {
                    // 问题4：圆角半径自适应（取柱宽/段高/3px 三者最小），
                    // 保证不同数值量级的图表（消费¥ vs Token）顶部圆角风格一致。
                    double r = Math.Min(Math.Min(barW / 2.0, segH / 2.0), 3.0);
                    var geo = new RectangleGeometry(rect, r, r);
                    if (geo.CanFreeze) geo.Freeze();
                    dc.DrawGeometry(brush, null, geo);
                }
                else
                {
                    dc.DrawRectangle(brush, null, rect);
                }
                accH += segH;
            }

            // hover 高亮列。
            if (i == _hoverIndex)
            {
                var hlRect = new Rect(padLeft + slot * i, plotTop, slot, plotH);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), null, hlRect);
            }
        }

        // 类别标签（按可用宽度稀疏化，避免重叠）。问题9：yyyy-MM-dd 缩短为 MM-dd 紧凑显示。
        int labelStep = Math.Max(1, (int)Math.Ceiling(count / Math.Max(1, plotW / 42)));
        for (int i = 0; i < count; i += labelStep)
        {
            var label = MakeText(ShortenDateLabel(categories[i]), textBrush, 9.5);
            double lx = padLeft + slot * i + slot / 2.0 - label.Width / 2.0;
            dc.DrawText(label, new Point(Math.Max(0, lx), baseline + 4));
        }
    }

    /// <summary>问题9：X 轴日期标签缩短——yyyy-MM-dd 取 MM-dd，其余原样返回（tooltip 仍用完整类别值）。</summary>
    private static string ShortenDateLabel(string label)
        => label.Length == 10 && label[4] == '-' && label[7] == '-' ? label.Substring(5) : label;

    /// <summary>判断某段是否为该柱的最顶层非零段（用于圆角顶绘制）。</summary>
    private static bool IsTopSegment(int seriesIdx, int catIdx, IReadOnlyList<ChartSeries> series, int count)
    {
        for (int s = seriesIdx + 1; s < series.Count; s++)
        {
            var vals = series[s].Values;
            if (vals != null && catIdx < vals.Count && vals[catIdx] > 0) return false;
        }
        return true;
    }

    /// <summary>解析系列画笔：优先系列声明颜色 → 色板 → 默认色板。</summary>
    private Brush ResolveSeriesBrush(int index, ChartSeries series)
    {
        // 系列声明颜色。
        if (!string.IsNullOrEmpty(series.Color))
        {
            var parsed = TryParseColor(series.Color!);
            if (parsed.HasValue) return FreezeBrush(parsed.Value);
        }
        // 色板。
        if (Palette != null)
        {
            var colors = Palette.GetSeriesColors(index + 1);
            if (colors.Count > index)
            {
                var pc = TryParseColor(colors[index]);
                if (pc.HasValue) return FreezeBrush(pc.Value);
            }
        }
        // 默认色板循环。
        return FreezeBrush(DefaultPalette[index % DefaultPalette.Length]);
    }

    /// <summary>尝试解析颜色字符串（"#RRGGBB" / "#AARRGGBB"）。</summary>
    private static Color? TryParseColor(string color)
    {
        try { return (Color)ColorConverter.ConvertFromString(color); }
        catch { return null; }
    }

    /// <summary>创建可冻结的纯色画笔。</summary>
    private static SolidColorBrush FreezeBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        if (b.CanFreeze) b.Freeze();
        return b;
    }

    /// <summary>将控件内部坐标映射为命中的堆叠柱数据点（tooltip 显示日期 + 各系列分项值）。</summary>
    public bool TryGetTooltip(Point position, out HoverTooltipData data)
    {
        data = default!;
        var categories = Categories;
        var series = Series;
        if (categories == null || categories.Count == 0 || series == null || series.Count == 0 || _plotW <= 0)
            return false;

        int index = Math.Clamp((int)((position.X - _plotLeft) / (_plotW / categories.Count)), 0, categories.Count - 1);
        var unit = string.IsNullOrWhiteSpace(Unit) ? string.Empty : $" {Unit}";

        // 构建各系列分项文本。问题9/10：系列名（模型名）前缀是否显示由全局 tooltip 设置控制，
        // 关闭后仅显示数值（避免模型名等英文标识占据 tooltip）。
        double total = 0;
        var parts = new List<string>();
        var showName = UsageMonitor.App.Helpers.TooltipDisplaySettings.ShowFieldName;
        foreach (var s in series)
        {
            double v = s.Values != null && index < s.Values.Count ? Math.Max(0, s.Values[index]) : 0;
            total += v;
            if (v > 0)
                parts.Add(showName ? $"{s.Name} {FormatValue(v)}{unit}" : $"{FormatValue(v)}{unit}");
        }

        var title = categories[index];
        var value = $"{FormatValue(total)}{unit}";
        var detail = parts.Count > 0 ? string.Join("\n", parts) : null;
        data = new HoverTooltipData(title, value, detail);
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
    private static FormattedText MakeText(string text, Brush brush, double size, bool bold = false)
    {
        var typeface = bold
            ? new Typeface(LabelTypeface.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal)
            : LabelTypeface;
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, brush, VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);
    }
}
