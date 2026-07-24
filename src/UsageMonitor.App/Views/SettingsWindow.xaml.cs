using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Views;

/// <summary>
/// 设置窗口 - 配置刷新间隔、任务栏显示、插件管理、诊断日志入口、触发区域调试矩形
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ConfigService _configService;

    public SettingsWindow(MainViewModel viewModel, ConfigService configService)
    {
        _configService = configService;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;

        // req-073：订阅 ViewModel 的关闭请求（保存/取消按钮触发）
        viewModel.RequestCloseSettings += OnRequestCloseSettings;
    }

    /// <summary>
    /// req-073：ViewModel 请求关闭设置窗口（保存成功或取消）。
    /// </summary>
    private void OnRequestCloseSettings(object? sender, bool saved)
    {
        // 保存失败时不关闭（LastSaveError 已由 SaveAllSettingsCommand 检查）
        if (saved && !string.IsNullOrEmpty(_configService.LastSaveError))
        {
            System.Windows.MessageBox.Show(
                $"配置保存失败：\n{_configService.LastSaveError}\n\n窗口已保持打开，请修改后重试。",
                "保存失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return;
        }
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // req-073：DataTemplate 内的 x:Name 元素需在模板加载后通过 VisualTree 查找
        // 延迟到 ContentControl 实际渲染后再初始化
        Dispatcher.BeginInvoke(new Action(() =>
        {
            InitializeTemplateElements();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// req-073：初始化 DataTemplate 内的命名元素（LogPathTextBox / CookieDirAclStatus / ConfigDirAclStatus / RingMetricOrderList）。
    /// </summary>
    private void InitializeTemplateElements()
    {
        // 查找 LogPathTextBox 并设置日志路径
        var logPathTextBox = FindVisualChildByName<System.Windows.Controls.TextBox>(this, "LogPathTextBox");
        if (logPathTextBox != null)
            logPathTextBox.Text = FileLogger.GetCurrentLogPath();

        // 查找 RingMetricOrderList 并更新颜色
        var ringMetricOrderList = FindVisualChildByName<ItemsControl>(this, "RingMetricOrderList");
        if (ringMetricOrderList != null)
            UpdateRingMetricItemColors(ringMetricOrderList);

        // 加载安全页 ACL 状态
        LoadSecurityTabStatus();
    }

    /// <summary>
    /// 按名称查找可视化树中的子元素。
    /// </summary>
    private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;
            var found = FindVisualChildByName<T>(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// req-089：加载安全标签页的 ACL 状态显示。
    /// </summary>
    private void LoadSecurityTabStatus()
    {
        try
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UsageMonitor");
            var cookieDir = Path.Combine(appDataDir, "cookies");

            // req-073：从 VisualTree 查找 DataTemplate 内的元素
            var cookieDirAclStatus = FindVisualChildByName<TextBlock>(this, "CookieDirAclStatus");
            var configDirAclStatus = FindVisualChildByName<TextBlock>(this, "ConfigDirAclStatus");

            if (cookieDirAclStatus != null)
            {
                var tightened = UsageMonitor.Core.Security.CookieDirAccessControl.IsTightened(cookieDir);
                cookieDirAclStatus.Text = tightened ? "✅ 已收紧" : "⚠️ 默认（未收紧）";
            }

            if (configDirAclStatus != null)
            {
                var tightened = UsageMonitor.Core.Security.ConfigDirAccessControl.IsTightened(_configService.ConfigFilePath);
                configDirAclStatus.Text = tightened ? "✅ 已收紧" : "⚠️ 默认（未收紧）";
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SettingsWindow", $"LoadSecurityTabStatus failed: {ex.Message}");
        }
    }

    /// <summary>
    /// req-089：手动触发 ACL 收紧按钮。
    /// </summary>
    private void ApplyAclTightening_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UsageMonitor");
            var cookieDir = Path.Combine(appDataDir, "cookies");
            Directory.CreateDirectory(cookieDir);

            var cookieResult = UsageMonitor.Core.Security.CookieDirAccessControl.ApplyTightening(cookieDir);
            var configResult = UsageMonitor.Core.Security.ConfigDirAccessControl.ApplyTightening(_configService.ConfigFilePath);

            LoadSecurityTabStatus(); // 刷新显示

            var msg = (cookieResult && configResult)
                ? "ACL 收紧已成功应用。"
                : "ACL 收紧部分应用（可能因权限不足跳过）。详见日志。";
            System.Windows.MessageBox.Show(msg, "安全设置",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            FileLogger.Error("SettingsWindow", "ApplyAclTightening failed", ex);
            System.Windows.MessageBox.Show($"应用失败：{ex.Message}", "安全设置",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// req-053：根据 GlobalEnabledRingChartMetrics 更新环形图中心数字项的颜色。
    /// req-073：改为接收 ItemsControl 参数（从 VisualTree 查找后传入）。
    /// </summary>
    private void UpdateRingMetricItemColors(ItemsControl ringMetricOrderList)
    {
        if (ringMetricOrderList == null) return;
        var enabled = _configService.Settings.GlobalEnabledRingChartMetrics;

        // 获取主题感知的画刷
        var enabledBrush = TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush
                          ?? System.Windows.Media.Brushes.Black;
        var disabledBrush = TryFindResource("TextTertiaryBrush") as System.Windows.Media.Brush
                           ?? System.Windows.Media.Brushes.Gray;

        foreach (var item in ringMetricOrderList.Items)
        {
            var container = ringMetricOrderList.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
            if (container?.ContentTemplate == null) continue;
            var tb = FindVisualChild<TextBlock>(container);
            if (tb == null) continue;

            var key = item as string;
            if (string.IsNullOrEmpty(key)) continue;

            var isEnabled = enabled.Exists(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase));
            tb.Foreground = isEnabled ? enabledBrush : disabledBrush;
            tb.FontWeight = isEnabled ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    /// <summary>
    /// 查找可视化树中的子元素。
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// 关闭设置窗口时由 App.xaml.cs 显式调用：托盘悬浮窗触发区域调试遮罩（<see cref="TriggerAreaOverlayWindow"/>）由
    /// 主 VM 的 EditTriggerAreaCommand 触发显示，SettingsWindow 自身不持有实例。
    /// 这里仅保留日志/兼容入口，不做任何遮罩生命周期管理。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // 触发区域调试遮罩由其拥有者（App.xaml.cs 注入到 MainViewModel.OpenTriggerOverlayAction）创建，
        // 这里无须做清理；保留 override 以便未来扩展。
        base.OnClosing(e);
    }

    /// <summary>Open the logs folder in Windows Explorer.</summary>
    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(FileLogger.LogDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = FileLogger.LogDir,
                UseShellExecute = true,
                Verb = "open"
            });
            FileLogger.Info("SettingsWindow", $"Opened logs folder: {FileLogger.LogDir}");
        }
        catch (Exception ex)
        {
            FileLogger.Error("SettingsWindow", "Failed to open logs folder", ex);
            System.Windows.MessageBox.Show($"Cannot open logs folder:\n{ex.Message}",
                "UsageMonitor", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Open the debug folder (XHR / DOM dumps).</summary>
    private void OpenDebugFolder_Click(object sender, RoutedEventArgs e)
    {
        var debugDir = Path.Combine(FileLogger.LogDir, "debug");
        try
        {
            Directory.CreateDirectory(debugDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = debugDir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Cannot open debug folder:\n{ex.Message}",
                "UsageMonitor", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Copy the latest log file contents to clipboard for easy sharing.</summary>
    private void CopyLatestLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = FileLogger.GetCurrentLogPath();
            if (!File.Exists(path))
            {
                System.Windows.MessageBox.Show("No log file yet.", "UsageMonitor",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            var content = File.ReadAllText(path);
            System.Windows.Clipboard.SetText(content);
            System.Windows.MessageBox.Show($"Copied latest log:\n{Path.GetFileName(path)}\n\nLength: {content.Length} chars",
                "UsageMonitor", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Copy failed:\n{ex.Message}",
                "UsageMonitor", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// req-073：原「保存设置」按钮已移除，改用底部全局保存栏（SaveAllSettingsCommand）。
    /// 保留此方法仅为兼容旧代码引用，实际不再使用。
    /// </summary>
    [Obsolete("req-073：已改用底部全局保存栏，此方法不再使用")]
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // 兼容旧代码：直接调用 SaveAllSettingsCommand
        if (DataContext is MainViewModel vm && vm.SaveAllSettingsCommand.CanExecute(null))
        {
            vm.SaveAllSettingsCommand.Execute(null);
        }
    }

    // =====================================================================
    // S4：任务栏迷你图表页事件处理（与 S2 卡片管理页同构）
    // =====================================================================

    /// <summary>S4：Mini 图表账号节点展开/收起切换。</summary>
    private void OnMiniAccountNodeToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.MiniAccountNode node)
            node.IsExpanded = !node.IsExpanded;
    }

    /// <summary>S4：Mini 图表节点展开/收起切换。</summary>
    private void OnMiniChartNodeToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.MiniChartNode node)
            node.IsExpanded = !node.IsExpanded;
    }

    // --- S4：Mini 图表拖拽排序 ---
    private ViewModels.MiniChartNode? _miniChartDragSource;
    private System.Windows.Point _miniChartDragStartPos;

    /// <summary>S4：Mini 图表拖拽开始（记录拖拽源）。</summary>
    private void OnMiniChartDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.MiniChartNode node)
        {
            _miniChartDragSource = node;
            _miniChartDragStartPos = e.GetPosition(null);
        }
    }

    /// <summary>S4：Mini 图表拖拽移动（达到阈值后执行拖放并保存）。</summary>
    private void OnMiniChartDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DragReorderCore(sender, _miniChartDragSource, _miniChartDragStartPos, e,
            el => FindAncestorDataContext<ViewModels.MiniChartNode>(el),
            el => FindAncestorDataContext<ViewModels.MiniAccountNode>(el)?.Save()))
        {
            _miniChartDragSource = null;
        }
    }

    // --- S4：Mini 数据组拖拽排序 ---
    private ViewModels.MiniDataGroupNode? _miniDataGroupDragSource;
    private System.Windows.Point _miniDataGroupDragStartPos;

    /// <summary>S4：Mini 数据组拖拽开始。</summary>
    private void OnMiniDataGroupDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.MiniDataGroupNode node)
        {
            _miniDataGroupDragSource = node;
            _miniDataGroupDragStartPos = e.GetPosition(null);
        }
    }

    /// <summary>S4：Mini 数据组拖拽移动。</summary>
    private void OnMiniDataGroupDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DragReorderCore(sender, _miniDataGroupDragSource, _miniDataGroupDragStartPos, e,
            el => FindAncestorDataContext<ViewModels.MiniDataGroupNode>(el),
            el => FindAncestorDataContext<ViewModels.MiniAccountNode>(el)?.Save()))
        {
            _miniDataGroupDragSource = null;
        }
    }

    /// <summary>
    /// S4：泛型拖拽重排核心逻辑（Mini 图表 / 数据组共用）。
    /// <para>达到系统拖拽阈值后，在同一个 ItemsControl 内将拖拽源移动到目标位置，
    /// 并调用 saveAfterMove 持久化。返回 true 表示拖拽已结束（已移动或已中止），调用方应清空拖拽源。</para>
    /// </summary>
    private bool DragReorderCore<TNode>(object sender, TNode? source, System.Windows.Point dragStartPos,
        System.Windows.Input.MouseEventArgs e,
        Func<DependencyObject?, TNode?> findTarget,
        Action<DependencyObject?> saveAfterMove) where TNode : class
    {
        if (source == null || e.LeftButton != MouseButtonState.Pressed) return true;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - dragStartPos.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - dragStartPos.Y) < SystemParameters.MinimumVerticalDragDistance)
            return false; // 未达阈值，拖拽仍在进行

        var target = findTarget(e.OriginalSource as DependencyObject);
        if (target != null && !ReferenceEquals(target, source))
        {
            var sourceElement = sender as FrameworkElement;
            var itemsControl = FindVisualParent<ItemsControl>(sourceElement);
            if (itemsControl?.ItemsSource is System.Collections.ObjectModel.ObservableCollection<TNode> coll)
            {
                var fromIdx = coll.IndexOf(source);
                var toIdx = coll.IndexOf(target);
                if (fromIdx >= 0 && toIdx >= 0)
                {
                    coll.Move(fromIdx, toIdx);
                    saveAfterMove(sourceElement);
                }
            }
        }
        return true;
    }

    /// <summary>
    /// req-053：单击环形图中心数字项，切换启用/禁用状态。禁用后文字变灰色。
    /// </summary>
    private void OnRingMetricToggleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        var key = tb.DataContext as string;
        if (string.IsNullOrEmpty(key)) return;

        var settings = _configService.Settings;
        var enabled = settings.GlobalEnabledRingChartMetrics;

        // 获取主题感知的画刷
        var enabledBrush = TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush
                          ?? System.Windows.Media.Brushes.Black;
        var disabledBrush = TryFindResource("TextTertiaryBrush") as System.Windows.Media.Brush
                           ?? System.Windows.Media.Brushes.Gray;

        // 切换启用状态
        var exists = enabled.Exists(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            enabled.RemoveAll(m => string.Equals(m, key, StringComparison.OrdinalIgnoreCase));
            tb.Foreground = disabledBrush;
            tb.FontWeight = FontWeights.Normal;
        }
        else
        {
            enabled.Add(key);
            tb.Foreground = enabledBrush;
            tb.FontWeight = FontWeights.Bold;
        }

        // req-053：同步到所有 ProviderUsageViewModel，让半圆环图立即反映变化
        if (DataContext is MainViewModel vm)
        {
            vm.SyncGlobalEnabledMetricsToAllProviders();
        }

        // req-053：立即持久化到配置文件，避免重启后丢失
        try
        {
            _configService.Save();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("SettingsWindow", $"OnRingMetricToggleClick Save failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// req-107 B9：设置界面“校验插件”按钮——选择插件 defaults.json 即校验，
    /// 复用与 <c>--validate-plugin</c> 命令行相同的 <see cref="UsageMonitor.Core.Plugins.PluginValidator"/> 校验代码。
    /// </summary>
    private void OnValidatePluginClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择插件显示声明文件 (defaults.json)",
            Filter = "插件显示声明 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var sdkVersion = typeof(SettingsWindow).Assembly.GetName().Version ?? new Version(0, 24, 3);
            var result = UsageMonitor.Core.Plugins.PluginValidator.Validate(json, sdkVersion);
            System.Windows.MessageBox.Show(
                this,
                result.ToReport(),
                result.IsValid ? "插件校验通过" : "插件校验失败",
                System.Windows.MessageBoxButton.OK,
                result.IsValid ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"校验异常：{ex.Message}",
                "插件校验",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// S1：账号昵称 TextBox 失焦时提交（仅在校验通过时落盘，重名拒绝保存）。
    /// 实时校验在 ViewModel.Nickname setter 中完成，此处仅负责触发持久化。
    /// </summary>
    private void OnAccountNicknameLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.TextBox)?.DataContext is ViewModels.PluginAccountItemViewModel accountVm)
        {
            accountVm.CommitNickname();
        }
    }

    // =====================================================================
    // S2：卡片管理页事件处理
    // =====================================================================

    /// <summary>S2：账号节点展开/收起切换。</summary>
    private void OnAccountNodeToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.AccountNode node)
            node.IsExpanded = !node.IsExpanded;
    }

    /// <summary>S2：图表节点展开/收起切换。</summary>
    private void OnChartNodeToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.ChartNode node)
            node.IsExpanded = !node.IsExpanded;
    }

    /// <summary>S2：添加图表按钮——弹出 ContextMenu 显示可添加的图表列表。</summary>
    private void OnAddChartClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ViewModels.AccountNode node) return;
        if (node.AvailableCharts.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "所有声明图表均已添加。", "提示",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        // 用 ContextMenu 展示可添加图表
        var menu = new ContextMenu();
        foreach (var chart in node.AvailableCharts)
        {
            var item = new MenuItem { Header = chart.ChartId, Tag = chart };
            item.Click += (_, _) => node.AddChartCommand.Execute(chart);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = sender as UIElement;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // --- S2：图表拖拽排序 ---
    private ViewModels.ChartNode? _chartDragSource;
    private System.Windows.Point _chartDragStartPos;

    /// <summary>S2：图表拖拽开始（记录拖拽源）。</summary>
    private void OnChartDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.ChartNode node)
        {
            _chartDragSource = node;
            _chartDragStartPos = e.GetPosition(null);
        }
    }

    /// <summary>S2：图表拖拽移动（达到阈值后执行拖放）。</summary>
    private void OnChartDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_chartDragSource == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _chartDragStartPos.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _chartDragStartPos.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // 查找拖拽目标（同一 ItemsControl 内的另一个 ChartNode）
        var target = FindAncestorDataContext<ViewModels.ChartNode>(e.OriginalSource as DependencyObject);
        if (target != null && !ReferenceEquals(target, _chartDragSource))
        {
            // 找到父账号节点并执行移动
            var sourceElement = sender as FrameworkElement;
            var itemsControl = FindVisualParent<ItemsControl>(sourceElement);
            if (itemsControl?.ItemsSource is System.Collections.ObjectModel.ObservableCollection<ViewModels.ChartNode> charts)
            {
                var fromIdx = charts.IndexOf(_chartDragSource);
                var toIdx = charts.IndexOf(target);
                if (fromIdx >= 0 && toIdx >= 0)
                {
                    charts.Move(fromIdx, toIdx);
                    // 拖拽后保存顺序
                    var accountNode = FindAncestorDataContext<ViewModels.AccountNode>(sourceElement);
                    accountNode?.Save();
                }
            }
        }
        _chartDragSource = null;
    }

    // --- S2：数据组拖拽排序 ---
    private ViewModels.DataGroupNode? _dataGroupDragSource;
    private System.Windows.Point _dataGroupDragStartPos;

    /// <summary>S2：数据组拖拽开始。</summary>
    private void OnDataGroupDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.DataGroupNode node)
        {
            _dataGroupDragSource = node;
            _dataGroupDragStartPos = e.GetPosition(null);
        }
    }

    /// <summary>S2：数据组拖拽移动。</summary>
    private void OnDataGroupDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dataGroupDragSource == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dataGroupDragStartPos.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dataGroupDragStartPos.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var target = FindAncestorDataContext<ViewModels.DataGroupNode>(e.OriginalSource as DependencyObject);
        if (target != null && !ReferenceEquals(target, _dataGroupDragSource))
        {
            var sourceElement = sender as FrameworkElement;
            var itemsControl = FindVisualParent<ItemsControl>(sourceElement);
            if (itemsControl?.ItemsSource is System.Collections.ObjectModel.ObservableCollection<ViewModels.DataGroupNode> groups)
            {
                var fromIdx = groups.IndexOf(_dataGroupDragSource);
                var toIdx = groups.IndexOf(target);
                if (fromIdx >= 0 && toIdx >= 0)
                {
                    groups.Move(fromIdx, toIdx);
                    // 拖拽后保存顺序
                    var accountNode = FindAncestorDataContext<ViewModels.AccountNode>(sourceElement);
                    accountNode?.Save();
                }
            }
        }
        _dataGroupDragSource = null;
    }

    /// <summary>S2：向上查找可视化树中指定类型的 DataContext。</summary>
    private static T? FindAncestorDataContext<T>(DependencyObject? element) where T : class
    {
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.DataContext is T result)
                return result;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    /// <summary>S2：向上查找可视化树中指定类型的父元素。</summary>
    private static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T result) return result;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

}
