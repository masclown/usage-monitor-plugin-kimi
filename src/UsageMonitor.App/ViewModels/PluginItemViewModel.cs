using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.App.Helpers;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Services.Auth;

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
    // S1：统一鉴权管理器（供账号行 Sub 状态灯读取登录态，可为 null）。
    private readonly AuthManager? _authManager;
    // S1：账号列表展开态。
    private bool _isAccountsExpanded;

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

    /// <summary>插件定义的配置字段列表</summary>
    public IReadOnlyList<ConfigField> ConfigFields { get; set; } = Array.Empty<ConfigField>();

    /// <summary>打开配置对话框的命令</summary>
    public IRelayCommand ConfigCommand { get; }

    /// <summary>
    /// 创建插件列表项视图模型
    /// </summary>
    public PluginItemViewModel(IUsageProvider provider, ConfigService configService, AuthManager? authManager = null)
    {
        _provider = provider;
        _configService = configService;
        _authManager = authManager;
        ConfigFields = provider.ConfigFields;
        ConfigCommand = new RelayCommand(() => OpenConfigDialog());
        // S1：账号列表展开/收起 与 添加账号 命令
        ToggleAccountsCommand = new RelayCommand(() => IsAccountsExpanded = !IsAccountsExpanded);
        AddAccountCommand = new RelayCommand(AddAccount);
    }

    /// <summary>
    /// 打开插件配置对话框（公共，可被主窗口卡片上的“⚙ 设置”按钮复用）。
    /// <para>S6：账号增删改已统一迁移到插件管理页，本方法仅负责打开瘦身后的配置窗口；
    /// 可选传入 <paramref name="accountId"/> 使图表/迷你图表启用开关按账号生效
    /// （缺省 null → "default"，即 Provider 级入口行为）。</para>
    /// </summary>
    public void OpenConfigDialog(string? accountId = null)
    {
        var config = _configService.GetProviderConfig(ProviderId, _provider);
        // 通用登录配置：只要插件声明了 LoginConfig（不限于 MiniMax），配置窗口就显示“获取登录态”按钮
        // req-065 B4：传递 ConfigService 给 PluginConfigWindow，用于 BrowserLoginService 实例化
        // req-fix-Kimi-ConfigFields 动态模式：传递 _provider 让 PluginConfigWindow 监听 Mode ComboBox 变化时
        // 重新调用 provider.ConfigFields 拉取与新模式匹配的字段列表。
        // S6：传递 accountId 使图表/迷你图表启用开关按账号生效；旧卡片图表多选参数已随该区删除。
        var configWindow = new Views.PluginConfigWindow(
            DisplayName, ConfigFields, config, _provider.LoginConfig,
            configService: _configService, provider: _provider, accountId: accountId);
        configWindow.Owner = System.Windows.Application.Current.Windows
            .OfType<Window>().FirstOrDefault(w => w.IsActive);

        if (configWindow.ShowDialog() == true)
        {
            _configService.UpdateProviderConfig(ProviderId, config);
            // S6：图表/迷你图表启用开关由窗口内部经 ConfigService.SetVisibleCharts /
            // SetVisibleMiniCharts 直接落盘（AccountCustomizations），不再回写 ProviderCardChartKinds。
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

    // =====================================================================
    // S1：账号列表（插件管理页内嵌账号增删改的唯一入口）
    // =====================================================================

    /// <summary>S1：该插件下的账号行集合。</summary>
    public ObservableCollection<PluginAccountItemViewModel> Accounts { get; } = new();

    /// <summary>S1：账号列表是否展开（点击徽标 / 箭头切换）。</summary>
    public bool IsAccountsExpanded
    {
        get => _isAccountsExpanded;
        set
        {
            if (_isAccountsExpanded == value) return;
            _isAccountsExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandArrowText));
            // 展开时刷新各账号行状态灯（登录态可能已变化）
            if (value)
            {
                foreach (var a in Accounts) a.RefreshStatus();
            }
        }
    }

    /// <summary>S1：展开箭头字符（收起 ▸ / 展开 ▾）。</summary>
    public string ExpandArrowText => _isAccountsExpanded ? "▾" : "▸";

    /// <summary>S1：账号数（徽标「账号 × N」）。</summary>
    public int AccountCount => Accounts.Count;

    /// <summary>S1：是否无账号（控制「+ 创建账号」空状态按钮显示）。</summary>
    public bool HasNoAccounts => Accounts.Count == 0;

    /// <summary>S1：是否存在可用账号（绿点：启用 且 有凭据＝API Key 或登录态）。</summary>
    public bool HasUsableAccount => Accounts.Any(a => a.IsEnabled && (a.HasApiKey || a.HasLoginState));

    /// <summary>S1：切换账号列表展开/收起。</summary>
    public IRelayCommand ToggleAccountsCommand { get; }

    /// <summary>S1：添加新账号。</summary>
    public IRelayCommand AddAccountCommand { get; }

    /// <summary>
    /// S1：从配置加载该 Provider 的账号并重建账号行集合（启动装配与重建时调用）。
    /// </summary>
    public void ReloadAccounts()
    {
        // 先解除旧账号行的 PropertyChanged 订阅，防止集合重建后仍被回调
        foreach (var old in Accounts) old.PropertyChanged -= OnAccountItemPropertyChanged;
        Accounts.Clear();
        try
        {
            foreach (var account in _configService.GetAccounts(ProviderId))
            {
                var vm = new PluginAccountItemViewModel(account, ProviderId, _configService, _authManager, this);
                vm.PropertyChanged += OnAccountItemPropertyChanged;
                Accounts.Add(vm);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("PluginItem", $"加载账号列表失败：{ProviderId}", ex);
        }
        NotifyAccountSummaryChanged();
    }

    /// <summary>S1：账号行属性变化回调——启用/凭据状态变动时重算徽标绿点。</summary>
    private void OnAccountItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PluginAccountItemViewModel.IsEnabled)
            or nameof(PluginAccountItemViewModel.HasApiKey)
            or nameof(PluginAccountItemViewModel.HasLoginState))
        {
            NotifyAccountSummaryChanged();
        }
    }

    /// <summary>S1：通知账号汇总属性变化（计数 / 绿点 / 空状态）。</summary>
    public void NotifyAccountSummaryChanged()
    {
        OnPropertyChanged(nameof(AccountCount));
        OnPropertyChanged(nameof(HasNoAccounts));
        OnPropertyChanged(nameof(HasUsableAccount));
    }

    /// <summary>S1：某账号昵称落定后批量复检所有账号行，清除过期重名错误。</summary>
    public void RevalidateAllNicknames()
    {
        foreach (var a in Accounts) a.RevalidateNickname();
    }

    /// <summary>S1：添加新账号并展开列表，新行立即可编辑（AddAccount 内部 Save 会触发卡片重建）。</summary>
    private void AddAccount()
    {
        try
        {
            var account = _configService.AddAccount(ProviderId, null);
            var vm = new PluginAccountItemViewModel(account, ProviderId, _configService, _authManager, this);
            vm.PropertyChanged += OnAccountItemPropertyChanged;
            Accounts.Add(vm);
            IsAccountsExpanded = true;
            NotifyAccountSummaryChanged();
        }
        catch (Exception ex)
        {
            FileLogger.Error("PluginItem", $"添加账号失败：{ProviderId}", ex);
            System.Windows.MessageBox.Show($"添加账号失败：{ex.Message}", "添加失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>S1：从集合移除指定账号行（RemoveAccount 成功后由账号行回调）。</summary>
    public void RemoveAccountItem(PluginAccountItemViewModel item)
    {
        item.PropertyChanged -= OnAccountItemPropertyChanged;
        Accounts.Remove(item);
        NotifyAccountSummaryChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
