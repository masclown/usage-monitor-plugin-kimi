using System.ComponentModel;
using System.Runtime.CompilerServices;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.MiniChart;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// req-098：单个 Provider 任务栏迷你图表配置项的 ViewModel。
/// <para>
/// 包装 <see cref="TaskbarMiniChartConfig"/>（持久化 DTO），把所有 setter
/// 转发到 <c>AppSettings.TaskbarMiniChartConfigs</c> 并触发 ConfigService.Save()，
/// 再 INPC 通知 XAML 刷新。
/// </para>
/// <para>
/// 不负责 ProviderId / DisplayName 变化（这两个字段由宿主装配时一次性写入）。
/// </para>
/// </summary>
public class TaskbarMiniChartProviderViewModel : INotifyPropertyChanged
{
    private readonly Core.Services.ConfigService _configService;
    private bool _isVisible;
    private MiniChartKind _chartKind;
    private MiniChartContentKind _contentKind;
    private MiniChartContentKind? _secondaryKind;
    private bool _showLogo;

    /// <summary>Provider 唯一标识（不可变）。</summary>
    public string ProviderId { get; }

    /// <summary>Provider 显示名（不可变）。</summary>
    public string ProviderDisplayName { get; }

    /// <summary>插件声明的可选迷你图类型（XAML 绑定到 ComboBox ItemsSource）。</summary>
    public IReadOnlyList<MiniChartKind> AvailableChartKinds { get; }

    /// <summary>插件声明的可选内容类型（XAML 绑定到 ComboBox ItemsSource）。</summary>
    public IReadOnlyList<MiniChartContentKind> AvailableContentKinds { get; }

    /// <summary>是否在任务栏显示。变更立即写盘。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            Persist();
            OnPropertyChanged();
        }
    }

    /// <summary>图表类型。变更立即写盘。</summary>
    public MiniChartKind ChartKind
    {
        get => _chartKind;
        set
        {
            if (_chartKind == value) return;
            _chartKind = value;
            Persist();
            OnPropertyChanged();
        }
    }

    /// <summary>主显示内容。变更立即写盘。</summary>
    public MiniChartContentKind ContentKind
    {
        get => _contentKind;
        set
        {
            if (_contentKind == value) return;
            _contentKind = value;
            Persist();
            OnPropertyChanged();
        }
    }

    /// <summary>副显示内容（可选）。变更立即写盘。</summary>
    public MiniChartContentKind? SecondaryKind
    {
        get => _secondaryKind;
        set
        {
            if (_secondaryKind == value) return;
            _secondaryKind = value;
            Persist();
            OnPropertyChanged();
        }
    }

    /// <summary>是否显示 Provider Logo。变更立即写盘。</summary>
    public bool ShowLogo
    {
        get => _showLogo;
        set
        {
            if (_showLogo == value) return;
            _showLogo = value;
            Persist();
            OnPropertyChanged();
        }
    }

    public TaskbarMiniChartProviderViewModel(
        string providerId,
        string providerDisplayName,
        IReadOnlyList<MiniChartKind> availableChartKinds,
        IReadOnlyList<MiniChartContentKind> availableContentKinds,
        Core.Services.ConfigService configService)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        ProviderDisplayName = providerDisplayName ?? providerId;
        AvailableChartKinds = availableChartKinds ?? Array.Empty<MiniChartKind>();
        AvailableContentKinds = availableContentKinds ?? Array.Empty<MiniChartContentKind>();
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        // 从已有配置中读取（若有），否则用默认。
        // req-098：GetValueOrDefault 不支持引用类型 null 默认值，用 TryGetValue 避免 CS8620 警告。
        var existing = (TaskbarMiniChartConfig?)null;
        _configService.Settings.TaskbarMiniChartConfigs.TryGetValue(providerId, out var found);
        if (found != null) existing = found;
        if (existing != null)
        {
            _isVisible = existing.IsVisible;
            _chartKind = existing.ChartKind;
            _contentKind = existing.ContentKind;
            _secondaryKind = existing.SecondaryKind;
            _showLogo = existing.ShowLogo;
        }
        else
        {
            _isVisible = true;
            _chartKind = MiniChartKind.MiniRingChart;
            _contentKind = MiniChartContentKind.PrimaryMetric;
            _showLogo = true;
        }
    }

    /// <summary>把当前字段持久化到 <c>AppSettings.TaskbarMiniChartConfigs</c>。</summary>
    private void Persist()
    {
        _configService.UpdateSettings(s =>
        {
            s.TaskbarMiniChartConfigs[ProviderId] = new TaskbarMiniChartConfig
            {
                IsVisible = _isVisible,
                ChartKind = _chartKind,
                ContentKind = _contentKind,
                SecondaryKind = _secondaryKind,
                ShowLogo = _showLogo
            };
        });
        try
        {
            _configService.Save();
        }
        catch (Exception ex)
        {
            UsageMonitor.Core.Services.FileLogger.Warn(
                "TaskbarMiniChartProviderViewModel",
                $"Persist {ProviderId} failed: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}