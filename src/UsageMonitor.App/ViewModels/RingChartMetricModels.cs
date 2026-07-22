using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.App.Controls;
using UsageMonitor.App.Helpers;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// req-026：设置窗口"环形图中心" Tab 中单个 Provider 的 metric 勾选状态集合。
/// <para>每行 Provider 对应一个本实例，<see cref="Metrics"/> 列出该 Provider 支持的全部 metric，
/// 勾选态由 <c>AppSettings.ProviderEnabledRingChartMetrics[ProviderId]</c> + 全局默认合并解析。</para>
/// </summary>
public class ProviderRingChartMetricGroup
{
    /// <summary>Provider 唯一标识。</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Provider 中文显示名。</summary>
    public string ProviderDisplayName { get; set; } = string.Empty;

    /// <summary>该 Provider 支持的环形图中心 metric 勾选项集合。</summary>
    public ObservableCollection<RingChartMetricChoice> Metrics { get; } = new();
}

/// <summary>
/// req-026：单个环形图中心 metric 的勾选项，对应设置窗口一列 CheckBox。
/// <para>Key 绑定 RingChartControl 的 <c>MetricKey</c>；<see cref="IsEnabled"/> 即是否纳入已启用集合。</para>
/// </summary>
public class RingChartMetricChoice : INotifyPropertyChanged
{
    private bool _isEnabled;

    /// <summary>metric 键（如 <c>"Percent"</c> / <c>"Credits"</c>）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>中文显示名（设置窗口 CheckBox.Content）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>勾选状态（写回 <c>AppSettings.ProviderEnabledRingChartMetrics</c>）。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
