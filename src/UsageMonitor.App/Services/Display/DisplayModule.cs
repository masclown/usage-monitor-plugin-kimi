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
    // S1：统一鉴权管理器（供账号行 Sub 状态灯读取登录态，可为 null 表示未接入）。
    private readonly UsageMonitor.Core.Services.Auth.AuthManager? _authManager;
    // S1：每个 Provider 的账号结构签名缓存（accountId+Enabled+cardIds），
    //   供 SyncCardsWithAccounts 增量比对：仅账号结构真正变化时才重建卡片，
    //   避免主题 / 刷新间隔等无关配置变更误触发卡片重建。
    private readonly Dictionary<string, string> _accountSignatures = new(StringComparer.OrdinalIgnoreCase);
    // S1：防止 SyncCardsWithAccounts 重入的保护标志。
    private bool _isSyncingCards;

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
        IRefreshService refreshService, Action<string>? reLoginHandler,
        UsageMonitor.Core.Services.Auth.AuthManager? authManager = null)
    {
        _pluginManager = pluginManager;
        _configService = configService;
        _refreshService = refreshService;
        _reLoginHandler = reLoginHandler;
        _authManager = authManager;
    }

    /// <inheritdoc/>
    public void Build()
    {
        foreach (var plugin in _pluginManager.Plugins)
        {
            // S1：计算该 Provider 的 (Account, Card) 元组列表（过滤禁用账号；无账号时回退默认单卡）
            var cardTuples = BuildCardTuples(plugin.Provider.ProviderId);

            // 读取已保存的显示模式与卡片图表多选（未配置时回退插件声明：req-107 B6 优先 Card.Charts）
            var savedMode = TaskbarModeResolver.Resolve(_configService.Settings, plugin.Provider.ProviderId);
            var savedCardCharts = _configService.GetProviderCardChartKinds(plugin.Provider.ProviderId);
            if (savedCardCharts.Count == 0)
                savedCardCharts = ChartKindExtractor.ExtractDeclaredChartKinds(plugin.Provider).ToList();

            var item = new PluginItemViewModel(plugin.Provider, _configService, _authManager)
            {
                ProviderId = plugin.Provider.ProviderId,
                DisplayName = plugin.Provider.DisplayName,
                Version = plugin.Provider.Version,
                Author = plugin.Provider.Author,
                Description = plugin.Provider.Description,
                IsEnabled = plugin.IsEnabled,
                DisplayMode = savedMode
            };
            // S6：旧 item.InitCardChartKinds(savedCardCharts) 已删除——卡片图表多选写入路径（ProviderCardChartKinds）
            // 随插件配置窗口瘦身一并清除；savedCardCharts 仅作为读取兼容路径继续流入卡片 VM 驱动渲染。
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
            };
            PluginItems.Add(item);
            // S1：加载该插件下的账号列表（供徽标计数 / 绿点 / 展开列表使用）
            item.ReloadAccounts();

            // S1：创建卡片 VM（与 RebuildCardsForProvider 共用逻辑）并记录账号结构签名
            CreateCardVms(plugin, item, cardTuples, savedMode, savedCardCharts);
            _accountSignatures[plugin.Provider.ProviderId] = BuildAccountSignature(plugin.Provider.ProviderId);
        }

        RebuildEnabledCards();
    }

    /// <summary>
    /// S1：计算指定 Provider 的 (AccountId, CardId) 元组列表。
    /// <para>无账号 → 回退单卡向后兼容路径 (default, default-card)；
    /// 有账号 → 逐账号枚举卡片，并跳过 <c>Enabled=false</c> 的禁用账号（不为其生成卡片）。</para>
    /// </summary>
    private List<(string AccountId, string CardId)> BuildCardTuples(string providerId)
    {
        var accounts = _configService.GetAccounts(providerId);
        if (accounts.Count == 0)
            return new List<(string AccountId, string CardId)> { ("default", "default-card") };
        return accounts
            .Where(a => a.Enabled)
            .SelectMany(a => _configService.GetCards(providerId, a.AccountId)
                .Select(c => (AccountId: a.AccountId, CardId: c.CardId)))
            .ToList();
    }

    /// <summary>
    /// S1：为指定 Provider 创建卡片 VM 并加入 <see cref="Usages"/>（Build 与 RebuildCardsForProvider 共用）。
    /// <para>包含重新登录回调注入、账号昵称卡片标题解析、ConfigService 订阅接线。</para>
    /// </summary>
    private void CreateCardVms(UsageMonitor.Core.Plugins.LoadedPlugin plugin, PluginItemViewModel item,
        List<(string AccountId, string CardId)> cardTuples, TaskbarDisplayMode savedMode,
        IReadOnlyList<CardChartKind> savedCardCharts)
    {
        var providerId = plugin.Provider.ProviderId;

        // req-091-005：仅当 Provider 声明 LoginConfig 且宿主提供回调时注入重新登录动作。
#pragma warning disable CS0618 // LoginConfig 已过时（req-096），此处仍用于判定是否显示重新登录按钮，保持向后兼容
        var supportsReLogin = plugin.Provider.LoginConfig != null;
#pragma warning restore CS0618
        Action? reLoginAction = supportsReLogin && _reLoginHandler != null
            ? () => _reLoginHandler(providerId)
            : null;

        // req-109：每个 (Account, Card) 二元组派生一个 ProviderUsageViewModel
        foreach (var (accountId, cardId) in cardTuples)
        {
            // B2：按账号昵称解析卡片标题——账号存在且 UseNickname=true 且昵称非空时显示昵称，
            // 否则回退 Provider 显示名。昵称变更后由 ProviderUsageViewModel.OnConfigChanged 实时刷新。
            var account = _configService.GetAccount(providerId, accountId);
            var cardDisplayName = (account is { UseNickname: true } && !string.IsNullOrWhiteSpace(account.Nickname))
                ? account.Nickname.Trim()
                : plugin.Provider.DisplayName;

            var usageVm = new ProviderUsageViewModel(
                // S6：卡片“⚙ 设置”按钮打开配置窗口时携带本卡片的账号上下文，
                // 使图表/迷你图表启用开关按当前账号生效（accountId 来自 (Account, Card) 元组）。
                () => item.OpenConfigDialog(accountId),
                () => _refreshService.RefreshProviderAsync(providerId),
                reLoginAction,
                accountId: accountId,
                cardId: cardId)
            {
                ProviderId = providerId,
                DisplayName = cardDisplayName,
                IconPath = ProviderUsageViewModel.ResolveIconPath(providerId),
                IsEnabled = plugin.IsEnabled,
                DisplayMode = savedMode,
                CardChartKinds = savedCardCharts,
                RenderKinds = plugin.Provider.Card?.RenderKinds ?? Array.Empty<string>(),
                CollapseVisibleParts = plugin.Provider.CollapseVisibleParts ?? Array.Empty<string>(),
                // req-107 B8：SupportsPeriodSwitch / ExtraTooltipLines 接口成员已收敛为 [Obsolete]；
                // 周期切换能力交由 Card.Line.Slicer(Period)、tooltip 扩展行交由 Card.Chart.Tooltip；VM 初始化不再从接口读取。
                Provider = plugin.Provider,
            };
            usageVm.AttachConfigService(_configService);
            Usages.Add(usageVm);
        }
    }

    /// <summary>
    /// S1：构建指定 Provider 的账号结构签名（accountId + Enabled + cardIds）。
    /// <para>签名变化即视为账号结构变更（增 / 删账号、启停账号、增删卡片），
    /// 昵称修改不计入（由 ProviderUsageViewModel.ReloadDisplayNameFromAccount 单独处理）。</para>
    /// </summary>
    private string BuildAccountSignature(string providerId)
    {
        var accounts = _configService.GetAccounts(providerId);
        if (accounts.Count == 0) return "__legacy__";
        var parts = new List<string>();
        foreach (var a in accounts)
        {
            var cardIds = string.Join(",", _configService.GetCards(providerId, a.AccountId).Select(c => c.CardId));
            parts.Add($"{a.AccountId}:{a.Enabled}:{cardIds}");
        }
        return string.Join("|", parts);
    }

    /// <summary>
    /// S1：重建指定 Provider 的卡片集合（账号增删改后实时刷新主窗口卡片）。
    /// <para>先移除该 Provider 旧卡片（并解除其 ConfigChanged 订阅防止泄漏），
    /// 再按最新账号结构重建，最后刷新已启用卡片集合。必须在 UI 线程调用。</para>
    /// </summary>
    public void RebuildCardsForProvider(string providerId)
    {
        // 移除旧卡片（解除 ConfigService 订阅，防止重复订阅 / 内存泄漏）
        var oldCards = Usages.Where(u => string.Equals(u.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var old in oldCards)
        {
            old.AttachConfigService(null);
            Usages.Remove(old);
        }

        var plugin = _pluginManager.GetPlugin(providerId);
        var item = PluginItems.FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        if (plugin == null || item == null)
        {
            RebuildEnabledCards();
            return;
        }

        var cardTuples = BuildCardTuples(providerId);
        var savedMode = TaskbarModeResolver.Resolve(_configService.Settings, providerId);
        var savedCardCharts = _configService.GetProviderCardChartKinds(providerId);
        if (savedCardCharts.Count == 0)
            savedCardCharts = ChartKindExtractor.ExtractDeclaredChartKinds(plugin.Provider).ToList();

        CreateCardVms(plugin, item, cardTuples, savedMode, savedCardCharts);
        _accountSignatures[providerId] = BuildAccountSignature(providerId);

        UsageMonitor.Core.Services.FileLogger.Info("DisplayModule",
            $"RebuildCardsForProvider 完成：{providerId}，卡片数={cardTuples.Count}");
        RebuildEnabledCards();
    }

    /// <summary>
    /// S1：按账号结构签名增量同步卡片集合（ConfigChanged 触发）。
    /// <para>仅重建账号结构真正变化的 Provider，避免主题 / 刷新间隔等无关配置变更误触发重建。
    /// 复用既有 ConfigChanged 事件链路，不新增事件。必须在 UI 线程调用。</para>
    /// </summary>
    public void SyncCardsWithAccounts()
    {
        if (_isSyncingCards) return;
        _isSyncingCards = true;
        try
        {
            foreach (var plugin in _pluginManager.Plugins)
            {
                var providerId = plugin.Provider.ProviderId;
                var current = BuildAccountSignature(providerId);
                if (_accountSignatures.TryGetValue(providerId, out var last) && last == current)
                    continue;
                RebuildCardsForProvider(providerId);
            }
        }
        finally
        {
            _isSyncingCards = false;
        }
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

    /// <summary>
    /// 分发准备：重新解析所有卡片的 Provider 图标（供启动时 favicon 预取完成后回填）。
    /// <para>由 App 在预取任务完成后于 UI 线程调用；仅刷新图标，不重建卡片。</para>
    /// </summary>
    public void RefreshIcons()
    {
        foreach (var vm in Usages)
            vm.RefreshIcon();
    }
}
