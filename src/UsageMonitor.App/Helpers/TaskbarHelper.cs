using System.Runtime.InteropServices;

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

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public const int GWL_STYLE = -16;
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;

    public const int SW_SHOW = 5;
    public const int SW_HIDE = 0;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

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
    public bool EmbedWindow(IntPtr windowHandle, int width = 300)
    {
        if (_taskbarHandle == IntPtr.Zero && !Initialize())
            return false;

        _childWindowHandle = windowHandle;

        // 获取任务栏矩形
        TaskbarNativeMethods.GetWindowRect(_taskbarHandle, out var taskbarRect);

        // 设置窗口为任务栏的子窗口
        TaskbarNativeMethods.SetParent(windowHandle, _taskbarHandle);

        // 修改窗口样式
        var style = TaskbarNativeMethods.GetWindowLong(windowHandle, TaskbarNativeMethods.GWL_STYLE);
        style |= TaskbarNativeMethods.WS_CHILD;
        style |= TaskbarNativeMethods.WS_VISIBLE;
        TaskbarNativeMethods.SetWindowLong(windowHandle, TaskbarNativeMethods.GWL_STYLE, style);

        // 计算嵌入位置（任务栏右侧，通知区域左边）
        var height = taskbarRect.Height;
        var x = taskbarRect.Width - width - 80; // 留出通知区域空间
        var y = 0;

        // 移动并显示窗口
        TaskbarNativeMethods.MoveWindow(windowHandle, x, y, width, height, true);
        TaskbarNativeMethods.ShowWindow(windowHandle, TaskbarNativeMethods.SW_SHOW);

        _isEmbedded = true;
        return true;
    }

    /// <summary>
    /// 更新嵌入窗口的位置和大小
    /// </summary>
    public void UpdatePosition(int width = 300)
    {
        if (!_isEmbedded || _childWindowHandle == IntPtr.Zero)
            return;

        TaskbarNativeMethods.GetWindowRect(_taskbarHandle, out var taskbarRect);
        var height = taskbarRect.Height;
        var x = taskbarRect.Width - width - 80;

        TaskbarNativeMethods.MoveWindow(_childWindowHandle, x, 0, width, height, true);
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
