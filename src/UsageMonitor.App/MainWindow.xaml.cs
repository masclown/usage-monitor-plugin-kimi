using System.Windows;
using UsageMonitor.App.ViewModels;

namespace UsageMonitor.App;

/// <summary>
/// 主窗口 - 显示各AI服务商的用量概览
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// 打开设置窗口
    /// </summary>
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new Views.SettingsWindow(_viewModel);
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    /// <summary>
    /// 打开历史窗口。委托给 App 单例的 ShowHistoryWindow，避免重复创建 VM。
    /// </summary>
    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.ShowHistoryWindow();
        }
    }

    /// <summary>
    /// 关闭窗口时隐藏而非退出（最小化到托盘）
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
