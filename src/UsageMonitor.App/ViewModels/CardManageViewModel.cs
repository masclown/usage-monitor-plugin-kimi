using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.App.Helpers;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// S2：卡片管理页 ViewModel（三级折叠：账号 → 图表 → 数据组 + S5 tooltip 字段多选）。
/// <para>跨 Provider 列出所有已启用账号，每个账号展开后显示图表列表，图表展开后显示数据组与 tooltip 字段。</para>
/// </summary>
public class CardManageViewModel : INotifyPropertyChanged
{
    private readonly PluginManager _pluginManager;
    private readonly ConfigService _configService;

    /// <summary>所有已启用账号的节点集合（跨 Provider）。</summary>
    public ObservableCollection<AccountNode> AccountNodes { get; } = new();

    /// <summary>创建卡片管理 ViewModel 并加载账号列表。</summary>
    public CardManageViewModel(PluginManager pluginManager, ConfigService configService)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        Reload();
    }

    /// <summary>重新加载所有已启用账号及其图表配置（页面切入时调用）。</summary>
    public void Reload()
    {
        AccountNodes.Clear();
        try
        {
            foreach (var plugin in _pluginManager.Plugins)
            {
                var provider = plugin.Provider;
                var card = provider.Card;
                if (card == null || card.Charts.Count == 0) continue;

                var accounts = _configService.GetAccounts(provider.ProviderId);
                if (accounts.Count == 0)
                {
                    // 无显式账号时回退 "default"
                    AccountNodes.Add(CreateAccountNode(provider, card, "default", null));
                }
                else
                {
                    foreach (var acct in accounts.Where(a => a.Enabled))
                    {
                        AccountNodes.Add(CreateAccountNode(provider, card, acct.AccountId, acct));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("CardManage", "加载账号列表失败", ex);
        }
    }

    /// <summary>为指定 Provider + 账号创建账号节点（含图表子节点构建）。</summary>
    private AccountNode CreateAccountNode(IUsageProvider provider, CardDeclaration card, string accountId, Account? account)
    {
        var displayName = account?.UseNickname == true && !string.IsNullOrWhiteSpace(account.Nickname)
            ? account.Nickname!
            : provider.DisplayName ?? provider.ProviderId;

        var node = new AccountNode(this, provider.ProviderId, accountId, displayName,
            ProviderUsageViewModel.ResolveIconPath(provider.ProviderId), card);
        node.LoadCharts();
        return node;
    }

    /// <summary>保存指定账号的图表配置到 ConfigService（统一走 SetCardChartConfiguration）。</summary>
    internal void SaveAccountConfig(AccountNode accountNode)
    {
        try
        {
            var config = new AccountCustomization
            {
                VisibleCharts = accountNode.Charts.Where(c => c.IsEnabled).Select(c => c.ChartId).ToList(),
                ChartOrders = new Dictionary<string, int>(),
                VisibleDataGroups = new Dictionary<string, List<string>?>(),
                DataGroupOrders = new Dictionary<string, Dictionary<string, int>>(),
                VisibleTooltipFields = new Dictionary<string, List<string>?>(),
                CurrentDataGroupIds = new Dictionary<string, string>(),
            };

            for (int i = 0; i < accountNode.Charts.Count; i++)
            {
                var chart = accountNode.Charts[i];
                config.ChartOrders[chart.ChartId] = i;

                // 数据组可见性与排序
                var visibleGroups = chart.DataGroups.Where(g => g.IsEnabled).Select(g => g.GroupId).ToList();
                config.VisibleDataGroups[chart.ChartId] = visibleGroups;

                var groupOrders = new Dictionary<string, int>();
                for (int j = 0; j < chart.DataGroups.Count; j++)
                    groupOrders[chart.DataGroups[j].GroupId] = j;
                config.DataGroupOrders[chart.ChartId] = groupOrders;

                // tooltip 字段
                var tooltipFields = chart.TooltipFields.Where(f => f.IsChecked).Select(f => f.FieldName).ToList();
                config.VisibleTooltipFields[chart.ChartId] = tooltipFields;
            }

            _configService.SetCardChartConfiguration(accountNode.ProviderId, config, accountNode.AccountId);
            FileLogger.Info("CardManage", $"已保存账号配置：{accountNode.ProviderId}:{accountNode.AccountId}");
        }
        catch (Exception ex)
        {
            FileLogger.Error("CardManage", $"保存账号配置失败：{accountNode.ProviderId}:{accountNode.AccountId}", ex);
        }
    }

    /// <summary>获取指定 Provider 的插件声明（供账号节点添加图表时使用）。</summary>
    internal CardDeclaration? GetCardDeclaration(string providerId)
    {
        var plugin = _pluginManager.Plugins.FirstOrDefault(p =>
            string.Equals(p.Provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        return plugin?.Provider.Card;
    }

    /// <summary>内部暴露 ConfigService（供子节点读取配置）。</summary>
    internal ConfigService GetConfigServiceInternal() => _configService;

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// S2 一级节点：账号（跨 Provider 的已启用账号）。
/// <para>展开后显示该账号下的图表列表（插件声明默认 + AccountCustomization 覆盖合并）。</para>
/// </summary>
public class AccountNode : INotifyPropertyChanged
{
    private readonly CardManageViewModel _owner;
    private readonly CardDeclaration _cardDeclaration;
    private bool _isExpanded;

    /// <summary>所属 Provider ID。</summary>
    public string ProviderId { get; }

    /// <summary>账号 ID。</summary>
    public string AccountId { get; }

    /// <summary>显示名（昵称优先，回退 Provider 名）。</summary>
    public string DisplayName { get; }

    /// <summary>Provider 图标路径（可能为 null）。</summary>
    public string? IconPath { get; }

    /// <summary>该账号下的图表节点集合。</summary>
    public ObservableCollection<ChartNode> Charts { get; } = new();

    /// <summary>是否展开（折叠/展开图表列表）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandArrow)); } }
    }

    /// <summary>展开箭头字符。</summary>
    public string ExpandArrow => _isExpanded ? "▾" : "▸";

    /// <summary>是否无图表（控制"配置图表"按钮显示）。</summary>
    public bool HasNoCharts => Charts.Count == 0;

    /// <summary>可添加的图表列表（尚未添加的声明图表）。</summary>
    public ObservableCollection<ChartDeclaration> AvailableCharts { get; } = new();

    /// <summary>创建账号节点。</summary>
    public AccountNode(CardManageViewModel owner, string providerId, string accountId,
        string displayName, string? iconPath, CardDeclaration cardDeclaration)
    {
        _owner = owner;
        _cardDeclaration = cardDeclaration;
        ProviderId = providerId;
        AccountId = accountId;
        DisplayName = displayName;
        IconPath = iconPath;

        AddChartCommand = new RelayCommand<ChartDeclaration>(AddChart);
        RestoreDefaultChartsCommand = new RelayCommand(RestoreDefaultCharts);
    }

    /// <summary>添加图表命令（从 Popup 列表选择）。</summary>
    public IRelayCommand<ChartDeclaration> AddChartCommand { get; }

    /// <summary>恢复默认图表集命令（账号无图表时显示）。</summary>
    public IRelayCommand RestoreDefaultChartsCommand { get; }

    /// <summary>从插件声明 + AccountCustomization 合并加载图表列表。</summary>
    public void LoadCharts()
    {
        Charts.Clear();
        try
        {
            var configService = GetConfigService();
            var eff = configService.GetEffectiveAccountCustomization(ProviderId, AccountId);
            var declaredCharts = _cardDeclaration.Charts;

            // 确定可见图表列表：用户配置优先，否则沿用声明顺序
            List<ChartDeclaration> orderedCharts;
            if (eff.VisibleCharts != null && eff.VisibleCharts.Count > 0)
            {
                // 按用户排序 + 可见性过滤
                orderedCharts = eff.VisibleCharts
                    .Select(id => declaredCharts.FirstOrDefault(c => c.ChartId == id))
                    .Where(c => c != null)
                    .Cast<ChartDeclaration>()
                    .ToList();
                // 追加声明中有但用户列表未含的（设为不可见）
                foreach (var dc in declaredCharts.Where(dc => !eff.VisibleCharts.Contains(dc.ChartId)))
                    orderedCharts.Add(dc);
            }
            else
            {
                // 按 ChartOrders 排序或声明默认顺序
                orderedCharts = declaredCharts
                    .OrderBy(c => eff.ChartOrders.TryGetValue(c.ChartId, out var o) ? o : c.DefaultOrder)
                    .ToList();
            }

            foreach (var chartDecl in orderedCharts)
            {
                bool isVisible = eff.VisibleCharts == null || eff.VisibleCharts.Contains(chartDecl.ChartId);
                var chartNode = new ChartNode(this, chartDecl, isVisible, eff);
                Charts.Add(chartNode);
            }
            RefreshAvailableCharts();
        }
        catch (Exception ex)
        {
            FileLogger.Error("CardManage", $"加载图表列表失败：{ProviderId}:{AccountId}", ex);
        }
        OnPropertyChanged(nameof(HasNoCharts));
    }

    /// <summary>添加图表（从可用列表中选择后加入）。</summary>
    private void AddChart(ChartDeclaration? chartDecl)
    {
        if (chartDecl == null) return;
        if (Charts.Any(c => c.ChartId == chartDecl.ChartId)) return;

        var configService = GetConfigService();
        var eff = configService.GetEffectiveAccountCustomization(ProviderId, AccountId);
        var chartNode = new ChartNode(this, chartDecl, true, eff);
        Charts.Add(chartNode);
        RefreshAvailableCharts();
        OnPropertyChanged(nameof(HasNoCharts));
        Save();
    }

    /// <summary>恢复默认图表集（按插件声明恢复）。</summary>
    private void RestoreDefaultCharts()
    {
        Charts.Clear();
        foreach (var chartDecl in _cardDeclaration.Charts)
        {
            var configService = GetConfigService();
            var eff = configService.GetEffectiveAccountCustomization(ProviderId, AccountId);
            Charts.Add(new ChartNode(this, chartDecl, true, eff));
        }
        RefreshAvailableCharts();
        OnPropertyChanged(nameof(HasNoCharts));
        Save();
    }

    /// <summary>刷新可添加图表列表（排除已添加的）。</summary>
    internal void RefreshAvailableCharts()
    {
        AvailableCharts.Clear();
        var existingIds = Charts.Select(c => c.ChartId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dc in _cardDeclaration.Charts.Where(dc => !existingIds.Contains(dc.ChartId)))
            AvailableCharts.Add(dc);
    }

    /// <summary>保存当前账号配置（委托给 owner）。</summary>
    internal void Save() => _owner.SaveAccountConfig(this);

    /// <summary>获取 ConfigService 实例。</summary>
    private ConfigService GetConfigService() => _owner.GetConfigServiceInternal();

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// S2 二级节点：图表（拖拽手柄 + 启用 CheckBox + 图表名 + 展开按钮）。
/// <para>展开后显示数据组列表与 tooltip 字段多选。</para>
/// </summary>
public class ChartNode : INotifyPropertyChanged
{
    private readonly AccountNode _parent;
    private bool _isEnabled;
    private bool _isExpanded;

    /// <summary>图表声明 ID。</summary>
    public string ChartId { get; }

    /// <summary>图表显示名（从 ChartId 提取简短名称）。</summary>
    public string DisplayName { get; }

    /// <summary>图表类型描述。</summary>
    public string KindText { get; }

    /// <summary>是否启用（绑定 CheckBox）。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
                _parent.Save();
            }
        }
    }

    /// <summary>是否展开（显示数据组 + tooltip 字段）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandArrow)); } }
    }

    /// <summary>展开箭头字符。</summary>
    public string ExpandArrow => _isExpanded ? "▾" : "▸";

    /// <summary>数据组节点集合。</summary>
    public ObservableCollection<DataGroupNode> DataGroups { get; } = new();

    /// <summary>Tooltip 字段多选项集合（S5 融入）。</summary>
    public ObservableCollection<TooltipFieldItem> TooltipFields { get; } = new();

    /// <summary>创建图表节点并初始化数据组与 tooltip 字段。</summary>
    public ChartNode(AccountNode parent, ChartDeclaration declaration, bool isVisible, AccountCustomization eff)
    {
        _parent = parent;
        ChartId = declaration.ChartId;
        _isEnabled = isVisible;
        DisplayName = ExtractChartName(declaration.ChartId);
        KindText = declaration.Kind.ToString();

        // 初始化数据组
        var visibleGroups = eff.VisibleDataGroups.TryGetValue(ChartId, out var vg) ? vg : null;
        var groupOrders = eff.DataGroupOrders.TryGetValue(ChartId, out var go) ? go : null;

        var orderedGroups = declaration.DataGroups
            .OrderBy(g => groupOrders != null && groupOrders.TryGetValue(g.Id, out var o) ? o : int.MaxValue)
            .ToList();

        foreach (var dg in orderedGroups)
        {
            bool groupVisible = visibleGroups == null || visibleGroups.Contains(dg.Id);
            DataGroups.Add(new DataGroupNode(this, dg, groupVisible));
        }

        // 初始化 tooltip 字段（S5）
        var savedFields = eff.VisibleTooltipFields.TryGetValue(ChartId, out var tf) ? tf : null;
        foreach (var option in TooltipFieldCatalog.CommonFields)
        {
            bool isChecked = savedFields == null
                ? declaration.Tooltip?.Fields?.Contains(option.FieldName) == true
                : savedFields.Contains(option.FieldName);
            TooltipFields.Add(new TooltipFieldItem(this, option.FieldName, option.Display, isChecked));
        }
    }

    /// <summary>从 ChartId 提取简短显示名（去掉 Provider 前缀）。</summary>
    private static string ExtractChartName(string chartId)
    {
        // 形如 "mm.chart.usage_bar" → "usage_bar"
        var parts = chartId.Split('.');
        return parts.Length > 2 ? string.Join(".", parts.Skip(2)) : chartId;
    }

    /// <summary>通知父账号保存配置。</summary>
    internal void NotifyChanged() => _parent.Save();

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// S2 三级节点：数据组（启用 CheckBox + 数据组名 + 字段 chips + 删除按钮）。
/// </summary>
public class DataGroupNode : INotifyPropertyChanged
{
    private readonly ChartNode _parent;
    private bool _isEnabled;

    /// <summary>数据组 ID。</summary>
    public string GroupId { get; }

    /// <summary>数据组显示名（从 ID 提取）。</summary>
    public string DisplayName { get; }

    /// <summary>字段标签列表（只读 chips）。</summary>
    public IReadOnlyList<string> FieldChips { get; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
                _parent.NotifyChanged();
            }
        }
    }

    /// <summary>创建数据组节点。</summary>
    public DataGroupNode(ChartNode parent, DataGroup dataGroup, bool isVisible)
    {
        _parent = parent;
        GroupId = dataGroup.Id;
        _isEnabled = isVisible;
        DisplayName = ExtractGroupName(dataGroup.Id);
        FieldChips = dataGroup.Fields.Select(f => f.FieldName).ToList();
    }

    /// <summary>从数据组 ID 提取简短显示名。</summary>
    private static string ExtractGroupName(string groupId)
    {
        var parts = groupId.Split('.');
        return parts.Length > 2 ? string.Join(".", parts.Skip(2)) : groupId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// S5：Tooltip 字段多选项（每字段一个带 IsChecked 的 item VM，纯 MVVM 方式）。
/// </summary>
public class TooltipFieldItem : INotifyPropertyChanged
{
    private readonly ChartNode _parent;
    private bool _isChecked;

    /// <summary>SDK 字段名。</summary>
    public string FieldName { get; }

    /// <summary>中文显示名。</summary>
    public string Display { get; }

    /// <summary>是否选中（勾选即持久化）。</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                OnPropertyChanged();
                _parent.NotifyChanged();
            }
        }
    }

    /// <summary>创建 tooltip 字段选项。</summary>
    public TooltipFieldItem(ChartNode parent, string fieldName, string display, bool isChecked)
    {
        _parent = parent;
        FieldName = fieldName;
        Display = display;
        _isChecked = isChecked;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
