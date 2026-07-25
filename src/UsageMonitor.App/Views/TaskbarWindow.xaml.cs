using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.MiniChart;
using UsageMonitor.Core.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace UsageMonitor.App.Views;

/// <summary>
/// 任务栏嵌入窗口 - 通过 TaskbarHelper.EmbedWindow(SetParent + WS_CHILD) 真正嵌入 Windows 任务栏
/// - 默认嵌入后停靠在任务栏右侧通知区域左边
/// - 整窗可拖：拖动期间在任务栏宽度范围内左右滑动，松手后位置以 0~1 相对比例写入 config.json
/// - 只显示已启用的 Provider 卡片（与主窗口一致），不再展示未启用的 Provider
/// - 显示模式支持三种（每 Provider 独立）：Text / MiniLineChart / RingChart
/// - req-088 B5：数据源改为 ITaskbarMiniChartRegistry，XAML 通过 MiniChartTemplateSelector 按 Kind 选模板
/// </summary>
public partial class TaskbarWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigService _configService;
    private readonly TaskbarHelper _taskbarHelper;
    private readonly RefreshService _refreshService;
    private readonly ITaskbarMiniChartRegistry _miniChartRegistry;

    /// <summary>
    /// req-088 B5：迷你图列表数据源（来自 Registry + EnabledUsages 关联），XAML ItemsControl 绑定此集合。
    /// </summary>
    public ObservableCollectionEx<MiniChartItemViewModel> VisibleMiniCharts { get; } = new();

    // Win32 句柄与父坐标系参考
    private IntPtr _hwnd;
    private TaskbarNativeMethods.RECT _taskbarRect;
    private int _embedWidth;

    /// <summary>窗口交互拖拽模式：无 / 移动位置 / 调整左边缘 / 调整右边缘。</summary>
    private enum DragMode { None, Move, ResizeLeft, ResizeRight }

    /// <summary>边缘热区宽度（DIP）：鼠标进入窗口左右两侧此范围内触发宽度调整。</summary>
    private const int ResizeEdge = 6;

    /// <summary>手动调整时允许的最小窗口宽度（像素）。</summary>
    private const int MinResizeWidth = 120;

    // 拖拽状态
    private DragMode _dragMode = DragMode.None;
    private bool _isDragging;
    private bool _hasPlacedOnce;
    private System.Drawing.Point _dragStartCursorScreen;
    private int _dragStartWindowLeft;
    private int _dragStartWindowWidth;

    // 位置保存节流（req-063 B8）
    private readonly DispatcherTimer _savePositionTimer;

    // req-052：可见性检查定时器
    private DispatcherTimer? _visibilityCheckTimer;

    public TaskbarWindow(MainViewModel viewModel, ConfigService configService, TaskbarHelper taskbarHelper, RefreshService refreshService, ITaskbarMiniChartRegistry miniChartRegistry)
    {
        _viewModel = viewModel;
        _configService = configService;
        _taskbarHelper = taskbarHelper;
        _refreshService = refreshService;
        _miniChartRegistry = miniChartRegistry;
        InitializeComponent();
        DataContext = this;
        // 默认光标为移动样式
        Cursor = System.Windows.Input.Cursors.SizeAll;

        // req-088 B5：构建 VisibleMiniCharts 集合（从 Registry + EnabledUsages 关联）。
        RebuildVisibleMiniCharts();
        _viewModel.EnabledUsages.CollectionChanged += OnEnabledUsagesChanged;
        // S4：订阅配置变更，设置页保存 Mini 配置后即时重建任务栏迷你图（带签名守卫）。
        _configService.ConfigChanged += OnConfigChanged;
        foreach (var u in _viewModel.EnabledUsages)
        {
            AttachUsageVmListeners(u);
        }

        // req-063 B8：位置保存节流 timer
        _savePositionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _savePositionTimer.Tick += (_, _) =>
        {
            _savePositionTimer.Stop();
            SavePositionToConfig();
        };

        LocationChanged += OnLocationChanged;

        SourceInitialized += OnSourceInitializedOverride;
        Loaded += OnLoadedOverride;
        Activated += OnActivatedOverride;

        StartVisibilityCheckTimer();

        Loaded += (_, _) =>
        {
            if (RootBorder != null)
            {
                RootBorder.MouseEnter += OnRootBorderMouseEnter;
                RootBorder.MouseLeave += OnRootBorderMouseLeave;
            }
            // req-105：启动所有迷你图项的倒计时 timer，让 RefreshCountdownText 每秒刷新。
            foreach (var item in VisibleMiniCharts)
                item.StartCountdownTimer();
        };
        Unloaded += (_, _) =>
        {
            if (RootBorder != null)
            {
                RootBorder.MouseEnter -= OnRootBorderMouseEnter;
                RootBorder.MouseLeave -= OnRootBorderMouseLeave;
            }
            // req-105：停止所有迷你图项的倒计时 timer，避免内存泄漏。
            foreach (var item in VisibleMiniCharts)
                item.StopCountdownTimer();
        };

        Closed += (_, _) =>
        {
            _visibilityCheckTimer?.Stop();
            _visibilityCheckTimer = null;
            // S4：退订配置变更事件，避免窗口关闭后仍触发重建。
            _configService.ConfigChanged -= OnConfigChanged;
            // req-105：关闭时也确保 timer 释放。
            foreach (var item in VisibleMiniCharts)
                item.StopCountdownTimer();
        };
    }

    /// <summary>
    /// S4：当前可见迷你图列表的签名（ProviderId:ChartId 拼接），用于 ConfigChanged 守卫比较，
    /// 避免位置/宽度保存等高频 ConfigChanged 触发无意义的重建（防闪烁、防 timer 重置）。
    /// </summary>
    private string _lastMiniChartSignature = string.Empty;

    /// <summary>
    /// req-088 B5：从 Registry 拉取所有 descriptor，关联到 EnabledUsages 中的 ProviderUsageViewModel，
    /// 组装为 MiniChartItemViewModel 列表写入 VisibleMiniCharts。
    /// <para>Phase 2 修复：增加签名守卫，与 OnConfigChanged 路径共用 _lastMiniChartSignature，
    /// 避免 EnabledUsagesChanged 路径无谓重建（账号操作触发任务栏多次重建）。</para>
    /// </summary>
    private void RebuildVisibleMiniCharts()
    {
        var newList = BuildVisibleMiniChartList();
        var newSignature = ComputeMiniChartSignature(newList);
        // 签名未变则跳过重建（与 OnConfigChanged 路径共用签名字段，避免双重计算或状态不一致）。
        if (string.Equals(newSignature, _lastMiniChartSignature, StringComparison.Ordinal))
            return;
        ReplaceVisibleMiniCharts(newList);
    }

    /// <summary>
    /// S4：构建可见迷你图列表（不写入集合，供重建与签名比较复用）。
    /// <para>过滤规则与 req-088/req-099/req-109 保持一致：仅显示已启用且有对应卡片 VM 的 Provider；
    /// 按用户配置 VisibleMiniCharts 精确过滤（null 表示全部可见）。</para>
    /// </summary>
    private List<MiniChartItemViewModel> BuildVisibleMiniChartList()
    {
        var result = new List<MiniChartItemViewModel>();
        // req-110 兼容：多账号/多卡片后同一 Provider 可能存在多个卡片 VM，ToDictionary 会因重复键崩溃；
        // 改用 GroupBy 取首个（任务栏迷你图仍以 Provider 为粒度，取第一张启用卡片的数据源）。
        var usages = _viewModel.EnabledUsages
            .GroupBy(u => u.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in _miniChartRegistry.GetAll())
        {
            // req-099 修复（Bug4）：仅显示已启用且有对应卡片 VM 的迷你图，跳过未启用/无数据的 Provider，避免任务栏空环。
            if (!usages.TryGetValue(descriptor.ProviderId, out var usageVm) || usageVm == null)
                continue;
            // req-109：按用户配置的 VisibleMiniCharts 过滤。
            //   - visibleMiniCharts == null → 全部可见（向后兼容）
            //   - descriptor.ChartId == null → 旧注册路径，不按 chartId 过滤（仅 Provider 粒度：空集合则隐藏）
            //   - descriptor.ChartId != null → 按 chartId 精确过滤（不在列表则隐藏）
            // Phase 2 修复：从 usageVm 透传真实 AccountId/CardId，避免硬编码 "default" 导致有账号用户的配置静默失效。
            var visibleMiniCharts = _viewModel.GetEffectiveVisibleMiniCharts(descriptor.ProviderId, usageVm.AccountIdSafe, usageVm.CardIdSafe);
            if (visibleMiniCharts != null)
            {
                if (descriptor.ChartId != null)
                {
                    if (!visibleMiniCharts.Contains(descriptor.ChartId)) continue;
                }
                else if (visibleMiniCharts.Count == 0)
                {
                    continue;
                }
            }
            result.Add(new MiniChartItemViewModel(descriptor, usageVm)
            {
                // 问题8：解析本 mini 图表的有效 Tooltip/文本字段（用户配置优先，回退声明；null = 沿用旧渲染）
                EffectiveTooltipFields = ResolveMiniTooltipFields(descriptor, usageVm),
                // 问题11/12：解析本 mini 图表的可见数据组（用户勾选；null = 全部声明组可见）
                VisibleDataGroupIds = ResolveMiniVisibleDataGroups(descriptor, usageVm),
                AccountName = ResolveAccountName(descriptor.ProviderId, usageVm.AccountIdSafe)
            });
        }
        // 问题11：注入可见数据组后重新对齐初始数据组索引（构造时尚未注入）。
        foreach (var item in result)
            item.ReinitializeDataGroupIndex();
        return result;
    }

    /// <summary>
    /// 问题11/12：解析指定 mini 图表的可见数据组 ID 列表（AccountCustomization.VisibleMiniDataGroups）。
    /// <para>null = 未配置（全部声明组可见）；非 null 时列表顺序即展示/滚轮切换顺序，可含虚拟倒计时组 ID。</para>
    /// </summary>
    private IReadOnlyList<string>? ResolveMiniVisibleDataGroups(MiniChartDescriptor descriptor, ProviderUsageViewModel usageVm)
    {
        try
        {
            if (descriptor.ChartId != null)
            {
                var eff = _configService.GetEffectiveAccountCustomization(descriptor.ProviderId, usageVm.AccountIdSafe, usageVm.CardIdSafe);
                if (eff.VisibleMiniDataGroups.TryGetValue(descriptor.ChartId, out var groups) && groups != null)
                    return groups;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("TaskbarWindow", $"ResolveMiniVisibleDataGroups({descriptor.ProviderId}:{descriptor.ChartId}) failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 问题8：解析指定 mini 图表的有效 Tooltip/文本字段。
    /// <para>三态语义：用户配置（AccountCustomization.MiniTooltipFields[chartId]）非 null → 直接返回；
    /// 无用户配置 → 回退声明的 tooltip.fields（DeclaredTooltipFields）；都无 → null（沿用旧渲染路径）。</para>
    /// </summary>
    private IReadOnlyList<string>? ResolveMiniTooltipFields(MiniChartDescriptor descriptor, ProviderUsageViewModel usageVm)
    {
        try
        {
            if (descriptor.ChartId != null)
            {
                var eff = _configService.GetEffectiveAccountCustomization(descriptor.ProviderId, usageVm.AccountIdSafe, usageVm.CardIdSafe);
                if (eff.MiniTooltipFields.TryGetValue(descriptor.ChartId, out var userFields) && userFields != null)
                    return userFields;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("TaskbarWindow", $"ResolveMiniTooltipFields({descriptor.ProviderId}:{descriptor.ChartId}) failed: {ex.Message}");
        }
        return descriptor.DeclaredTooltipFields;
    }

    /// <summary>问题8：解析账号显示名（昵称优先，回退账号 ID）。</summary>
    private string ResolveAccountName(string providerId, string accountId)
    {
        try
        {
            var account = _configService.GetAccounts(providerId)
                .FirstOrDefault(a => string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
            if (account != null && !string.IsNullOrWhiteSpace(account.Nickname)) return account.Nickname!;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("TaskbarWindow", $"ResolveAccountName({providerId}:{accountId}) failed: {ex.Message}");
        }
        return accountId;
    }

    /// <summary>
    /// S4：计算迷你图列表签名（ProviderId:ChartId:有效字段:可见数据组 按序拼接），用于守卫比较。
    /// <para>问题8/11：签名包含有效 Tooltip 字段与可见数据组，保证设置页仅改勾选时也能触发重建。</para>
    /// </summary>
    private static string ComputeMiniChartSignature(IEnumerable<MiniChartItemViewModel> items)
        => string.Join("|", items.Select(i =>
            $"{i.ProviderId}:{i.Descriptor.ChartId ?? string.Empty}:{(i.EffectiveTooltipFields == null ? "~" : string.Join(",", i.EffectiveTooltipFields))}:{(i.VisibleDataGroupIds == null ? "~" : string.Join(",", i.VisibleDataGroupIds))}"));

    /// <summary>
    /// S4：用新列表替换 VisibleMiniCharts（停旧 timer → 清空 → 写入 → 启新 timer → 重算窗口宽度）。
    /// <para>替换后更新签名缓存；窗口已加载时为新项启动倒计时 timer（StartCountdownTimer 幂等）。</para>
    /// </summary>
    private void ReplaceVisibleMiniCharts(List<MiniChartItemViewModel> newList)
    {
        foreach (var item in VisibleMiniCharts)
            item.StopCountdownTimer();
        VisibleMiniCharts.Clear();
        foreach (var item in newList)
            VisibleMiniCharts.Add(item);
        _lastMiniChartSignature = ComputeMiniChartSignature(newList);
        if (IsLoaded)
        {
            foreach (var item in VisibleMiniCharts)
                item.StartCountdownTimer();
        }
        // 迷你图增减会影响窗口宽度，重算一次（内部对 _hwnd/拖拽态有守卫）
        RecalculateSize();
    }

    /// <summary>
    /// S4：ConfigService 配置变更回调——带守卫地重建迷你图列表。
    /// <para>设置页保存 Mini 配置（SetMiniChartConfiguration → Save → ConfigChanged）后即时刷新任务栏；
    /// 位置/宽度保存等高频变更因签名不变而被跳过，避免闪烁与 timer 重置。异步调度到 UI 线程执行。</para>
    /// </summary>
    private void OnConfigChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var newList = BuildVisibleMiniChartList();
            var newSignature = ComputeMiniChartSignature(newList);
            // 签名未变（如仅保存窗口位置/宽度）→ 跳过重建
            if (string.Equals(newSignature, _lastMiniChartSignature, StringComparison.Ordinal))
                return;
            ReplaceVisibleMiniCharts(newList);
        }), DispatcherPriority.Normal);
    }

    /// <summary>
    /// req-088 B5：当 EnabledUsages 集合变化（启用 / 停用 Provider）时重建列表。
    /// </summary>
    private void OnEnabledUsagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (ProviderUsageViewModel u in e.OldItems) DetachUsageVmListeners(u);
        if (e.NewItems != null)
            foreach (ProviderUsageViewModel u in e.NewItems) AttachUsageVmListeners(u);
        RebuildVisibleMiniCharts();
    }

    /// <summary>req-088 B5：订阅单个 ProviderUsageViewModel 的 PropertyChanged，触发对应 MiniChartItemViewModel 刷新。</summary>
    private void AttachUsageVmListeners(ProviderUsageViewModel vm)
    {
        vm.PropertyChanged += OnUsageVmPropertyChanged;
    }

    private void DetachUsageVmListeners(ProviderUsageViewModel vm)
    {
        vm.PropertyChanged -= OnUsageVmPropertyChanged;
    }

    private void OnUsageVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ProviderUsageViewModel vm) return;
        // req-109：一个 Provider 可能有多个 Mini 图表（按 chartId 注册），全部刷新。
        foreach (var item in VisibleMiniCharts.Where(x => x.ProviderId == vm.ProviderId))
            item.RefreshFromUsageVm();
    }

    private void OnRootBorderMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { }
    private void OnRootBorderMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { }
    private void OnRefreshAllClick(object sender, RoutedEventArgs e) { }

    /// <summary>
    /// req-029：安全包装单个 Provider 刷新调用——捕捉异常 + 写日志。
    /// </summary>
    private async Task TryRefreshAsync(string providerId)
    {
        try
        {
            await _refreshService.RefreshProviderAsync(providerId);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("TaskbarWindow",
                $"req-029 RefreshProviderAsync({providerId}) failed: {ex.Message}", ex);
        }
    }

    private void OnSourceInitializedOverride(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(ApplyWin32Position), DispatcherPriority.Background);

    private void OnLoadedOverride(object? sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(ApplyWin32Position), DispatcherPriority.Background);

    private void OnActivatedOverride(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(ApplyWin32Position), DispatcherPriority.Background);

    private void ApplyWin32Position()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_isDragging) return;
        var taskbarHandle = _taskbarHelper.GetHandle();
        if (taskbarHandle == IntPtr.Zero) return;

        TaskbarNativeMethods.RECT taskbarRect;
        if (!TaskbarNativeMethods.GetWindowRect(taskbarHandle, out taskbarRect)) return;
        if (taskbarRect.Width <= 0) return;

        var width = (int)ComputeWindowWidth();
        if (width < 100) width = 280;
        var height = taskbarRect.Height;
        var relX = _configService.Settings.TaskbarRelativeX ?? 0.5;
        var rightMargin = 80;
        var leftMargin = 20;
        var usableWidth = taskbarRect.Right - taskbarRect.Left - rightMargin - leftMargin;
        var x = taskbarRect.Left + leftMargin + (int)(relX * (usableWidth - width));
        var y = taskbarRect.Top;
        TaskbarNativeMethods.SetWindowPos(_hwnd, TaskbarNativeMethods.HWND_TOPMOST,
            x, y, width, height,
            TaskbarNativeMethods.SWP_SHOWWINDOW | TaskbarNativeMethods.SWP_NOACTIVATE);
    }

    private void StartVisibilityCheckTimer()
    {
        _visibilityCheckTimer?.Stop();
        _visibilityCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(5) };
        _visibilityCheckTimer.Tick += OnVisibilityCheckTimerTick;
        _visibilityCheckTimer.Start();
    }

    private void OnVisibilityCheckTimerTick(object? sender, EventArgs e)
    {
        if (_hwnd == IntPtr.Zero || _isDragging) return;
        if (!IsVisible)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { if (!IsVisible) Show(); ApplyWin32Position(); }
                catch (Exception ex) { FileLogger.Warn("TaskbarWindow", $"req-052: 重新显示窗口失败: {ex.Message}"); }
            }), DispatcherPriority.Normal);
            return;
        }
    }

    public void EmbedIntoTaskbar()
    {
        if (!_taskbarHelper.Initialize()) return;
        if (!IsVisible) Show();
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (_hwnd == IntPtr.Zero) return;
        var taskbarHandle = _taskbarHelper.GetHandle();
        if (taskbarHandle == IntPtr.Zero) return;
        TaskbarNativeMethods.GetWindowRect(taskbarHandle, out _taskbarRect);
        _embedWidth = (int)ComputeWindowWidth();
        var relX = _configService.Settings.TaskbarRelativeX ?? 0.5;
        if (_taskbarHelper.EmbedWindow(_hwnd, _embedWidth, relX)) _hasPlacedOnce = true;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (e.Handled || e.ChangedButton != MouseButton.Left) return;
        // 问题14：记录按下时的命中元素，供 Up 时判断“原地点击”触发迷你环刷新
        //（窗口拖拽占用 Preview 事件链并 e.Handled=true，冒泡到 RingChartControl 的 CenterClick 永远不会触发）。
        _pressOriginalSource = e.OriginalSource as DependencyObject;
        var mode = HitTestEdge(e.GetPosition(this).X);
        if (e.ClickCount == 2 && mode != DragMode.Move)
        {
            _configService.Settings.TaskbarWidth = null;
            try { _configService.Save(); }
            catch (Exception ex) { FileLogger.Error("TaskbarWindow", "清除手动宽度失败", ex); }
            RecalculateSize();
            e.Handled = true;
            return;
        }
        _dragMode = mode;
        var taskbarHandle = _taskbarHelper.GetHandle();
        if (taskbarHandle != IntPtr.Zero) TaskbarNativeMethods.GetWindowRect(taskbarHandle, out _taskbarRect);
        TaskbarNativeMethods.GetWindowRect(_hwnd, out var curRect);
        _dragStartWindowLeft = curRect.Left;
        _dragStartWindowWidth = curRect.Width;
        _dragStartCursorScreen = System.Windows.Forms.Cursor.Position;
        _isDragging = true;
        Mouse.Capture(this);
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (e.Handled) return;
        if (!_isDragging) { UpdateCursorForPosition(e.GetPosition(this).X); return; }
        if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
        var cur = System.Windows.Forms.Cursor.Position;
        var dx = cur.X - _dragStartCursorScreen.X;
        if (_dragMode == DragMode.ResizeRight)
        {
            var newWidth = ClampResizeWidth(_dragStartWindowWidth + dx);
            _embedWidth = newWidth;
            TaskbarNativeMethods.MoveWindow(_hwnd, _dragStartWindowLeft, _taskbarRect.Top, newWidth, _taskbarRect.Height, true);
        }
        else if (_dragMode == DragMode.ResizeLeft)
        {
            var rightEdge = _dragStartWindowLeft + _dragStartWindowWidth;
            var newWidth = ClampResizeWidth(_dragStartWindowWidth - dx);
            var newLeft = rightEdge - newWidth;
            var minLeft = _taskbarRect.Left + TaskbarLeftMargin;
            if (newLeft < minLeft) { newLeft = minLeft; newWidth = rightEdge - newLeft; }
            _embedWidth = newWidth;
            TaskbarNativeMethods.MoveWindow(_hwnd, newLeft, _taskbarRect.Top, newWidth, _taskbarRect.Height, true);
        }
        else
        {
            var newLeft = ClampXToTaskbar(_dragStartWindowLeft + dx);
            TaskbarNativeMethods.MoveWindow(_hwnd, newLeft, _taskbarRect.Top, _embedWidth, _taskbarRect.Height, true);
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (e.Handled || e.ChangedButton != MouseButton.Left || !_isDragging) return;
        if (_dragMode == DragMode.ResizeLeft || _dragMode == DragMode.ResizeRight) SaveWidthToConfig();
        // 问题14：未发生明显位移的“原地点击”→ 命中迷你半圆环时触发该 Provider 刷新。
        var cur = System.Windows.Forms.Cursor.Position;
        var isClick = Math.Abs(cur.X - _dragStartCursorScreen.X) <= 3 &&
                      Math.Abs(cur.Y - _dragStartCursorScreen.Y) <= 3;
        var wasMoveMode = _dragMode == DragMode.Move; // EndDrag 会重置 _dragMode，先行留存
        EndDrag();
        e.Handled = true;
        if (isClick && wasMoveMode) TryHandleMiniRingClick();
    }

    /// <summary>问题14：按下时的原始命中元素（用于原地点击定位被点中的迷你图项）。</summary>
    private DependencyObject? _pressOriginalSource;

    /// <summary>
    /// 问题14：原地点击命中迷你半圆环（MiniRingChart）时触发对应 Provider 刷新。
    /// <para>从按下时的原始命中元素向上查找 MiniChartItemViewModel（DataContext），
    /// Kind 为 MiniRingChart 时异步刷新（fire-and-forget，失败仅记日志）。</para>
    /// </summary>
    private void TryHandleMiniRingClick()
    {
        var element = _pressOriginalSource;
        _pressOriginalSource = null;
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.DataContext is MiniChartItemViewModel item)
            {
                if (item.Kind == UsageMonitor.Core.Plugins.MiniChart.MiniChartKind.MiniRingChart &&
                    !string.IsNullOrEmpty(item.ProviderId))
                {
                    FileLogger.Info("TaskbarWindow", $"问题14：迷你环点击刷新 {item.ProviderId}");
                    _ = TryRefreshAsync(item.ProviderId);
                }
                return;
            }
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
    }

    private void EndDrag()
    {
        _isDragging = false;
        _dragMode = DragMode.None;
        Mouse.Capture(null);
    }

    private const int TaskbarLeftMargin = 20;
    private const int TaskbarRightMargin = 80;

    private int ClampXToTaskbar(int screenX)
    {
        var minX = _taskbarRect.Left + TaskbarLeftMargin;
        var maxX = _taskbarRect.Right - TaskbarRightMargin - _embedWidth;
        if (maxX < minX) maxX = minX;
        return Math.Max(minX, Math.Min(maxX, screenX));
    }

    private DragMode HitTestEdge(double xInWindow)
    {
        if (ActualWidth <= 0) return DragMode.Move;
        if (xInWindow <= ResizeEdge) return DragMode.ResizeLeft;
        if (xInWindow >= ActualWidth - ResizeEdge) return DragMode.ResizeRight;
        return DragMode.Move;
    }

    private void UpdateCursorForPosition(double xInWindow)
    {
        Cursor = HitTestEdge(xInWindow) == DragMode.Move
            ? System.Windows.Input.Cursors.SizeAll
            : System.Windows.Input.Cursors.SizeWE;
    }

    private int ClampResizeWidth(int width)
    {
        var maxWidth = _taskbarRect.Width - TaskbarLeftMargin - TaskbarRightMargin;
        if (maxWidth < MinResizeWidth) maxWidth = MinResizeWidth;
        return Math.Max(MinResizeWidth, Math.Min(maxWidth, width));
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_hasPlacedOnce) return;
        _savePositionTimer.Stop();
        _savePositionTimer.Start();
    }

    private void SavePositionToConfig()
    {
        TaskbarNativeMethods.GetWindowRect(_hwnd, out var curRect);
        var usableWidth = _taskbarRect.Width - TaskbarLeftMargin - TaskbarRightMargin;
        var span = usableWidth - _embedWidth;
        if (span <= 0) return;
        var ratio = (double)(curRect.Left - _taskbarRect.Left - TaskbarLeftMargin) / span;
        ratio = Math.Max(0, Math.Min(1, ratio));
        _configService.Settings.TaskbarRelativeX = ratio;
        try { _configService.Save(); }
        catch (Exception ex) { FileLogger.Error("TaskbarWindow", "保存任务栏窗口位置失败", ex); }
    }

    private void SaveWidthToConfig()
    {
        TaskbarNativeMethods.GetWindowRect(_hwnd, out var curRect);
        _embedWidth = curRect.Width;
        _configService.Settings.TaskbarWidth = curRect.Width;
        var usableWidth = _taskbarRect.Width - TaskbarLeftMargin - TaskbarRightMargin;
        var span = usableWidth - _embedWidth;
        if (span > 0)
        {
            var ratio = (double)(curRect.Left - _taskbarRect.Left - TaskbarLeftMargin) / span;
            _configService.Settings.TaskbarRelativeX = Math.Max(0, Math.Min(1, ratio));
        }
        try { _configService.Save(); }
        catch (Exception ex) { FileLogger.Error("TaskbarWindow", "保存任务栏窗口宽度失败", ex); }
    }

    /// <summary>
    /// req-088 B5：根据 MiniChartItems（来自 Registry）计算所需窗口宽度。
    /// 各 Kind 对应固定宽度：MiniText=按内容 / MiniRingChart=58 / MiniLineChart=132 / 占位图=80。
    /// </summary>
    private double ComputeWindowWidth()
    {
        if (_configService.Settings.TaskbarWidth is double manual && manual > 0)
        {
            var maxManual = _taskbarRect.Width > 0
                ? _taskbarRect.Width - TaskbarLeftMargin - TaskbarRightMargin
                : 1200;
            if (maxManual < MinResizeWidth) maxManual = MinResizeWidth;
            return Math.Max(MinResizeWidth, Math.Min(maxManual, manual));
        }

        if (VisibleMiniCharts.Count == 0) return 240;
        double total = 24;
        foreach (var item in VisibleMiniCharts)
        {
            total += item.Kind switch
            {
                MiniChartKind.MiniLineChart => 132,
                MiniChartKind.MiniRingChart => 58,
                MiniChartKind.MiniBarChart => 88,
                MiniChartKind.MiniHeatMap => 128,
                _ => MeasureMiniTextWidth(item)
            };
        }
        return Math.Min(1200, Math.Max(280, total));
    }

    /// <summary>
    /// 测量 MiniText 模板的实际宽度（DIP）。问题8：按 ShowProviderInText + MiniTextBody 字面估算，避免 FormattedText 在 Loaded 前 NRE；
    /// 问题13：勾选 logo 时额外预留 logo 宽度。
    /// </summary>
    private double MeasureMiniTextWidth(MiniChartItemViewModel item)
    {
        var displayId = item.ShowProviderInText ? (item.ProviderId ?? "") : "";
        var bodyText = item.MiniTextBody ?? "";
        var logoWidth = item.ShowLogo ? 20.0 : 0.0;
        return displayId.Length * 8.0 + bodyText.Length * 7.5 + 24 + logoWidth;
    }

    public void RecalculateSize()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_isDragging) return;
        var taskbarHandle = _taskbarHelper.GetHandle();
        if (taskbarHandle == IntPtr.Zero) return;
        TaskbarNativeMethods.GetWindowRect(taskbarHandle, out _taskbarRect);
        TaskbarNativeMethods.GetWindowRect(_hwnd, out var curRect);
        _embedWidth = (int)ComputeWindowWidth();
        var newLeft = ClampXToTaskbar(curRect.Left);
        TaskbarNativeMethods.MoveWindow(_hwnd, newLeft, _taskbarRect.Top, _embedWidth, _taskbarRect.Height, true);
    }

    private async void OnRingChartCenterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Controls.RingChartControl ringChart) return;
        var providerId = ringChart.Tag as string;
        if (string.IsNullOrEmpty(providerId)) return;
        await TryRefreshAsync(providerId);
    }

    private void OnRingChartMetricKeyChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e) { }

    /// <summary>
    /// req-107 B4：迷你环滚轮切换数据组。
    /// <para>滚轮向上 = 上一组（delta -1），滚轮向下 = 下一组（delta +1），循环切换。
    /// 实际发生切换时标记 Handled 阻止 ScrollViewer 横向滚动；
    /// 无数据组或仅 1 组时不处理，滚轮事件透传给外层 ScrollViewer。</para>
    /// </summary>
    private void OnRingChartPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not MiniChartItemViewModel item) return;
        var delta = e.Delta > 0 ? -1 : 1; // 向上滚 = 上一组，向下滚 = 下一组
        if (item.CycleDataGroup(delta))
            e.Handled = true; // 切换成功才吞掉事件，避免任务栏窗口内横向滚动
    }
}

/// <summary>
/// req-088 B5：ObservableCollection 的轻量扩展（暴露 Clear / Add / FirstOrDefault 等 LINQ 友好的 API）。
/// 这里只用标准 ObservableCollection 即可，留此类型为以后扩展预留。
/// </summary>
public class ObservableCollectionEx<T> : System.Collections.ObjectModel.ObservableCollection<T>
{
}
