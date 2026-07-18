using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Display the live log file path so users can copy it for diagnostics.
        if (LogPathTextBox != null)
            LogPathTextBox.Text = FileLogger.GetCurrentLogPath();
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
    /// req-027：保存设置按钮的 Click 处理器——保存成功→关闭窗口；保存失败→弹错误 MessageBox + 不关闭。
    /// <para>
    /// 与之前 <c>SaveSettingsCommand</c> 的区别：本方法把"窗口关闭"嵌入到成功路径里，
    /// 用户点击一次就完成"保存 + 退出设置"两步。失败时不关闭，让用户改完再点一次。
    /// </para>
    /// <para>
    /// 数据源：<see cref="ConfigService.LastSaveError"/>。Save() 同步执行：成功时清空该属性，
    /// 失败时填上 <c>$"{异常类型}: {消息}"</c>；窗体读它的状态决定后续动作。
    /// </para>
    /// </summary>
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 触发保存（直接调 _configService.Save() 以保证异常路径被本 try/catch 接住）
            _configService.Save();

            if (!string.IsNullOrEmpty(_configService.LastSaveError))
            {
                // 保存失败：弹错误 + 不关闭
                FileLogger.Warn("SettingsWindow",
                    $"保存设置失败（LastSaveError 已设置）：{_configService.LastSaveError}");
                System.Windows.MessageBox.Show(
                    $"配置保存失败：\n{_configService.LastSaveError}\n\n可能是磁盘满、权限不足或文件被占用。窗口已保持打开，请修改后重试。",
                    "保存失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            // 保存成功：关闭窗口（按 req-027 Q1 A + Q4 A，无 toast 反馈）
            FileLogger.Info("SettingsWindow", "保存设置成功，关闭设置窗口");
            this.Close();
        }
        catch (Exception ex)
        {
            // Save() 自身抛出（如 JSON 写入失败）的兜底
            FileLogger.Warn("SettingsWindow", "保存设置抛出异常", ex);
            System.Windows.MessageBox.Show(
                $"保存失败：\n{ex.Message}",
                "保存失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
