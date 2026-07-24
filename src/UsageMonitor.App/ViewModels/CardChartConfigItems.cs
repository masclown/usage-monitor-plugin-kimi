using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// req-107 B6 演进：单卡片的图表配置 VM 项，供设置窗口「卡片图表与数据组」分区绑定。
/// <para>承载图表可见性、排序与嵌套的数据组配置项。</para>
/// </summary>
public sealed class CardChartConfigItem : INotifyPropertyChanged
{
    public string ChartId { get; init; } = "";
    public string ChartKind { get; init; } = "";
    public string Title { get; init; } = "";

    private bool _isVisible;
    /// <summary>是否在卡片中显示。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<DataGroupConfigItem> DataGroups { get; } = new();

    /// <summary>req-105：该图表的 Tooltip 显示字段（SDK 字段名集合；null 语义下空集合 = 沿用 defaults.json 默认）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> TooltipFields { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 单个数据组的配置 VM 项（嵌套在 <see cref="CardChartConfigItem.DataGroups"/> 内）。
/// </summary>
public sealed class DataGroupConfigItem : INotifyPropertyChanged
{
    public string DataGroupId { get; init; } = "";
    public string DisplayName { get; init; } = "";

    private bool _isVisible;
    /// <summary>该数据组是否在对应图表中显示。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}