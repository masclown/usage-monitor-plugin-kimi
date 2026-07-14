using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UsageMonitor.App.ViewModels;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace UsageMonitor.App.Views;

/// <summary>
/// 托盘悬浮窗 - 鼠标悬停托盘图标时弹出
/// - 显示所有 Provider 的卡片式摘要（与主窗口卡片风格一致）
/// - 鼠标离开后延迟关闭（可在设置中调整延迟毫秒数）
/// - 鼠标进入悬浮窗时取消关闭计时
/// </summary>
public partial class TrayTooltipWindow : Window
{
    private readonly MainViewModel _viewModel;
    private DispatcherTimer? _hideTimer;

    public TrayTooltipWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 鼠标进入悬浮窗时取消关闭
        MouseEnter += (_, _) => CancelHide();
        // 鼠标离开悬浮窗时启动延迟关闭
        MouseLeave += (_, _) => RequestHide();
        // 窗口停用时关闭（点击其他窗口）
        Deactivated += (_, _) => ForceHide();
    }

    /// <summary>
    /// 在指定屏幕坐标处显示悬浮窗（默认在光标左上方）
    /// </summary>
    public void ShowNearCursor(Point screenPos)
    {
        // 让窗口先 Measure 一次以获得 ActualWidth/Height
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Arrange(new Rect(DesiredSize));

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var workArea = SystemParameters.WorkArea;

        // 默认定位：光标左上方
        var width = ActualWidth > 0 ? ActualWidth : 320;
        var height = ActualHeight > 0 ? ActualHeight : 200;

        var x = screenPos.X - width / 2.0;
        var y = screenPos.Y - height - 12;

        // 边界保护：不能超出工作区
        if (x + width > workArea.Right) x = workArea.Right - width - 8;
        if (x < workArea.Left) x = workArea.Left + 8;
        if (y < workArea.Top) y = screenPos.Y + 20; // 改为光标下方
        if (y + height > workArea.Bottom) y = workArea.Bottom - height - 8;

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
    /// 立即强制隐藏
    /// </summary>
    public void ForceHide()
    {
        _hideTimer?.Stop();
        _hideTimer = null;
        if (IsVisible) Hide();
    }
}
