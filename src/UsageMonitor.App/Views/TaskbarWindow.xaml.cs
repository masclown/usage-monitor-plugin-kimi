using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;
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
/// </summary>
public partial class TaskbarWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigService _configService;
    private readonly TaskbarHelper _taskbarHelper;

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
    private int _dragStartWindowLeft;           // 拖拽起始时窗口左边（屏幕坐标）
    private int _dragStartWindowWidth;          // 拖拽起始时窗口宽度（用于调整宽度模式）

    // 位置保存节流
    private DispatcherTimer? _savePositionTimer;

    public TaskbarWindow(MainViewModel viewModel, ConfigService configService, TaskbarHelper taskbarHelper)
    {
        _viewModel = viewModel;
        _configService = configService;
        _taskbarHelper = taskbarHelper;
        InitializeComponent();
        DataContext = _viewModel;
        // 默认光标为移动样式（进入边缘热区时由 UpdateCursorForPosition 切换为水平调整箭头）
        Cursor = System.Windows.Input.Cursors.SizeAll;

        // 位置变更触发节流保存
        LocationChanged += OnLocationChanged;

        // ★ WPF 在 Loaded / Activated / SourceInitialized 阶段会强制重置窗口位置到 Left/Top 值。
        // 我们用 Win32 SetWindowPos 已经设过任务栏位置，但 WPF Loaded 后会覆盖。
        // 解决：在 WPF 每个关键阶段都强制用 Win32 SetWindowPos 重置回任务栏位置。
        SourceInitialized += OnSourceInitializedOverride;
        Loaded += OnLoadedOverride;
        Activated += OnActivatedOverride;
    }

    private void OnSourceInitializedOverride(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ApplyWin32Position), DispatcherPriority.Background);
    }

    private void OnLoadedOverride(object? sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ApplyWin32Position), DispatcherPriority.Background);
    }

    private void OnActivatedOverride(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ApplyWin32Position), DispatcherPriority.Background);
    }

    /// <summary>
    /// 用 Win32 SetWindowPos 强制把窗口位置设到任务栏内（pixel 绝对坐标）。
    /// 必须在 WPF Loaded/Activated 之后调用，强制覆盖 WPF 内部的 Left/Top 重置行为。
    /// 不依赖 EmbedIntoTaskbar 阶段记录的字段，自己重新查任务栏尺寸 —— 因为 Loaded 可能先触发。
    /// </summary>
    private void ApplyWin32Position()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_isDragging) return;   // 拖拽进行中不抢位置，避免与用户拖拽打架
        var taskbarHandle = _taskbarHelper.GetHandle();
        if (taskbarHandle == IntPtr.Zero) return;

        TaskbarNativeMethods.RECT taskbarRect;
        if (!TaskbarNativeMethods.GetWindowRect(taskbarHandle, out taskbarRect)) return;
        if (taskbarRect.Width <= 0) return;

        var width = (int)ComputeWindowWidth();
        if (width < 100) width = 280;
        var height = taskbarRect.Height;
        // 读 relX：保存的相对位置（默认 0.5 = 任务栏正中），与 EmbedIntoTaskbar 保持一致
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

    /// <summary>
    /// 外部入口：把窗口真正嵌入任务栏，并应用已保存的相对位置。
    /// 由 App.InitializeTaskbarWindow 调用。
    /// </summary>
    public void EmbedIntoTaskbar()
    {
        if (!_taskbarHelper.Initialize()) return;

        // ★ 关键修复：WPF Window 的 hwnd 只有在 Show() 之后才创建。
        // 原代码先 new WindowInteropHelper(this).Handle 然后再 Show()，结果 Handle=IntPtr.Zero，
        // SetParent(0, ...) / GetWindowLong(0, ...) / SetWindowPos(0, ...) 全部失败 → 嵌入失败。
        // 正确顺序：先 Show() 创建 hwnd，再用 EnsureHandle() 拿 hwnd（Handle 属性不保证强制创建）。
        if (!IsVisible) Show();
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (_hwnd == IntPtr.Zero) return;

        var taskbarHandle = _taskbarHelper.GetHandle();
        if (taskbarHandle == IntPtr.Zero) return;
        TaskbarNativeMethods.GetWindowRect(taskbarHandle, out _taskbarRect);

        _embedWidth = (int)ComputeWindowWidth();

        // 现在调用 EmbedWindow：内部 SetWindowLongPtr(GWL_HWNDPARENT) + 清理 style +
        // SetWindowPos 强制位置在任务栏内。
        // 关键：不调用 WPF 的 Left/Top setter —— 它会触发 WPF 内部 SetWindowPos 覆盖我们
        // 已经设好的屏幕绝对坐标位置。
        // 位置计算：把 config.json 保存的 TaskbarRelativeX 直接传给 EmbedWindow（一次 SetWindowPos）。
        //   默认值 0.5 = 任务栏正中（避免用户感觉"窗口在屏幕右上角"）。
        var relX = _configService.Settings.TaskbarRelativeX ?? 0.5;
        _taskbarHelper.EmbedWindow(_hwnd, _embedWidth, relX);

        // 注意：之前这里调 ApplySavedRelativeX() 会被 EmbedWindow 后的 SetWindowPos 覆盖，
        // 导致保存的位置反复重设。现在 EmbedWindow 内部已用 relX 计算一次位置，调用顺序无关。

        _hasPlacedOnce = true;
    }

    /// <summary>
    /// 拖拽开始：记录起点（屏幕光标 + 窗口当前屏幕左边），
    /// Mouse.Capture(this) 让光标跑出任务栏外也能继续收 MouseMove。
    /// </summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (e.Handled) return;
        if (e.ChangedButton != MouseButton.Left) return;

        // 命中检测：落在左右边缘热区 => 调整宽度；否则 => 移动位置。
        var mode = HitTestEdge(e.GetPosition(this).X);

        // 双击边缘热区：清除用户手动宽度，恢复内容自适应。
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

        // 本窗是顶级窗口（视觉嵌入，非 WS_CHILD），MoveWindow/GetWindowRect 全走「屏幕坐标」。
        // 拖拽开始时刷新任务栏矩形（防止任务栏被移动/改尺寸），并记录窗口当前屏幕左边+宽度+光标屏幕位置。
        var taskbarHandle = _taskbarHelper.GetHandle();
        if (taskbarHandle != IntPtr.Zero)
            TaskbarNativeMethods.GetWindowRect(taskbarHandle, out _taskbarRect);
        TaskbarNativeMethods.GetWindowRect(_hwnd, out var curRect);
        _dragStartWindowLeft = curRect.Left;
        _dragStartWindowWidth = curRect.Width;
        _dragStartCursorScreen = System.Windows.Forms.Cursor.Position;

        _isDragging = true;
        Mouse.Capture(this);
        e.Handled = true;
    }

    /// <summary>
    /// 拖拽中：根据屏幕光标 delta 计算任务栏内新 X，MoveWindow 移动。
    /// 边界裁剪：始终处于 [0, taskbarRect.Width - embedWidth - 80]。
    /// </summary>
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (e.Handled) return;

        // 非拖拽状态：根据鼠标位置更新光标，引导用户识别“两侧可调整宽度、中间可移动位置”。
        if (!_isDragging)
        {
            UpdateCursorForPosition(e.GetPosition(this).X);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        var cur = System.Windows.Forms.Cursor.Position;
        var dx = cur.X - _dragStartCursorScreen.X;

        if (_dragMode == DragMode.ResizeRight)
        {
            // 右边缘：左边界不动，宽度随光标水平位移增减。
            var newWidth = ClampResizeWidth(_dragStartWindowWidth + dx);
            _embedWidth = newWidth;
            TaskbarNativeMethods.MoveWindow(_hwnd, _dragStartWindowLeft, _taskbarRect.Top, newWidth, _taskbarRect.Height, true);
        }
        else if (_dragMode == DragMode.ResizeLeft)
        {
            // 左边缘：右边界不动，左边界随光标移动，宽度反向增减。
            var rightEdge = _dragStartWindowLeft + _dragStartWindowWidth;
            var newWidth = ClampResizeWidth(_dragStartWindowWidth - dx);
            var newLeft = rightEdge - newWidth;
            // 左边界不越过任务栏左边距（并据此回算宽度）。
            var minLeft = _taskbarRect.Left + TaskbarLeftMargin;
            if (newLeft < minLeft)
            {
                newLeft = minLeft;
                newWidth = rightEdge - newLeft;
            }
            _embedWidth = newWidth;
            TaskbarNativeMethods.MoveWindow(_hwnd, newLeft, _taskbarRect.Top, newWidth, _taskbarRect.Height, true);
        }
        else
        {
            // 移动位置：屏幕坐标系下的新左边 = 起始窗口左边 + 光标水平位移。
            // Y 恒为任务栏顶端 _taskbarRect.Top，只允许水平移动（保持嵌在任务栏内）。
            var newLeft = ClampXToTaskbar(_dragStartWindowLeft + dx);
            TaskbarNativeMethods.MoveWindow(_hwnd, newLeft, _taskbarRect.Top, _embedWidth, _taskbarRect.Height, true);
        }
    }

    /// <summary>
    /// 拖拽结束：释放 Mouse.Capture；后续由 LocationChanged 节流保存新位置。
    /// </summary>
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (e.Handled) return;
        if (e.ChangedButton != MouseButton.Left) return;
        if (!_isDragging) return;

        // 调整宽度结束：持久化用户手动宽度（ResizeLeft 会改变 X，SaveWidthToConfig 内一并重存相对位置）。
        if (_dragMode == DragMode.ResizeLeft || _dragMode == DragMode.ResizeRight)
            SaveWidthToConfig();

        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        _isDragging = false;
        _dragMode = DragMode.None;
        Mouse.Capture(null);
    }

    /// <summary>任务栏可用区左右边距（与 TaskbarHelper.EmbedWindow 保持一致）。</summary>
    private const int TaskbarLeftMargin = 20;
    private const int TaskbarRightMargin = 80;

    /// <summary>
    /// 把窗口左边的屏幕 X 裁剪到任务栏可用区间 [Left+左边距, Right-右边距-窗口宽]。
    /// 保证窗口始终横向落在任务栏内、不与通知区重叠。
    /// </summary>
    private int ClampXToTaskbar(int screenX)
    {
        var minX = _taskbarRect.Left + TaskbarLeftMargin;
        var maxX = _taskbarRect.Right - TaskbarRightMargin - _embedWidth;
        if (maxX < minX) maxX = minX;
        return Math.Max(minX, Math.Min(maxX, screenX));
    }

    /// <summary>
    /// 命中检测：判断鼠标在窗口内的水平位置属于哪种拖拽模式。
    /// 左/右边缘 ResizeEdge 范围内为调整宽度，中间区域为移动位置。
    /// 坐标使用 WPF DIP（e.GetPosition(this) 与 ActualWidth 同域）。
    /// </summary>
    private DragMode HitTestEdge(double xInWindow)
    {
        if (ActualWidth <= 0) return DragMode.Move;
        if (xInWindow <= ResizeEdge) return DragMode.ResizeLeft;
        if (xInWindow >= ActualWidth - ResizeEdge) return DragMode.ResizeRight;
        return DragMode.Move;
    }

    /// <summary>
    /// 非拖拽时根据鼠标水平位置设置光标：两侧边缘热区显示水平调整箭头(SizeWE)，
    /// 中间区域显示移动光标(SizeAll)，引导用户识别可调整/可移动区域。
    /// </summary>
    private void UpdateCursorForPosition(double xInWindow)
    {
        Cursor = HitTestEdge(xInWindow) == DragMode.Move
            ? System.Windows.Input.Cursors.SizeAll
            : System.Windows.Input.Cursors.SizeWE;
    }

    /// <summary>
    /// 将手动调整的宽度钳制到 [MinResizeWidth, 任务栏可用宽度] 区间，
    /// 防止窗口过窄无法操作或超出任务栏可用区。
    /// </summary>
    private int ClampResizeWidth(int width)
    {
        var maxWidth = _taskbarRect.Width - TaskbarLeftMargin - TaskbarRightMargin;
        if (maxWidth < MinResizeWidth) maxWidth = MinResizeWidth;
        return Math.Max(MinResizeWidth, Math.Min(maxWidth, width));
    }

    // =========================================================
    // 位置保存：LocationChanged 500ms 节流，写 0~1 相对比例
    // =========================================================

    /// <summary>
    /// 窗口位置变更事件节流：500ms 内重复事件推迟保存，
    /// 拖动过程中只写一次盘而非每帧。
    /// </summary>
    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_hasPlacedOnce) return;

        _savePositionTimer?.Stop();
        _savePositionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _savePositionTimer.Tick += (_, _) =>
        {
            _savePositionTimer?.Stop();
            SavePositionToConfig();
        };
        _savePositionTimer.Start();
    }

    /// <summary>
    /// 把当前父坐标系 X 偏移换算为 0~1 相对比例，写入 config.json。
    /// 任务栏宽度变化或 DPI 变更后下次启动自动适配。
    /// </summary>
    private void SavePositionToConfig()
    {
        TaskbarNativeMethods.GetWindowRect(_hwnd, out var curRect);

        // 与 EmbedWindow / ApplyWin32Position 的 relX→X 映射互为逆运算，避免“拖到某处→下次启动漂移”：
        //   X = taskbarLeft + 左边距 + relX * (usableWidth - width)
        //   ⇒ relX = (X - taskbarLeft - 左边距) / (usableWidth - width)
        var usableWidth = _taskbarRect.Width - TaskbarLeftMargin - TaskbarRightMargin;
        var span = usableWidth - _embedWidth;
        if (span <= 0) return;

        var ratio = (double)(curRect.Left - _taskbarRect.Left - TaskbarLeftMargin) / span;
        ratio = Math.Max(0, Math.Min(1, ratio));

        _configService.Settings.TaskbarRelativeX = ratio;
        try
        {
            _configService.Save();
            FileLogger.Info("TaskbarWindow",
                $"TaskbarRelativeX 已保存：{ratio:F3} (winLeft={curRect.Left}, taskbarLeft={_taskbarRect.Left}, span={span})");
        }
        catch (Exception ex)
        {
            FileLogger.Error("TaskbarWindow", "保存任务栏窗口位置失败", ex);
        }
    }

    /// <summary>
    /// 保存用户手动调整后的窗口宽度到 config.json（写入 AppSettings.TaskbarWidth）。
    /// 因 ResizeLeft 会改变窗口 X，故同时按当前位置重存 TaskbarRelativeX，避免下次启动位置漂移。
    /// </summary>
    private void SaveWidthToConfig()
    {
        TaskbarNativeMethods.GetWindowRect(_hwnd, out var curRect);
        _embedWidth = curRect.Width;
        _configService.Settings.TaskbarWidth = curRect.Width;

        // 同步重算相对位置（与 SavePositionToConfig 的 relX 映射一致），保证宽度+位置一起持久化。
        var usableWidth = _taskbarRect.Width - TaskbarLeftMargin - TaskbarRightMargin;
        var span = usableWidth - _embedWidth;
        if (span > 0)
        {
            var ratio = (double)(curRect.Left - _taskbarRect.Left - TaskbarLeftMargin) / span;
            _configService.Settings.TaskbarRelativeX = Math.Max(0, Math.Min(1, ratio));
        }

        try
        {
            _configService.Save();
            FileLogger.Info("TaskbarWindow", $"TaskbarWidth 已保存：{_embedWidth}px (winLeft={curRect.Left})");
        }
        catch (Exception ex)
        {
            FileLogger.Error("TaskbarWindow", "保存任务栏窗口宽度失败", ex);
        }
    }

    // =========================================================
    // 尺寸计算：与已启用 Provider 列表联动（Provider 过滤生效点）
    // =========================================================

    /// <summary>
    /// 根据各 Provider 显示模式计算所需窗口高度（取最大值）。
    /// 只考虑已启用的 Provider（_viewModel.EnabledUsages）。
    /// </summary>
    private double ComputeWindowHeight()
    {
        double max = 36;  // 文字模式基础高度
        foreach (var usage in _viewModel.EnabledUsages)
        {
            var h = usage.DisplayMode switch
            {
                TaskbarDisplayMode.MiniLineChart => 56,
                TaskbarDisplayMode.RingChart => 56,
                _ => 36
            };
            if (h > max) max = h;
        }
        return max;
    }

    /// <summary>
    /// 根据各 Provider 显示模式计算窗口宽度。
    /// 文字模式每项约 120px，折线图每项 132px，圆环图每项 96px。
    /// 只考虑已启用的 Provider（_viewModel.EnabledUsages）。
    /// </summary>
    private double ComputeWindowWidth()
    {
        // 手动优先：用户拖拽调整过宽度后，直接采用持久化值（钳制到合理区间）。
        if (_configService.Settings.TaskbarWidth is double manual && manual > 0)
        {
            var maxManual = _taskbarRect.Width > 0
                ? _taskbarRect.Width - TaskbarLeftMargin - TaskbarRightMargin
                : 1200;
            if (maxManual < MinResizeWidth) maxManual = MinResizeWidth;
            return Math.Max(MinResizeWidth, Math.Min(maxManual, manual));
        }

        if (_viewModel.EnabledUsages.Count == 0) return 240;

        double total = 24; // 左右 padding
        foreach (var usage in _viewModel.EnabledUsages)
        {
            total += usage.DisplayMode switch
            {
                TaskbarDisplayMode.MiniLineChart => 132,
                TaskbarDisplayMode.RingChart => 96,
                _ => MeasureTextItemWidth(usage)   // 文字模式：按实际内容长度自适应
            };
        }
        return Math.Min(1200, Math.Max(280, total));
    }

    /// <summary>
    /// 测量文字模式下单个 Provider 项的显示宽度（DIP，直接作为像素使用，与图表项固定值口径一致）。
    /// 与 TaskbarTextTemplate 结构对应：DisplayName(SemiBold,12) + RemainingText(Normal,12)
    /// + 4(第二个 TextBlock 左 Margin) + 16(StackPanel 右 Margin) + 10(安全余量)。
    /// </summary>
    private double MeasureTextItemWidth(ProviderUsageViewModel usage)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var nameFace = new Typeface(System.Windows.SystemFonts.MessageFontFamily,
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var remainFace = new Typeface(System.Windows.SystemFonts.MessageFontFamily,
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var nameWidth = MeasureText(usage.DisplayName, nameFace, 12, pixelsPerDip);
        var remainWidth = MeasureText(usage.RemainingText, remainFace, 12, pixelsPerDip);
        return nameWidth + remainWidth + 4 + 16 + 10;
    }

    /// <summary>用 FormattedText 测量单段文本在指定字体下的渲染宽度（DIP）。</summary>
    private static double MeasureText(string? text, Typeface typeface, double fontSize, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSize,
            System.Windows.Media.Brushes.Black,
            pixelsPerDip);
        return ft.Width;
    }

    /// <summary>
    /// 刷新窗口尺寸（在 Provider 模式改变或启用集合变化时由 App 调用）。
    /// 重算宽度后用 MoveWindow 维持当前 X 不变。
    /// </summary>
    public void RecalculateSize()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_isDragging) return;   // 拖拽中不重排，避免与用户操作打架

        var taskbarHandle = _taskbarHelper.GetHandle();
        if (taskbarHandle == IntPtr.Zero) return;
        TaskbarNativeMethods.GetWindowRect(taskbarHandle, out _taskbarRect);

        // 保持当前屏幕 X（仅在越界时裁剪），重算宽度后重定位。
        TaskbarNativeMethods.GetWindowRect(_hwnd, out var curRect);
        _embedWidth = (int)ComputeWindowWidth();
        var newLeft = ClampXToTaskbar(curRect.Left);

        // ★ 关键修复：与拖拽同源 bug——原写 Y=0 会把窗口移到屏幕最顶端。
        //   顶级窗口须用屏幕坐标，Y 恒为任务栏顶端。
        TaskbarNativeMethods.MoveWindow(_hwnd, newLeft, _taskbarRect.Top, _embedWidth, _taskbarRect.Height, true);
    }
}
