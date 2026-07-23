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

        // req-103：初始化卡片排序列表
        RefreshCardOrderIfNeeded();

        // req-104：初始化多进度条字段列表
        RefreshMultiProgressFieldItemsIfNeeded();

        // req-097：初始化图表顺序列表
        RefreshChartOrderItemsIfNeeded();

        // req-098：初始化任务栏迷你图表配置项
        RefreshTaskbarMiniChartOptionsIfNeeded();
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

    /// <summary>
    /// req-103：切换到「卡片排序」分区时刷新列表。
    /// </summary>
    private void RefreshCardOrderIfNeeded()
    {
        if (DataContext is MainViewModel vm)
            vm.RefreshCardOrderItems();
    }

    /// <summary>
    /// req-104：切换到「多进度条」分区时刷新字段列表。
    /// </summary>
    private void RefreshMultiProgressFieldItemsIfNeeded()
    {
        if (DataContext is MainViewModel vm)
            vm.RefreshMultiProgressFieldItems();
    }

    /// <summary>
    /// req-097：切换到「图表顺序」分区时刷新列表。
    /// </summary>
    private void RefreshChartOrderItemsIfNeeded()
    {
        if (DataContext is MainViewModel vm)
            vm.RefreshChartOrderItems();
    }

    /// <summary>
    /// req-098：切换到「任务栏迷你图表」分区时刷新列表。
    /// <para>从 <c>_pluginManager.Plugins</c> 重新收集 SupportedMiniCharts 非空的 Provider，
    /// 与既有用户配置合并（保留用户已修改项）。</para>
    /// </summary>
    private void RefreshTaskbarMiniChartOptionsIfNeeded()
    {
        if (DataContext is MainViewModel vm)
            vm.RefreshTaskbarMiniChartOptions();
    }

    // =====================================================================
    // REQ-103 卡片排序拖拽事件处理器
    // =====================================================================

    private System.Windows.Point _cardOrderDragStartPoint;
    private bool _cardOrderIsDragging;

    /// <summary>
    /// req-103：记录拖拽起始点。
    /// </summary>
    private void CardOrderListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _cardOrderDragStartPoint = e.GetPosition(null);
        _cardOrderIsDragging = false;
    }

    /// <summary>
    /// req-103：检测拖拽距离，超过阈值后启动拖拽操作。
    /// </summary>
    private void CardOrderListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _cardOrderIsDragging)
            return;

        var currentPos = e.GetPosition(null);
        var diff = _cardOrderDragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is not System.Windows.Controls.ListBox listBox) return;
            var draggedItem = FindVisualParent<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
            if (draggedItem?.Content is not Helpers.CardOrderItem cardOrderItem) return;

            _cardOrderIsDragging = true;
            var dragData = new System.Windows.DataObject("CardOrderItem", cardOrderItem);
            System.Windows.DragDrop.DoDragDrop(listBox, dragData, System.Windows.DragDropEffects.Move);
            _cardOrderIsDragging = false;
        }
    }

    /// <summary>
    /// req-103：拖拽悬停时设置效果。
    /// </summary>
    private void CardOrderListBox_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("CardOrderItem"))
            e.Effects = System.Windows.DragDropEffects.Move;
        else
            e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// req-103：放下时执行排序并持久化。
    /// </summary>
    private void CardOrderListBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("CardOrderItem")) return;
        if (DataContext is not MainViewModel vm) return;

        var droppedItem = e.Data.GetData("CardOrderItem") as Helpers.CardOrderItem;
        if (droppedItem == null) return;

        // 找到目标位置
        var targetItem = FindVisualParent<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
        if (targetItem?.Content is not Helpers.CardOrderItem targetCardItem) return;

        var items = vm.CardOrderItems;
        var oldIndex = items.IndexOf(droppedItem);
        var newIndex = items.IndexOf(targetCardItem);

        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;

        // 移动元素
        items.Move(oldIndex, newIndex);

        // 持久化排序结果
        vm.SaveCardOrder();

        e.Handled = true;
    }

    /// <summary>
    /// 向上查找指定类型的父元素。
    /// </summary>
    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
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

    // =====================================================================
    // REQ-097 图表顺序拖拽事件处理器
    // =====================================================================

    private System.Windows.Point _chartOrderDragStartPoint;
    private bool _chartOrderIsDragging;

    /// <summary>
    /// req-097：记录拖拽起始点。
    /// </summary>
    private void ChartOrderListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _chartOrderDragStartPoint = e.GetPosition(null);
        _chartOrderIsDragging = false;
    }

    /// <summary>
    /// req-097：检测拖拽距离，超过阈值启动拖拽。
    /// </summary>
    private void ChartOrderListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _chartOrderIsDragging) return;
        var currentPos = e.GetPosition(null);
        var diff = _chartOrderDragStartPoint - currentPos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is not System.Windows.Controls.ListBox listBox) return;
            var draggedItem = FindVisualParent<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
            if (draggedItem?.Content is not Helpers.ChartOrderItem chartOrderItem) return;
            _chartOrderIsDragging = true;
            var dragData = new System.Windows.DataObject("ChartOrderItem", chartOrderItem);
            System.Windows.DragDrop.DoDragDrop(listBox, dragData, System.Windows.DragDropEffects.Move);
            _chartOrderIsDragging = false;
        }
    }

    /// <summary>
    /// req-097：拖拽进入时设置效果。
    /// </summary>
    private void ChartOrderListBox_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("ChartOrderItem") ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// req-097：放置时调整顺序。
    /// </summary>
    private void ChartOrderListBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("ChartOrderItem")) return;
        if (DataContext is not MainViewModel vm) return;
        var droppedItem = e.Data.GetData("ChartOrderItem") as Helpers.ChartOrderItem;
        if (droppedItem == null) return;
        var targetItem = FindVisualParent<System.Windows.Controls.ListBoxItem>(e.OriginalSource as DependencyObject);
        if (targetItem?.Content is not Helpers.ChartOrderItem targetChartItem) return;
        var items = vm.ChartOrderItems;
        var oldIndex = items.IndexOf(droppedItem);
        var newIndex = items.IndexOf(targetChartItem);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;
        items.Move(oldIndex, newIndex);
        vm.SaveChartOrder();
        e.Handled = true;
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
}
