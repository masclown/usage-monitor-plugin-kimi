using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 单个服务商的用量显示模型
/// </summary>
public class ProviderUsageViewModel : INotifyPropertyChanged
{
    private string _providerId = string.Empty;
    private string _displayName = string.Empty;
    private string _statusText = "未查询";
    private double _usagePercentage;
    private string _usedText = "--";
    private string _totalText = "--";
    private string _remainingText = "--";
    private string _lastUpdateText = "--";
    private bool _isEnabled = true;
    private bool _isError;
    private string? _errorMessage;

    public string ProviderId { get => _providerId; set { _providerId = value; OnPropertyChanged(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public double UsagePercentage { get => _usagePercentage; set { _usagePercentage = value; OnPropertyChanged(); } }
    public string UsedText { get => _usedText; set { _usedText = value; OnPropertyChanged(); } }
    public string TotalText { get => _totalText; set { _totalText = value; OnPropertyChanged(); } }
    public string RemainingText { get => _remainingText; set { _remainingText = value; OnPropertyChanged(); } }
    public string LastUpdateText { get => _lastUpdateText; set { _lastUpdateText = value; OnPropertyChanged(); } }
    public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
    public bool IsError { get => _isError; set { _isError = value; OnPropertyChanged(); } }
    public string? ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }

    /// <summary>
    /// 根据 UsageInfo 更新显示数据
    /// </summary>
    public void UpdateFromUsage(UsageInfo usage)
    {
        IsError = !usage.IsSuccess;
        ErrorMessage = usage.ErrorMessage;
        LastUpdateText = usage.LastUpdated.ToString("HH:mm:ss");

        if (!usage.IsSuccess)
        {
            StatusText = "查询失败";
            return;
        }

        UsagePercentage = usage.GetUsagePercentage();

        if (usage.TotalAmount > 0)
        {
            UsedText = $"{usage.UsedAmount:F2} {usage.Unit}";
            TotalText = $"{usage.TotalAmount:F2} {usage.Unit}";
            RemainingText = $"{usage.GetRemainingAmount():F2} {usage.Unit}";
            StatusText = $"{usage.GetUsagePercentage():F1}% 已使用";
        }
        else if (usage.UsedTokens > 0)
        {
            UsedText = FormatTokens(usage.UsedTokens);
            TotalText = usage.TotalTokens > 0 ? FormatTokens(usage.TotalTokens) : "不限";
            RemainingText = usage.TotalTokens > 0 ? FormatTokens(usage.GetRemainingTokens()) : "--";
            StatusText = usage.TotalTokens > 0
                ? $"{usage.GetUsagePercentage():F1}% 已使用"
                : $"已用 {UsedText}";
        }
        else
        {
            StatusText = "暂无数据";
        }
    }

    private static string FormatTokens(long count)
    {
        if (count < 0) return "不限";
        if (count >= 1_000_000) return $"{count / 1_000_000.0:F1}M";
        if (count >= 1_000) return $"{count / 1_000.0:F1}K";
        return count.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 插件列表项视图模型
/// </summary>
public class PluginItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;

    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 主窗口ViewModel - 管理用量数据显示和交互逻辑
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly PluginManager _pluginManager;
    private readonly ConfigService _configService;
    private readonly RefreshService _refreshService;

    /// <summary>各服务商的用量显示列表</summary>
    public ObservableCollection<ProviderUsageViewModel> Usages { get; } = new();

    /// <summary>插件列表</summary>
    public ObservableCollection<PluginItemViewModel> PluginItems { get; } = new();

    /// <summary>刷新间隔（秒）</summary>
    public int RefreshInterval
    {
        get => _configService.Settings.RefreshIntervalSeconds;
        set
        {
            _configService.Settings.RefreshIntervalSeconds = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>是否启用任务栏显示</summary>
    public bool ShowInTaskbar
    {
        get => _configService.Settings.ShowInTaskbar;
        set
        {
            _configService.Settings.ShowInTaskbar = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>是否开机自启</summary>
    public bool AutoStart
    {
        get => _configService.Settings.AutoStart;
        set
        {
            _configService.Settings.AutoStart = value;
            _configService.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>刷新命令</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>保存设置命令</summary>
    public IRelayCommand SaveSettingsCommand { get; }

    public MainViewModel(PluginManager pluginManager, ConfigService configService, RefreshService refreshService)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _refreshService = refreshService;

        RefreshCommand = new RelayCommand(async () => await refreshService.RefreshAllAsync());
        SaveSettingsCommand = new RelayCommand(() => _configService.Save());

        // 初始化插件列表
        foreach (var plugin in pluginManager.Plugins)
        {
            var item = new PluginItemViewModel
            {
                ProviderId = plugin.Provider.ProviderId,
                DisplayName = plugin.Provider.DisplayName,
                Version = plugin.Provider.Version,
                Author = plugin.Provider.Author,
                Description = plugin.Provider.Description,
                IsEnabled = plugin.IsEnabled
            };
            PluginItems.Add(item);

            // 初始化用量显示
            var usageVm = new ProviderUsageViewModel
            {
                ProviderId = plugin.Provider.ProviderId,
                DisplayName = plugin.Provider.DisplayName,
                IsEnabled = plugin.IsEnabled
            };
            Usages.Add(usageVm);
        }
    }

    /// <summary>
    /// 更新所有用量数据
    /// </summary>
    public void UpdateUsages(IReadOnlyList<UsageInfo> usages)
    {
        foreach (var usage in usages)
        {
            var vm = Usages.FirstOrDefault(u => u.ProviderId == usage.ProviderId);
            vm?.UpdateFromUsage(usage);
        }
    }

    /// <summary>
    /// 更新插件启用状态
    /// </summary>
    public void UpdatePluginEnabled(string providerId, bool isEnabled)
    {
        _configService.Settings.PluginEnabled[providerId] = isEnabled;

        var plugin = _pluginManager.GetPlugin(providerId);
        if (plugin != null) plugin.IsEnabled = isEnabled;

        var usageVm = Usages.FirstOrDefault(u => u.ProviderId == providerId);
        if (usageVm != null) usageVm.IsEnabled = isEnabled;

        _configService.Save();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
