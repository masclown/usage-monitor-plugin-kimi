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
            // req-105：关闭时也确保 timer 释放。
            foreach (var item in VisibleMiniCharts)
                item.StopCountdownTimer();
        };
    }

    /// <summary>
    /// req-088 B5：从 Registry 拉取所有 descriptor，关联到 EnabledUsages 中的 ProviderUsageViewModel，
    /// 组装为 MiniChartItemViewModel 列表写入 VisibleMiniCharts。
    /// </summary>
    private void RebuildVisibleMiniCharts()
    {
        VisibleMiniCharts.Clear();
        var usages = _viewModel.EnabledUsages.ToDictionary(u => u.ProviderId, StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in _miniChartRegistry.GetAll())
        {
            // req-099 修复（Bug4）：仅显示已启用且有对应卡片 VM 的迷你图，跳过未启用/无数据的 Provider，避免任务栏空环。
            if (!usages.TryGetValue(descriptor.ProviderId, out var usageVm) || usageVm == null)
                continue;
            VisibleMiniCharts.Add(new MiniChartItemViewModel(descriptor, usageVm));
        }
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
        var item = VisibleMiniCharts.FirstOrDefault(x => x.ProviderId == vm.ProviderId);
        item?.RefreshFromUsageVm();
    }

    private void OnRootBorderMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { }
    private void OnRootBorderMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { }
    private async void OnRefreshAllClick(object sender, RoutedEventArgs e) { }

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
        EndDrag();
        e.Handled = true;
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
    /// 测量 MiniText 模板的实际宽度（DIP）。基于 ProviderId + UsagePercent 字面估算，避免 FormattedText 在 Loaded 前 NRE。
    /// </summary>
    private double MeasureMiniTextWidth(MiniChartItemViewModel item)
    {
        var displayId = item.ProviderId ?? "";
        var pctText = item.UsagePercent.HasValue ? $"{item.UsagePercent.Value:F0}%" : "--";
        return displayId.Length * 8.0 + pctText.Length * 7.5 + 24;
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
    private void OnRingChartPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) { }
}

/// <summary>
/// req-088 B5：ObservableCollection 的轻量扩展（暴露 Clear / Add / FirstOrDefault 等 LINQ 友好的 API）。
/// 这里只用标准 ObservableCollection 即可，留此类型为以后扩展预留。
/// </summary>
public class ObservableCollectionEx<T> : System.Collections.ObjectModel.ObservableCollection<T>
{
}
