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
    /// <summary>日期 yyyy-MM-dd</summary>
    public string Day { get; set; } = string.Empty;

    /// <summary>该日某种"代表百分比"。各 Provider 折叠时一般取 EndUsedPercent。</summary>
    public double Percent { get; set; }

    /// <summary>该格子的背景画笔（XAML 端用 PercentToBrushConverter 算好）</summary>
    public Brush Background { get; set; } = Brushes.Transparent;
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
public class YearHeatMapControl : FrameworkElement
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

    public YearHeatMapControl()
    {
        MinHeight = 150;
        MinWidth = 320;
        // 订阅档位变更：底部图例与各档位颜色随档位表动态更新。
        if (System.Threading.Interlocked.Exchange(ref _tierSubscribed, 1) == 0)
            UsageMonitor.App.Helpers.UsageTierScale.TierChanged += OnTierChangedStatic;
    }

    private static int _tierSubscribed;

    /// <summary>档位表刷新后：触发所有热力图重绘（图例按 Tiers 重新取色）。</summary>
    private static void OnTierChangedStatic(object? sender, EventArgs e)
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
        var parsed = new List<(DateTime date, Brush bg)>();
        if (Cells != null)
        {
            foreach (var item in Cells)
            {
                if (item is YearHeatMapCell yc &&
                    DateTime.TryParseExact(yc.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt))
                {
                    parsed.Add((dt.Date, yc.Background));
                }
            }
        }
        parsed.Sort((a, b) => a.date.CompareTo(b.date));

        var colsCountMax = Math.Max(1, (int)Math.Floor((gridWidth + gap) / step));

        // 计算网格起始周一 + 需要的列数（列数超出可视宽度时保留最近若干周）
        DateTime gridStartMonday;
        int cols;
        if (parsed.Count > 0)
        {
            var first = parsed[0].date;
            var last = parsed[^1].date;
            int dowFirst = ((int)first.DayOfWeek + 6) % 7; // 周一=0
            gridStartMonday = first.AddDays(-dowFirst);
            int totalCols = (int)((last - gridStartMonday).TotalDays / 7) + 1;
            cols = Math.Min(totalCols, colsCountMax);
            if (totalCols > cols)
                gridStartMonday = gridStartMonday.AddDays((totalCols - cols) * 7);
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
        foreach (var (date, bg) in parsed)
        {
            int col = (int)((date - gridStartMonday).TotalDays / 7);
            if (col < 0 || col >= cols) continue;
            int dow = ((int)date.DayOfWeek + 6) % 7;
            DrawCell(dc, gridLeft + col * step, gridTop + dow * step, cell, bg);
            if (!monthFirstCol.ContainsKey(date.Month)) monthFirstCol[date.Month] = col;
        }

        // 3) 月份标签
        foreach (var kv in monthFirstCol)
        {
            if (kv.Value >= cols) continue;
            var t = MakeText($"{kv.Key}月", TextBrush, 10, dpi);
            dc.DrawText(t, new Point(gridLeft + kv.Value * step, 1));
        }

        // 4) 底部"少 → 多"图例（空 / 低 / 中 / 高）
        DrawLegend(dc, gridLeft, gridTop + rows * step + 4, gridWidth, dpi);
    }

    /// <summary>绘制底部图例：少 [空][低][中][高] 多。</summary>
    private void DrawLegend(DrawingContext dc, double left, double top, double gridWidth, double dpi)
    {
        double sw = 11, sgap = 3;
        // 图例色块 = 空档 + UsageTierScale 各档位（低/注意/中/高），随档位表增减自动同步。
        var tiers = UsageMonitor.App.Helpers.UsageTierScale.Tiers;
        var swatches = new Brush[tiers.Count + 1];
        swatches[0] = EmptyCellBrush;
        for (int i = 0; i < tiers.Count; i++)
            swatches[i + 1] = new SolidColorBrush(tiers[i].Color);
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
