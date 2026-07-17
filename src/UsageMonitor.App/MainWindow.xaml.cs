using System.Windows;
using UsageMonitor.App.Controls;
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
        var settingsWindow = new Views.SettingsWindow(_viewModel, _viewModel.ConfigService);
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
    /// req-007：主窗口卡片折线图周期切换事件。
    /// <para>
    /// 从 <paramref name="sender"/>（<see cref="MiniLineChartControl"/>）的 <c>DataContext</c>
    /// 反查对应的 <see cref="ProviderUsageViewModel"/>，调用其 <c>HandlePeriodChanged</c> 走插件
    /// <c>SetPeriodAsync</c> + 重新切片 + loading 蒙版全流程。仅 <c>SupportsPeriodSwitch=true</c>
    /// 的插件会触发此事件（控件内部已校验）。
    /// </para>
    /// </summary>
    private void OnLineChartPeriodChanged(object sender, PeriodChangedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ProviderUsageViewModel vm)
        {
            vm.HandlePeriodChanged(e.Period);
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
