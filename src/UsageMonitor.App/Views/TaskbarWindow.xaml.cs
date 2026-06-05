using System.Windows;
using System.Windows.Interop;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.ViewModels;

namespace UsageMonitor.App.Views;

/// <summary>
/// 任务栏嵌入窗口 - 在 Windows 任务栏中显示 AI 用量摘要信息
/// 使用 Windows Shell API 将窗口嵌入到任务栏右侧
/// </summary>
public partial class TaskbarWindow : Window
{
    private readonly TaskbarHelper _taskbarHelper;
    private readonly MainViewModel _viewModel;

    public TaskbarWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _taskbarHelper = new TaskbarHelper();
        DataContext = viewModel;
    }

    /// <summary>
    /// 窗口加载后嵌入到任务栏
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;

        if (_taskbarHelper.Initialize())
        {
            _taskbarHelper.EmbedWindow(handle, 300);
        }
    }

    /// <summary>
    /// 更新任务栏窗口宽度
    /// </summary>
    public void UpdateWidth(int width)
    {
        _taskbarHelper.UpdatePosition(width);
    }

    /// <summary>
    /// 显示任务栏窗口
    /// </summary>
    public void ShowInTaskbar()
    {
        Show();
        if (_taskbarHelper.IsEmbedded)
            _taskbarHelper.UpdatePosition();
    }

    /// <summary>
    /// 隐藏任务栏窗口
    /// </summary>
    public void HideFromTaskbar()
    {
        Hide();
        _taskbarHelper.UnembedWindow();
    }

    protected override void OnClosed(EventArgs e)
    {
        _taskbarHelper.Dispose();
        base.OnClosed(e);
    }
}
