using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Services;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

// =========================================================
// WPF / WinForms 命名冲突解决说明
// =========================================================
// 项目 .csproj 同时启用了 <UseWPF> + <UseWindowsForms> + <ImplicitUsings>，
// SDK 会自动生成 GlobalUsings.g.cs 注入：global using global::System.Windows.Forms;
// 因此本文件必须用 alias 显式选择 WPF 的同名类型，避免 ambiguous 编译错。
//（如果改为 Style=Expression 类型的 alias 会隐藏 using；这里是常规模型 alias。）
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace UsageMonitor.App.Views;

/// <summary>
/// 托盘悬浮窗 - 鼠标悬停托盘图标时弹出
/// - 显示已启用 Provider 的卡片式摘要（与主窗口卡片风格一致）
/// - 鼠标离开后延迟关闭（可在设置中调整延迟毫秒数）
/// - 鼠标进入悬浮窗时取消关闭计时
/// - 支持整窗拖拽：拖拽结束后位置写入配置文件，下次启动保留
/// </summary>
public partial class TrayTooltipWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ConfigService _configService;
    private DispatcherTimer? _hideTimer;

    /// <summary>是否已经由本次 ShowNearCursor 完成首次定位（用于 LocationChanged 过滤编程设置首帧）</summary>
    private bool _hasPlacedOnce;
    /// <summary>拖拽节流保存：位置变更 500ms 后才真正写盘</summary>
    private DispatcherTimer? _savePositionTimer;

    // 手动拖拽字段（WPF Window.DragMove() 在 WindowStyle=None + ShowActivated=False + AllowsTransparency=True 的窗体中不可靠，
    // 所以采用业界标准的鼠标捕获方案：Mouse.Capture + PreviewMouseMove + WinForms Cursor 读取）
    /// <summary>是否正处于手动拖拽状态</summary>
    private bool _isDragging;
    /// <summary>拖拽起始点：按下时光标在屏幕坐标系中的位置（win32 坐标）</summary>
    private System.Drawing.Point _dragStartCursorScreen;
    /// <summary>拖拽起始点：按下时窗口的 Left/Top</summary>
    private Point _dragStartWindowLeftTop;

    // req-036：右侧拖拽调整宽度字段
    /// <summary>是否正处于宽度调整状态</summary>
    private bool _isResizingWidth;
    /// <summary>宽度调整起始点：按下时光标在屏幕坐标系的 X</summary>
    private double _resizeStartCursorX;
    /// <summary>宽度调整起始点：按下时窗口的 Width</summary>
    private double _resizeStartWidth;
    /// <summary>最小宽度限制（避免拖到 0）</summary>
    private const double MinWindowWidth = 200;

    public TrayTooltipWindow(MainViewModel viewModel, ConfigService configService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _configService = configService;
        DataContext = viewModel;

        // 鼠标进入悬浮窗时取消关闭
        MouseEnter += (_, _) => CancelHide();
        // 鼠标离开悬浮窗时启动延迟关闭
        MouseLeave += (_, _) => RequestHide();
        // 窗口停用时关闭（点击其他窗口）
        Deactivated += (_, _) => ForceHide();

        // 窗口位置变更：拖拽结束后由 OnLocationChanged 统一节流保存
        LocationChanged += OnLocationChanged;

        // 拖拽入口改为 override OnPreviewMouseXxx 虚方法（见类下方）—— 不使用 routed event 订阅，
        // 是因为在 WindowStyle=None + ShowActivated=False + AllowTransparency=True 这几个 flag 重叠下，
        // routed event 订阅者不一定能可靠收到输入；override 是 WPF 框架 100% 调用的入口。

        // req-036：加载保存的悬浮窗宽度（若有）
        var savedWidth = _configService.Settings.TrayTooltipWindowWidth;
        if (savedWidth.HasValue && savedWidth.Value >= MinWindowWidth)
        {
            Width = savedWidth.Value;
        }

        // req-036：右侧拖拽条事件绑定
        Loaded += (_, _) =>
        {
            if (RightResizeGrip != null)
            {
                RightResizeGrip.MouseLeftButtonDown += OnResizeGripMouseDown;
                RightResizeGrip.MouseMove += OnResizeGripMouseMove;
                RightResizeGrip.MouseLeftButtonUp += OnResizeGripMouseUp;
            }
        };
        // 窗口关闭时移除事件订阅，避免内存泄漏
        Closed += (_, _) =>
        {
            if (RightResizeGrip != null)
            {
                RightResizeGrip.MouseLeftButtonDown -= OnResizeGripMouseDown;
                RightResizeGrip.MouseMove -= OnResizeGripMouseMove;
                RightResizeGrip.MouseLeftButtonUp -= OnResizeGripMouseUp;
            }
        };
    }

    /// <summary>
    /// 在指定屏幕坐标处显示悬浮窗。
    /// 优先使用上次保存的拖拽位置（<see cref="AppSettings.TrayTooltipPosition"/>）；
    /// 若用户从未拖拽过或保存位置已超出当前屏幕，则回退到默认（光标附近）。
    /// </summary>
    public void ShowNearCursor(Point screenPos)
    {
        // req-050：使用设置的 Width 进行布局，避免 SizeToContent 导致宽度被内容决定
        // 先 Measure 以获得内容高度，但宽度使用 Width 属性（如果已设置）
        var targetWidth = double.IsNaN(Width) ? 300 : Width;  // Width 可能是 NaN（未设置）
        Measure(new Size(targetWidth, double.PositiveInfinity));
        Arrange(new Rect(0, 0, targetWidth, DesiredSize.Height));

        var width = ActualWidth > 0 ? ActualWidth : targetWidth;
        var height = ActualHeight > 0 ? ActualHeight : 200;
        var workArea = SystemParameters.WorkArea;

        double x, y;

        // 优先级 1：配置中有保存位置且仍然在屏幕工作区内
        var saved = _configService.Settings.TrayTooltipPosition;
        if (saved != null && IsPositionInBounds(saved.X, saved.Y, width, height, workArea))
        {
            x = saved.X;
            y = saved.Y;
        }
        else
        {
            // 优先级 2：默认围绕光标定位（光标左上方，与原行为一致）
            x = screenPos.X - width / 2.0;
            y = screenPos.Y - height - 12;

            // 边界保护：不能超出工作区
            if (x + width > workArea.Right) x = workArea.Right - width - 8;
            if (x < workArea.Left) x = workArea.Left + 8;
            if (y < workArea.Top) y = screenPos.Y + 20;
            if (y + height > workArea.Bottom) y = workArea.Bottom - height - 8;
        }

        _hasPlacedOnce = true;
        Left = x;
        Top = y;

        CancelHide();
        if (!IsVisible) Show();
    }

    /// <summary>
    /// 启动延迟关闭
    /// </summary>
    public void RequestHide(int delayMs = 500)
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(0, delayMs))
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            Hide();
        };
        _hideTimer.Start();
    }

    /// <summary>
    /// 取消正在等待的关闭计时
    /// </summary>
    public void CancelHide()
    {
        _hideTimer?.Stop();
        _hideTimer = null;
    }

    /// <summary>
    /// 立即强制隐藏。req-050：隐藏前保存当前宽度，确保下次打开时宽度一致。
    /// </summary>
    public void ForceHide()
    {
        _hideTimer?.Stop();
        _hideTimer = null;
        if (IsVisible)
        {
            // req-050：隐藏前保存当前宽度，避免下次打开时宽度变化
            SaveWidthToConfig();
            Hide();
        }
    }

    /// <summary>req-050：保存当前宽度到配置（仅在宽度有效时保存）。</summary>
    private void SaveWidthToConfig()
    {
        if (ActualWidth >= MinWindowWidth && !double.IsNaN(ActualWidth))
        {
            _configService.Settings.TrayTooltipWindowWidth = ActualWidth;
            try
            {
                _configService.Save();
            }
            catch (Exception ex)
            {
                FileLogger.Warn("TrayTooltipWindow", $"保存宽度失败：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 拖拽开始：override Window.OnPreviewMouseLeftButtonDown。
    /// <para>
    /// 为什么用 override 而不是 subscribe routed event：
    /// 1. WPF 框架调用源是 Window -> HwndSource，预览事件在 WPF 中是模拟的，从根向叶传递
    ///    （Win32 层没有 Preview/Bubble 区别）。“在 WPF 框架能收到的”回调点就是 OnPreviewXxx 虚方法。
    /// 2. routed event 依赖订阅者列表，本窗叠加的 5 个 flag 偶尔会让订阅者不被回调（社区报告）。
    /// 3. 虚方法override 是 WPF 设计预留的可靠钩子点，走不晕。
    /// </para>
    /// </summary>
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // 必须 base，否则 WPF 默认 RaiseEvent 会丢失其他 attach handler
        base.OnPreviewMouseLeftButtonDown(e);
        if (e.Handled) return;

        if (e.ChangedButton != MouseButton.Left) return;

        // 滚动条（thumb / track / button）按下时让 ScrollViewer 自己处理滚动，不进入拖拽
        if (IsInsideScrollBar(e.OriginalSource as DependencyObject)) return;

        // 记录起始状态：屏幕坐标系的光标位置 + 窗口当前位置
        // 使用 WinForms.Cursor.Position 而非 e.GetPosition(WPF-window)：二者在 AllowTransparency=True 下可能不一致；
        // WinForms 的 API 直接走 Win32 GetCursorPos，不依赖 WPF 的 hit-test 坐标系，在所有场景下都准确。
        var win32Cursor = System.Windows.Forms.Cursor.Position;
        _dragStartCursorScreen = win32Cursor;
        _dragStartWindowLeftTop = new Point(Left, Top);
        _isDragging = true;

        // 强制本 Window 接收后续所有鼠标事件（即使光标跑到屏幕外）
        Mouse.Capture(this);

        e.Handled = true;
    }

    /// <summary>
    /// 拖拽中：override Window.OnPreviewMouseMove。读取 WinForms 光标位置并更新 Window.Left/Top。
    /// 只在 MouseDown 已启动拖拽且左键仍按住时进行更新。
    /// </summary>
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (e.Handled) return;

        if (!_isDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            // 鼠标左键不在按下状态（异常路径，正常会通过 OnPreviewMouseLeftButtonUp 结束）
            EndDrag();
            return;
        }

        var win32Cursor = System.Windows.Forms.Cursor.Position;
        var dx = win32Cursor.X - _dragStartCursorScreen.X;
        var dy = win32Cursor.Y - _dragStartCursorScreen.Y;
        Left = _dragStartWindowLeftTop.X + dx;
        Top = _dragStartWindowLeftTop.Y + dy;
    }

    /// <summary>
    /// 拖拽结束：override Window.OnPreviewMouseLeftButtonUp。Mouse.Capture(null) 释放。
    /// </summary>
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (e.Handled) return;

        if (e.ChangedButton != MouseButton.Left) return;
        if (!_isDragging) return;
        EndDrag();
        e.Handled = true;
    }

    /// <summary>
    /// 退出拖拽状态：重置标志位 + 释放鼠标捕获。
    /// LocationChanged 节流保存会在后续 500ms 内自动存储新位置。
    /// </summary>
    private void EndDrag()
    {
        _isDragging = false;
        Mouse.Capture(null);
    }

    /// <summary>
    /// 判断命中点是否落在 ScrollBar 部件（thumb / track / 上下按钮等）上。
    /// </summary>
    private static bool IsInsideScrollBar(DependencyObject? source)
    {
        while (source != null)
        {
            // 这里 ScrollBar 走的是文件顶部 alias 解析为 WPF 的 System.Windows.Controls.Primitives.ScrollBar。
            if (source is ScrollBar) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    /// <summary>
    /// 窗口位置变化时，统一节流保存。
    /// 仅当最近一次变化是用户拖拽（非编程设置）时才真正落库。
    /// </summary>
    private void OnLocationChanged(object? sender, EventArgs e)
    {
        // 抑制编程设置时的首次定位；保留其他时机的拖拽捕获
        if (!_hasPlacedOnce) return;

        // 重置节流计时器：500ms 内还有新位置就推迟保存
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
    /// 将当前 Left/Top 写入到 AppSettings.TrayTooltipPosition，并触发一次 Save()。
    /// 若当前位置已超出屏幕工作区（例如分辨率变更），则保留配置项不变（不入库）。
    /// </summary>
    private void SavePositionToConfig()
    {
        var workArea = SystemParameters.WorkArea;
        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;

        if (!IsPositionInBounds(Left, Top, w, h, workArea))
        {
            // 位置已超出可视范围（例如以前是 4K 现在是 1080p），不保存避免下次启动后看不见
            FileLogger.Warn("TrayTooltipWindow",
                $"跳过保存：当前拖拽位置 ({Left:0},{Top:0}) 已超出工作区 {workArea}");
            return;
        }

        _configService.Settings.TrayTooltipPosition = new TrayTooltipPosition
        {
            X = Left,
            Y = Top
        };
        try
        {
            _configService.Save();
            FileLogger.Info("TrayTooltipWindow",
                $"已保存拖拽位置到配置：({Left:0},{Top:0})");
        }
        catch (Exception ex)
        {
            // 配置保存失败不应阻塞悬浮窗使用，仅记录日志
            FileLogger.Error("TrayTooltipWindow", "保存悬浮窗位置失败", ex);
        }
    }

    /// <summary>
    /// 检查 (x, y) 作为窗口左上角、尺寸 (w, h) 是否仍大部分落在当前屏幕工作区内。
    /// 用于加载已有保存位置前做"分辨率变更"健壮性校验：要求至少一半边长在屏幕内（避免完全不可见）。
    /// </summary>
    private static bool IsPositionInBounds(double x, double y, double w, double h, Rect workArea)
    {
        var visibleRatio = 0.5;
        var minVisibleW = w * visibleRatio;
        var minVisibleH = h * visibleRatio;

        var visibleW = Math.Min(x + w, workArea.Right) - Math.Max(x, workArea.Left);
        var visibleH = Math.Min(y + h, workArea.Bottom) - Math.Max(y, workArea.Top);

        return visibleW >= minVisibleW && visibleH >= minVisibleH;
    }

    // =====================================================================
    // req-036：右侧拖拽调整宽度
    // =====================================================================

    /// <summary>右侧拖拽条鼠标按下：记录起始状态 + 捕获鼠标。</summary>
    private void OnResizeGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        _resizeStartCursorX = PointToScreen(e.GetPosition(this)).X;
        _resizeStartWidth = ActualWidth;
        _isResizingWidth = true;
        Mouse.Capture(RightResizeGrip);
        e.Handled = true;
    }

    /// <summary>右侧拖拽条鼠标移动：更新窗口宽度。</summary>
    private void OnResizeGripMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizingWidth) return;
        var currentX = PointToScreen(e.GetPosition(this)).X;
        var dx = currentX - _resizeStartCursorX;
        var newWidth = Math.Max(MinWindowWidth, _resizeStartWidth + dx);
        Width = newWidth;
        e.Handled = true;
    }

    /// <summary>右侧拖拽条鼠标松开：保存宽度到配置。</summary>
    private void OnResizeGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isResizingWidth = false;
        Mouse.Capture(null);
        // req-050：使用统一的保存方法
        SaveWidthToConfig();
        FileLogger.Info("TrayTooltipWindow", $"拖拽结束，已保存悬浮窗宽度：{ActualWidth:0}px");
        e.Handled = true;
    }
}
