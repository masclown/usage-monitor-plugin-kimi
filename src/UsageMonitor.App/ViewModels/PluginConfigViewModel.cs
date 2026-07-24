using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.ViewModels;

/// <summary>
/// 图表/迷你图表启用开关的单项模型（chartId + 显示名 + 初始勾选态）。
/// <para>由 <see cref="PluginConfigViewModel"/> 在构造时按插件 defaults.json 声明顺序生成，
/// 视图层据此创建 CheckBox 列表。</para>
/// </summary>
public sealed class ChartSwitchItem
{
    /// <summary>图表声明 ID（如 "mm.chart.usage_bar"）</summary>
    public string ChartId { get; }

    /// <summary>简短显示名（去掉 Provider 前缀，与卡片管理页 ChartNode 规则一致）</summary>
    public string DisplayName { get; }

    /// <summary>初始勾选态（legacy/null 全选语义时为 true）</summary>
    public bool InitialChecked { get; }

    /// <summary>初始化 ChartSwitchItem。</summary>
    public ChartSwitchItem(string chartId, string displayName, bool initialChecked)
    {
        ChartId = chartId;
        DisplayName = displayName;
        InitialChecked = initialChecked;
    }
}

/// <summary>
/// req-069-009/010/011：插件配置窗口视图模型——承载 PluginConfigWindow 的全部业务逻辑。
/// <para>
/// 职责：表单保存（必填校验 + ProviderConfig 写入）、Cookie 获取（BrowserLoginService 调用 +
/// 防重复锁 + 结果反馈）、Mode 切换持久化（QueryMode 落盘 + 字段列表重拉）、
/// 卡片图表/任务栏迷你图表启用开关的初始态计算与持久化决策（含 isLegacyFill 守卫）。
/// </para>
/// <para>
/// 视图层（code-behind）仅保留动态控件构建与控件值读写：WPF 动态表单生成属视图职责，
/// 业务决策/持久化/服务调用全部集中在本 VM。
/// </para>
/// <para>
/// 高危语义保留（Phase 2 修复）：旧 ProviderCardChartKinds 类型名（如 "Ring","Line"）被兼容层
/// 填进 VisibleCharts 时判定 isLegacyFill → 回显全勾选；用户未改动任何开关时跳过写入，
/// 保持 null 兼容路径，不得回退。
/// </para>
/// </summary>
public class PluginConfigViewModel : INotifyPropertyChanged
{
    private readonly ProviderConfig _config;
    private readonly BrowserLoginConfig? _loginConfig;
    private readonly ConfigService? _configService;
    private readonly IUsageProvider? _provider;
    private readonly string _accountId;

    /// <summary>当前生效的配置字段列表（Mode 切换后由 provider.ConfigFields 重拉更新）。</summary>
    public IReadOnlyList<ConfigField> ConfigFields { get; private set; }

    // =====================================================================
    // 图表启用开关状态（S6 + Phase 2 修复语义）
    // =====================================================================

    /// <summary>S6：卡片图表启用开关项列表（按插件 Card.Charts 声明顺序；声明缺失时为空 → 视图隐藏整组）。</summary>
    public IReadOnlyList<ChartSwitchItem> CardChartItems { get; } = Array.Empty<ChartSwitchItem>();

    /// <summary>S6：任务栏迷你图表启用开关项列表（按插件 Taskbar.MiniCharts 声明顺序；声明缺失时为空 → 视图隐藏整组）。</summary>
    public IReadOnlyList<ChartSwitchItem> MiniChartItems { get; } = Array.Empty<ChartSwitchItem>();

    /// <summary>Phase 2 修复：卡片图表开关初始是否为 legacy/null 全选语义（用户未改动时跳过写入）。</summary>
    private readonly bool _cardChartIsLegacyAll;

    /// <summary>Phase 2 修复：迷你图表开关初始是否为 legacy/null 全选语义（用户未改动时跳过写入）。</summary>
    private readonly bool _miniChartIsLegacyAll;

    // =====================================================================
    // Cookie 获取按钮状态
    // =====================================================================

    private bool _isGetCookieEnabled = true;
    private string _getCookieButtonText = string.Empty;

    /// <summary>是否显示"获取登录态"按钮（插件声明了 BrowserLoginConfig 时为 true）。</summary>
    public bool HasLoginConfig => _loginConfig != null;

    /// <summary>"获取登录态"按钮文本（优先取插件声明的 UiButtonText；登录中/完成后动态更新）。</summary>
    public string GetCookieButtonText
    {
        get => _getCookieButtonText;
        private set { if (_getCookieButtonText != value) { _getCookieButtonText = value; OnPropertyChanged(); } }
    }

    /// <summary>"获取登录态"按钮是否可用（登录进行中禁用，防止重复触发）。</summary>
    public bool IsGetCookieEnabled
    {
        get => _isGetCookieEnabled;
        private set { if (_isGetCookieEnabled != value) { _isGetCookieEnabled = value; OnPropertyChanged(); } }
    }

    /// <summary>窗口内部标题文本（"{插件名} 配置"）。</summary>
    public string WindowTitle { get; }

    // =====================================================================
    // 命令
    // =====================================================================

    /// <summary>保存命令：收集表单值 → 必填校验 → 写入 ProviderConfig → 持久化图表开关 → 请求关闭窗口。</summary>
    public IRelayCommand SaveCommand { get; }

    /// <summary>取消命令：不保存直接请求关闭窗口。</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>获取登录态命令（异步）：启动 Edge 登录窗口并提取 Cookie 填入字段。</summary>
    public IAsyncRelayCommand GetCookieCommand { get; }

    // =====================================================================
    // 视图交互契约（事件 + 委托）
    // =====================================================================

    /// <summary>请求关闭窗口（参数为 DialogResult：true=已保存 / false=取消）。</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>Cookie 提取成功后通知视图填入 Cookie 输入控件（参数为完整 Cookie 字符串）。</summary>
    public event Action<string>? CookieReceived;

    /// <summary>视图层赋值：收集当前表单所有字段的值（fieldKey → value，保存语义——始终返回字符串）。</summary>
    public Func<Dictionary<string, string>>? CollectFormValues { get; set; }

    /// <summary>视图层赋值：收集卡片图表开关当前勾选态（chartId → isChecked）。</summary>
    public Func<Dictionary<string, bool>>? CollectCardChartStates { get; set; }

    /// <summary>视图层赋值：收集迷你图表开关当前勾选态（chartId → isChecked）。</summary>
    public Func<Dictionary<string, bool>>? CollectMiniChartStates { get; set; }

    // =====================================================================
    // 登录防重复（进程级共享）
    // =====================================================================

    /// <summary>
    /// 正在登录中的 ProviderId 集合（进程级共享，避免同一插件重复触发登录）。
    /// <para>采用 HashSet 支持多 ProviderId 独立并发控制：DeepSeek 登录中点击 MiniMax 按钮不被阻塞。</para>
    /// <para>req-064 B12：大小写不敏感，避免 "MiniMax" 与 "minimax" 绕过防重复锁。</para>
    /// </summary>
    private static readonly HashSet<string> _isLoginInProgress = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>保护 <see cref="_isLoginInProgress"/> 的锁对象。</summary>
    private static readonly object _loginInProgressLock = new();

    // =====================================================================
    // 构造
    // =====================================================================

    /// <summary>
    /// 创建插件配置窗口视图模型。
    /// </summary>
    /// <param name="pluginName">插件显示名称（标题用）</param>
    /// <param name="configFields">插件定义的配置字段</param>
    /// <param name="config">当前配置（读取和写入）</param>
    /// <param name="loginConfig">可选的浏览器登录配置（非 null 时显示"获取登录态"按钮）</param>
    /// <param name="configService">可选 ConfigService（登录成功后自动重载 + 图表开关持久化）</param>
    /// <param name="provider">可选插件实例（Mode 切换重拉字段 + 图表声明来源）</param>
    /// <param name="accountId">账号上下文（null/空 → "default"）</param>
    public PluginConfigViewModel(
        string pluginName,
        IReadOnlyList<ConfigField> configFields,
        ProviderConfig config,
        BrowserLoginConfig? loginConfig = null,
        ConfigService? configService = null,
        IUsageProvider? provider = null,
        string? accountId = null)
    {
        ConfigFields = configFields;
        _config = config;
        _loginConfig = loginConfig;
        _configService = configService;
        _provider = provider;
        _accountId = string.IsNullOrWhiteSpace(accountId) ? "default" : accountId.Trim();

        WindowTitle = $"{pluginName} 配置";
        _getCookieButtonText = loginConfig?.UiButtonText ?? "🌐 获取登录态";

        // S6：初始化两组图表启用开关项（含 Phase 2 isLegacyFill 检测）
        CardChartItems = BuildCardChartItems(out _cardChartIsLegacyAll);
        MiniChartItems = BuildMiniChartItems(out _miniChartIsLegacyAll);

        SaveCommand = new RelayCommand(ExecuteSave);
        CancelCommand = new RelayCommand(ExecuteCancel);
        GetCookieCommand = new AsyncRelayCommand(ExecuteGetCookieAsync);
    }

    // =====================================================================
    // 图表开关初始态计算（S6 + Phase 2 修复）
    // =====================================================================

    /// <summary>
    /// S6：按插件 Card.Charts 声明计算卡片图表开关初始项。
    /// <para>Phase 2 修复：检测旧配置回显错误——旧 ProviderCardChartKinds 类型名（如 "Ring","Line"）
    /// 被兼容层填进 VisibleCharts，与声明 chartId 永不匹配 → 开关全部回显为不勾选。
    /// 判定 isLegacyFill：VisibleCharts 非 null 且含任一不在声明 chartId 集合中的值。</para>
    /// </summary>
    /// <param name="isLegacyAll">输出：是否为 legacy/null 全选语义。</param>
    /// <returns>开关项列表；声明缺失时返回空列表（视图隐藏整组）。</returns>
    private IReadOnlyList<ChartSwitchItem> BuildCardChartItems(out bool isLegacyAll)
    {
        isLegacyAll = false;
        var charts = _provider?.Card?.Charts;
        if (_provider == null || _configService == null || charts == null || charts.Count == 0)
            return Array.Empty<ChartSwitchItem>();

        var eff = _configService.GetEffectiveAccountCustomization(_provider.ProviderId, _accountId);
        var declaredIds = new HashSet<string>(charts.Select(c => c.ChartId), StringComparer.Ordinal);
        bool isLegacyFill = eff.VisibleCharts != null
            && eff.VisibleCharts.Any(v => !declaredIds.Contains(v));
        isLegacyAll = eff.VisibleCharts == null || isLegacyFill;

        var items = new List<ChartSwitchItem>(charts.Count);
        foreach (var chart in charts)
        {
            // Phase 2 修复：legacy/null 时全选回显，避免旧配置被误显示为未勾选。
            var isChecked = isLegacyAll || eff.VisibleCharts!.Contains(chart.ChartId);
            items.Add(new ChartSwitchItem(chart.ChartId, ExtractChartShortName(chart.ChartId), isChecked));
        }
        return items;
    }

    /// <summary>
    /// S6：按插件 Taskbar.MiniCharts 声明计算迷你图表开关初始项。
    /// <para>Phase 2 修复：同卡片图表侧逻辑——检测旧配置回显错误。</para>
    /// </summary>
    /// <param name="isLegacyAll">输出：是否为 legacy/null 全选语义。</param>
    /// <returns>开关项列表；声明缺失时返回空列表（视图隐藏整组）。</returns>
    private IReadOnlyList<ChartSwitchItem> BuildMiniChartItems(out bool isLegacyAll)
    {
        isLegacyAll = false;
        var miniCharts = _provider?.Taskbar?.MiniCharts;
        if (_provider == null || _configService == null || miniCharts == null || miniCharts.Count == 0)
            return Array.Empty<ChartSwitchItem>();

        var eff = _configService.GetEffectiveAccountCustomization(_provider.ProviderId, _accountId);
        var declaredIds = new HashSet<string>(miniCharts.Select(m => m.ChartId), StringComparer.Ordinal);
        bool isLegacyFill = eff.VisibleMiniCharts != null
            && eff.VisibleMiniCharts.Any(v => !declaredIds.Contains(v));
        isLegacyAll = eff.VisibleMiniCharts == null || isLegacyFill;

        var items = new List<ChartSwitchItem>(miniCharts.Count);
        foreach (var mini in miniCharts)
        {
            var isChecked = isLegacyAll || eff.VisibleMiniCharts!.Contains(mini.ChartId);
            items.Add(new ChartSwitchItem(mini.ChartId, ExtractChartShortName(mini.ChartId), isChecked));
        }
        return items;
    }

    /// <summary>
    /// 从 chartId 提取简短显示名（去掉 Provider 前缀，与卡片管理页 ChartNode 规则一致：
    /// "mm.chart.usage_bar" → "usage_bar"；不足三段时原样返回）。
    /// </summary>
    public static string ExtractChartShortName(string chartId)
    {
        var parts = chartId.Split('.');
        return parts.Length > 2 ? string.Join(".", parts.Skip(2)) : chartId;
    }

    // =====================================================================
    // 保存逻辑
    // =====================================================================

    /// <summary>
    /// 保存命令执行：收集表单值 → 必填校验 → 写入 ProviderConfig → 持久化图表开关 → 请求关闭。
    /// </summary>
    private void ExecuteSave()
    {
        var values = CollectFormValues?.Invoke() ?? new Dictionary<string, string>();

        foreach (var field in ConfigFields)
        {
            if (!values.TryGetValue(field.Key, out var value))
                continue;

            // 验证必填项
            if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                System.Windows.MessageBox.Show(
                    $"\"{field.DisplayName}\" 为必填项",
                    "验证失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _config.SetValue(field.Key, value);
        }

        // S6：持久化卡片图表 / 任务栏迷你图表启用开关（落点同卡片管理页 / 迷你图表页）。
        // 空集合是合法用户选择（= 不显示任何图表），语义与 AccountCustomization 契约一致。
        PersistChartSwitches(
            CollectCardChartStates?.Invoke(),
            CollectMiniChartStates?.Invoke());

        CloseRequested?.Invoke(true);
    }

    /// <summary>取消命令执行：不保存直接请求关闭窗口。</summary>
    private void ExecuteCancel() => CloseRequested?.Invoke(false);

    /// <summary>
    /// S6：保存时持久化两组启用开关。
    /// <para>卡片图表落 AccountCustomization.VisibleCharts（ConfigService.SetVisibleCharts），
    /// 迷你图表落 VisibleMiniCharts（ConfigService.SetVisibleMiniCharts）——
    /// 与设置窗口【卡片管理】/【任务栏迷你图表】页同一数据落点；
    /// 两个窄写入方法只更新单一字段，不触碰排序/数据组等兄弟配置，避免双写冲突。
    /// 声明缺失（开关区未构建 → states 为 null/空）时跳过对应写入，不产生空配置条目。</para>
    /// <para>Phase 2 修复：若初始为 legacy/null 全选语义且用户未改动任何开关，则跳过写入（保持 null 兼容路径）。</para>
    /// </summary>
    /// <param name="cardStates">卡片图表当前勾选态（chartId → isChecked；未构建时为 null）</param>
    /// <param name="miniStates">迷你图表当前勾选态（chartId → isChecked；未构建时为 null）</param>
    private void PersistChartSwitches(Dictionary<string, bool>? cardStates, Dictionary<string, bool>? miniStates)
    {
        if (_provider == null || _configService == null) return;

        if (cardStates is { Count: > 0 } && CardChartItems.Count > 0)
        {
            bool cardChanged = CardChartItems.Any(item =>
                cardStates.GetValueOrDefault(item.ChartId, false) != item.InitialChecked);
            if (!(_cardChartIsLegacyAll && !cardChanged))
            {
                var visible = cardStates.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
                _configService.SetVisibleCharts(_provider.ProviderId, visible, _accountId);
            }
        }

        if (miniStates is { Count: > 0 } && MiniChartItems.Count > 0)
        {
            bool miniChanged = MiniChartItems.Any(item =>
                miniStates.GetValueOrDefault(item.ChartId, false) != item.InitialChecked);
            if (!(_miniChartIsLegacyAll && !miniChanged))
            {
                var visible = miniStates.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
                _configService.SetVisibleMiniCharts(_provider.ProviderId, visible, _accountId);
            }
        }
    }

    // =====================================================================
    // Mode 切换（req-fix-Kimi-ConfigFields 动态模式）
    // =====================================================================

    /// <summary>
    /// req-fix-Kimi-ConfigFields 动态模式：判断字段 key 是否为模式选择字段（QueryMode）。
    /// 集中维护双模式插件的 Mode 字段 key，新增插件时只需扩展此判定。
    /// </summary>
    public static bool IsModeFieldKey(string fieldKey) => fieldKey == "QueryMode";

    /// <summary>是否支持 Mode 动态切换（传入了 provider 实例时为 true，视图据此决定 ComboBox 是否挂监听）。</summary>
    public bool SupportsModeSwitch => _provider != null;

    /// <summary>读取当前 ProviderConfig 中指定键的已存值（供视图构建控件初始值；不存在时返回 null）。</summary>
    /// <param name="key">配置字段键名。</param>
    public string? ConfigFieldValue(string key) => _config.GetValue(key);

    /// <summary>
    /// req-fix-Kimi-ConfigFields 动态模式：Mode 字段切换时的业务处理。
    /// <list type="number">
    ///   <item><description>立即把 Mode 字段值持久化到 config（只持久化 Mode，不影响其他字段——
    ///     Mode 切换是用户明确的"试切"动作，下次开窗要看到新模式；其他字段不持久化避免误保存触发必填校验）</description></item>
    ///   <item><description>重新调用 provider.ConfigFields 拉取与新模式匹配的字段列表</description></item>
    /// </list>
    /// 视图层收到返回值后负责重建表单并恢复用户已填值。
    /// </summary>
    /// <param name="currentValues">视图层当前所有输入控件的值快照（跳过空值语义）</param>
    /// <returns>新的字段列表；provider 为 null 时返回 null（不重建）。</returns>
    public IReadOnlyList<ConfigField>? HandleModeChange(IReadOnlyDictionary<string, string> currentValues)
    {
        if (_provider == null) return null;

        try
        {
            // 1. Mode 字段值持久化（只持久化 Mode，不影响其他字段）
            if (_configService != null && currentValues.TryGetValue("QueryMode", out var newMode))
            {
                _config.SetValue("QueryMode", newMode);
                try
                {
                    _configService.UpdateProviderConfig(_provider.ProviderId, _config);
                }
                catch (Exception saveEx)
                {
                    FileLogger.Warn("PluginConfigVM",
                        $"Mode 切换后持久化失败 ({_provider.ProviderId} -> {newMode}): {saveEx.Message}");
                }
            }

            // 2. 重新拉取字段列表（双模式插件按 mode 返回不同字段）
            ConfigFields = _provider.ConfigFields;
            return ConfigFields;
        }
        catch (Exception ex)
        {
            FileLogger.Error("PluginConfigVM", $"HandleModeChange failed: {ex.Message}", ex);
            return null;
        }
    }

    // =====================================================================
    // Cookie 获取（BrowserLoginService）
    // =====================================================================

    /// <summary>
    /// "获取登录态"命令执行——启动独立 Edge 窗口让用户登录，自动提取 Cookie 后通知视图填入字段。
    /// <para>
    /// 通过 <see cref="BrowserLoginService"/> 启动临时 Edge + CDP 提取明文 Cookie，
    /// 并以 JSON 格式持久化（含 userAgent、时间戳、count、domain 等元数据）。
    /// </para>
    /// <para>
    /// 防止重复触发：使用静态锁 <see cref="_isLoginInProgress"/> 避免同一 ProviderId 的多次并发登录。
    /// </para>
    /// </summary>
    private async Task ExecuteGetCookieAsync()
    {
        if (_loginConfig == null) return;

        // 防重复调用：同一 ProviderId 已有进行中的登录任务，直接拒绝
        lock (_loginInProgressLock)
        {
            if (_isLoginInProgress.Contains(_loginConfig.ProviderId))
                return;
            _isLoginInProgress.Add(_loginConfig.ProviderId);
        }

        IsGetCookieEnabled = false;
        var originalContent = GetCookieButtonText;
        GetCookieButtonText = "🔄 启动浏览器中...";

        try
        {
            // req-065 B4：BrowserLoginService 去静态化，每次登录创建独立实例避免并发时 LastError 互相覆盖
            var loginService = new BrowserLoginService(_configService);
            var data = await loginService.LoginAndExtractCookieAsync(_loginConfig);

            if (data == null || string.IsNullOrEmpty(data.Cookie))
            {
                // 显示真实错误信息（来自 BrowserLoginService.LastError）
                var lastError = loginService.LastError;
                var message = "未获取到 Cookie。\n\n";
                if (!string.IsNullOrEmpty(lastError))
                {
                    message += $"【真实错误】{lastError}\n\n";
                }
                message +=
                    "可能原因：\n" +
                    "① 您取消了登录\n" +
                    "② Edge 启动失败或被阻止（首次运行需联网下载 Playwright 浏览器）\n" +
                    "③ 未检测到 " + (_loginConfig.RequiredCookieDomain ?? "目标域名") +
                    $" 域的会话 Cookie（请确认已 {_loginConfig.LoginUrl} 完成登录）\n" +
                    $"④ 登录超时（{_loginConfig.LoginTimeout.TotalMinutes:0}分钟）\n\n" +
                    $"请重试，或检查 Edge 是否能正常访问 {_loginConfig.LoginUrl}";
                System.Windows.MessageBox.Show(
                    message,
                    "获取 Cookie 失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 通知视图将 Cookie 填入 Cookie 字段
            CookieReceived?.Invoke(data.Cookie);

            // 更新状态提示（显示关键元数据）
            GetCookieButtonText = $"✅ 已获取 {data.Count} 条 Cookie（{data.Domain}）";

            System.Windows.MessageBox.Show(
                $"Cookie 获取成功！\n\n" +
                $"服务商: {data.ProviderId}\n" +
                $"域名: {data.Domain}\n" +
                $"条数: {data.Count}\n" +
                $"保存于: {data.SavedAt:yyyy-MM-dd HH:mm:ss}\n" +
                $"Cookie 长度: {data.Cookie.Length} 字符（完整内容已安全保存，不在弹窗中显示）\n\n" +
                $"持久化路径: %AppData%\\UsageMonitor\\cookies\\{data.ProviderId}.json\n\n" +
                "Edge 浏览器窗口已被自动关闭。\n" +
                "点击【保存】按钮保存配置后，回到主界面右键托盘 → 立即刷新即可看到用量数据。",
                "Cookie 已填入字段",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"启动登录窗口时出错：\n\n{ex.Message}\n\n请检查 Edge 浏览器是否已正确安装。",
                "启动失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // 释放防重复锁
            lock (_loginInProgressLock)
            {
                _isLoginInProgress.Remove(_loginConfig.ProviderId);
            }
            IsGetCookieEnabled = true;
            GetCookieButtonText = originalContent;
        }
    }

    // =====================================================================
    // INotifyPropertyChanged
    // =====================================================================

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发属性变更通知。</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
