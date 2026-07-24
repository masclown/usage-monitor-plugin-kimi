using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using UsageMonitor.App.Helpers;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// S4：任务栏迷你图表页 ViewModel（三级折叠：账号 → mini 图表 → 数据组）。
/// <para>与 <see cref="CardManageViewModel"/> 同构：跨 Provider 列出所有已启用账号，
/// 每个账号展开后显示 mini 图表列表（插件声明 <c>Taskbar.MiniCharts</c> + AccountCustomization 覆盖合并），
/// 图表展开后显示数据组。持久化统一走 <see cref="ConfigService.SetMiniChartConfiguration"/>。</para>
/// </summary>
public class MiniChartManageViewModel : INotifyPropertyChanged
{
    private readonly PluginManager _pluginManager;
    private readonly ConfigService _configService;

    /// <summary>所有已启用账号的节点集合（跨 Provider）。</summary>
    public ObservableCollection<MiniAccountNode> AccountNodes { get; } = new();

    /// <summary>创建任务栏迷你图表管理 ViewModel 并加载账号列表。</summary>
    public MiniChartManageViewModel(PluginManager pluginManager, ConfigService configService)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        Reload();
    }

    /// <summary>重新加载所有已启用账号及其 mini 图表配置（页面切入时调用）。</summary>
    public void Reload()
    {
        AccountNodes.Clear();
        try
        {
            foreach (var plugin in _pluginManager.Plugins)
            {
                var provider = plugin.Provider;
                var taskbar = provider.Taskbar;
                // 仅处理声明了 mini 图表的 Provider（与卡片管理页对齐：无声明者跳过）
                if (taskbar == null || taskbar.MiniCharts.Count == 0) continue;

                var accounts = _configService.GetAccounts(provider.ProviderId);
                if (accounts.Count == 0)
                {
                    // 无显式账号时回退 "default"
                    AccountNodes.Add(CreateAccountNode(provider, taskbar, "default", null));
                }
                else
                {
                    foreach (var acct in accounts.Where(a => a.Enabled))
                    {
                        AccountNodes.Add(CreateAccountNode(provider, taskbar, acct.AccountId, acct));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("MiniChartManage", "加载账号列表失败", ex);
        }
    }

    /// <summary>为指定 Provider + 账号创建账号节点（含 mini 图表子节点构建）。</summary>
    private MiniAccountNode CreateAccountNode(IUsageProvider provider, TaskbarDeclaration taskbar, string accountId, Account? account)
    {
        var displayName = account?.UseNickname == true && !string.IsNullOrWhiteSpace(account.Nickname)
            ? account.Nickname!
            : provider.DisplayName ?? provider.ProviderId;

        var node = new MiniAccountNode(this, provider.ProviderId, accountId, displayName,
            ProviderUsageViewModel.ResolveIconPath(provider.ProviderId), taskbar);
        node.LoadMiniCharts();
        return node;
    }

    /// <summary>保存指定账号的 mini 图表配置到 ConfigService（统一走 SetMiniChartConfiguration）。
    /// <para>仅持久化 Mini 专属字段：VisibleMiniCharts（启用列表，顺序即显示顺序）、
    /// VisibleMiniDataGroups、MiniDataGroupOrders。</para>
    /// </summary>
    internal void SaveAccountConfig(MiniAccountNode accountNode)
    {
        try
        {
            var config = new AccountCustomization
            {
                // 启用的 mini 图表按当前显示顺序写入（列表顺序即任务栏显示顺序）
                VisibleMiniCharts = accountNode.MiniCharts.Where(c => c.IsEnabled).Select(c => c.ChartId).ToList(),
                VisibleMiniDataGroups = new Dictionary<string, List<string>?>(),
                MiniDataGroupOrders = new Dictionary<string, Dictionary<string, int>>(),
            };

            foreach (var chart in accountNode.MiniCharts)
            {
                // 数据组可见性（启用列表，顺序即显示顺序）
                var visibleGroups = chart.DataGroups.Where(g => g.IsEnabled).Select(g => g.GroupId).ToList();
                config.VisibleMiniDataGroups[chart.ChartId] = visibleGroups;

                // 数据组排序（全部数据组的当前序号）
                var groupOrders = new Dictionary<string, int>();
                for (int j = 0; j < chart.DataGroups.Count; j++)
                    groupOrders[chart.DataGroups[j].GroupId] = j;
                config.MiniDataGroupOrders[chart.ChartId] = groupOrders;
            }

            _configService.SetMiniChartConfiguration(accountNode.ProviderId, config, accountNode.AccountId);
            FileLogger.Info("MiniChartManage", $"已保存账号 Mini 图表配置：{accountNode.ProviderId}:{accountNode.AccountId}");
        }
        catch (Exception ex)
        {
            FileLogger.Error("MiniChartManage", $"保存账号 Mini 图表配置失败：{accountNode.ProviderId}:{accountNode.AccountId}", ex);
        }
    }

    /// <summary>内部暴露 ConfigService（供子节点读取配置）。</summary>
    internal ConfigService GetConfigServiceInternal() => _configService;

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// S4 一级节点：账号（跨 Provider 的已启用账号）。
/// <para>展开后显示该账号下的 mini 图表列表（插件声明默认 + AccountCustomization 覆盖合并）。</para>
/// </summary>
public class MiniAccountNode : INotifyPropertyChanged
{
    private readonly MiniChartManageViewModel _owner;
    private readonly TaskbarDeclaration _taskbarDeclaration;
    private bool _isExpanded;

    /// <summary>所属 Provider ID。</summary>
    public string ProviderId { get; }

    /// <summary>账号 ID。</summary>
    public string AccountId { get; }

    /// <summary>显示名（昵称优先，回退 Provider 名）。</summary>
    public string DisplayName { get; }

    /// <summary>Provider 图标路径（可能为 null）。</summary>
    public string? IconPath { get; }

    /// <summary>该账号下的 mini 图表节点集合。</summary>
    public ObservableCollection<MiniChartNode> MiniCharts { get; } = new();

    /// <summary>是否展开（折叠/展开 mini 图表列表）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandArrow)); } }
    }

    /// <summary>展开箭头字符。</summary>
    public string ExpandArrow => _isExpanded ? "▾" : "▸";

    /// <summary>创建账号节点。</summary>
    public MiniAccountNode(MiniChartManageViewModel owner, string providerId, string accountId,
        string displayName, string? iconPath, TaskbarDeclaration taskbarDeclaration)
    {
        _owner = owner;
        _taskbarDeclaration = taskbarDeclaration;
        ProviderId = providerId;
        AccountId = accountId;
        DisplayName = displayName;
        IconPath = iconPath;
    }

    /// <summary>从插件声明（Taskbar.MiniCharts）+ AccountCustomization 合并加载 mini 图表列表。</summary>
    public void LoadMiniCharts()
    {
        MiniCharts.Clear();
        try
        {
            var configService = _owner.GetConfigServiceInternal();
            var eff = configService.GetEffectiveAccountCustomization(ProviderId, AccountId);
            var declaredCharts = _taskbarDeclaration.MiniCharts;

            // 确定可见图表列表：用户配置优先（列表顺序即显示顺序），否则沿用声明顺序
            List<MiniChartDeclaration> orderedCharts;
            if (eff.VisibleMiniCharts != null && eff.VisibleMiniCharts.Count > 0)
            {
                // 按用户排序 + 可见性过滤
                orderedCharts = eff.VisibleMiniCharts
                    .Select(id => declaredCharts.FirstOrDefault(c => c.ChartId == id))
                    .Where(c => c != null)
                    .Cast<MiniChartDeclaration>()
                    .ToList();
                // 追加声明中有但用户列表未含的（设为不可见）
                foreach (var dc in declaredCharts.Where(dc => !eff.VisibleMiniCharts.Contains(dc.ChartId)))
                    orderedCharts.Add(dc);
            }
            else
            {
                // 沿用声明默认顺序
                orderedCharts = declaredCharts.ToList();
            }

            foreach (var chartDecl in orderedCharts)
            {
                bool isVisible = eff.VisibleMiniCharts == null || eff.VisibleMiniCharts.Contains(chartDecl.ChartId);
                MiniCharts.Add(new MiniChartNode(this, chartDecl, isVisible, eff));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("MiniChartManage", $"加载 mini 图表列表失败：{ProviderId}:{AccountId}", ex);
        }
    }

    /// <summary>保存当前账号配置（委托给 owner）。</summary>
    internal void Save() => _owner.SaveAccountConfig(this);

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// S4 二级节点：mini 图表（拖拽手柄 + 启用 CheckBox + 图表名 + 展开按钮）。
/// <para>展开后显示数据组列表。与卡片管理页 <c>ChartNode</c> 同构（无 tooltip 字段多选——Mini 配置不持久化 tooltip）。</para>
/// </summary>
public class MiniChartNode : INotifyPropertyChanged
{
    private readonly MiniAccountNode _parent;
    private bool _isEnabled;
    private bool _isExpanded;

    /// <summary>mini 图表声明 ID。</summary>
    public string ChartId { get; }

    /// <summary>图表显示名（从 ChartId 提取简短名称）。</summary>
    public string DisplayName { get; }

    /// <summary>图表类型描述。</summary>
    public string KindText { get; }

    /// <summary>是否启用（绑定 CheckBox；变更即持久化）。</summary>
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

    /// <summary>是否展开（显示数据组）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandArrow)); } }
    }

    /// <summary>展开箭头字符。</summary>
    public string ExpandArrow => _isExpanded ? "▾" : "▸";

    /// <summary>数据组节点集合。</summary>
    public ObservableCollection<MiniDataGroupNode> DataGroups { get; } = new();

    /// <summary>创建 mini 图表节点并初始化数据组。</summary>
    public MiniChartNode(MiniAccountNode parent, MiniChartDeclaration declaration, bool isVisible, AccountCustomization eff)
    {
        _parent = parent;
        ChartId = declaration.ChartId;
        _isEnabled = isVisible;
        DisplayName = ExtractChartName(declaration.ChartId);
        KindText = declaration.Kind.ToString();

        // 初始化数据组（可见性 + 排序从 Mini 专属字段读取）
        var visibleGroups = eff.VisibleMiniDataGroups.TryGetValue(ChartId, out var vg) ? vg : null;
        var groupOrders = eff.MiniDataGroupOrders.TryGetValue(ChartId, out var go) ? go : null;

        var orderedGroups = declaration.DataGroups
            .OrderBy(g => groupOrders != null && groupOrders.TryGetValue(g.Id, out var o) ? o : int.MaxValue)
            .ToList();

        foreach (var dg in orderedGroups)
        {
            bool groupVisible = visibleGroups == null || visibleGroups.Contains(dg.Id);
            DataGroups.Add(new MiniDataGroupNode(this, dg, groupVisible));
        }
    }

    /// <summary>从 ChartId 提取简短显示名（去掉 Provider 前缀）。</summary>
    private static string ExtractChartName(string chartId)
    {
        // 形如 "mm.mini.ring" → "ring"
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
/// S4 三级节点：数据组（启用 CheckBox + 数据组名 + 字段 chips）。与卡片管理页 <c>DataGroupNode</c> 同构。
/// </summary>
public class MiniDataGroupNode : INotifyPropertyChanged
{
    private readonly MiniChartNode _parent;
    private bool _isEnabled;

    /// <summary>数据组 ID。</summary>
    public string GroupId { get; }

    /// <summary>数据组显示名（从 ID 提取）。</summary>
    public string DisplayName { get; }

    /// <summary>字段标签列表（只读 chips）。</summary>
    public IReadOnlyList<string> FieldChips { get; }

    /// <summary>是否启用（变更即持久化）。</summary>
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
    public MiniDataGroupNode(MiniChartNode parent, DataGroup dataGroup, bool isVisible)
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
