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
/// <para>图表列表为「图表实例 + 折叠分界线」的混合有序列表：允许同一声明图表添加多个实例（chartId#n），
/// 并可通过拖拽分界线调整折叠时保留可见的图表范围。</para>
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

    /// <summary>重新加载所有已启用账号及其图表配置（页面切入时调用）。
    /// <para>req-110 P1-1 对齐：卡片严格跟随账号生命周期——无账号的插件不出现在卡片管理页
    /// （与主窗口 DisplayModule.BuildCardTuples “无账号→不显示卡片”行为一致），不再回退隐形 default 节点。</para></summary>
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

                // 仅列出已启用且已配置凭据的账号；无账号/未配凭据的插件跳过
                // （未设置账号前不应出现在卡片管理，与主窗口“无账号→不显示卡片”行为对齐）。
                var accounts = _configService.GetAccounts(provider.ProviderId);
                foreach (var acct in accounts.Where(a => a.Enabled))
                {
                    var acctConfig = _configService.GetEffectiveAccountConfig(provider.ProviderId, acct.AccountId, provider);
                    if (!UsageMonitor.Core.Services.CredentialProbe.HasConfiguredCredential(
                            provider.ProviderId, acctConfig, acct.AccountId, provider.ConfigFields))
                        continue;
                    AccountNodes.Add(CreateAccountNode(provider, card, acct.AccountId, acct));
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

    /// <summary>问题11：持久化账号（卡片）顺序——按 AccountNodes 当前顺序提取去重 ProviderId 列表
    /// 写入 <c>ProviderCardOrder</c> 并保存，触发 ConfigChanged → 主窗口按新顺序重建卡片。</summary>
    internal void SaveAccountOrder()
    {
        try
        {
            var orderedProviders = AccountNodes
                .Select(n => n.ProviderId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
#pragma warning disable CS0618 // ProviderCardOrder 标记过时但仍是主窗口卡片排序的落点
            _configService.Settings.ProviderCardOrder = orderedProviders;
#pragma warning restore CS0618
            _configService.Save();
        }
        catch (Exception ex)
        {
            FileLogger.Error("CardManage", "保存账号（卡片）顺序失败", ex);
        }
    }

    /// <summary>
    /// 保存指定账号的图表配置到 ConfigService（统一走 SetCardChartConfiguration）。
    /// <para>遍历混合列表（图表实例 + 分界线）：勾选的图表实例按序写入 VisibleCharts（实例 ID），
    /// 数据组/排序/tooltip 字段按实例 ID 落盘，分界线位置折算为「折叠时保留可见的勾选图表数量」。</para>
    /// </summary>
    internal void SaveAccountConfig(AccountNode accountNode)
    {
        try
        {
            var config = new AccountCustomization
            {
                VisibleCharts = new List<string>(),
                ChartOrders = new Dictionary<string, int>(),
                VisibleDataGroups = new Dictionary<string, List<string>?>(),
                DataGroupOrders = new Dictionary<string, Dictionary<string, int>>(),
                VisibleTooltipFields = new Dictionary<string, List<string>?>(),
                CurrentDataGroupIds = new Dictionary<string, string>(),
            };

            var order = 0;
            var checkedBeforeDivider = 0;
            var dividerSeen = false;
            foreach (var item in accountNode.ChartItems)
            {
                if (item is DividerNode) { dividerSeen = true; continue; }
                if (item is not ChartNode chart) continue;

                if (chart.IsEnabled)
                {
                    config.VisibleCharts.Add(chart.InstanceId);
                    config.ChartOrders[chart.InstanceId] = order++;
                    if (!dividerSeen) checkedBeforeDivider++;
                }

                // 数据组可见性与排序（按实例 ID 落盘，支持同图表多实例独立配置）
                config.VisibleDataGroups[chart.InstanceId] = chart.DataGroups.Where(g => g.IsEnabled).Select(g => g.GroupId).ToList();
                var groupOrders = new Dictionary<string, int>();
                for (var j = 0; j < chart.DataGroups.Count; j++)
                    groupOrders[chart.DataGroups[j].GroupId] = j;
                config.DataGroupOrders[chart.InstanceId] = groupOrders;

                // tooltip 字段
                config.VisibleTooltipFields[chart.InstanceId] = chart.TooltipFields.Where(f => f.IsChecked).Select(f => f.FieldName).ToList();

                // req-115：色阶来源（仅对支持色阶的图表落盘；默认全局源也写入，保持显式语义）
                if (chart.SupportsColorTiers && !string.IsNullOrEmpty(chart.SelectedTierSource))
                    config.ChartColorTierSources[chart.InstanceId] = chart.SelectedTierSource;
            }

            config.CollapseDividerIndex = checkedBeforeDivider;
            _configService.SetCardChartConfiguration(accountNode.ProviderId, config, accountNode.AccountId);
            FileLogger.Info("CardManage", $"已保存账号配置：{accountNode.ProviderId}:{accountNode.AccountId}，分界线={checkedBeforeDivider}");
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
/// 卡片管理图表列表项基类：图表实例节点（<see cref="ChartNode"/>）与折叠分界线节点（<see cref="DividerNode"/>）的公共父类，
/// 使二者可共存于同一有序集合（<see cref="AccountNode.ChartItems"/>）并统一拖拽排序。
/// </summary>
public abstract class CardChartListItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>属性变更通知（供派生类调用）。</summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 折叠分界线节点：卡片管理图表列表中的可拖拽分界线。
/// <para>分界线之上的图表在用量卡片折叠时保持可见，之下则隐藏。每个账号列表中有且仅有一个分界线节点。</para>
/// </summary>
public class DividerNode : CardChartListItem
{
    /// <summary>固定显示文案。</summary>
    public string Label => "折叠分界线";
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

    /// <summary>该账号下的图表列表项混合集合（图表实例节点 + 唯一分界线节点，有序）。</summary>
    public ObservableCollection<CardChartListItem> ChartItems { get; } = new();

    /// <summary>该账号下的图表实例节点（从混合集合中筛出，供保存/统计使用）。</summary>
    public IReadOnlyList<ChartNode> Charts => ChartItems.OfType<ChartNode>().ToList();

    /// <summary>该账号唯一的折叠分界线节点。</summary>
    public DividerNode Divider { get; } = new();

    /// <summary>是否展开（折叠/展开图表列表）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandArrow)); } }
    }

    /// <summary>展开箭头字符。</summary>
    public string ExpandArrow => _isExpanded ? "▾" : "▸";

    /// <summary>是否无图表（控制"配置图表"按钮显示）。</summary>
    public bool HasNoCharts => !ChartItems.OfType<ChartNode>().Any();

    /// <summary>可添加的图表列表（声明图表全集，允许重复添加多实例）。</summary>
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
        RemoveChartCommand = new RelayCommand<ChartNode>(RemoveChart);
        RestoreDefaultChartsCommand = new RelayCommand(RestoreDefaultCharts);
    }

    /// <summary>添加图表命令（从 Popup 列表选择）。</summary>
    public IRelayCommand<ChartDeclaration> AddChartCommand { get; }

    /// <summary>删除图表实例命令（图表行右侧 × 按钮）。</summary>
    public IRelayCommand<ChartNode> RemoveChartCommand { get; }

    /// <summary>恢复默认图表集命令（账号无图表时显示）。</summary>
    public IRelayCommand RestoreDefaultChartsCommand { get; }

    /// <summary>从插件声明 + AccountCustomization 合并加载图表列表（含分界线定位）。</summary>
    public void LoadCharts()
    {
        ChartItems.Clear();
        try
        {
            var configService = GetConfigService();
            var eff = configService.GetEffectiveAccountCustomization(ProviderId, AccountId);
            var declaredCharts = _cardDeclaration.Charts;
            var declaredById = declaredCharts.ToDictionary(c => c.ChartId, c => c);

            // 1) 用户配置的可见实例（有序，勾选态）；旧兼容层可能填入 CardChartKind 名，不匹配 chartId 时跳过。
            var orderedInstances = new List<string>();
            if (eff.VisibleCharts != null)
            {
                foreach (var inst in eff.VisibleCharts)
                {
                    if (string.IsNullOrEmpty(inst)) continue;
                    if (declaredById.ContainsKey(StripSuffix(inst)) && !orderedInstances.Contains(inst))
                        orderedInstances.Add(inst);
                }
            }
            var configured = orderedInstances.Count > 0;
            if (!configured)
            {
                // 未配置：按声明默认顺序全部勾选
                foreach (var c in declaredCharts.OrderBy(c => c.DefaultOrder))
                    orderedInstances.Add(c.ChartId);
            }

            foreach (var inst in orderedInstances)
            {
                var decl = declaredById[StripSuffix(inst)];
                ChartItems.Add(new ChartNode(this, decl, inst, true, eff));
            }

            // 2) 声明中未被任何实例代表的图表 → 追加为未勾选节点（便于重新启用）
            if (configured)
            {
                var representedBases = orderedInstances.Select(StripSuffix).ToHashSet(StringComparer.Ordinal);
                foreach (var dc in declaredCharts.Where(dc => !representedBases.Contains(dc.ChartId)))
                    ChartItems.Add(new ChartNode(this, dc, dc.ChartId, false, eff));
            }

            // 3) 分界线定位：用户配置 > 插件声明 > 列表末尾
            var chartCount = ChartItems.Count;
            var dividerIndex = eff.CollapseDividerIndex ?? _cardDeclaration.CollapseDividerIndex ?? chartCount;
            if (dividerIndex < 0) dividerIndex = 0;
            if (dividerIndex > chartCount) dividerIndex = chartCount;
            ChartItems.Insert(dividerIndex, Divider);

            RefreshAvailableCharts();
        }
        catch (Exception ex)
        {
            FileLogger.Error("CardManage", $"加载图表列表失败：{ProviderId}:{AccountId}", ex);
        }
        OnPropertyChanged(nameof(HasNoCharts));
    }

    /// <summary>添加图表实例（允许同一声明图表重复添加，自动生成 chartId#n 实例 ID，插入到分界线之前）。</summary>
    private void AddChart(ChartDeclaration? chartDecl)
    {
        if (chartDecl == null) return;

        var configService = GetConfigService();
        var eff = configService.GetEffectiveAccountCustomization(ProviderId, AccountId);
        var instanceId = NextInstanceId(chartDecl.ChartId);
        var chartNode = new ChartNode(this, chartDecl, instanceId, true, eff);

        // 新实例插入到分界线之前（若存在），否则追加到末尾
        var dividerIdx = ChartItems.IndexOf(Divider);
        if (dividerIdx >= 0) ChartItems.Insert(dividerIdx, chartNode);
        else ChartItems.Add(chartNode);

        OnPropertyChanged(nameof(HasNoCharts));
        Save();
    }

    /// <summary>删除图表实例节点（从混合列表移除并保存；仅针对图表节点，分界线不受影响）。</summary>
    private void RemoveChart(ChartNode? chartNode)
    {
        if (chartNode == null || !ChartItems.Contains(chartNode)) return;
        ChartItems.Remove(chartNode);
        OnPropertyChanged(nameof(HasNoCharts));
        Save();
    }

    /// <summary>生成下一个图表实例 ID：首个实例为 chartId，其后为 chartId#2、chartId#3…。</summary>
    private string NextInstanceId(string chartId)
    {
        var existing = ChartItems.OfType<ChartNode>().Count(c => string.Equals(c.ChartId, chartId, StringComparison.Ordinal));
        return existing == 0 ? chartId : $"{chartId}#{existing + 1}";
    }

    /// <summary>恢复默认图表集（按插件声明恢复，分界线回到声明位置/末尾）。</summary>
    private void RestoreDefaultCharts()
    {
        ChartItems.Clear();
        var configService = GetConfigService();
        var eff = configService.GetEffectiveAccountCustomization(ProviderId, AccountId);
        foreach (var chartDecl in _cardDeclaration.Charts.OrderBy(c => c.DefaultOrder))
            ChartItems.Add(new ChartNode(this, chartDecl, chartDecl.ChartId, true, eff));

        var dividerIndex = _cardDeclaration.CollapseDividerIndex ?? ChartItems.Count;
        if (dividerIndex > ChartItems.Count) dividerIndex = ChartItems.Count;
        ChartItems.Insert(dividerIndex, Divider);

        RefreshAvailableCharts();
        OnPropertyChanged(nameof(HasNoCharts));
        Save();
    }

    /// <summary>刷新可添加图表列表（声明图表全集，允许重复添加多实例）。</summary>
    internal void RefreshAvailableCharts()
    {
        AvailableCharts.Clear();
        foreach (var dc in _cardDeclaration.Charts)
            AvailableCharts.Add(dc);
    }

    /// <summary>去除图表实例 ID 的 #n 后缀，返回基础 chartId。</summary>
    private static string StripSuffix(string instanceId)
    {
        var idx = instanceId.LastIndexOf('#');
        return idx > 0 ? instanceId.Substring(0, idx) : instanceId;
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
/// S2 二级节点：图表实例（拖拽手柄 + 启用 CheckBox + 图表名 + 展开按钮）。
/// <para>展开后显示数据组列表与 tooltip 字段多选。同一声明图表可存在多个实例（<see cref="InstanceId"/> 带 #n 后缀）。</para>
/// </summary>
public class ChartNode : CardChartListItem
{
    private readonly AccountNode _parent;
    private bool _isEnabled;
    private bool _isExpanded;

    /// <summary>图表实例 ID（首个实例等于 chartId，追加实例为 chartId#n）。</summary>
    public string InstanceId { get; }

    /// <summary>图表声明 ID（基础 chartId，去除 #n 后缀）。</summary>
    public string ChartId { get; }

    /// <summary>图表显示名（从 ChartId 提取简短名称；多实例时附加序号）。</summary>
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

    /// <summary>删除本图表实例的命令（图表行 × 按钮，委托父账号 RemoveChartCommand）。</summary>
    public IRelayCommand RemoveSelfCommand { get; }

    /// <summary>数据组节点集合。</summary>
    public ObservableCollection<DataGroupNode> DataGroups { get; } = new();

    /// <summary>Tooltip 字段多选项集合（S5 融入）。</summary>
    public ObservableCollection<TooltipFieldItem> TooltipFields { get; } = new();

    /// <summary>req-115：图表类型是否支持色阶（控制色阶来源下拉可见性，按 ChartKindSpec 能力表）。</summary>
    public bool SupportsColorTiers { get; }

    /// <summary>req-115：色阶来源选项（全局色阶 + 已装 charts/ 图表样式包，Id 为 pack:&lt;packId&gt; 形态）。</summary>
    public IReadOnlyList<DisplayPackOption> TierSourceOptions { get; }

    private string _selectedTierSource;

    /// <summary>req-115：当前色阶来源（"global:usage-tier-default" 或 "pack:&lt;packId&gt;"，选中即持久化并触发卡片重取色）。</summary>
    public string SelectedTierSource
    {
        get => _selectedTierSource;
        set
        {
            var v = string.IsNullOrEmpty(value) ? GlobalTierSourceKey : value;
            if (!string.Equals(_selectedTierSource, v, StringComparison.OrdinalIgnoreCase))
            {
                _selectedTierSource = v;
                OnPropertyChanged();
                _parent.Save();
            }
        }
    }

    /// <summary>req-115：全局色阶源键（与插件声明 colorTiers.ref 的全局引用同名）。</summary>
    internal const string GlobalTierSourceKey = "global:usage-tier-default";

    /// <summary>创建图表实例节点并初始化数据组与 tooltip 字段。</summary>
    public ChartNode(AccountNode parent, ChartDeclaration declaration, string instanceId, bool isVisible, AccountCustomization eff)
    {
        _parent = parent;
        InstanceId = instanceId;
        ChartId = declaration.ChartId;
        _isEnabled = isVisible;
        DisplayName = BuildDisplayName(declaration.ChartId, instanceId, declaration.Display);
        KindText = KindToChinese(declaration.Kind);
        RemoveSelfCommand = new RelayCommand(() => _parent.RemoveChartCommand.Execute(this));

        // req-115：色阶来源初始化——仅支持色阶的图表类型展示下拉；已保存源（实例级优先回退图表级）缺省为全局色阶
        SupportsColorTiers = ChartKindSpecRegistry.GetSpec(declaration.Kind)?.SupportsColorTiers == true;
        TierSourceOptions = BuildTierSourceOptions();
        var savedSource = ResolveTierSource(eff.ChartColorTierSources, instanceId, declaration.ChartId);
        _selectedTierSource = !string.IsNullOrEmpty(savedSource)
            && TierSourceOptions.Any(o => string.Equals(o.Id, savedSource, StringComparison.OrdinalIgnoreCase))
            ? savedSource!
            : GlobalTierSourceKey;

        // 初始化数据组（实例级配置优先，回退图表级）
        var visibleGroups = ResolveGroups(eff.VisibleDataGroups, instanceId, declaration.ChartId);
        var groupOrders = ResolveOrders(eff.DataGroupOrders, instanceId, declaration.ChartId);

        var orderedGroups = declaration.DataGroups
            .OrderBy(g => groupOrders != null && groupOrders.TryGetValue(g.Id, out var o) ? o : int.MaxValue)
            .ToList();

        foreach (var dg in orderedGroups)
        {
            bool groupVisible = visibleGroups == null || visibleGroups.Contains(dg.Id);
            DataGroups.Add(new DataGroupNode(this, dg, groupVisible));
        }

        // 初始化 tooltip 字段（按图表声明的数据组派生可选项，避免列出当前图表不涉及的字段；实例级配置优先，回退图表级，再回退声明）
        // 问题9：按用户保存的字段顺序排列（已勾选字段在前且保持保存顺序，未勾选按目录顺序追加），拖拽排序重进页面后不丢失。
        var savedFields = ResolveTooltipFields(eff, instanceId, declaration.ChartId);
        var catalogOptions = TooltipFieldCatalog.GetFieldsForChart(declaration);
        var orderedOptions = savedFields == null
            ? (IEnumerable<TooltipFieldCatalog.TooltipFieldOption>)catalogOptions
            : catalogOptions.OrderBy(o =>
            {
                var idx = savedFields.FindIndex(f => string.Equals(f, o.FieldName, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? idx : int.MaxValue;
            });
        foreach (var option in orderedOptions)
        {
            bool isChecked = savedFields == null
                ? declaration.Tooltip?.Fields?.Contains(option.FieldName) == true
                : savedFields.Contains(option.FieldName);
            TooltipFields.Add(new TooltipFieldItem(this, option.FieldName, option.Display, isChecked));
        }
    }

    /// <summary>构建图表显示名（优先声明的中文 display，回退 chartId 提取名；多实例附加序号标记）。</summary>
    private static string BuildDisplayName(string chartId, string instanceId, string? display)
    {
        var baseName = !string.IsNullOrWhiteSpace(display) ? display! : ExtractChartName(chartId);
        return string.Equals(instanceId, chartId, StringComparison.Ordinal)
            ? baseName
            : $"{baseName}（{instanceId.Substring(chartId.Length + 1)}）";
    }

    /// <summary>图表类型中文映射（设置界面展示用）。</summary>
    private static string KindToChinese(DeclarativeChartKind kind) => kind switch
    {
        DeclarativeChartKind.Bar => "进度条",
        DeclarativeChartKind.Line => "折线图",
        DeclarativeChartKind.HeatMap => "热力图",
        DeclarativeChartKind.Number => "数字",
        DeclarativeChartKind.Ring => "环形图",
        _ => kind.ToString()
    };

    /// <summary>解析实例级/图表级数据组可见性配置（实例级优先）。</summary>
    private static List<string>? ResolveGroups(Dictionary<string, List<string>?> dict, string instanceId, string chartId)
    {
        if (!string.Equals(instanceId, chartId, StringComparison.Ordinal) && dict.TryGetValue(instanceId, out var inst))
            return inst;
        return dict.TryGetValue(chartId, out var chart) ? chart : null;
    }

    /// <summary>解析实例级/图表级数据组排序配置（实例级优先）。</summary>
    private static Dictionary<string, int>? ResolveOrders(Dictionary<string, Dictionary<string, int>> dict, string instanceId, string chartId)
    {
        if (!string.Equals(instanceId, chartId, StringComparison.Ordinal) && dict.TryGetValue(instanceId, out var inst))
            return inst;
        return dict.TryGetValue(chartId, out var chart) ? chart : null;
    }

    /// <summary>解析实例级/图表级 tooltip 字段配置（实例级优先）。</summary>
    private static List<string>? ResolveTooltipFields(AccountCustomization eff, string instanceId, string chartId)
    {
        if (!string.Equals(instanceId, chartId, StringComparison.Ordinal) && eff.VisibleTooltipFields.TryGetValue(instanceId, out var inst))
            return inst;
        return eff.VisibleTooltipFields.TryGetValue(chartId, out var chart) ? chart : null;
    }

    /// <summary>req-115：解析实例级/图表级色阶来源配置（实例级优先）。</summary>
    private static string? ResolveTierSource(Dictionary<string, string> dict, string instanceId, string chartId)
    {
        if (!string.Equals(instanceId, chartId, StringComparison.Ordinal) && dict.TryGetValue(instanceId, out var inst))
            return inst;
        return dict.TryGetValue(chartId, out var chart) ? chart : null;
    }

    /// <summary>req-115：构建色阶来源选项：全局色阶 + 已装 charts/ 图表样式包（pack:&lt;packId&gt;）。
    /// <para>单测环境无 App 实例时仅剩全局选项（防御性 null 保护）。</para></summary>
    private static List<DisplayPackOption> BuildTierSourceOptions()
    {
        var options = new List<DisplayPackOption> { new(GlobalTierSourceKey, "全局色阶") };
        try
        {
            var registry = (System.Windows.Application.Current as UsageMonitor.App.App)?.DisplayPacks;
            if (registry != null)
            {
                foreach (var pack in registry.ChartStylePacks)
                    options.Add(new DisplayPackOption($"pack:{pack.Id}", pack.EffectiveDisplayName));
            }
        }
        catch { /* 包列举失败不影响页面加载 */ }
        return options;
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
        DisplayName = !string.IsNullOrWhiteSpace(dataGroup.Display) ? dataGroup.Display! : ExtractGroupName(dataGroup.Id);
        FieldChips = dataGroup.Fields.Select(f => FieldChineseLabel(f.FieldName)).ToList();
    }

    /// <summary>解析字段中文标签（取 SDK 元数据描述并去除括号补充说明；缺失时回退字段名）。</summary>
    private static string FieldChineseLabel(string fieldName)
    {
        var meta = UsageFieldMetadataRegistry.Get(fieldName);
        if (meta == null || string.IsNullOrWhiteSpace(meta.Description)) return fieldName;
        var desc = meta.Description;
        var idx = desc.IndexOfAny(new[] { '（', '(' });
        if (idx > 0) desc = desc.Substring(0, idx);
        return desc.Trim();
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
