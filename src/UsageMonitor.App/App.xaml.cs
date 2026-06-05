using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.App.ViewModels;

namespace UsageMonitor.App;

/// <summary>
/// 应用程序入口 - 初始化系统托盘、插件管理器和核心服务
/// </summary>
public partial class App : System.Windows.Application
{
    private NotifyIcon? _notifyIcon;
    private PluginManager _pluginManager = null!;
    private ConfigService _configService = null!;
    private RefreshService _refreshService = null!;
    private MainViewModel _viewModel = null!;
    private MainWindow? _mainWindow;
    private Views.TaskbarWindow? _taskbarWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 初始化核心服务
        _configService = new ConfigService();
        _configService.Load();

        _pluginManager = new PluginManager();

        // 先加载外部插件（会清空列表）
        _pluginManager.LoadPlugins();

        // 再注册内置插件（不会被清空）
        RegisterBuiltinPlugins();

        // 同步插件启用状态
        foreach (var plugin in _pluginManager.Plugins)
        {
            plugin.IsEnabled = _configService.Settings.PluginEnabled
                .GetValueOrDefault(plugin.Provider.ProviderId, true);
        }

        _refreshService = new RefreshService(_pluginManager, _configService);

        // 创建ViewModel
        _viewModel = new MainViewModel(_pluginManager, _configService, _refreshService);

        // 初始化系统托盘
        InitializeTrayIcon();

        // 监听用量更新
        _refreshService.UsageRefreshed += OnUsageRefreshed;

        // 启动定时刷新
        _refreshService.Start();

        // 初始化任务栏窗口
        if (_configService.Settings.ShowInTaskbar)
            InitializeTaskbarWindow();

        // 显示主窗口
        ShowMainWindow();
    }

    /// <summary>
    /// 注册内置插件（Deepseek、MiMo、OpenAI）
    /// </summary>
    private void RegisterBuiltinPlugins()
    {
        _pluginManager.RegisterPlugin(new Plugin.Deepseek.DeepseekProvider());
        _pluginManager.RegisterPlugin(new Plugin.MiMo.MiMoProvider());
        _pluginManager.RegisterPlugin(new Plugin.OpenAI.OpenAIProvider());
    }

    /// <summary>
    /// 初始化系统托盘图标和右键菜单
    /// </summary>
    private void InitializeTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "UsageMonitor - AI用量监控",
            Icon = SystemIcons.Application, // 后续替换为自定义图标
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        // 构建右键菜单
        var contextMenu = new ContextMenuStrip();

        contextMenu.Items.Add("打开主窗口", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("立即刷新", null, async (_, _) => await _refreshService.RefreshAllAsync());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("设置", null, (_, _) => ShowSettingsWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("退出", null, (_, _) => Shutdown());

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    /// <summary>
    /// 用量数据更新回调
    /// </summary>
    private void OnUsageRefreshed(object? sender, UsageRefreshedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel.UpdateUsages(e.Usages);

            // 更新托盘提示文本
            if (_notifyIcon != null)
            {
                var tooltipParts = e.Usages
                    .Where(u => u.IsSuccess)
                    .Select(u => u.GetShortDisplayText());
                var tooltip = string.Join("\n", tooltipParts);
                _notifyIcon.Text = string.IsNullOrEmpty(tooltip) ? "UsageMonitor" : tooltip;
            }
        });
    }

    /// <summary>
    /// 初始化任务栏嵌入窗口
    /// </summary>
    private void InitializeTaskbarWindow()
    {
        _taskbarWindow = new Views.TaskbarWindow(_viewModel);
        _taskbarWindow.ShowInTaskbarDisplay();
    }

    /// <summary>
    /// 显示主窗口
    /// </summary>
    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_viewModel);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    /// <summary>
    /// 显示设置窗口
    /// </summary>
    private void ShowSettingsWindow()
    {
        var settingsWindow = new Views.SettingsWindow(_viewModel);
        settingsWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _refreshService.Dispose();
        _taskbarWindow?.Close();
        _notifyIcon?.Dispose();
        _configService.Save();
        base.OnExit(e);
    }
}
