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

    /// <summary>重新加载所有已启用账号及其 mini 图表配置（页面切入时调用）。
    /// <para>req-110 P1-1 对齐：无账号的插件不出现在迷你图表管理页，不再回退隐形 default 节点。</para></summary>
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

                // 仅列出已启用且已配置凭据的账号；无账号/未配凭据的插件跳过（与卡片管理页对齐）。
                var accounts = _configService.GetAccounts(provider.ProviderId);
                foreach (var acct in accounts.Where(a => a.Enabled))
                {
                    var acctConfig = _configService.GetEffectiveAccountConfig(provider.ProviderId, acct.AccountId, provider);
                    if (!UsageMonitor.Core.Services.CredentialProbe.HasConfiguredCredential(
                            provider.ProviderId, acctConfig, acct.AccountId, provider.ConfigFields))
                        continue;
                    AccountNodes.Add(CreateAccountNode(provider, taskbar, acct.AccountId, acct));
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

    /// <summary>问题13：持久化任务栏迷你图表顺序——按 AccountNodes 当前顺序提取去重 ProviderId 列表
    /// 写入 <c>TaskbarMiniChartOrder</c> 并保存，触发 ConfigChanged → 任务栏按新顺序重建。</summary>
    internal void SaveMiniAccountOrder()
    {
        try
        {
            var orderedProviders = AccountNodes
                .Select(n => n.ProviderId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _configService.Settings.TaskbarMiniChartOrder = orderedProviders;
            _configService.Save();
        }
        catch (Exception ex)
        {
            FileLogger.Error("MiniChartManage", "保存任务栏迷你图表顺序失败", ex);
        }
    }

    /// <summary>保存指定账号的 mini 图表配置到 ConfigService（统一走 SetMiniChartConfiguration）。
    /// <para>仅持久化 Mini 专属字段：VisibleMiniCharts（启用列表，顺序即显示顺序）、
    /// VisibleMiniDataGroups、MiniDataGroupOrders、MiniTooltipFields（问题8）。</para>
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
                MiniTooltipFields = new Dictionary<string, List<string>?>(),
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

                // 问题8：Tooltip/文本显示字段（勾选即仅显示列表内字段，全取消 = 不显示）
                config.MiniTooltipFields[chart.ChartId] = chart.TooltipFields.Where(f => f.IsChecked).Select(f => f.FieldName).ToList();
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
    private int? _width;

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

    /// <summary>用户覆盖的迷你图表宽度（DIP，40-400；null = 用插件声明值/宿主默认 120）。变更即持久化。</summary>
    public int? Width
    {
        get => _width;
        set
        {
            var clamped = value.HasValue ? Math.Clamp(value.Value, 40, 400) : (int?)null;
            if (_width != clamped)
            {
                _width = clamped;
                OnPropertyChanged();
                _owner.GetConfigServiceInternal().SetMiniChartWidth(ProviderId, clamped);
            }
        }
    }

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
        // 加载用户覆盖宽度（TaskbarMiniChartConfig.Width；未配置时为 null）
        var cfgService = owner.GetConfigServiceInternal();
        _width = cfgService.Settings.TaskbarMiniChartConfigs.TryGetValue(providerId, out var cfg) ? cfg.Width : null;
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
/// <para>展开后显示数据组列表与 Tooltip 字段多选（问题8），与卡片管理页 <c>ChartNode</c> 同构。</para>
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

    /// <summary>问题8：Tooltip/文本显示字段多选项集合（固定 5 项目录，与卡片管理页 S5 同构）。</summary>
    public ObservableCollection<MiniTooltipFieldItem> TooltipFields { get; } = new();

    /// <summary>创建 mini 图表节点并初始化数据组。</summary>
    public MiniChartNode(MiniAccountNode parent, MiniChartDeclaration declaration, bool isVisible, AccountCustomization eff)
    {
        _parent = parent;
        ChartId = declaration.ChartId;
        _isEnabled = isVisible;
        // 问题6：优先插件声明的中文 display，回退 chartId 尾段；类型描述用中文映射。
        DisplayName = !string.IsNullOrWhiteSpace(declaration.Display) ? declaration.Display! : ExtractChartName(declaration.ChartId);
        KindText = KindToChinese(declaration.Kind);

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

        // 问题12：迷你文本图表追加「刷新倒计时」虚拟数据组（非插件声明字段，仅控制文本段显示）。
        if (declaration.Kind == DeclarativeChartKind.MiniText)
        {
            var countdownGroup = new DataGroup
            {
                Id = UsageMonitor.App.Helpers.MiniTooltipFieldCatalog.RefreshCountdownVirtual,
                Display = "刷新倒计时",
            };
            bool countdownVisible = visibleGroups == null || visibleGroups.Contains(countdownGroup.Id);
            var countdownNode = new MiniDataGroupNode(this, countdownGroup, countdownVisible);
            // 按已保存排序插入（无排序时追加到末尾）
            if (groupOrders != null && groupOrders.TryGetValue(countdownGroup.Id, out var co) && co >= 0 && co < DataGroups.Count)
                DataGroups.Insert(co, countdownNode);
            else
                DataGroups.Add(countdownNode);
        }

        // 问题8：初始化 Tooltip/文本字段多选（用户配置优先，回退声明的 tooltip.fields）
        // 问题9：按用户保存的字段顺序排列（已勾选在前保持保存顺序，未勾选按目录顺序追加）。
        var savedFields = eff.MiniTooltipFields.TryGetValue(ChartId, out var sf) ? sf : null;
        var catalogOptions = UsageMonitor.App.Helpers.MiniTooltipFieldCatalog.GetOptions();
        var orderedOptions = savedFields == null
            ? (IEnumerable<UsageMonitor.App.Helpers.MiniTooltipFieldCatalog.MiniTooltipFieldOption>)catalogOptions
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
            TooltipFields.Add(new MiniTooltipFieldItem(this, option.FieldName, option.Display, isChecked));
        }
    }

    /// <summary>从 ChartId 提取简短显示名（去掉 Provider 前缀）。</summary>
    private static string ExtractChartName(string chartId)
    {
        // 形如 "mm.mini.ring" → "ring"
        var parts = chartId.Split('.');
        return parts.Length > 2 ? string.Join(".", parts.Skip(2)) : chartId;
    }

    /// <summary>问题6：迷你图表类型中文映射（与卡片管理页 KindToChinese 同构，设置界面展示用）。</summary>
    private static string KindToChinese(DeclarativeChartKind kind) => kind switch
    {
        DeclarativeChartKind.MiniRingChart => "迷你圆环",
        DeclarativeChartKind.MiniText => "迷你文本",
        DeclarativeChartKind.MiniLineChart => "迷你折线图",
        DeclarativeChartKind.MiniBarChart => "迷你柱状图",
        DeclarativeChartKind.MiniAreaChart => "迷你面积图",
        DeclarativeChartKind.Ring => "环形图",
        DeclarativeChartKind.Bar => "进度条",
        DeclarativeChartKind.Line => "折线图",
        DeclarativeChartKind.HeatMap => "热力图",
        DeclarativeChartKind.Number => "数字",
        _ => kind.ToString()
    };

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
        // 问题6：优先插件声明的中文 display，回退从组 ID 提取；字段 chips 显示中文标签。
        DisplayName = !string.IsNullOrWhiteSpace(dataGroup.Display) ? dataGroup.Display! : ExtractGroupName(dataGroup.Id);
        FieldChips = dataGroup.Fields.Select(f => UsageMonitor.App.Helpers.TooltipFieldCatalog.GetDisplay(f.FieldName)).ToList();
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
/// 问题8：Mini 图表 Tooltip/文本字段多选项（每字段一个带 IsChecked 的 item VM，勾选即持久化）。
/// <para>与卡片管理页 <c>TooltipFieldItem</c> 同构，父节点为 <see cref="MiniChartNode"/>。</para>
/// </summary>
public class MiniTooltipFieldItem : INotifyPropertyChanged
{
    private readonly MiniChartNode _parent;
    private bool _isChecked;

    /// <summary>字段名（SDK 字段或虚拟字段）。</summary>
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

    /// <summary>创建 Mini tooltip 字段选项。</summary>
    public MiniTooltipFieldItem(MiniChartNode parent, string fieldName, string display, bool isChecked)
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
