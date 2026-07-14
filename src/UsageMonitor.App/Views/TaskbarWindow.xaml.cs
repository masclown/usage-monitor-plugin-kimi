using System.Windows;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;

namespace UsageMonitor.App.Views;

/// <summary>
/// 任务栏浮动窗口 - 在 Windows 任务栏上方显示 AI 用量摘要信息
/// 支持三种显示模式（每 Provider 独立）：
/// - Text: DisplayName + 剩余额度
/// - MiniLineChart: 上方文字 + 下方迷你折线图
/// - RingChart: 圆环进度图 + 名称
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
            var workArea = SystemParameters.WorkArea;

            // 根据各 Provider 模式计算窗口高度
            var windowHeight = ComputeWindowHeight();
            // 根据各 Provider 模式计算窗口宽度（按固定宽度估算，可被 ScrollViewer 接管）
            var windowWidth = ComputeWindowWidth();

            // 如果任务栏在底部
            if (taskbarRect.Top > screenWidth / 2)
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
    /// 根据各 Provider 模式计算所需窗口高度（取最大值）
    /// </summary>
    private double ComputeWindowHeight()
    {
        double max = 36; // 文字模式基础高度
        foreach (var usage in _viewModel.Usages)
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
    /// 根据各 Provider 模式计算窗口宽度
    /// - 文字模式每项约 120px，折线图每项 132px，圆环图每项 96px
    /// </summary>
    private double ComputeWindowWidth()
    {
        if (_viewModel.Usages.Count == 0) return 240;

        double total = 24; // 左右 padding
        foreach (var usage in _viewModel.Usages)
        {
            total += usage.DisplayMode switch
            {
                TaskbarDisplayMode.MiniLineChart => 132,
                TaskbarDisplayMode.RingChart => 96,
                _ => 120
            };
        }
        // 上限不超过 1200，避免窗口过宽
        return Math.Min(1200, Math.Max(280, total));
    }

    /// <summary>
    /// 刷新窗口位置和大小（在 Provider 模式改变或数量变化时调用）
    /// </summary>
    public void RecalculateSize()
    {
        PositionNearTaskbar();
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
