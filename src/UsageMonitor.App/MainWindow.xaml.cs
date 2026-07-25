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

    /// <summary>
    /// 关闭主窗口的行为由配置驱动（req-fix-关闭最小化设置）：
    /// <para>· <c>Settings.MinimizeToTray = true</c>（默认）→ 静默最小化到托盘，不再弹「是否最小化」提示；</para>
    /// <para>· <c>Settings.MinimizeToTray = false</c> → 直接完全退出程序（走 App.Shutdown 统一清理流程）；</para>
    /// <para>· 托盘「退出」菜单已确认过退出（App._isRealShutdown=true）时无条件放行关闭。</para>
    /// 复选框入口：设置 → 常规 →「关闭主窗口时最小化到托盘」。
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // req-fix-托盘退出文案：托盘「退出」已确认过 → 放行关闭，不再弹最小化提示
        if (App._isRealShutdown)
        {
            e.Cancel = false;
            return;
        }

        // req-fix-关闭最小化设置：按用户配置决定「最小化到托盘」还是「完全退出」，不再弹提示窗
        if (_viewModel.ConfigService.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // 配置为不最小化 → 完全退出。ShutdownMode=OnExplicitShutdown，必须显式 Shutdown
        // 才能带走托盘图标与后台服务，否则窗口关闭后进程仍驻留托盘。
        App._isRealShutdown = true;
        e.Cancel = false;
        System.Windows.Application.Current.Shutdown();
    }
}
