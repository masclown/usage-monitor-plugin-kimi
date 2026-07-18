using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using FontFamily = System.Windows.Media.FontFamily;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 历史窗口单日单元格的颜色输入（XAML 端直接绑定已查表好的 brush）
/// </summary>
public class YearHeatMapCell
{
    /// <summary>完整数值文本；为空时 tooltip 回退到 Percent。</summary>
    public string ValueText { get; set; } = string.Empty;

    /// <summary>数值单位，例如 tokens、%</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>可选的同比前后 1 周平均等补充信息。</summary>
    public string ComparisonText { get; set; } = string.Empty;

    /// <summary>日期 yyyy-MM-dd</summary>
    public string Day { get; set; } = string.Empty;

    /// <summary>该日某种"代表百分比"。各 Provider 折叠时一般取 EndUsedPercent。</summary>
    public double Percent { get; set; }

    /// <summary>该格子的背景画刷（XAML 端用 PercentToBrushConverter 算好）</summary>
    public Brush Background { get; set; } = Brushes.Transparent;

    /// <summary>该日 Token 用量（req-009）。用于 <c>HeatMapTierScale.ResolveBrush</c> 重算背景色。</summary>
    public long Token { get; set; }
}

/// <summary>
/// 历史窗口"年度热力图"控件（GitHub 贡献图风格）。
/// <para>
/// - 以"周为列、星期为行（周一~周日）"的正确日历布局排布单元格
/// - 先铺一层圆角空格作为网底，再把有数据的单元格叠加上去
/// - 底部提供"少 → 多"语义图例（空 / 低 / 中 / 高）
/// - 颜色由 XAML 端 PercentToBrushConverter 预先算好后经 Cells 传入
/// </para>
/// </summary>
public class YearHeatMapControl : FrameworkElement, IHoverTooltipProvider
{
    /// <summary>单元格集合依赖属性</summary>
    public static readonly DependencyProperty CellsProperty = DependencyProperty.Register(
        nameof(Cells), typeof(IEnumerable), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(Array.Empty<YearHeatMapCell>(),
            FrameworkPropertyMetadataOptions.AffectsRender, OnCellsChanged));

    /// <summary>单个格子的边长（像素）</summary>
    public static readonly DependencyProperty CellSizeProperty = DependencyProperty.Register(
        nameof(CellSize), typeof(double), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(15.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>格子之间的间距</summary>
    public static readonly DependencyProperty CellGapProperty = DependencyProperty.Register(
        nameof(CellGap), typeof(double), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>空格子颜色</summary>
    public static readonly DependencyProperty EmptyCellBrushProperty = DependencyProperty.Register(
        nameof(EmptyCellBrush), typeof(Brush), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>文字颜色（行/月份/图例标签）</summary>
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable Cells
    {
        get => (IEnumerable)GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public double CellSize
    {
        get => (double)GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public double CellGap
    {
        get => (double)GetValue(CellGapProperty);
        set => SetValue(CellGapProperty, value);
    }

    public Brush EmptyCellBrush
    {
        get => (Brush)GetValue(EmptyCellBrushProperty);
        set => SetValue(EmptyCellBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    private readonly List<(Rect Bounds, YearHeatMapCell Cell)> _hitCells = new();
    private int _hoverIndex = -1;

    /// <summary>热力图控件构造：启用 Tab 聚焦和键盘浏览。</summary>
    public YearHeatMapControl()
    {
        MinHeight = 150;
        MinWidth = 320;
        Focusable = true;
        // req-018：订阅 HeatMapTierScale（6 档 Token 绝对值色阶）替代旧的 UsageTierScale（4 档百分比色阶）。
        // 两个订阅用同一个 _tierSubscribed 静态互斥锁保护，避免双重订阅。
        if (System.Threading.Interlocked.Exchange(ref _tierSubscribed, 1) == 0)
            UsageMonitor.App.Helpers.HeatMapTierScale.TierChanged += OnHeatMapTierChangedStatic;
    }

    private static int _tierSubscribed;

    /// <summary>req-018：热力图色阶（按 Provider Token）档位表刷新后，所有 YearHeatMapControl 重绘。</summary>
    private static void OnHeatMapTierChangedStatic(object? sender, EventArgs e)
    {
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
    /// req-018：当前控件绑定的 ProviderId，用于按 Provider 选色阶表（<see cref="HeatMapTierScale.ProviderTiers"/>）。
    /// 不传时回退到 <see cref="HeatMapTierScale.GenericDefaults"/> 兑底。
    /// </summary>
    public static readonly DependencyProperty ProviderIdProperty = DependencyProperty.Register(
        nameof(ProviderId), typeof(string), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>req-018：当前 ProviderId（不区分大小写去 ProviderTiers 取档位）。</summary>
    public string ProviderId
    {
        get => (string)GetValue(ProviderIdProperty);
        set => SetValue(ProviderIdProperty, value);
    }

    private static void OnCellsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (YearHeatMapControl)d;
        if (e.OldValue is INotifyCollectionChanged oldIncc)
            oldIncc.CollectionChanged -= c.OnCellsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newIncc)
            newIncc.CollectionChanged += c.OnCellsCollectionChanged;
        c.InvalidateVisual();
    }

    private void OnCellsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        _hitCells.Clear();

        var cell = Math.Max(4.0, CellSize);
        var gap = Math.Max(0.0, CellGap);
        var step = cell + gap;
        const double labelLeft = 26.0;   // 行标签宽度
        const double labelTop = 18.0;    // 月份标签高度
        const int rows = 7;              // 周一到周日
        var gridLeft = labelLeft;
        var gridTop = labelTop;
        var gridWidth = Math.Max(0, width - gridLeft - 8);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 行标签（一/二/.../日）
        var rowLabels = new[] { "一", "二", "三", "四", "五", "六", "日" };
        for (int r = 0; r < rows; r++)
        {
            var text = MakeText(rowLabels[r], TextBrush, 10, dpi);
            dc.DrawText(text, new Point(2, gridTop + r * step + (cell - text.Height) / 2.0));
        }

        // 解析所有单元格为 (日期, 画笔)，按日期升序
        var parsed = new List<(DateTime date, YearHeatMapCell cell)>();
        if (Cells != null)
        {
            foreach (var item in Cells)
            {
                if (item is YearHeatMapCell yc &&
                    DateTime.TryParseExact(yc.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt))
                {
                    parsed.Add((dt.Date, yc));
                }
            }
        }
        parsed.Sort((a, b) => a.date.CompareTo(b.date));

        var colsCountMax = Math.Max(1, (int)Math.Floor((gridWidth + gap) / step));

        // 计算网格起始周一 + 需要的列数（列数超出可视宽度时保留最近若干周）
        // req-021：网格起点改为以最后数据日期为终点、向左铺列（最新日期在右、左边无数据显空格）。
        DateTime gridStartMonday;
        int cols;
        if (parsed.Count > 0)
        {
            var first = parsed[0].date;
            var last = parsed[^1].date;
            int dowFirst = ((int)first.DayOfWeek + 6) % 7; // 周一=0
            int dowLast = ((int)last.DayOfWeek + 6) % 7; // 周一=0
            gridStartMonday = last.AddDays(-dowLast);
            // 计算首末跨度的总列数（含首尾之间缺的周）
            int totalCols = (int)((last - first).TotalDays / 7) + 1 + dowFirst;
            // req-021：右对齐——若实际列数 > 可视列数，从右侧截取（保留最新日期）
            cols = Math.Min(totalCols, colsCountMax);
            if (totalCols > cols)
            {
                // 起点 = 终点 - (cols - 1) 周
                gridStartMonday = gridStartMonday.AddDays(-(cols - 1) * 7);
            }
        }
        else
        {
            gridStartMonday = DateTime.Today;
            cols = colsCountMax;
        }

        // 1) 铺空格网底
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                DrawCell(dc, gridLeft + c * step, gridTop + r * step, cell, EmptyCellBrush);

        // 2) 叠加数据格 + 收集月份首列
        var monthFirstCol = new Dictionary<int, int>();
        foreach (var (date, cellData) in parsed)
        {
            int col = (int)((date - gridStartMonday).TotalDays / 7);
            if (col < 0 || col >= cols) continue;
            int dow = ((int)date.DayOfWeek + 6) % 7;
            var x = gridLeft + col * step;
            var y = gridTop + dow * step;
            DrawCell(dc, x, y, cell, cellData.Background);
            _hitCells.Add((new Rect(x, y, cell, cell), cellData));
            if (!monthFirstCol.ContainsKey(date.Month)) monthFirstCol[date.Month] = col;
        }

        // 3) 月份标签
        foreach (var kv in monthFirstCol)
        {
            if (kv.Value >= cols) continue;
            var t = MakeText($"{kv.Key}月", TextBrush, 10, dpi);
            dc.DrawText(t, new Point(gridLeft + kv.Value * step, 1));
        }

        // 4) 底部"少 → 多"图例（req-018：按 ProviderId 走 HeatMapTierScale，色块数 4~6 动态）
        DrawLegend(dc, gridLeft, gridTop + rows * step + 4, gridWidth, dpi, ProviderId);
    }

    /// <summary>鼠标移动时命中热力图日期并显示统一 tooltip。</summary>
    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (TryGetTooltip(e.GetPosition(this), out var data))
        {
            HoverTooltipPresenter.Show(this, data);
            InvalidateVisual();
        }
        // req-018：鼠标在空格网底 / 控件外时不弹 tooltip 是符合预期的，不打错误日志。
    }

    /// <summary>鼠标离开热力图后关闭 tooltip。</summary>
    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        HoverTooltipPresenter.Hide(this);
        InvalidateVisual();
    }

    /// <summary>通过方向键在有数据的日期之间浏览。</summary>
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_hitCells.Count == 0) return;
        var next = _hoverIndex < 0 ? 0 : _hoverIndex;
        next = e.Key switch
        {
            System.Windows.Input.Key.Left or System.Windows.Input.Key.Up => Math.Max(0, next - 1),
            System.Windows.Input.Key.Right or System.Windows.Input.Key.Down => Math.Min(_hitCells.Count - 1, next + 1),
            System.Windows.Input.Key.Home => 0,
            System.Windows.Input.Key.End => _hitCells.Count - 1,
            System.Windows.Input.Key.Enter => next,
            _ => -1
        };
        if (next < 0) return;
        _hoverIndex = next;
        if (TryGetTooltip(_hitCells[next].Bounds.Location, out var data))
            HoverTooltipPresenter.Show(this, data);
        e.Handled = true;
    }

    /// <summary>将内部坐标映射为热力图日期及其数值。</summary>
    public bool TryGetTooltip(Point position, out HoverTooltipData data)
    {
        data = default!;
        for (int i = 0; i < _hitCells.Count; i++)
        {
            var hit = _hitCells[i];
            if (!hit.Bounds.Contains(position)) continue;
            _hoverIndex = i;
            var value = string.IsNullOrWhiteSpace(hit.Cell.ValueText)
                ? $"{hit.Cell.Percent:0.##}"
                : hit.Cell.ValueText;
            var unit = string.IsNullOrWhiteSpace(hit.Cell.Unit) ? string.Empty : $" {hit.Cell.Unit}";
            data = new HoverTooltipData(hit.Cell.Day, $"{value}{unit}", hit.Cell.ComparisonText);
            return true;
        }
        return false;
    }

    /// <summary>
    /// req-018：绘制底部图例"少 → 多"。色阶改走 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale"/>（按 Provider Token 绝对值分档）。
    /// 色块数 = 当前 Provider 的档位数（MiniMax 6 档 / 其他 4 档），动态自适应。
    /// </summary>
    private void DrawLegend(DrawingContext dc, double left, double top, double gridWidth, double dpi, string? providerId)
    {
        double sw = 11, sgap = 3;

        // req-018：从 HeatMapTierScale 按 ProviderId 取色阶表。空 / 未知 → 走 GenericDefaults。
        var key = (providerId ?? string.Empty).Trim();
        IReadOnlyList<UsageMonitor.App.Helpers.HeatMapTier> tiers;
        if (!string.IsNullOrEmpty(key) && UsageMonitor.App.Helpers.HeatMapTierScale.ProviderTiers.TryGetValue(key, out var t) && t.Count > 0)
            tiers = t;
        else
            tiers = UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults;

        // 色块数组：首块 = "无用量"档（HeatMapTierScale.MiniMaxDefaults[0] / GenericDefaults[0]），
        // 与 EmptyCellBrush 视觉上区分（图例本身就是色阶提示，不是空格网底）。
        var swatches = new Brush[tiers.Count + 1];
        swatches[0] = tiers[0].ToBrush();
        for (int i = 1; i < tiers.Count; i++)
            swatches[i] = tiers[i].ToBrush();

        var less = MakeText("少", TextBrush, 10, dpi);
        var more = MakeText("多", TextBrush, 10, dpi);
        double total = less.Width + 4 + swatches.Length * (sw + sgap) + 4 + more.Width;
        double x = left + gridWidth - total;
        if (x < left) x = left;
        double cy = top + sw / 2.0;

        dc.DrawText(less, new Point(x, cy - less.Height / 2.0));
        x += less.Width + 4;
        foreach (var b in swatches)
        {
            DrawCell(dc, x, top, sw, b);
            x += sw + sgap;
        }
        x += 1;
        dc.DrawText(more, new Point(x, cy - more.Height / 2.0));
    }

    /// <summary>绘制单个圆角方格</summary>
    private static void DrawCell(DrawingContext dc, double x, double y, double size, Brush brush)
    {
        var rect = new Rect(x, y, size, size);
        var radius = Math.Min(3.5, size * 0.25);
        var geometry = new RectangleGeometry(rect, radius, radius);
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private FormattedText MakeText(string text, Brush brush, double size, double dpi)
        => new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal,
                FontWeights.SemiBold, FontStretches.Normal),
            size, brush, dpi);

    private Brush FindBrush(string key, Color fallback)
    {
        if (TryFindResource(key) is Brush b) return b;
        var f = new SolidColorBrush(fallback); f.Freeze(); return f;
    }
}
