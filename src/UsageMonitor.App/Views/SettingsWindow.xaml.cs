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

    /// <summary>S4：Mini 图表拖拽开始（记录拖拽源并捕获鼠标，保证移动事件持续路由到手柄）。</summary>
    private void OnMiniChartDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.MiniChartNode node)
        {
            _miniChartDragSource = node;
            _miniChartDragStartPos = e.GetPosition(null);
            if (sender is IInputElement input) input.CaptureMouse();
        }
    }

    /// <summary>S4：Mini 图表拖拽结束（释放捕获 + 清空拖拽源）。</summary>
    private void OnMiniChartDragEnd(object sender, MouseButtonEventArgs e)
    {
        ReleaseDragCapture(sender);
        _miniChartDragSource = null;
    }

    /// <summary>S4：Mini 图表拖拽移动（命中测试定位目标行，达到阈值后执行拖放并保存）。</summary>
    private void OnMiniChartDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_miniChartDragSource == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseDragCapture(sender); _miniChartDragSource = null; return; }
        if (!ExceedsDragThreshold(_miniChartDragStartPos, e)) return;
        PerformDragReorder(sender, _miniChartDragSource, e,
            el => FindAncestorDataContext<ViewModels.MiniAccountNode>(el)?.Save());
    }

    // --- S4：Mini 数据组拖拽排序 ---
    private ViewModels.MiniDataGroupNode? _miniDataGroupDragSource;
    private System.Windows.Point _miniDataGroupDragStartPos;

    /// <summary>S4：Mini 数据组拖拽开始（记录拖拽源并捕获鼠标）。</summary>
    private void OnMiniDataGroupDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.MiniDataGroupNode node)
        {
            _miniDataGroupDragSource = node;
            _miniDataGroupDragStartPos = e.GetPosition(null);
            if (sender is IInputElement input) input.CaptureMouse();
        }
    }

    /// <summary>S4：Mini 数据组拖拽结束。</summary>
    private void OnMiniDataGroupDragEnd(object sender, MouseButtonEventArgs e)
    {
        ReleaseDragCapture(sender);
        _miniDataGroupDragSource = null;
    }

    /// <summary>S4：Mini 数据组拖拽移动（问题5：命中测试 + 持续拖拽，不再依赖 OriginalSource 恰好悬停在目标手柄上）。</summary>
    private void OnMiniDataGroupDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_miniDataGroupDragSource == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseDragCapture(sender); _miniDataGroupDragSource = null; return; }
        if (!ExceedsDragThreshold(_miniDataGroupDragStartPos, e)) return;
        PerformDragReorder(sender, _miniDataGroupDragSource, e,
            el => FindAncestorDataContext<ViewModels.MiniAccountNode>(el)?.Save());
    }

    // --- 问题9：Mini Tooltip 字段拖拽排序 ---
    private ViewModels.MiniTooltipFieldItem? _miniTooltipFieldDragSource;
    private System.Windows.Point _miniTooltipFieldDragStartPos;

    /// <summary>问题9：Mini Tooltip 字段拖拽开始。</summary>
    private void OnMiniTooltipFieldDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.MiniTooltipFieldItem item)
        {
            _miniTooltipFieldDragSource = item;
            _miniTooltipFieldDragStartPos = e.GetPosition(null);
            if (sender is IInputElement input) input.CaptureMouse();
        }
    }

    /// <summary>问题9：Mini Tooltip 字段拖拽结束（释放捕获并保存顺序）。</summary>
    private void OnMiniTooltipFieldDragEnd(object sender, MouseButtonEventArgs e)
    {
        ReleaseDragCapture(sender);
        _miniTooltipFieldDragSource = null;
    }

    /// <summary>问题9：Mini Tooltip 字段拖拽移动（同一 WrapPanel 内拖放排序，顺序即 tooltip 行顺序）。</summary>
    private void OnMiniTooltipFieldDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_miniTooltipFieldDragSource == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseDragCapture(sender); _miniTooltipFieldDragSource = null; return; }
        if (!ExceedsDragThreshold(_miniTooltipFieldDragStartPos, e)) return;
        PerformDragReorder(sender, _miniTooltipFieldDragSource, e,
            el => FindAncestorDataContext<ViewModels.MiniAccountNode>(el)?.Save());
    }

    /// <summary>拖拽公共：释放鼠标捕获（已捕获时）。</summary>
    private static void ReleaseDragCapture(object sender)
    {
        if (sender is IInputElement input && input.IsMouseCaptured) input.ReleaseMouseCapture();
    }

    /// <summary>拖拽公共：判断鼠标移动是否超过系统拖拽阈值。</summary>
    private static bool ExceedsDragThreshold(System.Windows.Point startPos, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(null);
        return Math.Abs(pos.X - startPos.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(pos.Y - startPos.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    /// <summary>
    /// 拖拽公共（问题5）：在拖拽源所属 ItemsControl 内按鼠标位置命中目标项并执行 Move，随后持久化。
    /// <para>使用 VisualTreeHelper.HitTest 而非 e.OriginalSource：手柄捕获鼠标后 OriginalSource 始终是手柄自身，
    /// 必须按坐标命中才能找到目标行；拖拽源不清空，支持一次按住连续跨多行拖动。</para>
    /// </summary>
    private static void PerformDragReorder<TNode>(object sender, TNode source,
        System.Windows.Input.MouseEventArgs e, Action<DependencyObject?> saveAfterMove) where TNode : class
    {
        var sourceElement = sender as FrameworkElement;
        var itemsControl = FindVisualParent<ItemsControl>(sourceElement);
        if (itemsControl?.ItemsSource is not System.Collections.ObjectModel.ObservableCollection<TNode> coll) return;
        var hit = VisualTreeHelper.HitTest(itemsControl, e.GetPosition(itemsControl));
        var target = hit?.VisualHit == null ? null : FindAncestorDataContext<TNode>(hit.VisualHit);
        if (target == null || ReferenceEquals(target, source)) return;
        var fromIdx = coll.IndexOf(source);
        var toIdx = coll.IndexOf(target);
        if (fromIdx >= 0 && toIdx >= 0 && fromIdx != toIdx)
        {
            coll.Move(fromIdx, toIdx);
            saveAfterMove(sourceElement);
        }
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
    /// req-113：设置界面"校验插件"按钮——直接扫描 plugins 目录全部声明包并弹出聚合报告，
    /// 与 <c>--validate-plugin</c> 命令行共用 <see cref="UsageMonitor.Core.Plugins.PluginValidator"/> 校验代码。
    /// </summary>
    private void OnValidatePluginClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var pluginsRoot = (System.Windows.Application.Current as App)?.PluginsDirectory
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            var manifestNames = new[] { "plugin.json", "fetch.json", "display.json", "defaults.json" };
            var packageDirs = Directory.Exists(pluginsRoot)
                ? Directory.GetDirectories(pluginsRoot)
                    .Where(d => manifestNames.Any(f => File.Exists(Path.Combine(d, f))))
                    .ToList()
                : new List<string>();

            if (packageDirs.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    this, $"plugins 目录下未发现声明包：\n{pluginsRoot}", "插件校验",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var sdkVersion = App.CurrentSdkVersion();
            var sb = new System.Text.StringBuilder();
            var allValid = true;
            foreach (var dir in packageDirs)
            {
                var result = UsageMonitor.Core.Plugins.PluginValidator.ValidatePackageDirectory(dir, sdkVersion);
                allValid &= result.IsValid;
                sb.AppendLine($"【{Path.GetFileName(dir)}】");
                sb.Append(result.ToReport());
                sb.AppendLine();
            }

            System.Windows.MessageBox.Show(
                this,
                sb.ToString().TrimEnd(),
                allValid ? $"插件校验通过（{packageDirs.Count} 个包）" : "插件校验存在问题",
                System.Windows.MessageBoxButton.OK,
                allValid ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
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
    /// req-112：插件管理页"刷新"按钮——调用与目录监视热重载相同的统一重载管线，
    /// 完成后在按钮旁短暂显示"已加载 N 个插件"。
    /// </summary>
    private void OnReloadPluginsClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is not App app) return;
        var count = app.ReloadPluginsAndRebuild();
        ShowPluginActionStatus(sender, count >= 0 ? $"已加载 {count} 个插件" : "正在重载，请稍候再试");
    }

    /// <summary>
    /// req-114："安装插件"按钮——弹出"从文件夹 / 从压缩包"两项菜单。
    /// </summary>
    private void OnInstallPluginClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;

        var menu = new System.Windows.Controls.ContextMenu();
        var fromFolder = new System.Windows.Controls.MenuItem { Header = "从文件夹安装…" };
        fromFolder.Click += (_, _) => InstallPluginFromFolderDialog(btn);
        var fromZip = new System.Windows.Controls.MenuItem { Header = "从压缩包安装…" };
        fromZip.Click += (_, _) => InstallPluginFromZipDialog(btn);
        menu.Items.Add(fromFolder);
        menu.Items.Add(fromZip);
        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// req-114：选择插件文件夹并安装（.NET 8 OpenFolderDialog）。
    /// </summary>
    /// <param name="origin">发起按钮（用于定位状态提示文本）。</param>
    private void InstallPluginFromFolderDialog(System.Windows.Controls.Button origin)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择插件文件夹（含 plugin.json / defaults.json 等清单文件）" };
        if (dlg.ShowDialog(this) != true) return;
        RunPluginInstall(origin, overwrite =>
            ((App)System.Windows.Application.Current).InstallPluginFromFolder(dlg.FolderName, overwrite));
    }

    /// <summary>
    /// req-114：选择插件 zip 压缩包并安装。
    /// </summary>
    /// <param name="origin">发起按钮（用于定位状态提示文本）。</param>
    private void InstallPluginFromZipDialog(System.Windows.Controls.Button origin)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择插件压缩包",
            Filter = "插件压缩包 (*.zip)|*.zip",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;
        RunPluginInstall(origin, overwrite =>
            ((App)System.Windows.Application.Current).InstallPluginFromZip(dlg.FileName, overwrite));
    }

    /// <summary>
    /// req-114：执行安装并处理结果：同名包弹覆盖确认后重试；失败展示错误与校验明细；成功短暂显示状态提示。
    /// </summary>
    /// <param name="origin">发起按钮。</param>
    /// <param name="install">安装动作（参数为是否覆盖）。</param>
    private void RunPluginInstall(
        System.Windows.Controls.Button origin,
        Func<bool, UsageMonitor.Core.Plugins.PluginInstallResult> install)
    {
        try
        {
            var result = install(false);
            if (result.RequiresOverwriteConfirmation)
            {
                var confirm = System.Windows.MessageBox.Show(
                    this, $"plugins/{result.PackageName} 已存在，是否覆盖安装？", "安装插件",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (confirm != System.Windows.MessageBoxResult.Yes) return;
                result = install(true);
            }

            if (result.Success)
            {
                ShowPluginActionStatus(origin, $"已安装 {result.PackageName}");
                // 预校验警告不阻断安装，但提示给用户知晓
                if (result.Validation is { Warnings.Count: > 0 })
                {
                    System.Windows.MessageBox.Show(
                        this, $"安装成功，但存在警告：\n{result.Validation.ToReport()}", "安装插件",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            else
            {
                var detail = result.Validation != null ? "\n\n" + result.Validation.ToReport() : string.Empty;
                System.Windows.MessageBox.Show(
                    this, (result.Error ?? "安装失败") + detail, "安装插件失败",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this, $"安装异常：{ex.Message}", "安装插件",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// req-112/114：在插件操作区按钮旁的状态 TextBlock 短暂显示提示（3 秒后自动隐藏）。
    /// <para>状态 TextBlock 与按钮同属一个 StackPanel（DataTemplate 内无法用 x:Name 字段访问，改用父容器查找）。</para>
    /// </summary>
    /// <param name="sender">触发按钮。</param>
    /// <param name="message">提示文本。</param>
    private static void ShowPluginActionStatus(object sender, string message)
    {
        var panel = (sender as FrameworkElement)?.Parent as System.Windows.Controls.StackPanel;
        var text = panel?.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
        if (text == null) return;

        text.Text = message;
        text.Visibility = Visibility.Visible;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            text.Visibility = Visibility.Collapsed;
        };
        timer.Start();
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

    // --- S2：图表/分界线拖拽排序（混合列表 ChartItems：图表实例节点 + 分界线节点） ---
    private ViewModels.CardChartListItem? _chartDragSource;
    private System.Windows.Point _chartDragStartPos;

    /// <summary>S2：图表/分界线拖拽开始（记录拖拽源并捕获鼠标，保证移动事件持续路由到源元素，避免移出源后丢失事件）。</summary>
    private void OnChartDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.CardChartListItem item)
        {
            _chartDragSource = item;
            _chartDragStartPos = e.GetPosition(null);
            if (sender is IInputElement input) input.CaptureMouse();
        }
    }

    /// <summary>S2：图表/分界线拖拽结束（释放鼠标捕获并清空拖拽源）。</summary>
    private void OnChartDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (sender is IInputElement input && input.IsMouseCaptured) input.ReleaseMouseCapture();
        _chartDragSource = null;
    }

    /// <summary>S2：图表/分界线拖拽移动（按鼠标位置命中目标行，在同一混合列表内执行拖放并保存）。</summary>
    private void OnChartDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_chartDragSource == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            // 鼠标已释放（捕获状态下仍可能收到移动事件）：结束拖拽
            if (sender is IInputElement input && input.IsMouseCaptured) input.ReleaseMouseCapture();
            _chartDragSource = null;
            return;
        }

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _chartDragStartPos.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _chartDragStartPos.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var sourceElement = sender as FrameworkElement;
        var itemsControl = FindVisualParent<ItemsControl>(sourceElement);
        if (itemsControl?.ItemsSource is System.Collections.ObjectModel.ObservableCollection<ViewModels.CardChartListItem> items)
        {
            // 通过鼠标位置命中目标行（不要求鼠标恰好悬停在目标手柄上，大幅提升拖拽容差）
            var target = HitTestChartItem(itemsControl, e.GetPosition(itemsControl));
            if (target != null && !ReferenceEquals(target, _chartDragSource))
            {
                var fromIdx = items.IndexOf(_chartDragSource);
                var toIdx = items.IndexOf(target);
                if (fromIdx >= 0 && toIdx >= 0 && fromIdx != toIdx)
                {
                    items.Move(fromIdx, toIdx);
                    // 拖拽后保存顺序与分界线位置
                    var accountNode = FindAncestorDataContext<ViewModels.AccountNode>(sourceElement);
                    accountNode?.Save();
                }
            }
        }
    }

    /// <summary>S2：按相对 ItemsControl 的坐标命中该行对应的图表列表项（图表实例或分界线）。</summary>
    private static ViewModels.CardChartListItem? HitTestChartItem(ItemsControl itemsControl, System.Windows.Point pos)
    {
        var hit = VisualTreeHelper.HitTest(itemsControl, pos);
        return hit?.VisualHit == null ? null : FindAncestorDataContext<ViewModels.CardChartListItem>(hit.VisualHit);
    }

    // --- S2：数据组拖拽排序 ---
    private ViewModels.DataGroupNode? _dataGroupDragSource;
    private System.Windows.Point _dataGroupDragStartPos;

    /// <summary>S2：数据组拖拽开始（问题5：记录拖拽源并捕获鼠标，保证移动事件持续路由到手柄）。</summary>
    private void OnDataGroupDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.DataGroupNode node)
        {
            _dataGroupDragSource = node;
            _dataGroupDragStartPos = e.GetPosition(null);
            if (sender is IInputElement input) input.CaptureMouse();
        }
    }

    /// <summary>S2：数据组拖拽结束（释放捕获 + 清空拖拽源）。</summary>
    private void OnDataGroupDragEnd(object sender, MouseButtonEventArgs e)
    {
        ReleaseDragCapture(sender);
        _dataGroupDragSource = null;
    }

    /// <summary>S2：数据组拖拽移动（问题5：命中测试定位目标行 + 持续拖拽，修复无法拖拽调整顺序）。</summary>
    private void OnDataGroupDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dataGroupDragSource == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseDragCapture(sender); _dataGroupDragSource = null; return; }
        if (!ExceedsDragThreshold(_dataGroupDragStartPos, e)) return;
        PerformDragReorder(sender, _dataGroupDragSource, e,
            el => FindAncestorDataContext<ViewModels.AccountNode>(el)?.Save());
    }

    // --- 问题2：数据组区域横向滚动 / 拖拽平移 ---
    private System.Windows.Point _dataGroupPanStartPos;
    private double _dataGroupPanStartOffset;
    private bool _dataGroupPanCandidate;
    private bool _dataGroupPanning;

    /// <summary>问题2：数据组区域滚轮转横向滚动（仅当存在横向溢出时拦截，否则透传给外层纵向滚动）。</summary>
    private void OnDataGroupScrollWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv || sv.ScrollableWidth <= 0) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>问题2：数据组区域按下鼠标——记录平移起点（不立即捕获，避免影响勾选与拖拽排序）。</summary>
    private void OnDataGroupPanStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer sv || sv.ScrollableWidth <= 0) return;
        _dataGroupPanCandidate = true;
        _dataGroupPanning = false;
        _dataGroupPanStartPos = e.GetPosition(sv);
        _dataGroupPanStartOffset = sv.HorizontalOffset;
    }

    /// <summary>问题2：数据组区域拖拽平移（横向位移超过阈值且无其它元素捕获鼠标时接管，横向滚动内容）。</summary>
    private void OnDataGroupPanMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dataGroupPanCandidate || sender is not ScrollViewer sv) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _dataGroupPanCandidate = false; _dataGroupPanning = false; return; }
        // 排序手柄（☰）按下后自行捕获鼠标，此时不做平移，避免与拖拽排序冲突。
        if (Mouse.Captured != null && !ReferenceEquals(Mouse.Captured, sv)) return;
        var pos = e.GetPosition(sv);
        var dx = pos.X - _dataGroupPanStartPos.X;
        if (!_dataGroupPanning)
        {
            if (Math.Abs(dx) < 6 || Math.Abs(dx) <= Math.Abs(pos.Y - _dataGroupPanStartPos.Y)) return;
            _dataGroupPanning = true;
            sv.CaptureMouse();
        }
        sv.ScrollToHorizontalOffset(_dataGroupPanStartOffset - dx);
        e.Handled = true;
    }

    /// <summary>问题2：数据组区域松开鼠标——结束平移并释放捕获（平移过后不再触发下层点击）。</summary>
    private void OnDataGroupPanEnd(object sender, MouseButtonEventArgs e)
    {
        _dataGroupPanCandidate = false;
        if (_dataGroupPanning && sender is ScrollViewer sv)
        {
            if (sv.IsMouseCaptured) sv.ReleaseMouseCapture();
            e.Handled = true;
        }
        _dataGroupPanning = false;
    }

    // --- 问题9：卡片图表 Tooltip 字段拖拽排序 ---
    private ViewModels.TooltipFieldItem? _tooltipFieldDragSource;
    private System.Windows.Point _tooltipFieldDragStartPos;

    /// <summary>问题9：Tooltip 字段拖拽开始。</summary>
    private void OnTooltipFieldDragStart(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ViewModels.TooltipFieldItem item)
        {
            _tooltipFieldDragSource = item;
            _tooltipFieldDragStartPos = e.GetPosition(null);
            if (sender is IInputElement input) input.CaptureMouse();
        }
    }

    /// <summary>问题9：Tooltip 字段拖拽结束。</summary>
    private void OnTooltipFieldDragEnd(object sender, MouseButtonEventArgs e)
    {
        ReleaseDragCapture(sender);
        _tooltipFieldDragSource = null;
    }

    /// <summary>问题9：Tooltip 字段拖拽移动（同一 WrapPanel 内拖放排序，顺序即 tooltip 行顺序并即时持久化）。</summary>
    private void OnTooltipFieldDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_tooltipFieldDragSource == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseDragCapture(sender); _tooltipFieldDragSource = null; return; }
        if (!ExceedsDragThreshold(_tooltipFieldDragStartPos, e)) return;
        PerformDragReorder(sender, _tooltipFieldDragSource, e,
            el => FindAncestorDataContext<ViewModels.AccountNode>(el)?.Save());
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
