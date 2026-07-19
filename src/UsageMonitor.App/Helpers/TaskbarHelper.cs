using System.Runtime.InteropServices;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// Windows Shell DeskBand COM 接口定义
/// 用于将 WPF 窗口嵌入到 Windows 任务栏中
/// 参考 TrafficMonitor 的任务栏嵌入实现方式
/// </summary>
public static class TaskbarNativeMethods
{
    /// <summary>任务栏窗口类名</summary>
    public const string TaskbarClassName = "Shell_TrayWnd";

    /// <summary>任务栏通知区域窗口类名</summary>
    public const string TrayNotifyClassName = "TrayNotifyWnd";

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
        string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // 64 位 hwnd 安全封装：在 64 位系统 IntPtr 长度为 8，必须用 SetWindowLongPtr；32 位用 SetWindowLong。
    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        return (IntPtr)SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    public const int GWL_STYLE = -16;
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    // 顶级窗口标志集：必须从原 style 中清除，否则 OR 上 WS_CHILD 后 Win32 仍认其为顶级窗口。
    public const int WS_CAPTION     = 0x00C00000; // WS_BORDER | WS_DLGFRAME
    public const int WS_SYSMENU     = 0x00080000;
    public const int WS_THICKFRAME  = 0x00040000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;
    public const int WS_POPUP       = unchecked((int)0x80000000);

    public const int SW_SHOW = 5;
    public const int SW_HIDE = 0;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOZORDER    = 0x0004;
    public const uint SWP_NOACTIVATE  = 0x0010;
    public const uint SWP_SHOWWINDOW  = 0x0040;
    public const uint SWP_FRAMECHANGED = 0x0020;   // 强制重算 NC 区域、刷新窗口样式

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    /// <summary>显示器信息结构体（用于多显示器适配）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;      // 显示器全区域
        public RECT rcWork;         // 工作区域（不含任务栏）
        public uint dwFlags;        // MONITORINFOF_PRIMARY = 1 表示主屏
    }

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const uint MONITORINFOF_PRIMARY = 0x00000001;
}

/// <summary>
/// 任务栏辅助类 - 管理窗口嵌入到任务栏的逻辑
/// </summary>
public class TaskbarHelper : IDisposable
{
    private IntPtr _taskbarHandle;
    private IntPtr _trayHandle;
    private IntPtr _childWindowHandle;
    private bool _isEmbedded;

    /// <summary>是否已成功嵌入任务栏</summary>
    public bool IsEmbedded => _isEmbedded;

    /// <summary>
    /// 任务栏默认相对位置：0.5 = 任务栏正中。
    /// 用户拖动后会保存到 AppSettings.TaskbarRelativeX（0~1 相对比例）。
    /// </summary>
    public const double DefaultTaskbarRelativeX = 0.5;

    /// <summary>
    /// 返回已 Initialize 后的任务栏 hwnd；未初始化时返回 IntPtr.Zero。
    /// TaskbarWindow 拖拽逻辑使用此句柄获取任务栏矩形作为父坐标系参考。
    /// </summary>
    public IntPtr GetHandle() => _taskbarHandle;

    /// <summary>
    /// 初始化任务栏辅助类，查找任务栏窗口句柄
    /// </summary>
    public bool Initialize()
    {
        _taskbarHandle = TaskbarNativeMethods.FindWindow(TaskbarNativeMethods.TaskbarClassName, null);
        if (_taskbarHandle == IntPtr.Zero)
            return false;

        _trayHandle = TaskbarNativeMethods.FindWindowEx(_taskbarHandle, IntPtr.Zero,
            TaskbarNativeMethods.TrayNotifyClassName, null);

        return _taskbarHandle != IntPtr.Zero;
    }

    /// <summary>
    /// 将指定窗口嵌入到任务栏
    /// </summary>
    /// <param name="windowHandle">要嵌入的WPF窗口句柄</param>
    /// <param name="width">嵌入窗口宽度</param>
    public bool EmbedWindow(IntPtr windowHandle, int width, double relX = 0.5)
    {
        if (_taskbarHandle == IntPtr.Zero && !Initialize())
            return false;

        _childWindowHandle = windowHandle;

        // 获取任务栏矩形
        TaskbarNativeMethods.GetWindowRect(_taskbarHandle, out var taskbarRect);
        FileLogger.Info("TaskbarHelper", $"EmbedWindow: windowHandle=0x{windowHandle:X} taskbarHandle=0x{_taskbarHandle:X} taskbarRect=({taskbarRect.Left},{taskbarRect.Top})-({taskbarRect.Right},{taskbarRect.Bottom})");

        // ★ ★ ★ 关键架构决策：
        //   WPF Window 内部强制设为 WS_POPUP 顶级窗口，任何尝试设 WS_CHILD 都会被 WPF 自己的
        //   消息循环（SetParent(0, hwnd)）重置回 desktop，导致无父的子窗口异常态，从 EnumWindows 消失。
        //   实测：设上 WS_CHILD 后位置正确（任务栏内），但窗口马上看不见。
        //
        //   正确做法：不设 WS_CHILD，保留 WPF 原生 WS_POPUP。视觉上"嵌入"任务栏：
        //     1. SetWindowLongPtr(GWL_HWNDPARENT, taskbarHandle) 设"逻辑父"（用于 Spy++ 等工具显示）
        //     2. 清理顶级窗口装饰 flags（WS_CAPTION/SYSMENU/THICKFRAME 等）让它看起来像无边框
        //     3. SetWindowPos 强制位置在任务栏区域内（任务栏右侧、通知区左边）
        //   这样窗口始终是 WPF 顶级窗口可正常显示，但视觉上完全在任务栏内。

        // 1. 设逻辑父（仅诊断可见，不影响 WPF 窗口管理）
        const int GWL_HWNDPARENT = -8;
        TaskbarNativeMethods.SetWindowLongPtr(windowHandle, GWL_HWNDPARENT, _taskbarHandle);
        FileLogger.Info("TaskbarHelper", "EmbedWindow: SetWindowLongPtr(GWL_HWNDPARENT) done");

        // 2. 清理顶级窗口装饰
        var style = TaskbarNativeMethods.GetWindowLong(windowHandle, TaskbarNativeMethods.GWL_STYLE);
        FileLogger.Info("TaskbarHelper", $"EmbedWindow: style before = 0x{style:X8}");
        style &= ~TaskbarNativeMethods.WS_POPUP;          // 让它看起来不像 popup
        style &= ~TaskbarNativeMethods.WS_CAPTION;        // 去标题栏
        style &= ~TaskbarNativeMethods.WS_SYSMENU;        // 去系统菜单
        style &= ~TaskbarNativeMethods.WS_THICKFRAME;     // 去可调边框
        style &= ~TaskbarNativeMethods.WS_MINIMIZEBOX;
        style &= ~TaskbarNativeMethods.WS_MAXIMIZEBOX;
        // 注意：不加 WS_CHILD。WPF 会重置 parent 到 desktop。
        style |= TaskbarNativeMethods.WS_VISIBLE;
        TaskbarNativeMethods.SetWindowLong(windowHandle, TaskbarNativeMethods.GWL_STYLE, style);
        var styleAfter = TaskbarNativeMethods.GetWindowLong(windowHandle, TaskbarNativeMethods.GWL_STYLE);
        FileLogger.Info("TaskbarHelper", $"EmbedWindow: style after = 0x{styleAfter:X8}");

        // 3. 强制位置 + 大小：任务栏右侧，通知区左边
        // 关键：根据任务栏在屏幕的哪一边（顶/底/左/右）计算窗口位置。
        //   任务栏在底部（y 在屏幕中点之后）：y = taskbarRect.Top
        //   任务栏在顶部（y 在屏幕中点之前）：y = taskbarRect.Top (=0)
        //   任务栏在左/右（罕见）：x 同理
        // 使用 taskbarRect.Right/Top 屏幕绝对坐标，而不是 Width/Height 任务栏相对尺寸。
        // 多显示器适配：使用任务栏所在显示器的边界而非主屏 GetSystemMetrics
        var monitorHandle = TaskbarNativeMethods.MonitorFromWindow(_taskbarHandle,
            TaskbarNativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new TaskbarNativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<TaskbarNativeMethods.MONITORINFO>() };
        TaskbarNativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo);
        var screenW = monitorInfo.rcMonitor.Width;
        var screenH = monitorInfo.rcMonitor.Height;
        var screenLeft = monitorInfo.rcMonitor.Left;
        var screenTop = monitorInfo.rcMonitor.Top;
        FileLogger.Info("TaskbarHelper", $"EmbedWindow: monitor=({screenLeft},{screenTop}) {screenW}x{screenH} primary={monitorInfo.dwFlags == TaskbarNativeMethods.MONITORINFOF_PRIMARY}");
        var height = taskbarRect.Height;
        var width_ = width;  // 避免与外层 width 形参同名，alias

        // ★ 默认位置计算：
        //   任务栏中部（默认 relX=0.5）—— 避免用户感觉"窗口在屏幕右上角"
        //   之前默认在 taskbarRect.Right - width - 80 = 屏幕 x=3080 在 3440 宽屏幕上是 89% 位置
        //   留 80px 右边距给通知区，留 20px 左边距
        //   relX=0 表示最左，1 表示最右，0.5 表示正中
        var rightMargin = 80;
        var leftMargin = 20;
        var usableWidth = taskbarRect.Right - taskbarRect.Left - rightMargin - leftMargin;
        var maxXForRelX = usableWidth - width_;
        var x = taskbarRect.Left + leftMargin + (int)(relX * maxXForRelX);
        if (x + width_ > taskbarRect.Right - rightMargin) x = taskbarRect.Right - rightMargin - width_;
        if (x < taskbarRect.Left + leftMargin) x = taskbarRect.Left + leftMargin;
        var y = taskbarRect.Top;                      // 任务栏顶端（顶部任务栏 y=0；底部任务栏 y=1392）

        // 防御性：限制窗口完全在任务栏所在显示器内
        if (x + width_ > screenLeft + screenW) x = screenLeft + screenW - width_;
        if (x < screenLeft) x = screenLeft;
        if (y + height > screenTop + screenH) y = screenTop + screenH - height;
        if (y < screenTop) y = screenTop;

        bool swpOk = TaskbarNativeMethods.SetWindowPos(windowHandle, TaskbarNativeMethods.HWND_TOPMOST,
            x, y, width_, height,
            TaskbarNativeMethods.SWP_SHOWWINDOW | TaskbarNativeMethods.SWP_NOACTIVATE);
        var rectAfter = new TaskbarNativeMethods.RECT();
        TaskbarNativeMethods.GetWindowRect(windowHandle, out rectAfter);
        FileLogger.Info("TaskbarHelper", $"EmbedWindow: SetWindowPos returned={swpOk} screen={screenW}x{screenH} rect=({rectAfter.Left},{rectAfter.Top})-({rectAfter.Right},{rectAfter.Bottom})");

        _isEmbedded = true;
        return true;
    }

    /// <summary>
    /// 更新嵌入窗口的位置和大小（多显示器适配）
    /// </summary>
    public void UpdatePosition(int width = 300)
    {
        if (!_isEmbedded || _childWindowHandle == IntPtr.Zero)
            return;

        TaskbarNativeMethods.GetWindowRect(_taskbarHandle, out var taskbarRect);
        var height = taskbarRect.Height;
        // 使用任务栏绝对坐标而非相对宽度，适配多显示器偏移
        var x = taskbarRect.Right - width - 80;

        TaskbarNativeMethods.MoveWindow(_childWindowHandle, x, taskbarRect.Top, width, height, true);
    }

    /// <summary>
    /// 从任务栏中移除嵌入的窗口
    /// </summary>
    public void UnembedWindow()
    {
        if (_childWindowHandle != IntPtr.Zero)
        {
            TaskbarNativeMethods.ShowWindow(_childWindowHandle, TaskbarNativeMethods.SW_HIDE);
            TaskbarNativeMethods.SetParent(_childWindowHandle, IntPtr.Zero);
            _isEmbedded = false;
        }
    }

    public void Dispose()
    {
        UnembedWindow();
    }
}
