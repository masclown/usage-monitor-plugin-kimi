using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Views;

/// <summary>
/// 设置窗口 - 配置刷新间隔、任务栏显示、插件管理、诊断日志入口
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
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
