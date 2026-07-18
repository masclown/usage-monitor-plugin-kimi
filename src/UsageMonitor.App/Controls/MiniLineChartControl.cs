using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
// WPF+WinForms 混合项目下 MouseEventArgs / KeyEventArgs 出现在两个命名空间里，alias 到 WPF 侧。
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 迷你折线图控件（任务栏 / 卡片使用）。req-007 完整化后具备：
/// <list type="bullet">
/// <item>折线 + 渐变面积 + 最新点发光（沿用 v1 行为）</item>
/// <item>底部 X 轴等间距日期标签（4~5 个），数据来自 <see cref="Dates"/></item>
/// <item>右上角"近 7 天 / 近 30 天"分段切换按钮，启用条件为 <see cref="SupportsPeriodSwitch"/>；
///       点击触发 <see cref="PeriodChanged"/> 路由事件</item>
/// <item>Loading 蒙版：<see cref="IsLoading"/>=true 时控件半透明 + 中央"加载中..."文字 + 切换按钮变灰</item>
/// <item>真实 Tooltip：Title 取自 <see cref="Dates"/>，Detail 拼接 <see cref="ExtraTooltipLines"/></item>
/// </list>
/// 颜色档位统一取自 <see cref="UsageMonitor.App.Helpers.UsageTierScale"/>。
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

    // =====================================================================
    // req-007：折线图完整化新增依赖属性
    // =====================================================================

    /// <summary>每个数据点对应的日期字符串（"7/11" / "7月13日" / "2026-07-11"），用于 X 轴标签与 tooltip 标题。</summary>
    public static readonly DependencyProperty DatesProperty = DependencyProperty.Register(
        nameof(Dates), typeof(IReadOnlyList<string>), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(Array.Empty<string>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>tooltip 扩展文本行（每行一项；多行时按换行拼接展示）。</summary>
    public static readonly DependencyProperty ExtraTooltipLinesProperty = DependencyProperty.Register(
        nameof(ExtraTooltipLines), typeof(IReadOnlyList<string>), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>是否在右上角显示周期切换按钮（仅当插件声明 SupportsPeriodSwitch=true 时为 true）。</summary>
    public static readonly DependencyProperty SupportsPeriodSwitchProperty = DependencyProperty.Register(
        nameof(SupportsPeriodSwitch), typeof(bool), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>当前选中的周期（"7d" / "30d"），控件本身不强制校验，只用于按钮高亮与事件 payload。</summary>
    public static readonly DependencyProperty CurrentPeriodProperty = DependencyProperty.Register(
        nameof(CurrentPeriod), typeof(string), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(ChartPeriods.Week, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>是否处于加载态：为 true 时控件半透明 + 中央"加载中..."文字 + 切换按钮变灰。</summary>
    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading), typeof(bool), typeof(MiniLineChartControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// 周期切换路由事件（req-007）：用户点击右上角"近 7 天 / 近 30 天"按钮时由控件 RaiseEvent，
    /// VM 收到后调用 <c>IUsageProvider.SetPeriodAsync</c> 并切换 <see cref="IsLoading"/>。
    /// </summary>
    public static readonly RoutedEvent PeriodChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(PeriodChanged), RoutingStrategy.Bubble,
        typeof(EventHandler<PeriodChangedEventArgs>), typeof(MiniLineChartControl));

    /// <summary>周期切换事件，订阅者在 <see cref="PeriodChangedEventArgs.Period"/> 中拿到新值。</summary>
    public event EventHandler<PeriodChangedEventArgs> PeriodChanged
    {
        add => AddHandler(PeriodChangedEvent, value);
        remove => RemoveHandler(PeriodChangedEvent, value);
    }

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

    /// <summary>X 轴日期标签集合（req-007）。</summary>
    public IReadOnlyList<string> Dates
    {
        get => (IReadOnlyList<string>)GetValue(DatesProperty);
        set => SetValue(DatesProperty, value ?? Array.Empty<string>());
    }

    /// <summary>tooltip 扩展文本行（req-007）。</summary>
    public IReadOnlyList<string>? ExtraTooltipLines
    {
        get => (IReadOnlyList<string>?)GetValue(ExtraTooltipLinesProperty);
        set => SetValue(ExtraTooltipLinesProperty, value);
    }

    /// <summary>是否启用右上角周期切换按钮（req-007）。</summary>
    public bool SupportsPeriodSwitch
    {
        get => (bool)GetValue(SupportsPeriodSwitchProperty);
        set => SetValue(SupportsPeriodSwitchProperty, value);
    }

    /// <summary>当前周期（req-007）。</summary>
    public string CurrentPeriod
    {
        get => (string)GetValue(CurrentPeriodProperty);
        set => SetValue(CurrentPeriodProperty, value ?? ChartPeriods.Week);
    }

    /// <summary>是否处于加载态（req-007）。</summary>
    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>当前 hover 数据点索引</summary>
    private int _hoverIndex = -1;
    private double _plotLeft;
    private double _plotTop;
    private double _plotWidth;
    private double _plotHeight;

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

    // =====================================================================
    // 布局常量：顶部/底部/左右 padding 容纳 X 轴标签与右上角按钮
    // =====================================================================

    /// <summary>顶部 padding 高度：容纳右上角周期切换按钮（约 20px 高）+ 适当留白。</summary>
    private const double TopPaddingHeight = 24.0;

    /// <summary>底部 padding 高度：容纳 X 轴日期标签（约 14px 高）+ 适当留白。</summary>
    private const double BottomPaddingHeight = 18.0;

    /// <summary>左侧/右侧 padding：折线两端留白（沿用 v1 风格的 padding + 半线宽）。</summary>
    private const double SidePadding = 4.0;

    /// <summary>右上角周期按钮区宽度（容纳"近 7 天"和"近 30 天"两个分段按钮）。</summary>
    private const double PeriodButtonRowWidth = 132.0;

    /// <summary>右上角周期按钮高度。</summary>
    private const double PeriodButtonHeight = 22.0;

    /// <summary>右上角周期按钮距控件右边距。</summary>
    private const double PeriodButtonRightMargin = 6.0;

    /// <summary>右上角周期按钮距控件顶边距。</summary>
    private const double PeriodButtonTopMargin = 2.0;

    /// <summary>X 轴日期标签字体大小。</summary>
    private static readonly double AxisLabelFontSize = 10.0;

    /// <summary>Loading 蒙版中央"加载中..."文字字体大小。</summary>
    private static readonly double LoadingFontSize = 12.0;

    /// <summary>
    /// 绘制折线图：渐变面积 + 折线 + 最新点发光 + 右上角周期按钮 + X 轴日期标签 + Loading 蒙版。
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var values = Values;
        var hasData = values != null && values.Count >= 2;

        // 1) 始终绘制右上角周期按钮（SupportsPeriodSwitch=true 时），保证即使无数据也能切换。
        if (SupportsPeriodSwitch && !IsLoading)
        {
            DrawPeriodButtons(dc, width, height);
        }

        // 2) 始终绘制 X 轴日期标签（Dates 非空时），即使无折线也能展示日期骨架。
        DrawDateAxis(dc, width, height);

        // 3) 折线本体：没有足够数据时直接跳到 Loading 蒙版。
        if (hasData)
        {
            DrawLineChart(dc, values!, width, height);
        }

        // 4) Loading 蒙版：覆盖整个控件，半透明背景 + 中央文字 + 按钮变灰（仅 SupportsPeriodSwitch=true 时变灰）。
        if (IsLoading)
        {
            DrawLoadingOverlay(dc, width, height);
        }
    }

    /// <summary>绘制折线 + 面积 + 最新点发光。提取自原 OnRender（req-007 重构）。</summary>
    private void DrawLineChart(DrawingContext dc, IReadOnlyList<double> values, double width, double height)
    {
        var max = MaxValue <= 0 ? 100 : MaxValue;
        var plotLeft = SidePadding;
        var plotTop = TopPaddingHeight;
        var plotWidth = Math.Max(0, width - SidePadding * 2);
        var plotHeight = Math.Max(0, height - TopPaddingHeight - BottomPaddingHeight);

        // X 步长
        var stepX = plotWidth / (values.Count - 1);

        _plotLeft = plotLeft;
        _plotTop = plotTop;
        _plotWidth = plotWidth;
        _plotHeight = plotHeight;

        // 计算所有点坐标
        var points = new Point[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            var v = values[i];
            if (v < 0) v = 0;
            if (v > max) v = max;
            points[i] = new Point(plotLeft + i * stepX, plotTop + plotHeight * (1.0 - v / max));
        }

        var brush = SelectBrush(values[values.Count - 1]);
        var baseline = plotTop + plotHeight;

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

        // 3) hover 高亮：当前 hover 点画一个略大的描边圆
        if (_hoverIndex >= 0 && _hoverIndex < points.Length)
        {
            var hp = points[_hoverIndex];
            var dot = Math.Max(1.6, StrokeThickness);
            dc.DrawEllipse(MakeTranslucent(brush, 0x60), null, hp, dot * 1.8, dot * 1.8);
            dc.DrawEllipse(brush, null, hp, dot * 0.9, dot * 0.9);
        }

        // 4) 最新点发光圆点：外圈低透明 + 内圈实心
        var last = points[^1];
        var dot2 = Math.Max(1.6, StrokeThickness);
        dc.DrawEllipse(MakeTranslucent(brush, 0x40), null, last, dot2 * 2.2, dot2 * 2.2);
        dc.DrawEllipse(brush, null, last, dot2, dot2);
    }

    /// <summary>
    /// 绘制右上角"近 7 天 / 近 30 天"分段按钮（req-007）。
    /// 使用 <see cref="FormattedText"/> 绘制文字 + 背景矩形模拟分段样式；不引入 WPF RadioButton
    /// 子元素以避免 VisualTree 复杂度（命中测试在 <see cref="OnMouseLeftButtonDown"/> 中完成）。
    /// </summary>
    private void DrawPeriodButtons(DrawingContext dc, double width, double height)
    {
        // 按钮区位于顶部 padding 右上角
        var rowRight = width - PeriodButtonRightMargin;
        var rowLeft = rowRight - PeriodButtonRowWidth;
        var rowTop = PeriodButtonTopMargin;
        var rowBottom = rowTop + PeriodButtonHeight;
        var halfW = PeriodButtonRowWidth / 2.0;

        // 背景容器（圆角矩形，半透明）
        var containerBg = MakeTranslucent(ResolveThemeSurfaceAlt(), 0x55);
        DrawRoundedRect(dc, new Rect(rowLeft, rowTop, PeriodButtonRowWidth, PeriodButtonHeight), 4, containerBg, border: ResolveThemeBorder());

        // 当前激活色（取自当前折线 brush，让按钮与折线视觉关联）
        var accent = SelectBrushForButtons();
        var inactiveText = ResolveThemeTextSecondary();
        var activeText = ResolveThemeOnAccent();

        // 左半：近 7 天
        var leftIsActive = string.Equals(CurrentPeriod, ChartPeriods.Week, StringComparison.OrdinalIgnoreCase);
        DrawPeriodSegment(dc, new Rect(rowLeft, rowTop, halfW, PeriodButtonHeight),
            "近 7 天", leftIsActive, accent, activeText, inactiveText);

        // 右半：近 30 天
        var rightIsActive = string.Equals(CurrentPeriod, ChartPeriods.Month, StringComparison.OrdinalIgnoreCase);
        DrawPeriodSegment(dc, new Rect(rowLeft + halfW, rowTop, halfW, PeriodButtonHeight),
            "近 30 天", rightIsActive, accent, activeText, inactiveText);
    }

    /// <summary>绘制单个分段按钮（半边）；激活时填充实心 + 反色文字，未激活时透明底 + 次级文字色。</summary>
    private void DrawPeriodSegment(DrawingContext dc, Rect rect, string text,
        bool isActive, Brush accent, Brush activeText, Brush inactiveText)
    {
        if (isActive)
        {
            // 左半圆角 = 4，左下圆角 = 0；右半反之
            var isLeft = rect.Width > 0 && rect.X > 0; // 简化判断：实际根据 rect 决定（调用方保证）
            var path = BuildRoundedPath(rect, 4, isLeft ? RoundedSide.Left : RoundedSide.Right);
            dc.DrawGeometry(accent, null, path);
            DrawCenteredText(dc, text, rect, activeText, 11);
        }
        else
        {
            DrawCenteredText(dc, text, rect, inactiveText, 11);
        }
    }

    /// <summary>绘制 X 轴日期标签（req-007）：4~5 个等间距日期，等间距取自 Dates 数组。</summary>
    private void DrawDateAxis(DrawingContext dc, double width, double height)
    {
        var dates = Dates;
        if (dates == null || dates.Count == 0) return;

        // 选 4~5 个：取 Math.Min(5, dates.Count) 个等间距位置
        var labelCount = Math.Min(5, dates.Count);
        if (labelCount < 2) return; // 1 个点不画

        var plotLeft = SidePadding;
        var plotWidth = Math.Max(0, width - SidePadding * 2);
        var y = height - BottomPaddingHeight + 3;

        var textBrush = ResolveThemeTextTertiary();
        for (int k = 0; k < labelCount; k++)
        {
            // 第一个位置 = 第 0 个数据点；最后一个 = 最后一个数据点；中间等间距
            int idx;
            if (labelCount == 1) idx = 0;
            else if (k == 0) idx = 0;
            else if (k == labelCount - 1) idx = dates.Count - 1;
            else idx = (int)Math.Round((double)k * (dates.Count - 1) / (labelCount - 1));

            if (idx < 0 || idx >= dates.Count) continue;
            var label = dates[idx];
            if (string.IsNullOrEmpty(label)) continue;

            // X 坐标：等比缩放到 plotWidth
            var ratio = dates.Count > 1 ? (double)idx / (dates.Count - 1) : 0;
            var x = plotLeft + plotWidth * ratio;

            // 第一个标签左对齐，最后一个右对齐，中间居中
            var formatted = MakeText(label, AxisLabelFontSize, textBrush);
            double drawX = k switch
            {
                0 => x,
                _ when k == labelCount - 1 => x - formatted.Width,
                _ => x - formatted.Width / 2
            };
            dc.DrawText(formatted, new Point(Math.Max(0, drawX), y));
        }
    }

    /// <summary>绘制 Loading 蒙版：半透明白底 + 中央"加载中..."文字（req-007）。</summary>
    private void DrawLoadingOverlay(DrawingContext dc, double width, double height)
    {
        // 半透明遮罩：覆盖整个控件，让内容淡化（不依赖 Opacity 属性，避免影响子元素）
        var overlay = new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x14, 0x1A));
        if (overlay.CanFreeze) overlay.Freeze();
        dc.DrawRectangle(overlay, null, new Rect(0, 0, width, height));

        // 中央"加载中..."文字
        var text = "加载中...";
        var formatted = MakeText(text, LoadingFontSize, ResolveThemeTextPrimary());
        var x = (width - formatted.Width) / 2;
        var y = (height - formatted.Height) / 2;
        dc.DrawText(formatted, new Point(x, y));

        // 顶部按钮区灰色：模拟"按钮变灰"
        if (SupportsPeriodSwitch)
        {
            var rowRight = width - PeriodButtonRightMargin;
            var rowLeft = rowRight - PeriodButtonRowWidth;
            var rowTop = PeriodButtonTopMargin;
            var rowBottom = rowTop + PeriodButtonHeight;
            var halfW = PeriodButtonRowWidth / 2.0;

            // 容器底色
            var containerBg = MakeTranslucent(ResolveThemeSurfaceAlt(), 0x33);
            DrawRoundedRect(dc, new Rect(rowLeft, rowTop, PeriodButtonRowWidth, PeriodButtonHeight), 4, containerBg, border: ResolveThemeBorder());

            // 两个分段都用更淡的次级文字色绘制
            var dimText = ResolveThemeTextTertiary();
            DrawCenteredText(dc, "近 7 天", new Rect(rowLeft, rowTop, halfW, PeriodButtonHeight), dimText, 11);
            DrawCenteredText(dc, "近 30 天", new Rect(rowLeft + halfW, rowTop, halfW, PeriodButtonHeight), dimText, 11);
        }
    }

    /// <summary>在指定矩形中居中绘制文字（用 <see cref="FormattedText"/>）。</summary>
    private void DrawCenteredText(DrawingContext dc, string text, Rect rect, Brush brush, double fontSize)
    {
        var ft = MakeText(text, fontSize, brush);
        var x = rect.X + (rect.Width - ft.Width) / 2;
        var y = rect.Y + (rect.Height - ft.Height) / 2;
        dc.DrawText(ft, new Point(x, y));
    }

    /// <summary>用默认字体（Segoe UI / Microsoft YaHei UI 等）创建 FormattedText；不自动换行。</summary>
    private FormattedText MakeText(string text, double fontSize, Brush brush)
    {
        // 用 instance 自身作 visual 锚点，让 VisualTreeHelper.GetDpi 拿到真实 DPI。
        // 关键修复：原静态实现传 VisualTreeHelper.GetDpi(null) 会抛 NullReferenceException，
        // 触发路径：MiniMax 刷新后折线图 OnRender -> DrawPeriodButtons -> DrawPeriodSegment
        // -> DrawCenteredText -> MakeText，直接把 WPF Dispatcher 拖崩，
        // 表现为：TaskbarWindow 嵌入任务栏后立即消失、主窗口不再弹出、托盘图标不可见。
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(
                new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI, PingFang SC"),
                FontStyles.Normal,
                FontWeights.SemiBold,
                FontStretches.Normal),
            fontSize,
            brush,
            dpi);
        if (ft.MaxTextWidth > 0) ft.MaxTextWidth = ft.Width; // 防止自动换行
        return ft;
    }

    /// <summary>绘制圆角矩形（带可选描边）。</summary>
    private static void DrawRoundedRect(DrawingContext dc, Rect rect, double radius, Brush fill, Brush? border)
    {
        var path = BuildRoundedPath(rect, radius, RoundedSide.All);
        dc.DrawGeometry(fill, border is null ? null : new Pen(border, 1), path);
    }

    private enum RoundedSide { All, Left, Right }

    /// <summary>构造圆角矩形路径（支持只圆某一侧；用于分段按钮的半边圆角）。</summary>
    private static StreamGeometry BuildRoundedPath(Rect rect, double radius, RoundedSide side)
    {
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            // 简化实现：仅支持 All / Left / Right 三种；用绝对值钳制 radius 不超过半边
            var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2.0);
            if (side == RoundedSide.All)
            {
                ctx.BeginFigure(new Point(rect.X + r, rect.Y), true, true);
                ctx.LineTo(new Point(rect.Right - r, rect.Y), true, false);
                ctx.ArcTo(new Point(rect.Right, rect.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(rect.Right, rect.Bottom - r), true, false);
                ctx.ArcTo(new Point(rect.Right - r, rect.Bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(rect.X + r, rect.Bottom), true, false);
                ctx.ArcTo(new Point(rect.X, rect.Bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(rect.X, rect.Y + r), true, false);
                ctx.ArcTo(new Point(rect.X + r, rect.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            }
            else if (side == RoundedSide.Left)
            {
                // 左半圆角（4 角只有左上是圆角，左下也是圆角；右上右下直角）
                ctx.BeginFigure(new Point(rect.X + r, rect.Y), true, true);
                ctx.LineTo(new Point(rect.Right, rect.Y), true, false);
                ctx.LineTo(new Point(rect.Right, rect.Bottom), true, false);
                ctx.LineTo(new Point(rect.X + r, rect.Bottom), true, false);
                ctx.ArcTo(new Point(rect.X, rect.Bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                ctx.LineTo(new Point(rect.X, rect.Y + r), true, false);
                ctx.ArcTo(new Point(rect.X + r, rect.Y), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            else // Right
            {
                // 右半圆角
                ctx.BeginFigure(new Point(rect.X, rect.Y), true, true);
                ctx.LineTo(new Point(rect.Right - r, rect.Y), true, false);
                ctx.ArcTo(new Point(rect.Right, rect.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(rect.Right, rect.Bottom - r), true, false);
                ctx.ArcTo(new Point(rect.Right - r, rect.Bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(rect.X, rect.Bottom), true, false);
            }
        }
        path.Freeze();
        return path;
    }

    /// <summary>
    /// req-017：端点 hover 命中半径（DIP 像素）。鼠标仅在端点 ± 此范围内才触发 tooltip。
    /// </summary>
    private const double EndpointHitRadius = 8.0;

    /// <summary>
    /// req-017：鼠标移动时仅在端点 ± <see cref="EndpointHitRadius"/> 像素内触发 tooltip。
    /// <para>
    /// 原 req-007 实现在任意鼠标位置都 <see cref="TryGetTooltip"/> 返回 true → 贴线移动会一直弹 tooltip，
    /// 不符合"鼠标移到端点"的交互语义。本需求缩小触发范围为端点附近。
    /// </para>
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // req-034：诊断日志，确认 OnMouseMove 被调用
        UsageMonitor.Core.Services.FileLogger.Info("MiniLineChart",
            $"OnMouseMove: Values={Values?.Count ?? 0}, _plotWidth={_plotWidth:F1}");
        if (Values == null || Values.Count < 2 || _plotWidth <= 0)
        {
            HideTooltipIfShown();
            return;
        }

        var pos = e.GetPosition(this);
        var stepX = _plotWidth / (Values.Count - 1);

        // 仅在端点 ± 半径内才触发（req-017 Q2 A 决策）
        int nearestIndex = -1;
        double nearestDist = double.MaxValue;
        for (int i = 0; i < Values.Count; i++)
        {
            var px = _plotLeft + i * stepX;
            var dist = Math.Abs(pos.X - px);
            if (dist < EndpointHitRadius && dist < nearestDist)
            {
                nearestDist = dist;
                nearestIndex = i;
            }
        }

        if (nearestIndex >= 0)
        {
            if (nearestIndex != _hoverIndex)
            {
                _hoverIndex = nearestIndex;
                InvalidateVisual();
            }
            if (TryGetTooltip(new Point(_plotLeft + nearestIndex * stepX, 0), out var data))
                HoverTooltipPresenter.Show(this, data);
        }
        else
        {
            HideTooltipIfShown();
        }
    }

    /// <summary>
    /// req-017：清空 hover 状态并关闭 tooltip。
    /// <para>
    /// 仅在 hoverIndex 已激活时才重绘，避免每个 MouseMove 都触发 InvalidateVisual。
    /// </para>
    /// </summary>
    private void HideTooltipIfShown()
    {
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
        HoverTooltipPresenter.Hide(this);
    }

    /// <summary>鼠标离开图表区域后关闭 tooltip 并清除高亮点。</summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        HoverTooltipPresenter.Hide(this);
        InvalidateVisual();
    }

    /// <summary>
    /// 鼠标左键按下：处理右上角周期按钮点击（req-007）。
    /// <para>
    /// 命中"近 7 天"或"近 30 天"按钮矩形时，<see cref="CurrentPeriod"/> 设为对应值并 RaiseEvent
    /// <see cref="PeriodChangedEvent"/>；不命中按钮时不做任何处理，让 WPF 默认行为继续。
    /// </para>
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!SupportsPeriodSwitch || IsLoading) return;

        var pos = e.GetPosition(this);
        var hit = HitTestPeriodButton(pos);
        if (hit == null)
        {
            // req-020：诊断日志。未命中时记录鼠标位置 + 控件尺寸 + 期望按钮区范围，供排错。
            UsageMonitor.Core.Services.FileLogger.Info("MiniLineChartControl",
                $"OnMouseLeftButtonDown miss: pos={pos}, ActualWidth={ActualWidth}, ActualHeight={ActualHeight}, CurrentPeriod={CurrentPeriod}");
            return;
        }

        // 同样的周期重复点击不触发事件
        if (string.Equals(CurrentPeriod, hit, StringComparison.OrdinalIgnoreCase)) return;

        UsageMonitor.Core.Services.FileLogger.Info("MiniLineChartControl",
            $"OnMouseLeftButtonDown hit: {CurrentPeriod} -> {hit}");
        CurrentPeriod = hit;
        var args = new PeriodChangedEventArgs(hit) { Source = this };
        RaiseEvent(args);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <summary>
    /// req-020：周期切换按钮的额外点击命中 padding（DIP），避免“点不到按钮”问题。
    /// <para>
    /// 自定义绘制的按钮视觉区在右上角（132×22 px），但因为鼠标位置精度 + 窗体轻微偏移，靠近边缘的点击可能
    /// 落在视觉矩形外缘。本扩展使 HitTestPeriodButton 接受在视觉矩形 + 该 padding 范围内作为命中。
    /// </para>
    /// </summary>
    private const double PeriodButtonHitPadding = 4.0;

    /// <summary>
    /// req-007：根据点击位置判断命中哪个周期按钮（"7d" / "30d"），未命中返回 null。
    /// <para>
    /// req-020：接受视觉矩形 ± <see cref="PeriodButtonHitPadding"/> 像素范围内的点击作为命中，
    /// 避免“按钮太小”造成“近 30 天点不到”。点击未命中时也写一条 Info 日志供诊断。
    /// </para>
    /// </summary>
    private string? HitTestPeriodButton(Point pos)
    {
        var width = ActualWidth;
        var rowRight = width - PeriodButtonRightMargin;
        var rowLeft = rowRight - PeriodButtonRowWidth;
        var rowTop = PeriodButtonTopMargin;
        var rowBottom = rowTop + PeriodButtonHeight;
        var halfW = PeriodButtonRowWidth / 2.0;

        // req-020：在视觉矩形基础上额外接受 ± padding 范围内的点击
        var pad = PeriodButtonHitPadding;
        if (pos.Y < rowTop - pad || pos.Y > rowBottom + pad)
        {
            System.Diagnostics.Debug.WriteLine($"[MiniLineChart] PeriodButton miss: pos={pos} rowY=[{rowTop},{rowBottom}]");
            return null;
        }
        if (pos.X < rowLeft - pad || pos.X > rowRight + pad)
        {
            System.Diagnostics.Debug.WriteLine($"[MiniLineChart] PeriodButton miss: pos={pos} rowX=[{rowLeft},{rowRight}]");
            return null;
        }

        // req-020：pos.X 越界（负方向）但 Y 在范围内 → 默认落到 Week
        if (pos.X < rowLeft) return ChartPeriods.Week;
        // req-020：pos.X 越界（正方向）但 Y 在范围内 → 默认落到 Month
        if (pos.X > rowRight) return ChartPeriods.Month;

        // 左半 / 右半（视觉矩形内）
        if (pos.X < rowLeft + halfW) return ChartPeriods.Week;
        return ChartPeriods.Month;
    }

    /// <summary>键盘方向键浏览折线数据点，Enter 重显当前点提示。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Values == null || Values.Count == 0) return;
        var next = _hoverIndex < 0 ? 0 : _hoverIndex;
        next = e.Key switch
        {
            Key.Left or Key.Up => Math.Max(0, next - 1),
            Key.Right or Key.Down => Math.Min(Values.Count - 1, next + 1),
            Key.Home => 0,
            Key.End => Values.Count - 1,
            Key.Enter => next,
            _ => -1
        };
        if (next < 0) return;
        _hoverIndex = next;
        if (TryGetTooltip(new Point(_plotLeft + (_plotWidth <= 0 ? 0 : next * _plotWidth / Math.Max(1, Values.Count - 1)), 0), out var data))
            HoverTooltipPresenter.Show(this, data);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// 将控件内部坐标映射到 tooltip 数据（req-007：Title 优先取自 Dates，Detail 拼接 ExtraTooltipLines）。
    /// </summary>
    public bool TryGetTooltip(Point position, out HoverTooltipData data)
    {
        data = default!;
        if (Values == null || Values.Count == 0) return false;
        var index = GetIndex(position.X);
        var value = Values[index];
        var unit = string.IsNullOrWhiteSpace(ValueUnit) ? string.Empty : $" {ValueUnit}";
        var provider = string.IsNullOrWhiteSpace(ProviderName) ? "用量" : ProviderName;

        // Title：优先 Dates[index]（真实日期），否则用旧的 "Provider · 第 N 点" 形式兜底
        string title;
        if (Dates != null && index < Dates.Count && !string.IsNullOrEmpty(Dates[index]))
            title = Dates[index];
        else
            title = $"{provider} · 第 {index + 1} 点";

        var valueText = $"{value:0.##}{unit}";

        // Detail：优先 ExtraTooltipLines 多行拼接（req-007），否则保留 v1 的"时间标签"占位
        string? detail = null;
        if (ExtraTooltipLines != null && ExtraTooltipLines.Count > 0)
        {
            detail = string.Join("\n", ExtraTooltipLines);
        }
        else
        {
            detail = "时间标签：第 " + (index + 1) + " 个数据点";
        }

        data = new HoverTooltipData(title, valueText, detail);
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

    /// <summary>周期按钮专用 accent：取自当前最新数据点（保证按钮与折线颜色关联），无数据时回退到资源 AccentBrush。</summary>
    private Brush SelectBrushForButtons()
    {
        var values = Values;
        if (values != null && values.Count > 0)
            return SelectBrush(values[values.Count - 1]);
        return TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
    }

    /// <summary>从资源拿 SurfaceAltBrush，缺失时回退稳定颜色。</summary>
    private Brush ResolveThemeSurfaceAlt()
    {
        if (TryFindResource("SurfaceAltBrush") is Brush b) return b;
        return MakeFrozen(Color.FromArgb(0xE8, 0x1F, 0x24, 0x30));
    }

    /// <summary>从资源拿 BorderBrush，缺失时回退稳定颜色（用于按钮描边）。</summary>
    private Brush ResolveThemeBorder()
    {
        if (TryFindResource("TrackBrush") is Brush b) return b;
        return MakeFrozen(Color.FromArgb(0x44, 0x94, 0xA3, 0xB8));
    }

    /// <summary>次级文字色（未激活分段按钮用）。</summary>
    private Brush ResolveThemeTextSecondary()
    {
        if (TryFindResource("TextSecondaryBrush") is Brush b) return b;
        return MakeFrozen(Color.FromRgb(0xC4, 0xCF, 0xDD));
    }

    /// <summary>主文字色（加载提示用）。</summary>
    private Brush ResolveThemeTextPrimary()
    {
        if (TryFindResource("TextPrimaryBrush") is Brush b) return b;
        return MakeFrozen(Color.FromRgb(0xF8, 0xFA, 0xFC));
    }

    /// <summary>三级文字色（X 轴标签 / 灰色按钮用）。</summary>
    private Brush ResolveThemeTextTertiary()
    {
        if (TryFindResource("TextTertiaryBrush") is Brush b) return b;
        return MakeFrozen(Color.FromRgb(0x94, 0xA3, 0xB8));
    }

    /// <summary>激活色上的反色文字（OnAccentBrush）。</summary>
    private Brush ResolveThemeOnAccent()
    {
        if (TryFindResource("OnAccentBrush") is Brush b) return b;
        return MakeFrozen(Colors.White);
    }

    /// <summary>创建并冻结一个 SolidColorBrush（frozen 后可安全跨线程）。</summary>
    private static Brush MakeFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        if (b.CanFreeze) b.Freeze();
        return b;
    }

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

/// <summary>
/// <see cref="MiniLineChartControl.PeriodChanged"/> 路由事件的参数（req-007）。
/// </summary>
public sealed class PeriodChangedEventArgs : RoutedEventArgs
{
    /// <summary>用户点击的周期字符串（"7d" / "30d"）。</summary>
    public string Period { get; }

    /// <summary>构造周期切换事件参数。</summary>
    /// <param name="period">新周期（"7d" / "30d"）。</param>
    public PeriodChangedEventArgs(string period)
    {
        Period = period;
    }
}
