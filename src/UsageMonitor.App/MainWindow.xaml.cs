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
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProviderUsageViewModel vm)
            {
                vm.HandlePeriodChanged(e.Period);
            }
        }
        catch (Exception ex)
        {
            // req-031：顶层保护，任何未预期异常都不允许冒泡到 WPF Dispatcher 导致闪退
            UsageMonitor.Core.Services.FileLogger.Error("MainWindow",
                $"OnLineChartPeriodChanged({e.Period}) unhandled: {ex.Message}", ex);
        }
    }

    /// <summary>req-064 U5：首次关闭时提示用户"最小化到托盘"，避免误以为程序已退出。</summary>
    private bool _hasShownMinimizeHint;

    /// <summary>
    /// 关闭窗口时隐藏而非退出（最小化到托盘）
    /// req-064 U5：首次关闭弹提示，选"是"最小化、选"否"真退出；第二次起不再弹。
    /// req-fix-托盘退出文案：如果用户已经通过托盘「退出」菜单确认过退出（App._isRealShutdown=true），
    /// 这里跳过「最小化到托盘」提示，让 Shutdown 流程顺利关闭所有窗口。
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // req-fix-托盘退出文案：托盘「退出」已确认过 → 放行关闭，不再弹最小化提示
        if (App._isRealShutdown)
        {
            e.Cancel = false;
            return;
        }

        if (!_hasShownMinimizeHint)
        {
            _hasShownMinimizeHint = true;
            var result = System.Windows.MessageBox.Show(
                "关闭窗口将最小化到托盘继续监控。\n如需完全退出，请右键托盘图标 → 退出。\n\n是否继续最小化？",
                "UsageMonitor",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);
            if (result != System.Windows.MessageBoxResult.Yes)
            {
                _hasShownMinimizeHint = false;
                e.Cancel = false;  // 真退出
                return;
            }
        }
        e.Cancel = true;
        Hide();
    }
}
