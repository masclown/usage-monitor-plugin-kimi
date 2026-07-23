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
using UsageMonitor.App.Services;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 插件列表项视图模型 - 包含插件信息、启用状态和配置命令
/// </summary>
public class PluginItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private TaskbarDisplayMode _displayMode = TaskbarDisplayMode.Text;
    private readonly ConfigService _configService;
    private readonly IUsageProvider _provider;

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

    /// <summary>任务栏显示模式（与 ProviderUsageViewModel.DisplayMode 同步）</summary>
    public TaskbarDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value) return;
            _displayMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayModeText));
        }
    }

    /// <summary>显示模式下拉框的友好文本</summary>
    public string DisplayModeText => DisplayMode switch
    {
        TaskbarDisplayMode.Text => "文字",
        TaskbarDisplayMode.MiniLineChart => "折线图",
        TaskbarDisplayMode.RingChart => "圆环图",
        _ => "文字"
    };

    /// <summary>所有可选模式（用于 ComboBox 绑定）</summary>
    public IReadOnlyList<KeyValuePair<TaskbarDisplayMode, string>> AvailableDisplayModes { get; } = new[]
    {
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.Text, "文字"),
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.MiniLineChart, "折线图"),
        new KeyValuePair<TaskbarDisplayMode, string>(TaskbarDisplayMode.RingChart, "圆环图"),
    };

    private IReadOnlyList<CardChartKind> _cardChartKinds = Array.Empty<CardChartKind>();

    /// <summary>
    /// 卡片图表类型「集合」（多选）。设置时写入配置并保存；由 MainViewModel 订阅同步到对应的 ProviderUsageViewModel。
    /// </summary>
    public IReadOnlyList<CardChartKind> CardChartKinds
    {
        get => _cardChartKinds;
        set
        {
            _cardChartKinds = value ?? Array.Empty<CardChartKind>();
            _configService.SetProviderCardChartKinds(ProviderId, _cardChartKinds);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 初始化卡片图表多选（仅设置字段并通知，不触发持久化）。供启动装配时回显已保存/迁移的选择，
    /// 避免逐插件重复写盘（迁移得到的值会随后续任一配置变更一并持久化）。
    /// </summary>
    public void InitCardChartKinds(IReadOnlyList<CardChartKind> kinds)
    {
        _cardChartKinds = kinds ?? Array.Empty<CardChartKind>();
        OnPropertyChanged(nameof(CardChartKinds));
    }

    /// <summary>插件定义的配置字段列表</summary>
    public IReadOnlyList<ConfigField> ConfigFields { get; set; } = Array.Empty<ConfigField>();

    /// <summary>打开配置对话框的命令</summary>
    public IRelayCommand ConfigCommand { get; }

    /// <summary>
    /// 创建插件列表项视图模型
    /// </summary>
    public PluginItemViewModel(IUsageProvider provider, ConfigService configService)
    {
        _provider = provider;
        _configService = configService;
        ConfigFields = provider.ConfigFields;
        ConfigCommand = new RelayCommand(OpenConfigDialog);
    }

    /// <summary>
    /// 打开插件配置对话框（公共，可被主窗口卡片上的"⚙ 设置"按钮复用）
    /// </summary>
    public void OpenConfigDialog()
    {
        var config = _configService.GetProviderConfig(ProviderId, _provider);
        // 读取该 Provider 当前已选的卡片图表集合（含旧单选迁移），传入配置窗口用于回显 + 预览
        var currentCharts = _configService.GetProviderCardChartKinds(ProviderId);
        // 通用登录配置：只要插件声明了 LoginConfig（不限于 MiniMax），配置窗口就显示"获取登录态"按钮
        // req-065 B4：传递 ConfigService 给 PluginConfigWindow，用于 BrowserLoginService 实例化
        // req-fix-Kimi-ConfigFields 动态模式：传递 _provider 让 PluginConfigWindow 监听 Mode ComboBox 变化时
        // 重新调用 provider.ConfigFields 拉取与新模式匹配的字段列表。
        var configWindow = new Views.PluginConfigWindow(
            DisplayName, ConfigFields, config, _provider.LoginConfig,
            ChartKindExtractor.ExtractDeclaredChartKinds(_provider), currentCharts, _configService, _provider);
        configWindow.Owner = System.Windows.Application.Current.Windows
            .OfType<Window>().FirstOrDefault(w => w.IsActive);

        if (configWindow.ShowDialog() == true)
        {
            _configService.UpdateProviderConfig(ProviderId, config);
            // 持久化并同步卡片图表多选（setter 内部写配置 + Save + 通知）
            CardChartKinds = configWindow.SelectedCardChartKinds;
            // 检查保存是否真的成功（ConfigService.Save 失败时会被 catch 吞掉）
            if (!string.IsNullOrEmpty(_configService.LastSaveError))
            {
                System.Windows.MessageBox.Show(
                    $"配置保存失败：{_configService.LastSaveError}\n\n可能是磁盘满、权限不足或文件被占用。\n配置在本次会话有效，但重启后可能丢失。",
                    "保存失败",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
