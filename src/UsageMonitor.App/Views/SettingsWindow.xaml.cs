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
    private TriggerAreaOverlayWindow? _triggerOverlay;

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
    /// 勾选 / 取消勾选「显示触发区域调试矩形」时创建或销毁覆盖窗口。
    /// 覆盖窗口的 TextBox 双向同步由 <see cref="TriggerAreaOverlayWindow"/> 内部订阅 ConfigChanged 自行处理。
    /// </summary>
    private void ShowTriggerOverlayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (ShowTriggerOverlayCheckBox.IsChecked == true)
        {
            // 已存在则复用（避免重复创建覆盖窗口导致闪烁）
            if (_triggerOverlay == null)
            {
                _triggerOverlay = new TriggerAreaOverlayWindow(_configService);
                _triggerOverlay.Closed += (_, _) => _triggerOverlay = null;
            }
            if (!_triggerOverlay.IsVisible) _triggerOverlay.Show();
            FileLogger.Info("SettingsWindow", "TriggerAreaOverlayWindow 已显示");
        }
        else
        {
            _triggerOverlay?.Close();
            _triggerOverlay = null;
            FileLogger.Info("SettingsWindow", "TriggerAreaOverlayWindow 已隐藏");
        }
    }

    /// <summary>
    /// 关闭设置窗口时强制关闭覆盖窗口，避免遗留调试矩形。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_triggerOverlay != null)
        {
            _triggerOverlay.Close();
            _triggerOverlay = null;
        }
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
}
