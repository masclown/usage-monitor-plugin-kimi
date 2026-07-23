using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UsageMonitor.App.Helpers;
using UsageMonitor.App.ViewModels;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Modules;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Services.Display;

/// <summary>
/// req-099 B1：显示模块实现（App 层，因依赖 WPF 卡片 ViewModel 而不能放入 Core）。
/// <para>
/// 从 <c>MainViewModel</c> 抽离的"卡片集合装配 / 渲染路由 / 启用过滤 / 任务栏模式 / 图表顺序"
/// 逻辑集中于此，使 <c>MainViewModel</c> 收敛为协调者，通过 <see cref="IDisplayModule"/> 接口驱动显示。
/// </para>
/// <para>
/// 拥有三个卡片相关的可观察集合（主窗口、设置窗口、任务栏均可绑定）：
/// <see cref="Usages"/>（全量）/ <see cref="EnabledUsages"/>（已启用）/ <see cref="PluginItems"/>（插件项）。
/// </para>
/// </summary>
public sealed class DisplayModule : IDisplayModule
{
    private readonly PluginManager _pluginManager;
    private readonly ConfigService _configService;
    private readonly IRefreshService _refreshService;
    private readonly Action<string>? _reLoginHandler;

    /// <inheritdoc/>
    public event EventHandler? EnabledCardsChanged;

    /// <summary>各服务商的用量显示列表（全量，包含被禁用的项，用于切换时保留状态）。</summary>
    public ObservableCollection<ProviderUsageViewModel> Usages { get; } = new();

    /// <summary>仅展示已启用插件的用量卡片（主窗口 ItemsControl 实际绑定此集合）。</summary>
    public ObservableCollection<ProviderUsageViewModel> EnabledUsages { get; } = new();

    /// <summary>插件列表项集合（设置窗口"插件管理"绑定）。</summary>
    public ObservableCollection<PluginItemViewModel> PluginItems { get; } = new();

    /// <summary>
    /// 创建显示模块。
    /// </summary>
    /// <param name="pluginManager">插件管理器（提供已加载插件与启用状态）。</param>
    /// <param name="configService">配置服务（读取显示模式、卡片图表选择、卡片/图表顺序等）。</param>
    /// <param name="refreshService">刷新服务（卡片右上角"⟳ 刷新"按钮回调）。</param>
    /// <param name="reLoginHandler">
    /// 手动重新登录回调（传入 providerId）。为 null 时卡片隐藏"🔑 重新登录"按钮；
    /// 仅当插件声明了 <c>LoginConfig</c> 且本回调非空时才注入到卡片 VM。
    /// </param>
    public DisplayModule(PluginManager pluginManager, ConfigService configService,
        IRefreshService refreshService, Action<string>? reLoginHandler)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _refreshService = refreshService;
        _reLoginHandler = reLoginHandler;
    }

    /// <inheritdoc/>
    public void Build()
    {
        foreach (var plugin in _pluginManager.Plugins)
        {
            // req-109：枚举 (Account, Card) 二元组派生 N 张卡片（每 Provider 可有 N 个）。
            //   - 无 Accounts 配置 → 走单卡向后兼容路径（(default, default-card)）
            //   - 有 Accounts → 对每个账号取卡片列表，逐卡片创建 VM
            var accounts = _configService.GetAccounts(plugin.Provider.ProviderId);
            var cardTuples = accounts.Count > 0
                ? accounts.SelectMany(a => _configService.GetCards(plugin.Provider.ProviderId, a.AccountId)
                    .Select(c => (AccountId: a.AccountId, CardId: c.CardId))).ToList()
                : new List<(string AccountId, string CardId)> { ("default", "default-card") };

            // 读取已保存的显示模式与卡片图表多选（未配置时回退插件声明：req-107 B6 优先 Card.Charts）
            var savedMode = TaskbarModeResolver.Resolve(_configService.Settings, plugin.Provider.ProviderId);
            var savedCardCharts = _configService.GetProviderCardChartKinds(plugin.Provider.ProviderId);
            if (savedCardCharts.Count == 0)
                savedCardCharts = ChartKindExtractor.ExtractDeclaredChartKinds(plugin.Provider).ToList();

            // req-fix-DualModeProvider：装配时把当前 config 注入双模式 provider，让 ConfigFields getter 按 mode 返回字段。
            var currentConfig = _configService.GetProviderConfig(plugin.Provider.ProviderId, plugin.Provider);
            switch (plugin.Provider)
            {
                case UsageMonitor.Plugin.Kimi.KimiDualModeProvider kimiProvider:
                    kimiProvider.SetCurrentConfigSnapshot(currentConfig);
                    break;
                case UsageMonitor.Plugin.Deepseek.DeepseekDualModeProvider deepseekProvider:
                    deepseekProvider.SetCurrentConfigSnapshot(currentConfig);
                    break;
            }

            var item = new PluginItemViewModel(plugin.Provider, _configService)
            {
                ProviderId = plugin.Provider.ProviderId,
                DisplayName = plugin.Provider.DisplayName,
                Version = plugin.Provider.Version,
                Author = plugin.Provider.Author,
                Description = plugin.Provider.Description,
                IsEnabled = plugin.IsEnabled,
                DisplayMode = savedMode
            };
            item.InitCardChartKinds(savedCardCharts);
            // 双向同步：PluginItem 变更时同步到卡片 VM 与配置
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PluginItemViewModel.DisplayMode))
                {
                    ChangeTaskbarMode(item.ProviderId, item.DisplayMode);
                }
                else if (e.PropertyName == nameof(PluginItemViewModel.IsEnabled))
                {
                    SetPluginEnabled(item.ProviderId, item.IsEnabled);
                }
                else if (e.PropertyName == nameof(PluginItemViewModel.CardChartKinds))
                {
                    var targets = Usages.Where(u => u.ProviderId == item.ProviderId);
                    foreach (var t in targets) t.CardChartKinds = item.CardChartKinds;
                }
            };
            PluginItems.Add(item);

            // req-091-005：仅当 Provider 声明 LoginConfig 且宿主提供回调时注入重新登录动作。
            var providerId = plugin.Provider.ProviderId;
#pragma warning disable CS0618 // LoginConfig 已过时（req-096），此处仍用于判定是否显示重新登录按钮，保持向后兼容
            var supportsReLogin = plugin.Provider.LoginConfig != null;
#pragma warning restore CS0618
            Action? reLoginAction = supportsReLogin && _reLoginHandler != null
                ? () => _reLoginHandler(providerId)
                : null;

            // req-109：每个 (Account, Card) 二元组派生一个 ProviderUsageViewModel
            foreach (var (accountId, cardId) in cardTuples)
            {
                var usageVm = new ProviderUsageViewModel(
                    item.OpenConfigDialog,
                    () => _refreshService.RefreshProviderAsync(providerId),
                    reLoginAction,
                    accountId: accountId,
                    cardId: cardId)
                {
                    ProviderId = plugin.Provider.ProviderId,
                    DisplayName = plugin.Provider.DisplayName,
                    IconPath = ProviderUsageViewModel.ResolveIconPath(plugin.Provider.ProviderId),
                    IsEnabled = plugin.IsEnabled,
                    DisplayMode = savedMode,
                    CardChartKinds = savedCardCharts,
                    RenderKinds = plugin.Provider.DefaultRenderKinds,
                    CollapseVisibleParts = plugin.Provider.CollapseVisibleParts ?? Array.Empty<string>(),
                    // req-107 B8：SupportsPeriodSwitch / ExtraTooltipLines 接口成员已收敛为 [Obsolete]；
                    // 周期切换能力交由 Card.Line.Slicer(Period)、tooltip 扩展行交由 Card.Chart.Tooltip；VM 初始化不再从接口读取。
                    Provider = plugin.Provider,
                };
                usageVm.AttachConfigService(_configService);
                Usages.Add(usageVm);
            }
        }

        RebuildEnabledCards();
    }

    /// <inheritdoc/>
    public void RenderCard(UsageInfo data)
    {
        if (data == null) return;
        // req-109：当 UsageInfo.AccountId/CardId 填充时按 3 段路由；null 时回退到首匹配（向后兼容）。
        ProviderUsageViewModel? vm = (data.AccountId != null && data.CardId != null)
            ? Usages.FirstOrDefault(u =>
                string.Equals(u.ProviderId, data.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(u.AccountIdSafe, data.AccountId, StringComparison.Ordinal) &&
                string.Equals(u.CardIdSafe, data.CardId, StringComparison.Ordinal))
            : Usages.FirstOrDefault(u => string.Equals(u.ProviderId, data.ProviderId, StringComparison.OrdinalIgnoreCase));
        vm?.UpdateFromUsage(data);
    }

    /// <inheritdoc/>
    public void RenderCards(IReadOnlyList<UsageInfo> usages)
    {
        if (usages == null) return;
        foreach (var usage in usages)
            RenderCard(usage);
    }

    /// <inheritdoc/>
    public void RebuildEnabledCards()
    {
        EnabledUsages.Clear();

        // req-103：按用户配置的卡片顺序排序（自定义顺序优先，未配置的追加到末尾）
        var cardOrder = _configService.Settings.ProviderCardOrder;
        var enabledList = Usages.Where(vm => vm.IsEnabled).ToList();

        if (cardOrder.Count > 0)
        {
            foreach (var providerId in cardOrder)
            {
                var vm = enabledList.FirstOrDefault(x => string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
                if (vm != null)
                {
                    EnabledUsages.Add(vm);
                    enabledList.Remove(vm);
                }
            }
            foreach (var vm in enabledList) EnabledUsages.Add(vm);
        }
        else
        {
            foreach (var vm in enabledList) EnabledUsages.Add(vm);
        }

        UsageMonitor.Core.Services.FileLogger.Info("DisplayModule",
            $"RebuildEnabledCards 完成：Usages={Usages.Count}，EnabledUsages={EnabledUsages.Count}");
        EnabledCardsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void SetPluginEnabled(string providerId, bool isEnabled)
    {
        _configService.Settings.PluginEnabled[providerId] = isEnabled;

        var plugin = _pluginManager.GetPlugin(providerId);
        if (plugin != null) plugin.IsEnabled = isEnabled;

        var usageVm = Usages.FirstOrDefault(u => u.ProviderId == providerId);
        if (usageVm != null) usageVm.IsEnabled = isEnabled;

        RebuildEnabledCards();
        _configService.Save();
    }

    /// <inheritdoc/>
    public void ChangeTaskbarMode(string providerId, TaskbarDisplayMode mode)
    {
        _configService.Settings.ProviderTaskbarModes[providerId] = mode;

        var usageVm = Usages.FirstOrDefault(u => u.ProviderId == providerId);
        if (usageVm != null && usageVm.DisplayMode != mode)
            usageVm.DisplayMode = mode;

        _configService.Save();
    }

    /// <inheritdoc/>
    public IReadOnlyList<CardChartKind> GetChartOrder(string providerId, IReadOnlyList<CardChartKind> supportedCharts)
    {
        if (_configService.Settings.ProviderChartOrder.TryGetValue(providerId, out var customOrder) && customOrder.Count > 0)
        {
            // 过滤掉插件不再支持的图表类型，再追加插件新支持的类型
            var validOrder = customOrder.Where(c => supportedCharts.Contains(c)).ToList();
            var missingCharts = supportedCharts.Where(c => !validOrder.Contains(c)).ToList();
            validOrder.AddRange(missingCharts);
            return validOrder;
        }
        return supportedCharts;
    }
}
