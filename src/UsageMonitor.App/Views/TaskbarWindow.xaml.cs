using System.Windows;
using System.Windows.Interop;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.ViewModels;

namespace UsageMonitor.App.Views;

/// <summary>
/// 任务栏浮动窗口 - 在 Windows 任务栏上方显示 AI 用量摘要信息
/// 使用浮动定位方式，避免 SetParent 嵌入的兼容性问题
/// </summary>
public partial class TaskbarWindow : Window
{
    private readonly MainViewModel _viewModel;

    public TaskbarWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// 窗口初始化后定位到任务栏上方右侧
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PositionNearTaskbar();
    }

    /// <summary>
    /// 将窗口定位到任务栏上方右侧（系统托盘旁边）
    /// </summary>
    private void PositionNearTaskbar()
    {
        // 获取任务栏位置和高度
        var taskbarHandle = TaskbarNativeMethods.FindWindow(TaskbarNativeMethods.TaskbarClassName, null);
        if (taskbarHandle != IntPtr.Zero)
        {
            TaskbarNativeMethods.GetWindowRect(taskbarHandle, out var taskbarRect);

            // 获取屏幕工作区域（排除任务栏后的区域）
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var workArea = SystemParameters.WorkArea;

            // 定位到任务栏上方右侧
            // 使用工作区域的右边界（已排除任务栏宽度）
            var windowWidth = 350;
            var windowHeight = 36;

            // 如果任务栏在底部
            if (taskbarRect.Top > screenHeight / 2)
            {
                Left = workArea.Right - windowWidth - 10;
                Top = workArea.Bottom - windowHeight - 4;
            }
            else
            {
                // 任务栏在顶部
                Left = workArea.Right - windowWidth - 10;
                Top = workArea.Top + 4;
            }

            Width = windowWidth;
            Height = windowHeight;
        }
    }

    /// <summary>
    /// 显示任务栏窗口
    /// </summary>
    public void ShowInTaskbarDisplay()
    {
        PositionNearTaskbar();
        Show();
    }

    /// <summary>
    /// 隐藏任务栏窗口
    /// </summary>
    public void HideFromTaskbar()
    {
        Hide();
    }
}
