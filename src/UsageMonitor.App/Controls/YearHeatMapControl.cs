using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
    // req-067 B23：Typeface 缓存，避免每次 OnRender 重复创建
    private static readonly Typeface LabelTypeface = new(
        new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

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
    /// <summary>req-074：空单元格色，默认主题 TrackBrush。</summary>
    public static readonly DependencyProperty EmptyCellBrushProperty = DependencyProperty.Register(
        nameof(EmptyCellBrush), typeof(Brush), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>文字颜色（行/月份/图例标签）</summary>
    /// <summary>req-074：文本色，默认主题 TextSecondaryBrush。</summary>
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>req-105：Tooltip 字段白名单（卡片管理 tooltip 字段配置驱动）。
    /// <para>null/空 = 展示全部（向后兼容）；非空 = 仅展示列表内字段（含虚拟字段 __field_name__/__date__）。</para>
    /// </summary>
    public static readonly DependencyProperty TooltipFieldsProperty = DependencyProperty.Register(
        nameof(TooltipFields), typeof(System.Collections.Generic.IReadOnlyList<string>), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(null));

    /// <summary>req-105：热力图主值字段名（SDK 标准字段，用于 TooltipFields 白名单匹配，如 daily_cache_hit_value）。</summary>
    public static readonly DependencyProperty TooltipValueFieldProperty = DependencyProperty.Register(
        nameof(TooltipValueField), typeof(string), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(null));

    /// <summary>req-105：主值字段中文显示名（「字段名称」虚拟字段勾选时作为独立标签行展示）。</summary>
    public static readonly DependencyProperty TooltipFieldLabelProperty = DependencyProperty.Register(
        nameof(TooltipFieldLabel), typeof(string), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(null));

    /// <summary>问题10：对比行字段名（如 daily_token_value）。非 null 时，ComparisonText 行仅在该字段被勾选（或无过滤）时展示。</summary>
    public static readonly DependencyProperty TooltipComparisonFieldProperty = DependencyProperty.Register(
        nameof(TooltipComparisonField), typeof(string), typeof(YearHeatMapControl),
        new FrameworkPropertyMetadata(null));

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

    /// <summary>req-105：Tooltip 字段白名单（null/空 = 展示全部）。</summary>
    public System.Collections.Generic.IReadOnlyList<string>? TooltipFields
    {
        get => (System.Collections.Generic.IReadOnlyList<string>?)GetValue(TooltipFieldsProperty);
        set => SetValue(TooltipFieldsProperty, value);
    }

    /// <summary>req-105：热力图主值字段名（用于白名单匹配）。</summary>
    public string? TooltipValueField
    {
        get => (string?)GetValue(TooltipValueFieldProperty);
        set => SetValue(TooltipValueFieldProperty, value);
    }

    /// <summary>req-105：主值字段中文显示名（字段名称行）。</summary>
    public string? TooltipFieldLabel
    {
        get => (string?)GetValue(TooltipFieldLabelProperty);
        set => SetValue(TooltipFieldLabelProperty, value);
    }

    /// <summary>问题10：对比行字段名（null = 对比行跟随主值字段，旧行为）。</summary>
    public string? TooltipComparisonField
    {
        get => (string?)GetValue(TooltipComparisonFieldProperty);
        set => SetValue(TooltipComparisonFieldProperty, value);
    }

    private readonly List<(Rect Bounds, YearHeatMapCell Cell)> _hitCells = new();
    private int _hoverIndex = -1;
    // req-046：tooltip 悬停延迟定时器（100ms）
    private DispatcherTimer? _tooltipDelayTimer;
    private HoverTooltipData? _pendingTooltipData;

    /// <summary>热力图控件构造：启用 Tab 聚焦和键盘浏览。</summary>
    /// <summary>
    /// 构造函数。req-074：从主题资源解析 Brush 默认值。
    /// </summary>
    public YearHeatMapControl()
    {
        MinHeight = 150;
        MinWidth = 320;
        Focusable = true;

        // req-074：从主题资源解析 Brush 默认值
        if (EmptyCellBrush == null)
            SetValue(EmptyCellBrushProperty, TryFindResource("TrackBrush") as Brush ?? Brushes.Transparent);
        if (TextBrush == null)
            SetValue(TextBrushProperty, TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.LightGray);

        // req-063 B9：订阅 Unloaded 事件，控件卸载时解绑 CollectionChanged
        Unloaded += OnControlUnloaded;
        // req-018：订阅 HeatMapTierScale（6 档 Token 绝对值色阶）替代旧的 UsageTierScale（4 档百分比色阶）。
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

    /// <summary>req-063 B9：跟踪当前订阅的集合，用于 OnUnloaded 时解绑。</summary>
    private INotifyCollectionChanged? _subscribed;

    // req-067 B24：预排序缓存，避免每次 OnRender 重复解析和排序
    private List<(DateTime date, YearHeatMapCell cell)>? _sortedCellsCache;

    private static void OnCellsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (YearHeatMapControl)d;
        if (e.OldValue is INotifyCollectionChanged oldIncc)
            oldIncc.CollectionChanged -= c.OnCellsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newIncc)
        {
            newIncc.CollectionChanged += c.OnCellsCollectionChanged;
            c._subscribed = newIncc;
        }
        else
        {
            c._subscribed = null;
        }
        // req-067 B24：Cells 变化时清空缓存，下次 OnRender 时重建
        c._sortedCellsCache = null;
        c.InvalidateVisual();
    }

    /// <summary>req-063 B9：控件卸载时解绑 CollectionChanged，防止内存泄漏。</summary>
    private void OnControlUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed != null)
        {
            _subscribed.CollectionChanged -= OnCellsCollectionChanged;
            _subscribed = null;
        }
    }

    private void OnCellsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // req-067 B24：集合内容变化时清空缓存
        _sortedCellsCache = null;
        InvalidateVisual();
    }

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

        // req-067 B24：使用预排序缓存，避免每次 OnRender 重复解析和排序
        var parsed = _sortedCellsCache;
        if (parsed == null)
        {
            parsed = new List<(DateTime date, YearHeatMapCell cell)>();
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
            _sortedCellsCache = parsed;
        }

        var colsCountMax = Math.Max(1, (int)Math.Floor((gridWidth + gap) / step));

        // req-033：始终用满可视列数，最新日期在最右列，无数据日显空格。
        // 网格起点 = 最新日期所在周一 - (colsCountMax - 1) × 7 天。
        DateTime gridStartMonday;
        int cols;
        if (parsed.Count > 0)
        {
            var last = parsed[^1].date;
            int dowLast = ((int)last.DayOfWeek + 6) % 7; // 周一=0
            // 终点周一 = 最新日期所在周的周一
            var endMonday = last.AddDays(-dowLast);
            // 起点周一 = 终点周一 - (可视列数 - 1) 周
            cols = colsCountMax;
            gridStartMonday = endMonday.AddDays(-(cols - 1) * 7);
        }
        else
        {
            gridStartMonday = DateTime.Today;
            cols = colsCountMax;
        }

        // 1) 预计算数据格位置（修复1：避免空格网底与数据格双重绘制导致浅色主题下出现灰色边框）
        var dataCellPositions = new HashSet<(int col, int row)>();
        var dataCellDraws = new List<(double x, double y, YearHeatMapCell cellData, int col)>();
        foreach (var (date, cellData) in parsed)
        {
            int col = (int)((date - gridStartMonday).TotalDays / 7);
            if (col < 0 || col >= cols) continue;
            int dow = ((int)date.DayOfWeek + 6) % 7;
            dataCellPositions.Add((col, dow));
            dataCellDraws.Add((gridLeft + col * step, gridTop + dow * step, cellData, col));
        }

        // 2) 铺空格网底（仅绘制无数据的位置，避免反锯齿边缘透出底色形成边框）
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
            {
                if (dataCellPositions.Contains((c, r))) continue;
                DrawCell(dc, gridLeft + c * step, gridTop + r * step, cell, EmptyCellBrush);
            }

        // 3) 叠加数据格 + 收集月份首列
        // 修复3：Token<=0 的格子使用 EmptyCellBrush（随主题动态切换），避免硬编码颜色与主题不匹配
        var monthFirstCol = new Dictionary<int, int>();
        foreach (var (x, y, cellData, col) in dataCellDraws)
        {
            var bg = cellData.Token > 0 ? cellData.Background : EmptyCellBrush;
            DrawCell(dc, x, y, cell, bg);
            _hitCells.Add((new Rect(x, y, cell, cell), cellData));
        }
        foreach (var (date, cellData) in parsed)
        {
            int col = (int)((date - gridStartMonday).TotalDays / 7);
            if (col < 0 || col >= cols) continue;
            if (!monthFirstCol.ContainsKey(date.Month)) monthFirstCol[date.Month] = col;
        }

        // 4) 月份标签
        foreach (var kv in monthFirstCol)
        {
            if (kv.Value >= cols) continue;
            var t = MakeText($"{kv.Key}月", TextBrush, 10, dpi);
            dc.DrawText(t, new Point(gridLeft + kv.Value * step, 1));
        }

        // 5) 底部"少 → 多"图例（req-018：按 ProviderId 走 HeatMapTierScale，色块数 4~6 动态）
        DrawLegend(dc, gridLeft, gridTop + rows * step + 4, gridWidth, dpi, ProviderId);
    }

    /// <summary>鼠标移动时命中热力图日期并显示统一 tooltip。</summary>
    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // req-046 修复：记录之前的 hover 索引，只在索引变化时才重绘和更新 tooltip
        var prevHoverIndex = _hoverIndex;
        if (TryGetTooltip(e.GetPosition(this), out var data))
        {
            // TryGetTooltip 内部会修改 _hoverIndex，所以用 prevHoverIndex 判断是否变化
            if (_hoverIndex != prevHoverIndex)
            {
                InvalidateVisual();
                // req-046：不立即显示 tooltip，启动 100ms 延迟定时器
                _pendingTooltipData = data;
                StartTooltipDelayTimer();
            }
            // 索引未变化时不重绘、不更新 tooltip，避免快速移动时闪烁
        }
        else
        {
            StopTooltipDelayTimer();
            // 鼠标不在任何日期方格上，关闭 tooltip
            if (prevHoverIndex >= 0)
            {
                _hoverIndex = -1;
                HoverTooltipPresenter.Hide(this);
                InvalidateVisual();
            }
        }
    }

    /// <summary>鼠标离开热力图后关闭 tooltip。</summary>
    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        StopTooltipDelayTimer();
        _hoverIndex = -1;
        HoverTooltipPresenter.Hide(this);
        InvalidateVisual();
    }

    /// <summary>req-046：启动 100ms 延迟定时器，鼠标静止后显示 tooltip。</summary>
    private void StartTooltipDelayTimer()
    {
        if (_tooltipDelayTimer == null)
        {
            _tooltipDelayTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _tooltipDelayTimer.Tick += (_, _) =>
            {
                _tooltipDelayTimer.Stop();
                if (_pendingTooltipData != null)
                {
                    HoverTooltipPresenter.Show(this, _pendingTooltipData);
                    _pendingTooltipData = null;
                }
            };
        }
        _tooltipDelayTimer.Stop();
        _tooltipDelayTimer.Start();
    }

    /// <summary>req-046：停止延迟定时器。</summary>
    private void StopTooltipDelayTimer()
    {
        _tooltipDelayTimer?.Stop();
        _pendingTooltipData = null;
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

    /// <summary>将内部坐标映射为热力图日期及其数值。
    /// <para>问题4：启用字段过滤时，内容行严格按用户保存的字段顺序生成（拖拽排序即时生效）；
    /// 问题5：「字段名称」行仅在主值字段同时被勾选时展示（避免只勾对比字段时错误显示主值字段名）。</para>
    /// </summary>
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
            var valueLine = $"{value}{unit}";

            // req-105 三态语义：TooltipFields == null → 不过滤（日期标题 + 主值 + 对比行，向后兼容）。
            var fields = TooltipFields;
            if (fields == null)
            {
                string? d = !string.IsNullOrEmpty(hit.Cell.ComparisonText) ? hit.Cell.ComparisonText : null;
                data = new HoverTooltipData(hit.Cell.Day, valueLine, d);
                return true;
            }

            // 白名单过滤：按用户保存的字段顺序逐一生成内容行（问题4）。
            bool showValue = string.IsNullOrEmpty(TooltipValueField) || fields.Contains(TooltipValueField);
            var lines = new System.Collections.Generic.List<string>();
            string title = string.Empty;
            for (int fi = 0; fi < fields.Count; fi++)
            {
                var f = fields[fi];
                if (string.Equals(f, UsageMonitor.App.Helpers.TooltipFieldCatalog.DateVirtual, StringComparison.OrdinalIgnoreCase))
                {
                    // 问题6：日期仅在配置首位时作标题行（旧视觉不变）；排在其它字段之后时按配置位置插入内容行，不再强制置顶。
                    if (fi == 0) title = hit.Cell.Day;
                    else lines.Add(hit.Cell.Day);
                }
                else if (string.Equals(f, UsageMonitor.App.Helpers.TooltipFieldCatalog.FieldNameVirtual, StringComparison.OrdinalIgnoreCase))
                {
                    // 问题5：字段名称行 = 主值字段的显示名，仅在主值字段也被勾选时展示，名称与数值始终对应。
                    if (showValue && !string.IsNullOrEmpty(TooltipFieldLabel))
                        lines.Add(TooltipFieldLabel!);
                }
                else if (!string.IsNullOrEmpty(TooltipValueField) &&
                         string.Equals(f, TooltipValueField, StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add(valueLine);
                }
                else if (!string.IsNullOrEmpty(TooltipComparisonField) &&
                         string.Equals(f, TooltipComparisonField, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(hit.Cell.ComparisonText)) lines.Add(hit.Cell.ComparisonText);
                }
            }

            // 无任何可展示内容时不弹 tooltip（问题3）。
            if (string.IsNullOrEmpty(title) && lines.Count == 0) return false;

            // 首行内容进 Value 槽（加粗强调），其余进 Detail，保持用户拖拽顺序。
            var valueSlot = lines.Count > 0 ? lines[0] : string.Empty;
            string? detail = lines.Count > 1 ? string.Join("\n", lines.Skip(1)) : null;
            data = new HoverTooltipData(title, valueSlot, detail);
            return true;
        }
        return false;
    }

    /// <summary>
    /// req-018：绘制底部图例"少 → 多"。色阶改走 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale"/>（按 Provider Token 绝对值分档）。
    /// 色块数 = 当前 Provider 的档位数（持久化/声明色阶档数，缺省 4 档），动态自适应。
    /// </summary>
    private void DrawLegend(DrawingContext dc, double left, double top, double gridWidth, double dpi, string? providerId)
    {
        double sw = 11, sgap = 3;

        // req-018 / Stage E：按 ProviderId 取色阶表：用户持久化 → 插件声明默认 → GenericDefaults。
        var key = (providerId ?? string.Empty).Trim();
        IReadOnlyList<UsageMonitor.App.Helpers.HeatMapTier> tiers;
        if (!string.IsNullOrEmpty(key) && UsageMonitor.App.Helpers.HeatMapTierScale.ProviderTiers.TryGetValue(key, out var t) && t.Count > 0)
            tiers = t;
        else
            tiers = UsageMonitor.App.Helpers.HeatMapTierScale.GetDeclaredDefaults(key)
                ?? UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults;

        // 修复3：首块 = "无用量"档，使用 EmptyCellBrush（随主题切换），与网格空格视觉一致；
        // 其余色块按声明/持久化色阶展示。
        var swatches = new Brush[tiers.Count];
        swatches[0] = EmptyCellBrush;
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
            LabelTypeface, size, brush, dpi);

    private Brush FindBrush(string key, Color fallback)
    {
        if (TryFindResource(key) is Brush b) return b;
        var f = new SolidColorBrush(fallback); f.Freeze(); return f;
    }
}
