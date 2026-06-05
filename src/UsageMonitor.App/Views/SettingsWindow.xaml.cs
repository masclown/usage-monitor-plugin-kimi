using System.Windows;
using UsageMonitor.App.ViewModels;

namespace UsageMonitor.App.Views;

/// <summary>
/// 设置窗口 - 配置刷新间隔、任务栏显示、插件管理等
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
