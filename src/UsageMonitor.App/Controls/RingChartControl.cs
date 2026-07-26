using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;  // req-094：DoubleAnimation / QuadraticEase 用于 Percent 平滑过渡
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Typeface = System.Windows.Media.Typeface;
using FlowDirection = System.Windows.FlowDirection;

namespace UsageMonitor.App.Controls;

/// <summary>
/// 圆环进度图控件（REQ-003 增强版 + 原 Percent-only 行为兼容）。
/// <list type="bullet">
///   <item><description>中心数字可配置（<see cref="MetricKey"/>）：Percent / Credits / WeeklyLimit / RemainingQuota / ApiTokenUsed。</description></item>
///   <item><description>圆环中心上方叠 Provider Logo（<see cref="IconPath"/>），下方显示数字，垂直堆叠居中。</description></item>
///   <item><description>鼠标 hover + 滚轮循环切换 metric；离开后 sticky 计时器到点回默认。</description></item>
///   <item><description>老虎机式切换动画（向上滚出旧值、滚入新值），时长 <see cref="SwitchAnimationMs"/>。</description></item>
///   <item><description>保留原 Percent / ProviderName / ResetTimeText / IHoverTooltipProvider 等 API 完全兼容。</description></item>
/// </list>
/// </summary>
public class RingChartControl : FrameworkElement, IHoverTooltipProvider
{
    // req-067 B23：Typeface 缓存，避免每次 OnRender 重复创建
    private static readonly Typeface CenterTypeface = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    // =========================
    // 依赖属性（原版）
    // =========================

    /// <summary>Provider 短名称，用于 tooltip 标题</summary>
    public static readonly DependencyProperty ProviderNameProperty = DependencyProperty.Register(
        nameof(ProviderName), typeof(string), typeof(RingChartControl),
        new FrameworkPropertyMetadata(string.Empty));

    /// <summary>重置时间文案</summary>
    public static readonly DependencyProperty ResetTimeTextProperty = DependencyProperty.Register(
        nameof(ResetTimeText), typeof(string), typeof(RingChartControl),
        new FrameworkPropertyMetadata(string.Empty));

    /// <summary>百分比（0-100）</summary>
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>控件尺寸（直径，默认 44）</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(44.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>环线粗细（默认 5）</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>背景轨道颜色。req-074：默认值改为主题资源 TrackBrush。</summary>
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>进度条颜色。req-074：默认值改为主题资源 TextPrimaryBrush。</summary>
    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>警告色。req-074：默认值改为主题资源 WarningBrush。</summary>
    public static readonly DependencyProperty WarningBrushProperty = DependencyProperty.Register(
        nameof(WarningBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>危险色。req-074：默认值改为主题资源 DangerBrush。</summary>
    public static readonly DependencyProperty DangerBrushProperty = DependencyProperty.Register(
        nameof(DangerBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>警告阈值（默认 60）</summary>
    public static readonly DependencyProperty WarningThresholdProperty = DependencyProperty.Register(
        nameof(WarningThreshold), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>危险阈值（默认 85）</summary>
    public static readonly DependencyProperty DangerThresholdProperty = DependencyProperty.Register(
        nameof(DangerThreshold), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(85.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // =========================
    // 依赖属性（REQ-003 增强）
    // =========================

    /// <summary>Provider Logo 文件路径（pack:// 或文件路径）。空时仅显示数字。</summary>
    public static readonly DependencyProperty IconPathProperty = DependencyProperty.Register(
        nameof(IconPath), typeof(string), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>REQ-003：当前中心 metric 键（参见 <see cref="RingChartMetricKeys"/>）。</summary>
    public static readonly DependencyProperty MetricKeyProperty = DependencyProperty.Register(
        nameof(MetricKey), typeof(string), typeof(RingChartControl),
        new FrameworkPropertyMetadata(RingChartMetricKeys.Percent, OnMetricKeyChanged));

    /// <summary>REQ-003：5 种 metric 的"取数委托"列表，由宿主装配时填充。</summary>
    public static readonly DependencyProperty MetricProvidersProperty = DependencyProperty.Register(
        nameof(MetricProviders), typeof(IReadOnlyList<IRingMetricProvider>), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null, OnMetricProvidersChanged));

    /// <summary>REQ-003：滚轮循环切换顺序。空时回退到 <see cref="RingChartMetricKeys.DefaultOrder"/>。</summary>
    public static readonly DependencyProperty MetricOrderProperty = DependencyProperty.Register(
        nameof(MetricOrder), typeof(IReadOnlyList<string>), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null, OnMetricOrderChanged));

    /// <summary>REQ-003：sticky 秒数；&lt;=0 表示不回退默认。</summary>
    public static readonly DependencyProperty StickySecondsProperty = DependencyProperty.Register(
        nameof(StickySeconds), typeof(double), typeof(RingChartControl),
        new FrameworkPropertyMetadata(5.0));

    /// <summary>REQ-003：切换动画毫秒数；&lt;=0 禁用动画。</summary>
    public static readonly DependencyProperty SwitchAnimationMsProperty = DependencyProperty.Register(
        nameof(SwitchAnimationMs), typeof(int), typeof(RingChartControl),
        new FrameworkPropertyMetadata(180));

    /// <summary>REQ-005 SDK 兼容：当前要显示的中心文本（adapter / 外部直接覆盖）。</summary>
    public static readonly DependencyProperty CenterTextProperty = DependencyProperty.Register(
        nameof(CenterText), typeof(string), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>req-026：当前 Provider 已启用的 metric key 集合（决定中心数字是否显浅灰）。
    /// <para>解析由 <c>RingChartMetricResolver</c> 完成；主窗口装配时把结果绑到本属性。
    /// 未设置或为空集合时按"全部启用"处理（不显灰，保留旧行为）。</para></summary>
    public static readonly DependencyProperty EnabledMetricsProperty = DependencyProperty.Register(
        nameof(EnabledMetrics), typeof(IReadOnlyList<string>), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>req-026：当前 metric 被关闭时中心数字显示的画笔。req-074：默认改主题资源 TextTertiaryBrush。</summary>
    public static readonly DependencyProperty DisabledBrushProperty = DependencyProperty.Register(
        nameof(DisabledBrush), typeof(Brush), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>问题6：是否启用色阶直驱模式——进度弧颜色每次重绘时直接按百分比命中色阶档位
    /// （<see cref="TierColorConfigs"/> 私有档位优先，回退全局 UsageTierScale），不走 Warning/Danger 阈值链。
    /// 任务栏迷你半圆环使用本模式，保证进度颜色严格跟随色阶设置。</summary>
    public static readonly DependencyProperty UseTierScaleProperty = DependencyProperty.Register(
        nameof(UseTierScale), typeof(bool), typeof(RingChartControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>问题6：插件声明的私有色阶档位（null/空 = 回退全局 UsageTierScale 色阶）。仅 UseTierScale 模式消费。</summary>
    public static readonly DependencyProperty TierColorConfigsProperty = DependencyProperty.Register(
        nameof(TierColorConfigs), typeof(IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig>), typeof(RingChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>问题6：是否启用色阶直驱模式（CLR 包装）。</summary>
    public bool UseTierScale
    {
        get => (bool)GetValue(UseTierScaleProperty);
        set => SetValue(UseTierScaleProperty, value);
    }

    /// <summary>问题6：私有色阶档位（CLR 包装）。</summary>
    public IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig>? TierColorConfigs
    {
        get => (IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig>?)GetValue(TierColorConfigsProperty);
        set => SetValue(TierColorConfigsProperty, value);
    }

    /// <summary>Provider 短名称</summary>
    public string ProviderName
    {
        get => (string)GetValue(ProviderNameProperty);
        set => SetValue(ProviderNameProperty, value);
    }

    /// <summary>重置时间文案</summary>
    public string ResetTimeText
    {
        get => (string)GetValue(ResetTimeTextProperty);
        set => SetValue(ResetTimeTextProperty, value);
    }

    /// <summary>百分比</summary>
    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>直径</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>环线粗细</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>背景轨道颜色</summary>
    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>进度条颜色</summary>
    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    /// <summary>警告色</summary>
    public Brush WarningBrush
    {
        get => (Brush)GetValue(WarningBrushProperty);
        set => SetValue(WarningBrushProperty, value);
    }

    /// <summary>危险色</summary>
    public Brush DangerBrush
    {
        get => (Brush)GetValue(DangerBrushProperty);
        set => SetValue(DangerBrushProperty, value);
    }

    /// <summary>警告阈值</summary>
    public double WarningThreshold
    {
        get => (double)GetValue(WarningThresholdProperty);
        set => SetValue(WarningThresholdProperty, value);
    }

    /// <summary>危险阈值</summary>
    public double DangerThreshold
    {
        get => (double)GetValue(DangerThresholdProperty);
        set => SetValue(DangerThresholdProperty, value);
    }

    /// <summary>Provider Logo 文件路径。</summary>
    public string? IconPath
    {
        get => (string?)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    /// <summary>REQ-003：当前中心 metric 键。</summary>
    public string MetricKey
    {
        get => (string)GetValue(MetricKeyProperty);
        set => SetValue(MetricKeyProperty, value);
    }

    /// <summary>REQ-003：5 种 metric 取数委托列表。</summary>
    public IReadOnlyList<IRingMetricProvider>? MetricProviders
    {
        get => (IReadOnlyList<IRingMetricProvider>?)GetValue(MetricProvidersProperty);
        set => SetValue(MetricProvidersProperty, value);
    }

    /// <summary>REQ-003：metric 切换顺序。</summary>
    public IReadOnlyList<string>? MetricOrder
    {
        get => (IReadOnlyList<string>?)GetValue(MetricOrderProperty);
        set => SetValue(MetricOrderProperty, value);
    }

    /// <summary>REQ-003：sticky 秒数；&lt;=0 禁用回退。</summary>
    public double StickySeconds
    {
        get => (double)GetValue(StickySecondsProperty);
        set => SetValue(StickySecondsProperty, value);
    }

    /// <summary>REQ-003：切换动画毫秒数。</summary>
    public int SwitchAnimationMs
    {
        get => (int)GetValue(SwitchAnimationMsProperty);
        set => SetValue(SwitchAnimationMsProperty, value);
    }

    /// <summary>REQ-005 SDK 兼容：adapter 写入的中心文本。</summary>
    public string? CenterText
    {
        get => (string?)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    /// <summary>req-026：当前 Provider 已启用的 metric key 集合。null 或空集合表示全部启用。</summary>
    public IReadOnlyList<string>? EnabledMetrics
    {
        get => (IReadOnlyList<string>?)GetValue(EnabledMetricsProperty);
        set => SetValue(EnabledMetricsProperty, value);
    }

    /// <summary>req-051：是否显示 hover tooltip。默认 true，设为 false 时禁用内置 tooltip。</summary>
    public static readonly DependencyProperty ShowHoverTooltipProperty = DependencyProperty.Register(
        nameof(ShowHoverTooltip), typeof(bool), typeof(RingChartControl),
        new FrameworkPropertyMetadata(true));

    /// <summary>req-051：是否显示 hover tooltip。</summary>
    public bool ShowHoverTooltip
    {
        get => (bool)GetValue(ShowHoverTooltipProperty);
        set => SetValue(ShowHoverTooltipProperty, value);
    }

    /// <summary>req-051：是否为半圆环模式。默认 false（全圆环），设为 true 时 0% 在底部中心，100% 在顶部。</summary>
    public static readonly DependencyProperty IsHalfRingProperty = DependencyProperty.Register(
        nameof(IsHalfRing), typeof(bool), typeof(RingChartControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>req-051：是否为半圆环模式。</summary>
    public bool IsHalfRing
    {
        get => (bool)GetValue(IsHalfRingProperty);
        set => SetValue(IsHalfRingProperty, value);
    }

    /// <summary>req-026：当前 metric 被关闭时中心数字显示的画笔。</summary>
    public Brush DisabledBrush
    {
        get => (Brush)GetValue(DisabledBrushProperty);
        set => SetValue(DisabledBrushProperty, value);
    }

    /// <summary>req-051：MetricKey 变化时触发的事件，用于通知 ViewModel 更新 tooltip 内容。</summary>
    public event DependencyPropertyChangedEventHandler? MetricKeyChanged;

    /// <summary>触发 MetricKeyChanged 事件。</summary>
    private void RaiseMetricKeyChanged(DependencyPropertyChangedEventArgs e)
    {
        MetricKeyChanged?.Invoke(this, e);
    }

    // =========================
    // 内部状态（动画 + sticky）
    // =========================

    /// <summary>默认 metric 键（恢复目标）。</summary>
    public const string DefaultMetricKey = RingChartMetricKeys.Percent;

    /// <summary>当前已"被用户切换过"的标志：sticky 计时器到时仅当此标志为 true 才回退默认。</summary>
    private bool _hasUserSwitched;

    /// <summary>sticky 计时器：到点回退默认 metric。</summary>
    private DispatcherTimer? _stickyTimer;

    /// <summary>切换动画：老虎机式（向上滚出旧数字 / 向上滚入新数字）。</summary>
    private DispatcherTimer? _switchAnimTimer;

    /// <summary>当前动画进度 0~1（0 = 旧数字完全显示，1 = 新数字完全显示）。</summary>
    private double _switchAnimProgress;

    /// <summary>动画起始时显示的旧文本。</summary>
    private string? _switchOldText;

    /// <summary>动画目标新文本。</summary>
    private string? _switchNewText;

    /// <summary>动画起始时的 ticks（毫秒，Environment.TickCount）。</summary>
    private int _switchAnimStartMs;

    /// <summary>缓存的 logo bitmap（避免每次重绘都重新解码）。</summary>
    private BitmapImage? _cachedLogo;

    /// <summary>上次解码的 IconPath（用于检测路径变化）。</summary>
    private string? _lastIconPath;

    // =========================
    // 控件构造 + 鼠标 / 滚轮交互
    // =========================

    /// <summary>控件构造：启用鼠标 hover、键盘聚焦、捕获鼠标离开事件以驱动 sticky timer。</summary>
    /// <summary>
    /// 构造函数。req-074：从主题资源解析 Brush 默认值，回退硬编码值。
    /// </summary>
    public RingChartControl()
    {
        Focusable = true;
        Cursor = System.Windows.Input.Cursors.Hand;

        // req-074：从主题资源解析 Brush 默认值，确保主题切换时跟随更新
        if (TrackBrush == null)
            SetValue(TrackBrushProperty, TryFindResource("TrackBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x40)));
        if (ProgressBrush == null)
            SetValue(ProgressBrushProperty, TryFindResource("TextPrimaryBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)));
        if (WarningBrush == null)
            SetValue(WarningBrushProperty, TryFindResource("WarningBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)));
        if (DangerBrush == null)
            SetValue(DangerBrushProperty, TryFindResource("DangerBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
        if (DisabledBrush == null)
            SetValue(DisabledBrushProperty, TryFindResource("TextTertiaryBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));

        // REQ-003：当使用方卡 DataTemplate 装入本控件且未设置 MetricProviders 时，
        // 根据 DataContext 是否为 ProviderUsageViewModel 自动构造 5 个内置 IRingMetricProvider。
        DataContextChanged += OnDataContextChanged;
        // 问题6：Loaded 时订阅全局色阶变更事件（UseTierScale 模式下色阶编辑即时重绘）
        Loaded += OnControlLoaded;
        // req-063 B10：订阅 Unloaded 事件，控件卸载时停止内部计时器
        Unloaded += OnControlUnloaded;
    }

    /// <summary>问题6：控件加载时订阅全局色阶变更，保证色阶设置保存/预览后进度弧即时重绘。</summary>
    private void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        // 先退再订，避免控件反复 Load 时重复订阅
        UsageMonitor.App.Helpers.UsageTierScale.TierChanged -= OnGlobalTierChanged;
        UsageMonitor.App.Helpers.UsageTierScale.TierChanged += OnGlobalTierChanged;
    }

    /// <summary>问题6：全局色阶变更 → 重绘（仅 UseTierScale 模式需要，其它模式重绘无副作用）。</summary>
    private void OnGlobalTierChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(InvalidateVisual));

    /// <summary>REQ-003：根据 DataContext 自动构造内嵌 5 个 metric provider；遇到默认 Percent fallback 改走 ProviderUsageViewModel.UsagePercentage。</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ProviderUsageViewModel vm && MetricProviders == null)
        {
            MetricProviders = RingChartControlMetricProviders.BuildDefault(vm);
        }
    }

    /// <summary>鼠标移动时显示圆环完整百分比及重置时间。</summary>
    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // req-051：只有 ShowHoverTooltip=true 时才显示内置 tooltip
        if (!ShowHoverTooltip) return;
        if (TryGetTooltip(e.GetPosition(this), out var data))
            HoverTooltipPresenter.Show(this, data);
    }

    /// <summary>鼠标离开圆环后关闭 tooltip + 启动 sticky 计时器。</summary>
    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        HoverTooltipPresenter.Hide(this);
        RestartStickyTimer();
    }

    /// <summary>鼠标进入圆环：取消 sticky 计时器（用户继续操作时不回退）。</summary>
    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _stickyTimer?.Stop();
    }

    /// <summary>圆环图通过 Enter 重显完整数字提示。</summary>
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            if (TryGetTooltip(new Point(Size / 2.0, Size / 2.0), out var data))
                HoverTooltipPresenter.Show(this, data);
            e.Handled = true;
            return;
        }
        // 键盘滚轮等价支持：↑/↓ / PgUp/PgDown 切换 metric
        if (e.Key == Key.Up || e.Key == Key.Right || e.Key == Key.PageUp)
        {
            CycleMetric(+1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.PageDown)
        {
            CycleMetric(-1);
            e.Handled = true;
        }
    }

    /// <summary>REQ-003：滚轮循环切换 metric。delta &gt; 0 = 向后；delta &lt; 0 = 向前。</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var steps = e.Delta > 0 ? 1 : -1;
        // 一次滚动事件 delta 绝对值可能 &gt;120，按比例放大，保证连续滚动能连续切。
        var units = Math.Max(1, Math.Abs(e.Delta) / 120);
        for (var i = 0; i < units; i++)
            CycleMetric(steps);
        e.Handled = true;
    }

    /// <summary>req-051：中心区域点击事件。当点击圆环中心数字时触发，用于刷新等操作。</summary>
    public event RoutedEventHandler? CenterClick;

    /// <summary>req-051：鼠标左键按下时，如果点击在中心区域则触发 CenterClick 事件。</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Min(ActualWidth, ActualHeight) / 4; // 中心区域半径约为控件尺寸的 1/4
        var dist = Math.Sqrt(Math.Pow(pos.X - center.X, 2) + Math.Pow(pos.Y - center.Y, 2));
        if (dist <= radius)
        {
            CenterClick?.Invoke(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    /// <summary>REQ-003：根据方向把当前 metric 在 <see cref="MetricOrder"/> 中循环切 1 格。</summary>
    /// <param name="direction">+1 向后 / -1 向前。</param>
    public void CycleMetric(int direction)
    {
        // req-053：只在与已启用 metric 集合中循环，已禁用的跳过
        var enabledOrder = GetEnabledMetricOrder();
        if (enabledOrder.Count == 0) return;
        var currentIdx = -1;
        for (var i = 0; i < enabledOrder.Count; i++)
        {
            if (string.Equals(enabledOrder[i], MetricKey, StringComparison.OrdinalIgnoreCase))
            {
                currentIdx = i;
                break;
            }
        }
        if (currentIdx < 0) currentIdx = 0;
        var newIdx = ((currentIdx + direction) % enabledOrder.Count + enabledOrder.Count) % enabledOrder.Count;
        SetMetric(enabledOrder[newIdx], animate: SwitchAnimationMs > 0);
    }

    /// <summary>REQ-003：设置当前 metric 键。可选是否触发老虎机动画。</summary>
    public void SetMetric(string key, bool animate)
    {
        if (string.Equals(key, MetricKey, StringComparison.OrdinalIgnoreCase))
        {
            // 即使 key 不变也刷新 sticky（让 OnMouseMove 进来时也能续期）
            RestartStickyTimer();
            return;
        }
        var oldText = ResolveCenterText(MetricKey);
        MetricKey = key;
        _hasUserSwitched = !string.Equals(key, DefaultMetricKey, StringComparison.OrdinalIgnoreCase);
        RestartStickyTimer();
        if (animate)
            StartSwitchAnimation(oldText, ResolveCenterText(key));
        else
            InvalidateVisual();
    }

    /// <summary>REQ-003：清除用户切换标志并立即回退到默认 metric。</summary>
    public void ResetToDefaultMetric()
    {
        if (string.Equals(MetricKey, DefaultMetricKey, StringComparison.OrdinalIgnoreCase)) return;
        SetMetric(DefaultMetricKey, animate: SwitchAnimationMs > 0);
        _hasUserSwitched = false;
    }

    /// <summary>REQ-003：根据当前配置计算 metric 切换顺序（用户配置为空时回退默认）。</summary>
    private IReadOnlyList<string> EffectiveMetricOrder()
    {
        if (MetricOrder != null && MetricOrder.Count > 0) return MetricOrder;
        return RingChartMetricKeys.DefaultOrder;
    }

    /// <summary>req-053：从 EffectiveMetricOrder 中过滤出已启用的 metric，保持原顺序。</summary>
    private IReadOnlyList<string> GetEnabledMetricOrder()
    {
        var order = EffectiveMetricOrder();
        // null / 空集合表示全部启用，直接返回完整顺序
        if (EnabledMetrics == null || EnabledMetrics.Count == 0) return order;
        var result = new List<string>(order.Count);
        foreach (var key in order)
        {
            if (IsCurrentMetricEnabled(EnabledMetrics, key))
                result.Add(key);
        }
        return result;
    }

    /// <summary>REQ-003：sticky 计时器到时回退默认 metric（仅当用户切换过）。</summary>
    private void RestartStickyTimer()
    {
        _stickyTimer?.Stop();
        if (StickySeconds <= 0) return;
        if (!_hasUserSwitched) return; // 未切换过不浪费计时器
        _stickyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(StickySeconds)
        };
        _stickyTimer.Tick += (_, _) =>
        {
            _stickyTimer?.Stop();
            ResetToDefaultMetric();
        };
        _stickyTimer.Start();
    }

    /// <summary>
    /// req-063 B10：控件卸载时停止内部计时器，防止内存泄漏。
    /// </summary>
    private void OnControlUnloaded(object sender, RoutedEventArgs e)
    {
        _stickyTimer?.Stop();
        _stickyTimer = null;
        _switchAnimTimer?.Stop();
        _switchAnimTimer = null;
        // 问题6：退订全局色阶变更事件，避免静态事件长期持有控件引用
        UsageMonitor.App.Helpers.UsageTierScale.TierChanged -= OnGlobalTierChanged;
    }

    /// <summary>REQ-003：启动老虎机式数字切换动画。</summary>
    private void StartSwitchAnimation(string? oldText, string? newText)
    {
        if (SwitchAnimationMs <= 0)
        {
            InvalidateVisual();
            return;
        }
        _switchOldText = oldText;
        _switchNewText = newText;
        _switchAnimProgress = 0;
        _switchAnimStartMs = Environment.TickCount;
        _switchAnimTimer?.Stop();
        var frameMs = 16; // ≈60fps
        _switchAnimTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(frameMs)
        };
        _switchAnimTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount - _switchAnimStartMs;
            _switchAnimProgress = Math.Min(1.0, elapsed / (double)SwitchAnimationMs);
            if (_switchAnimProgress >= 1.0)
            {
                _switchAnimTimer?.Stop();
                _switchOldText = null;
                _switchNewText = null;
            }
            InvalidateVisual();
        };
        _switchAnimTimer.Start();
    }

    /// <summary>REQ-003：根据当前 metric 键计算要显示的中心文本（取数委托优先；fallback 为百分比）。</summary>
    private string? ResolveCenterText(string key)
    {
        // adapter 写入的 CenterText 优先级最高
        if (!string.IsNullOrEmpty(CenterText) && string.Equals(key, MetricKey, StringComparison.OrdinalIgnoreCase))
            return CenterText;

        if (MetricProviders != null)
        {
            foreach (var p in MetricProviders)
            {
                if (p != null && string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                    return p.GetText();
            }
        }

        // 内置 fallback：保持原有"纯百分比"行为，向后兼容。
        if (string.Equals(key, RingChartMetricKeys.Percent, StringComparison.OrdinalIgnoreCase))
        {
            var p = Percent;
            if (p < 0) p = 0;
            if (p > 100) p = 100;
            return p == Math.Floor(p)
                ? p.ToString("0", CultureInfo.InvariantCulture)
                : p.ToString("0.#", CultureInfo.InvariantCulture);
        }
        return null;
    }

    /// <summary>返回圆环中心完整百分比和重置时间。</summary>
    public bool TryGetTooltip(Point position, out HoverTooltipData data)
    {
        var provider = string.IsNullOrWhiteSpace(ProviderName) ? "用量" : ProviderName;
        var reset = string.IsNullOrWhiteSpace(ResetTimeText) ? "重置时间：未知" : $"重置时间：{ResetTimeText}";
        data = new HoverTooltipData($"{provider} · 当前用量", $"{Percent:0.##}%", reset);
        return true;
    }

    /// <summary>REQ-003 metric 键变化回调：触发重绘 + 同步元信息（req-093） + 同步 Percent 与动画（req-094）。</summary>
    private static void OnMetricKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (RingChartControl)d;
        var newKey = (string)e.NewValue;

        // req-093：从 MetricProviders 同步当前 metric 的色阶元信息（反转标记 / 阈值）。
        var matchedProvider = control.MetricProviders?
            .FirstOrDefault(p => string.Equals(p.Key, newKey, StringComparison.OrdinalIgnoreCase));
        if (matchedProvider != null)
        {
            control._currentMetricIsInverted = matchedProvider.IsInverted;
            control._currentMetricWarningThreshold = matchedProvider.GetWarningThreshold();
            control._currentMetricDangerThreshold = matchedProvider.GetDangerThreshold();

            // req-094：根据当前 metric 同步 Percent 并以 DoubleAnimation 平滑过渡弧度。
            var targetPercent = matchedProvider.GetPercent();
            if (control.SwitchAnimationMs > 0 && !double.IsNaN(targetPercent))
            {
                var anim = new DoubleAnimation
                {
                    To = targetPercent,
                    Duration = TimeSpan.FromMilliseconds(control.SwitchAnimationMs),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                control.BeginAnimation(PercentProperty, anim);
            }
            else
            {
                control.Percent = targetPercent;
            }
        }
        else
        {
            // 未匹配到 provider：重置元信息，走控件默认阈值。
            control._currentMetricIsInverted = false;
            control._currentMetricWarningThreshold = null;
            control._currentMetricDangerThreshold = null;
        }

        control.InvalidateVisual();
        // req-051：触发事件通知 ViewModel 更新 tooltip
        control.RaiseMetricKeyChanged(e);
    }

    // req-093：当前 metric 附带的色阶元信息（仅供 SelectBrush 读取）。
    private bool _currentMetricIsInverted;
    private double? _currentMetricWarningThreshold;
    private double? _currentMetricDangerThreshold;

    /// <summary>req-093：当前 metric 是否反转色阶。false=高%危险（默认）；true=低%危险。</summary>
    private bool CurrentMetricIsInverted => _currentMetricIsInverted;

    /// <summary>req-093：当前 metric 附带的警告阈值（百分比）。null 时回退到 <see cref="WarningThreshold"/>。</summary>
    private double? CurrentMetricWarningThreshold => _currentMetricWarningThreshold;

    /// <summary>req-093：当前 metric 附带的危险阈值（百分比）。null 时回退到 <see cref="DangerThreshold"/>。</summary>
    private double? CurrentMetricDangerThreshold => _currentMetricDangerThreshold;

    /// <summary>REQ-003 metric provider 列表变化回调：触发重绘。</summary>
    private static void OnMetricProvidersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RingChartControl)d).InvalidateVisual();
    }

    /// <summary>REQ-003 metric 顺序变化回调：触发重绘。</summary>
    private static void OnMetricOrderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RingChartControl)d).InvalidateVisual();
    }

    /// <summary>
    /// 绘制圆环进度图：背景轨道 + 前景进度弧 + 中心 Logo + 数字（动画时跑老虎机）。
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        var size = Size <= 0 ? 44 : Size;
        var stroke = StrokeThickness <= 0 ? 5 : StrokeThickness;
        var center = new Point(size / 2.0, size / 2.0);
        var radius = size / 2.0 - stroke / 2.0;

        if (radius <= 0) return;

        // 进度百分比（0-1）
        var percent = Percent;
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        Brush progressBrush;
        if (UseTierScale)
        {
            // 问题6：色阶直驱模式——每次重绘时直接按当前百分比命中色阶档位（私有档位优先，
            // 回退全局 UsageTierScale），不再依赖 Warning/Danger 阈值链与 ProgressBrush 绑定时序。
            progressBrush = UsageMonitor.App.Helpers.UsageTierScale.ResolveBrush(TierColorConfigs, percent);
        }
        else if (percent <= 0)
        {
            progressBrush = ProgressBrush;
        }
        else
        {
            progressBrush = SelectBrush(percent);
        }

        // req-051：半圆环模式只绘制上半部分
        if (IsHalfRing)
        {
            // 背景轨道（只绘制上半圆，从 6 点钟逆时针到 6 点钟，即上半部分）
            var trackGeometry = CreateHalfRingGeometry(center, radius, 1.0);
            dc.DrawGeometry(null, new Pen(TrackBrush, stroke), trackGeometry);

            if (percent > 0)
            {
                // 进度弧（半圆环，0% 在底部中心，100% 在顶部）
                var progressGeometry = CreateHalfRingGeometry(center, radius, percent / 100.0);

                // 发光
                var glowPen = new Pen(MakeTranslucent(progressBrush, 0x40), stroke + 3)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                if (glowPen.CanFreeze) glowPen.Freeze();
                dc.DrawGeometry(null, glowPen, progressGeometry);

                var pen = new Pen(progressBrush, stroke)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                if (pen.CanFreeze) pen.Freeze();
                dc.DrawGeometry(null, pen, progressGeometry);
            }
        }
        else
        {
            // 1. 背景轨道（浅色）
            dc.DrawEllipse(null, new Pen(TrackBrush, stroke), center, radius, radius);

            if (percent > 0)
            {
                // 3. 进度弧
                var progressGeometry = CreateArcGeometry(center, radius, percent / 100.0);

                // 发光
                var glowPen = new Pen(MakeTranslucent(progressBrush, 0x40), stroke + 3)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                if (glowPen.CanFreeze) glowPen.Freeze();
                dc.DrawGeometry(null, glowPen, progressGeometry);

                var pen = new Pen(progressBrush, stroke)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                if (pen.CanFreeze) pen.Freeze();
                dc.DrawGeometry(null, pen, progressGeometry);
            }
        }

        // 4. 中心 Logo + 数字（垂直堆叠）
        // req-026：当前 metric 被关闭时，中心数字画刷切换为 DisabledBrush（浅灰）
        var centerBrush = IsCurrentMetricEnabled(EnabledMetrics, MetricKey)
            ? progressBrush
            : (DisabledBrush ?? progressBrush);
        DrawCenterContent(dc, size, centerBrush);
    }

    /// <summary>判断当前 <see cref="MetricKey"/> 是否在已启用集合中。
    /// <para>req-026：null / 空集合表示「全部启用」，返回 true（保留旧行为）。
    /// key 匹配忽略大小写。</para></summary>
    private static bool IsCurrentMetricEnabled(IReadOnlyList<string>? enabled, string? metricKey)
    {
        if (enabled == null || enabled.Count == 0) return true;
        if (string.IsNullOrEmpty(metricKey)) return true;
        foreach (var m in enabled)
        {
            if (!string.IsNullOrEmpty(m) &&
                string.Equals(m, metricKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>在圆心绘制：Logo（上方） + 数字（下方）。动画时数字做"老虎机"上下滚动。</summary>
    private void DrawCenterContent(DrawingContext dc, double size, Brush brush)
    {
        var currentText = ResolveCenterText(MetricKey) ?? "";

        // 计算 logo 边长：直径的 32%，居中靠上
        var logoSize = Math.Max(0, size * 0.32);
        var gap = Math.Max(1.0, size * 0.04);
        // 数字字号：直径的 26% + 2（req-051：字号 +2）
        var fontSize = Math.Max(7.0, size * 0.26) + 2;
        // req-051：数字加粗（req-067 B23：使用缓存的 Typeface）
        var typeface = CenterTypeface;

        // 加载 logo bitmap（按需缓存）
        var iconPath = IconPath;
        if (!string.IsNullOrEmpty(iconPath) && logoSize > 0)
        {
            if (_cachedLogo == null || !string.Equals(_lastIconPath, iconPath, StringComparison.OrdinalIgnoreCase))
            {
                _cachedLogo = TryLoadLogo(iconPath);
                _lastIconPath = iconPath;
            }
            var logo = _cachedLogo;
            if (logo != null)
            {
                var logoX = (size - logoSize) / 2.0;
                var logoY = (size - (logoSize + gap + fontSize)) / 2.0;
                if (logoY < 0) logoY = 0;
                dc.DrawImage(logo, new Rect(logoX, logoY, logoSize, logoSize));
            }
        }

        // 数字位置：紧贴 logo 下沿 + gap
        // req-051：半圆环模式下，数字放在圆心位置，水平+垂直都居中
        double logoBottom;
        if (IsHalfRing)
        {
            // 半圆环：弧从7点到5点（300度），弧内区域在圆心附近
            // 数字基线放在圆心偏上（size*0.5 - 10px），确保不被底部裁切
            logoBottom = size * 0.5 + fontSize * 0.1 - 10;
        }
        else
        {
            logoBottom = !string.IsNullOrEmpty(iconPath) && logoSize > 0
                ? (size - (logoSize + gap + fontSize)) / 2.0 + logoSize + gap
                : (size - fontSize) / 2.0;
        }

        // 老虎机动画：根据进度计算 y 偏移（旧值向上滚出 / 新值从下方滚入）
        if (_switchAnimTimer != null && _switchOldText != null && _switchNewText != null)
        {
            // ease-out cubic
            var t = _switchAnimProgress;
            var eased = 1 - Math.Pow(1 - t, 3);
            var offset = (eased - 0.5) * size * 0.6; // 0~1 → -0.3h ~ +0.3h

            // 旧数字：随 t 从 0 升到 0.5 之前完全可见，之后 alpha 衰减；y 从 0 向上移到 -0.3h
            var oldAlpha = (byte)(255 * Math.Max(0, 1 - eased * 2));
            if (oldAlpha > 0)
            {
                DrawText(dc, _switchOldText, typeface, fontSize, MakeTranslucentBrush(brush, oldAlpha),
                    new Point((size - MeasureText(_switchOldText, typeface, fontSize)) / 2.0, logoBottom + offset));
            }
            // 新数字：t < 0.5 时 alpha 0，t=0.5~1 渐入；y 从 +0.3h 回到 0
            var newAlpha = (byte)(255 * Math.Max(0, (eased - 0.5) * 2));
            if (newAlpha > 0)
            {
                DrawText(dc, _switchNewText, typeface, fontSize, MakeTranslucentBrush(brush, newAlpha),
                    new Point((size - MeasureText(_switchNewText, typeface, fontSize)) / 2.0, logoBottom + offset));
            }
            return;
        }

        // 静态绘制
        if (!string.IsNullOrEmpty(currentText))
        {
            var textWidth = MeasureText(currentText, typeface, fontSize);
            DrawText(dc, currentText, typeface, fontSize, brush,
                new Point((size - textWidth) / 2.0, logoBottom));
        }
    }

    /// <summary>在 (x, y) 位置绘制一段文字。</summary>
    private void DrawText(DrawingContext dc, string text, Typeface typeface, double fontSize, Brush brush, Point origin)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, origin);
    }

    /// <summary>测量单段文本在指定字体下的渲染宽度（DIP）。</summary>
    private double MeasureText(string? text, Typeface typeface, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            System.Windows.Media.Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.Width;
    }

    /// <summary>尝试按路径加载 logo（pack:// URI 或文件路径），失败时返回 null 而不抛。</summary>
    private static BitmapImage? TryLoadLogo(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (path.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
                bmp.UriSource = new Uri(path, UriKind.Absolute);
            else
                bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            // 路径无效 / 文件不存在 / 非图像格式 → 静默失败
            return null;
        }
    }

    /// <summary>派生同色半透明画笔（用于进度弧发光）。</summary>
    private static Brush MakeTranslucent(Brush source, byte alpha)
    {
        if (source is SolidColorBrush scb)
        {
            var col = scb.Color;
            var b = new SolidColorBrush(Color.FromArgb(alpha, col.R, col.G, col.B));
            b.Freeze();
            return b;
        }
        return source;
    }

    /// <summary>派生同色指定 alpha 画笔（老虎机动画用）。</summary>
    private static Brush MakeTranslucentBrush(Brush source, byte alpha)
    {
        if (source is SolidColorBrush scb)
        {
            var col = scb.Color;
            var b = new SolidColorBrush(Color.FromArgb(alpha, col.R, col.G, col.B));
            b.Freeze();
            return b;
        }
        return source;
    }

    /// <summary>创建从顶部（-90°）开始、按百分比顺时针的圆弧</summary>
    private static PathGeometry CreateArcGeometry(Point center, double radius, double fraction)
    {
        var angle = Math.Min(360.0, fraction * 360.0);
        var start = new Point(center.X, center.Y - radius);

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };

        if (angle >= 360.0)
        {
            var mid = new Point(center.X, center.Y + radius);
            figure.Segments.Add(new ArcSegment(mid, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
            figure.Segments.Add(new ArcSegment(start, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
        }
        else
        {
            var radians = (angle - 90) * Math.PI / 180.0;
            var end = new Point(
                center.X + radius * Math.Cos(radians),
                center.Y + radius * Math.Sin(radians));
            var isLargeArc = angle > 180.0;
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>根据百分比选择画笔。req-093：支持 <see cref="CurrentMetricIsInverted"/> 反转模式与数据附带阈值。
    /// <para>B5：每个分支加 null 兜底链，最终回退到全局 <see cref="UsageTierScale.ResolveBrush"/>，
    /// 避免画刷 DP 未绑定时 Pen(null) 导致弧不可见（黑环问题）。显式绑定消费方（MainWindow/HistoryWindow）行为不受影响。</para>
    /// </summary>
    private Brush SelectBrush(double percent)
    {
        var warning = CurrentMetricWarningThreshold ?? WarningThreshold;
        var danger = CurrentMetricDangerThreshold ?? DangerThreshold;
        if (CurrentMetricIsInverted)
        {
            // 反转模式（剩余量语义）：低百分比危险
            // Phase0 评审修复：兜底 ResolveBrush 按“已用量语义”取色，需将剩余量转换为已用量（100-percent），
            // 否则低剩余（如 10%）会误取绿色（防御性修复：当前画刷 DP 恒非 null，此分支不可达）
            if (percent <= danger) return DangerBrush ?? WarningBrush ?? ProgressBrush ?? UsageTierScale.ResolveBrush(100.0 - percent);
            if (percent <= warning) return WarningBrush ?? ProgressBrush ?? UsageTierScale.ResolveBrush(100.0 - percent);
            return ProgressBrush ?? UsageTierScale.ResolveBrush(100.0 - percent);
        }
        // 正常模式（已用量语义）：高百分比危险
        if (percent >= danger) return DangerBrush ?? WarningBrush ?? ProgressBrush ?? UsageTierScale.ResolveBrush(percent);
        if (percent >= warning) return WarningBrush ?? ProgressBrush ?? UsageTierScale.ResolveBrush(percent);
        return ProgressBrush ?? UsageTierScale.ResolveBrush(percent);
    }

    /// <summary>req-051：创建半圆环几何图形。0% 在 8 点钟，100% 在 4 点钟，顺时针 240 度弧。</summary>
    /// <param name="center">圆心</param>
    /// <param name="radius">半径</param>
    /// <param name="fraction">进度分数（0-1）</param>
    private static PathGeometry CreateHalfRingGeometry(Point center, double radius, double fraction)
    {
        // 半圆环：从 8 点钟开始，顺时针经过 9 点钟（左）、12 点钟（顶）、3 点钟（右）到 4 点钟
        // WPF 角度：0°=3 点钟，90°=6 点钟，180°=9 点钟，270°=12 点钟
        // 8 点钟 = 150° (WPF), 4 点钟 = 30° (WPF)
        // 从 150° 顺时针到 30° = 240° 弧（150° → 180° → 270° → 360°/0° → 30°）
        var startAngle = 150.0; // 8 点钟
        var totalArc = 240.0; // 总弧度 240°
        var angle = Math.Min(totalArc, fraction * totalArc);

        // 计算起点（8 点钟）
        var startRadians = startAngle * Math.PI / 180.0;
        var start = new Point(
            center.X + radius * Math.Cos(startRadians),
            center.Y + radius * Math.Sin(startRadians));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };

        if (angle <= 0)
        {
            // 0% 时不绘制弧
        }
        else if (angle >= totalArc)
        {
            // 100% 时绘制完整的 240° 弧（从 8 点钟到 4 点钟）
            var endAngle = (startAngle + totalArc) % 360.0; // 30° = 4 点钟
            var endRadians = endAngle * Math.PI / 180.0;
            var end = new Point(
                center.X + radius * Math.Cos(endRadians),
                center.Y + radius * Math.Sin(endRadians));
            // 240° > 180°，所以是大弧
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
        }
        else
        {
            // 部分进度：计算终点位置
            var currentAngle = (startAngle + angle) % 360.0;
            var endRadians = currentAngle * Math.PI / 180.0;
            var end = new Point(
                center.X + radius * Math.Cos(endRadians),
                center.Y + radius * Math.Sin(endRadians));
            var isLargeArc = angle > 180.0;
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>
/// REQ-003 环形图中心 metric 取数委托契约（"提供一行文字 + 一个键"）。
/// <para>
/// 宿主（Taskbar 模板 / 卡片模板）装配 5 种内置 <see cref="IRingMetricProvider"/> 实现，
/// 控件本身只通过 <see cref="GetText"/> 拉当前展示的字符串。键值用于
/// <see cref="UsageMonitor.Core.Models.RingChartMetricKeys"/> 路由 + 切换顺序排序。
/// </para>
/// <para>
/// req-093：在原契约上扩展色阶元数据（<see cref="GetPercent"/> / <see cref="IsInverted"/> /
/// <see cref="GetWarningThreshold"/> / <see cref="GetDangerThreshold"/>），所有扩展方法都提供
/// 默认实现，向后兼容旧 Provider 不修改即可继续工作。
/// </para>
/// </summary>
public interface IRingMetricProvider
{
    /// <summary>metric 键（如 <c>Percent / Credits / WeeklyLimit / RemainingQuota / ApiTokenUsed</c>）。</summary>
    string Key { get; }

    /// <summary>当前要展示的中心文本（含格式化与单位）。</summary>
    string GetText();

    /// <summary>
    /// req-093：当前 metric 对应的百分比（0-100）。<see cref="RingChartControl"/> 在
    /// <c>OnMetricKeyChanged</c> 中调用本方法更新 <c>Percent</c> 依赖属性，使圆环弧度随
    /// 切换的 metric 同步更新（req-094 修复点）。
    /// <para>默认返回 0。旧 Provider 不实现本方法时控件走 "弧度不变" 的旧行为。</para>
    /// </summary>
    double GetPercent() => 0.0;

    /// <summary>
    /// req-093：色阶方向。false（默认）= 高百分比危险（已用量语义）；true = 低百分比危险
    /// （剩余量语义，如周限额剩余、积分余额）。默认 false 保持旧行为。
    /// </summary>
    bool IsInverted => false;

    /// <summary>
    /// req-093：警告阈值（百分比）。null 表示回退到 <c>RingChartControl.WarningThreshold</c>
    /// （默认 60）。插件可在 <c>RingMetricData</c> 中按 metric 语义覆盖。
    /// </summary>
    double? GetWarningThreshold() => null;

    /// <summary>
    /// req-093：危险阈值（百分比）。null 表示回退到 <c>RingChartControl.DangerThreshold</c>
    /// （默认 85）。插件可在 <c>RingMetricData</c> 中按 metric 语义覆盖。
    /// </summary>
    double? GetDangerThreshold() => null;
}