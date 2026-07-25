using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 应用全局配置模型
/// </summary>
public class AppSettings
{
    /// <summary>刷新间隔（秒），默认300秒（5分钟）</summary>
    public int RefreshIntervalSeconds { get; set; } = 300;

    /// <summary>是否启用任务栏显示</summary>
    public bool ShowInTaskbar { get; set; } = true;

    /// <summary>是否开机自启</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>是否最小化到托盘</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>任务栏显示的ProviderId列表（空表示全部显示）</summary>
    public List<string> TaskbarDisplayProviders { get; set; } = new();

    /// <summary>各服务商的配置列表</summary>
    public Dictionary<string, ProviderConfig> ProviderConfigs { get; set; } = new();

    /// <summary>各插件是否启用的映射</summary>
    public Dictionary<string, bool> PluginEnabled { get; set; } = new();

    /// <summary>
    /// req-096：持久化的登录态信息（重启后恢复“登录态计时”）。
    /// 仅存非敏感元数据（ProviderId / AccountId / AcquiredAt）；实际鉴权数据由各 AuthProvider 单独安全持久化，
    /// <see cref="LoginStateInfo.EncryptedData"/> 已标 [JsonIgnore] 不落盘，避免明文凭据写入 config.json。
    /// </summary>
    public List<LoginStateInfo> PersistedLoginStates { get; set; } = new();

    /// <summary>
    /// req-109：所有账号实体（多账号 UI 数据源）。每个 (ProviderId, AccountId) 唯一。
    /// <para>账号管理 UI 按用户决定放 <c>PluginConfigWindow</c> 内（认证层不变，仍按二段 key 鉴权）。</para>
    /// </summary>
    public List<Models.Account> Accounts { get; set; } = new();

    /// <summary>历史数据保留点数（默认 60，可调 30/60/120）</summary>
    public int HistoryPointCount { get; set; } = 60;

    /// <summary>是否启用托盘悬浮窗（鼠标悬停托盘图标时弹出）</summary>
    public bool ShowTrayTooltip { get; set; } = true;

    /// <summary>托盘悬浮窗关闭延迟（毫秒）</summary>
    public int TrayTooltipHideDelayMs { get; set; } = 500;

    /// <summary>
    /// req-098：用户对每个 Provider 任务栏迷你图表的配置（key = ProviderId）。
    /// <para>
    /// 未配置时 <c>MiniChartRegistryBootstrapper</c> 走默认注册路径（全部 Provider 用 RingChart
    /// + PrimaryMetric）。用户在"任务栏迷你图表"设置页修改后写回此字典。
    /// </para>
    /// </summary>
    public Dictionary<string, Models.TaskbarMiniChartConfig> TaskbarMiniChartConfigs { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>托盘悬浮窗触发区域宽度（像素，屏幕右下角向左延伸），默认 200</summary>
    public int TrayTriggerWidth { get; set; } = 200;

    /// <summary>托盘悬浮窗触发区域高度（像素，工作区底部向下延伸），默认 40</summary>
    public int TrayTriggerHeight { get; set; } = 40;

    /// <summary>req-036：托盘悬浮窗宽度（像素）。null 表示使用默认 300px；非 null 表示用户调整后的持久化值。</summary>
    public double? TrayTooltipWindowWidth { get; set; } = null;

    /// <summary>各 Provider 在任务栏的显示模式（key=ProviderId，缺省时为 Text）</summary>
    [Obsolete("已由 AccountCustomizations 取代，仅保留读取兼容")]
    public Dictionary<string, TaskbarDisplayMode> ProviderTaskbarModes { get; set; } = new();

    /// <summary>req-026：环形图中心数字“已启用 metric key”按 Provider 索引。
    /// 缺失 / 为空时回退到 <see cref="GlobalEnabledRingChartMetrics"/>。
    /// 典型 key：<c>"Percent"</c>、<c>"Credits"</c>、<c>"WeeklyLimit"</c> 等。
    /// </summary>
    [Obsolete("已由 AccountCustomizations 取代，仅保留读取兼容")]
    public Dictionary<string, List<string>> ProviderEnabledRingChartMetrics { get; set; } = new();

    /// <summary>req-026：环形图中心数字"全局已启用 metric key"列表（Provider 单独配置缺失时使用）。</summary>
    public List<string> GlobalEnabledRingChartMetrics { get; set; } = new() { "Percent" };

    /// <summary>
    /// req-022：任务栏显示的全局默认模式（当某 Provider 没有单独覆盖时使用）。默认 <see cref="TaskbarDisplayMode.Text"/>。
    /// <para>
    /// 兼容旧配置：旧版没有 <c>GlobalTaskbarMode</c> 字段时，反序列化后默认 Text；UI 端通过
    /// <see cref="UsageMonitor.App.Helpers.TaskbarModeResolver"/> 与 <see cref="ProviderTaskbarModes"/> 合并解析。
    /// </para>
    /// </summary>
    public TaskbarDisplayMode GlobalTaskbarMode { get; set; } = TaskbarDisplayMode.Text;

    /// <summary>圆环图警告阈值（百分比，达到后切到琥珀色，默认 60）</summary>
    public int RingChartWarningThreshold { get; set; } = 60;

    /// <summary>圆环图危险阈值（百分比，达到后切到红色，默认 85）</summary>
    public int RingChartDangerThreshold { get; set; } = 85;

    /// <summary>应用外观主题（深色 / 浅色）。启动时由 ThemeManager 应用，默认深色。</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>各 Provider 在主窗口卡片中展示的图表类型（key=ProviderId，缺省为 None 仅进度条）。
    /// <para>遗留的「单选」字段：仅用于向 <see cref="ProviderCardChartKinds"/> 迁移旧配置，新逻辑一律读写多选集合。</para></summary>
    [Obsolete("已由 AccountCustomizations 取代，仅保留读取兼容")]
    public Dictionary<string, CardChartKind> ProviderCardCharts { get; set; } = new();

    /// <summary>
    /// 各 Provider 在主窗口卡片中展示的图表类型「集合」（多选，key=ProviderId）。
    /// <para>
    /// 取代原先的单选 <see cref="ProviderCardCharts"/>：一个插件可同时勾选多个图表（如 MiniMax 的折线图 + 热力图），
    /// 卡片会按此集合叠加展示。首次从旧配置迁移时，会把 <see cref="ProviderCardCharts"/> 中的单值包装成单元素列表。
    /// 空列表或缺省表示不显示任何卡片图表（仅保留进度条）。
    /// </para>
    /// </summary>
    [Obsolete("已由 AccountCustomizations 取代，仅保留读取兼容")]
    public Dictionary<string, List<CardChartKind>> ProviderCardChartKinds { get; set; } = new();

    /// <summary>
    /// req-107 B7：账号级用户定制（key = <c>ProviderId:AccountId</c>，见 <see cref="AccountCustomization.MakeKey"/>）。
    /// <para>主程序最终视图 = 插件 defaults.json 默认 + 本账号级覆盖；同 Provider 多账号互不影响（供 req-109 多账号 UI）。
    /// 旧的 Provider 级零散字典（<see cref="ProviderCardCharts"/> / <see cref="ProviderCardChartKinds"/> 等）过渡期保留兼容读取，逐步合并到本结构。</para>
    /// </summary>
    public Dictionary<string, AccountCustomization> AccountCustomizations { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 托盘悬浮窗位置（屏幕坐标系，单位：像素）。
    /// <list type="bullet">
    /// <item><description>null = 从未拖拽过，使用默认行为（弹出于托盘图标/光标附近）</description></item>
    /// <item><description>非 null = 用户拖拽后保存的坐标，下次弹出悬浮窗直接用此位置</description></item>
    /// </list>
    /// 屏幕坐标系遵循 WPF SystemParameters.PrimaryScreen*，原点在左上角，正方向向右/向下。
    /// </summary>
    public TrayTooltipPosition? TrayTooltipPosition { get; set; }

    /// <summary>
    /// 任务栏窗口在父任务栏坐标系内的水平相对位置（0~1 浮点）。
    /// <list type="bullet">
    /// <item><description>0 = 任务栏最左</description></item>
    /// <item><description>1 = 任务栏最右（贴近通知区域）</description></item>
    /// <item><description>null = 从未拖拽过，使用默认位置（任务栏右端留通知区域）</description></item>
    /// </list>
    /// 任务栏宽度变化或 DPI 变更后自动适配，与绝对像素相比不会越界。
    /// </summary>
    public double? TaskbarRelativeX { get; set; }

    /// <summary>
    /// 任务栏窗口用户手动拖拽两侧边缘调整并持久化的宽度（像素）。
    /// <list type="bullet">
    /// <item><description>null = 从未手动调整，使用内容自适应默认宽度（文字模式按内容测量）</description></item>
    /// <item><description>非 null = 用户拖拽保存的固定宽度，优先于自适应</description></item>
    /// </list>
    /// </summary>
    public double? TaskbarWidth { get; set; }

    /// <summary>
    /// 用量色阶配置（按已用百分比换色的阈值 + 颜色 + 是否启用）。
    /// <para>
    /// 为空时由 <see cref="GetEffectiveUsageTierConfig"/> 回退到出厂默认 4 档（低/注意/中/高）。
    /// 详情见 <see cref="UsageTierConfig.Defaults"/>。
    /// </para>
    /// </summary>
    public List<UsageMonitor.Core.Models.UsageTierConfig> UsageTierConfig { get; set; } = new();

    // =====================================================================
    // REQ-009 热力图色阶配置（按 ProviderId 独立配置 token 绝对值分档表）
    // 默认填入 MiniMax 出厂 6 档；其他 Provider 不填时走 HeatMapTierScale.GenericDefaults 兑底。
    // =====================================================================

    /// <summary>
    /// REQ-009：按 ProviderId 索引的热力图色阶表（持久化）。
    /// <para>
    /// key 为 ProviderId（不区分大小写），value 为该 Provider 的色阶档位列表。
    /// 某 Provider 缺失或 value 为空时 <c>HeatMapTierScale.ResolveBrush</c> 走 <see cref="UsageMonitor.App.Helpers.HeatMapTierScale.GenericDefaults"/> 兑底。
    /// 设置页保存后会被 <c>App.OnStartup</c> 加载 + <c>HeatMapTierScale.ApplyConfig</c> 应用。
    /// </para>
    /// <para>
    /// req-066 A8：移除 minimax 业务耦合，默认值改为空字典。
    /// 启动时由 App.OnStartup 从 <see cref="IUsageProvider.HeatMapTiers"/> 装配各 Provider 的默认色阶。
    /// </para>
    /// </summary>
    public Dictionary<string, System.Collections.Generic.IList<UsageMonitor.Core.Models.HeatMapTierConfig>> ProviderHeatMapTiers { get; set; } = new();

    // =====================================================================
    // REQ-012 历史窗口 Provider 列表与插件启用状态联动 + 卸载清理工作流
    // - UninstalledProviderChoices: 记录用户对每个已卸载 Provider 的选择（"deleted"/"kept"），
    //   避免每次启动反复弹"是否删除历史数据"对话框。
    // - LastKnownInstalledPluginIds: 上次启动时已安装插件 ID 列表，用于本次启动时
    //   对比检测哪些插件被卸载。
    // =====================================================================

    /// <summary>
    /// req-012：已卸载 Provider 的用户选择（"deleted"=删除 / "kept"=保留）。
    /// <para>
    /// 启动时检测到新卸载的 Provider → 弹批量对话框 → 一次性选"删/保" → 写入此字典。
    /// 下次启动遇到同 ID 不再询问（已记录过选择）。
    /// </para>
    /// </summary>
    public Dictionary<string, string> UninstalledProviderChoices { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// req-012：上次启动时已安装的插件 ID 列表（用于本次启动对比检测哪些被卸载）。
    /// <para>
    /// 启动时与当前 <c>PluginManager.Plugins</c> 的 providerId 集合对比，差集 = 新卸载的 Provider。
    /// 首次启动时该字段为空（默认 new list），不会误弹对话框。
    /// </para>
    /// </summary>
    public List<string> LastKnownInstalledPluginIds { get; set; } = new();

    // =====================================================================
    // REQ-003 Taskbar 环形图增强
    // 详见 .dev_require/req-003-taskbar-ring-chart.md §5。
    // - RingChartMetricOrder：metric 切换顺序（元素为 RingChartMetricKeys 常量）。
    //   缺省或为空时由 RingChartMetricKeys.DefaultOrder 兜底；MainViewModel 在装配时再回退一次。
    // - RingChartStickySeconds：double，鼠标离开后回默认的等待秒数；<=0 表示永不回到默认。
    // - RingChartSwitchAnimationMs：int，数字切换老虎机动画时长（毫秒）；<=0 表示禁用动画。
    // =====================================================================

    /// <summary>
    /// REQ-003 §5：Taskbar 环形图中心数字的切换顺序。元素为 <see cref="RingChartMetricKeys"/> 常量字符串（未来可放自定义 IRingMetric 键）。
    /// 缺省或为空时由 <see cref="RingChartMetricKeys.DefaultOrder"/> 兜底。
    /// </summary>
    public List<string> RingChartMetricOrder { get; set; } = new(RingChartMetricKeys.DefaultOrder);

    /// <summary>REQ-003 §5：鼠标离开环形图后回默认 metric 的等待秒数（默认 5.0，0 = 永不回到默认）。</summary>
    public double RingChartStickySeconds { get; set; } = 5.0;

    /// <summary>REQ-003 §5：数字切换老虎机动画时长（毫秒，默认 180，0 = 禁用动画）。</summary>
    public int RingChartSwitchAnimationMs { get; set; } = 180;

    // =====================================================================
    // REQ-004 托盘悬浮窗触发区域矩形
    // 详见 .dev_require/req-004-tray-tooltip-trigger-area.md §1。
    // 与 TrayTooltipPosition 共存而非替代：后者记录悬浮窗窗口位置（左上角 X/Y），
    // 本字段记录「鼠标光标必须命中才会弹出悬浮窗」的命中矩形（X/Y/Width/Height）。
    // 触发区域与窗口位置独立：用户可单独把窗口拖到触发区域外。
    // 默认值 RectInt.DefaultBottomRight() 给屏幕右下角一块（240x120）；
    // 多显示器场景保存绝对坐标，重启时若屏幕拓扑变化由 RectInt.ClampToScreen 兜底回主屏。
    // =====================================================================

    /// <summary>REQ-004：托盘悬浮窗触发区域矩形（屏幕坐标系绝对坐标，单位像素）。</summary>
    public Models.RectInt TrayTooltipTriggerRect { get; set; } = Models.RectInt.DefaultBottomRight();

    // Stage B：req-021 的 LastCleanedZeroTokensAt 已随 token=0 清理退役移除（旧配置文件中的残留键反序列化时自动忽略）。

    /// <summary>
    /// req-090-003：Cookie 保留天数（默认 90 天，范围 7-365）。超过此天数的 Cookie 文件在启动时被清理。
    /// </summary>
    public int CookieRetentionDays { get; set; } = 90;

    // =====================================================================
    // REQ-103 卡片排序功能
    // =====================================================================

    /// <summary>
    /// req-103：用户自定义的卡片显示顺序（ProviderId 列表）。
    /// <para>
    /// 空列表或缺失时回退到默认启用顺序；支持 Provider 级别和账号级别的混合排序。
    /// 调整顺序后保存 → ConfigChanged 事件 → MainViewModel 重新排序 EnabledUsages。
    /// </para>
    /// </summary>
    [Obsolete("已由 AccountCustomizations 取代，仅保留读取兼容")]
    public List<string> ProviderCardOrder { get; set; } = new();

    // =====================================================================
    // REQ-104 多进度条与数字多排显示
    // =====================================================================

    /// <summary>
    /// req-104：用户选择的进度条显示字段（key=ProviderId，value=字段名列表）。
    /// <para>
    /// 空列表或缺失时显示插件声明的全部进度条；非空时仅显示选中的字段。
    /// 字段名对应 <see cref="UsageMonitor.Core.Models.MetricBarItem.Label"/>。
    /// </para>
    /// </summary>
    public Dictionary<string, List<string>> SelectedProgressFields { get; set; } = new();

    /// <summary>
    /// req-104：用户选择的数字多排显示字段（key=ProviderId，value=字段名列表）。
    /// <para>
    /// 空列表或缺失时显示插件声明的全部数字项；非空时仅显示选中的字段。
    /// 字段名对应 <see cref="UsageMonitor.Core.Models.MetricGridItem.Label"/>。
    /// </para>
    /// </summary>
    public Dictionary<string, List<string>> SelectedMetricFields { get; set; } = new();

    // =====================================================================
    // REQ-097 卡片图表顺序用户可调整
    // =====================================================================

    /// <summary>
    /// req-097：用户自定义的卡片图表显示顺序（key=ProviderId，value=图表类型列表）。
    /// <para>
    /// 空列表或缺失时回退到插件声明的 <see cref="IUsageProvider.SupportedCardCharts"/> 顺序；
    /// 调整顺序后保存 → ConfigChanged 事件 → MainViewModel 重新排序图表。
    /// </para>
    /// </summary>
    public Dictionary<string, List<CardChartKind>> ProviderChartOrder { get; set; } = new();
}

/// <summary>
/// 托盘悬浮窗拖拽后保存的位置（屏幕坐标系 X/Y，设备无关单位 DIP）。
/// 独立于窗口尺寸：只记住位置，宽高仍按 WPF 实际渲染值计算。
/// </summary>
public class TrayTooltipPosition
{
    /// <summary>悬浮窗左上角的 X 坐标（屏幕坐标系）</summary>
    public double X { get; set; }

    /// <summary>悬浮窗左上角的 Y 坐标（屏幕坐标系）</summary>
    public double Y { get; set; }
}

/// <summary>
/// 配置管理服务 - 负责读写应用配置，支持API Key加密存储
/// 配置文件保存在 %AppData%/UsageMonitor/config.json
/// </summary>
public class ConfigService : IConfigService
{
    /// <summary>req-060：序列化配置复用（写入用，带缩进）。</summary>
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>req-060：反序列化配置复用（读取用，大小写不敏感）。</summary>
    internal static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private AppSettings _settings;

    /// <summary>配置文件读写互斥锁：保证多线程下 Save/Load/UpdateProviderConfig/ReloadProviderConfigsFromDisk 的原子性，避免相互覆盖或写坏文件。</summary>
    private readonly object _ioLock = new();

    /// <summary>当前应用配置（注意：直接修改字段不经过 _ioLock，并发场景应使用 <see cref="UpdateSettings"/>）</summary>
    public AppSettings Settings => _settings;

    /// <summary>req-089：暴露 config.json 完整路径，供 ACL 收紧使用</summary>
    public string ConfigFilePath => _configFilePath;

    /// <summary>
    /// req-057：线程安全的配置修改 API。在 _ioLock 内执行 mutator，避免与并发 Save/Load 产生竞态。
    /// 修改后不自动持久化，调用方需显式调用 <see cref="Save"/> 持久化。
    /// </summary>
    /// <param name="mutator">在锁内执行的修改委托</param>
    public void UpdateSettings(Action<AppSettings> mutator)
    {
        lock (_ioLock)
        {
            mutator(_settings);
        }
    }

    /// <summary>配置变更事件</summary>
    public event EventHandler? ConfigChanged;

    /// <summary>
    /// 上次 Save() 失败的错误信息（null 表示成功）。
    /// UI 可以在保存后检查这个字段，提示用户磁盘满/权限不足等问题。
    /// </summary>
    public string? LastSaveError { get; private set; }

    /// <summary>
    /// 上次 Load() 失败的错误信息（null 表示成功）。
    /// </summary>
    public string? LastLoadError { get; private set; }

    /// <summary>
    /// 创建配置服务实例
    /// </summary>
    public ConfigService()
    {
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsageMonitor");
        _configFilePath = Path.Combine(_configDirectory, "config.json");
        _settings = new AppSettings();
    }

    /// <summary>
    /// 加载配置文件。
    /// 反序列化后做一次后置归一化：<see cref="AppSettings.RingChartMetricOrder"/> 为空时回填默认顺序，
    /// 触发区域矩形若宽高异常（&lt;0 或 NaN）则用 <see cref="Models.RectInt.DefaultBottomRight"/> 兜底，
    /// 避免升级到 REQ-003/REQ-004 后用户配置无关键导致 UI 闪烁或崩溃。
    /// </summary>
    public void Load()
    {
        // 读写主体加锁，避免与并发 Save 相互干扰。lock 可重入：文件不存在时内部调用的 Save() 会再次进入同一把锁。
        lock (_ioLock)
        {
            if (!File.Exists(_configFilePath))
            {
                _settings = new AppSettings();
                Save();
                return;
            }

            try
            {
                var json = File.ReadAllText(_configFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidDataException("配置文件为空（可能上次写入被中断）。");
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                // 解密敏感字段
                DecryptSensitiveFields();
            }
            catch (Exception ex)
            {
                LastLoadError = $"{ex.GetType().Name}: {ex.Message}";
                FileLogger.Error("ConfigService", $"加载配置失败: {ex.Message}", ex);
                // 保护用户配置：优先从上次成功保存的 .bak 恢复；恢复失败再备份损坏文件并回退到默认配置。
                // 避免「config.json 被写坏 → 静默重置为空 → 插件启用状态 / Cookie 等全部丢失」。
                if (!TryRecoverFromBackup())
                {
                    BackupCorruptedConfig();
                    _settings = new AppSettings();
                }
            }

            // 后置归一化：兼容旧版用户配置（缺新字段时回退默认）。
            NormalizeAfterLoad();
        }
    }

    /// <summary>
    /// 反序列化后做一次性归一化（REQ-003 / REQ-004 字段兜底）。
    /// 在锁内调用，调用方保证已持有 _ioLock。
    /// </summary>
    private void NormalizeAfterLoad()
    {
        if (_settings.RingChartMetricOrder == null || _settings.RingChartMetricOrder.Count == 0)
        {
            _settings.RingChartMetricOrder = new List<string>(RingChartMetricKeys.DefaultOrder);
        }

        if (_settings.TrayTooltipTriggerRect.Width <= 0 || _settings.TrayTooltipTriggerRect.Height <= 0)
        {
            _settings.TrayTooltipTriggerRect = Models.RectInt.DefaultBottomRight();
        }

        // sticky / animation 极值保护
        if (_settings.RingChartStickySeconds < 0) _settings.RingChartStickySeconds = 0;
        if (_settings.RingChartSwitchAnimationMs < 0) _settings.RingChartSwitchAnimationMs = 0;

        // req-060：RefreshIntervalSeconds 钳制到合理范围（30秒~24小时），避免 int 乘法溢出
        _settings.RefreshIntervalSeconds = Math.Clamp(_settings.RefreshIntervalSeconds, 30, 86400);

        // req-095：托盘悬浮窗关闭延迟钳制到 100-5000ms，非法值（旧配置或手改 JSON）自动修正。
        _settings.TrayTooltipHideDelayMs = Math.Clamp(_settings.TrayTooltipHideDelayMs, 100, 5000);
    }

    /// <summary>
    /// 尝试从上次成功保存留下的 <c>config.json.bak</c>（由原子写入 File.Replace 生成）恢复配置。
    /// 恢复成功后解密敏感字段并原子写回正式文件，返回 true。
    /// </summary>
    private bool TryRecoverFromBackup()
    {
        var bakPath = _configFilePath + ".bak";
        if (!File.Exists(bakPath)) return false;
        try
        {
            var json = File.ReadAllText(bakPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return false;
            var recovered = JsonSerializer.Deserialize<AppSettings>(json);
            if (recovered == null) return false;
            _settings = recovered;
            DecryptSensitiveFields();
            FileLogger.Warn("ConfigService", "config.json 损坏，已从 config.json.bak 成功恢复配置。");
            Save(); // 原子写回，修复损坏的正式文件（_ioLock 可重入）
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("ConfigService", $"从 config.json.bak 恢复失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>把损坏的 config.json 复制一份备份（.corrupted-时间戳），便于事后排查/手工恢复，避免被后续 Save 覆盖丢失。</summary>
    private void BackupCorruptedConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath)) return;
            var dst = _configFilePath + $".corrupted-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_configFilePath, dst, overwrite: true);
            FileLogger.Warn("ConfigService", $"已备份损坏的配置到 {Path.GetFileName(dst)}");
        }
        catch { /* 备份失败不阻断启动 */ }
    }

    /// <summary>
    /// 保存配置到文件。
    /// <para>req-057-004：完全把耗时的 JSON 序列化移出 _ioLock。锁内仅调用 <see cref="MakeSnapshot"/>
    /// 做 O(n) 字典/集合的浅拷贝，Phase 2 锁外执行加密 + JSON 深拷贝序列化 + 文件写入。</para>
    /// <para>snapshot 与 _settings 解耦，Phase 2 在锁外对 snapshot 做 JsonSerializer.Serialize 时，
    /// _settings 可以被并发的 <see cref="UpdateSettings"/> / <see cref="UpdateProviderConfig"/> 修改
    /// 而不影响 snapshot 的序列化结果。</para>
    /// </summary>
    public void Save()
    {
        LastSaveError = null;
        bool changed = false;
        AppSettings? snapshot = null;

        // Phase 1（锁内）：仅做内存浅拷贝（O(n) 字典/集合复制），不做 JSON round-trip。
        // 锁内持锁时间从毫秒级降到微秒级，并发 Save/UpdateSettings 不再相互阻塞。
        lock (_ioLock)
        {
            try
            {
                snapshot = MakeSnapshot();
            }
            catch (Exception ex)
            {
                LastSaveError = $"{ex.GetType().Name}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"保存配置失败(快照阶段): {ex.Message}");
            }
        }

        // Phase 2（锁外）：耗时的加密 + JSON 深拷贝序列化 + 文件写入。
        // req-057：跨进程 Mutex 保护文件写入，避免与 LoginHelper 等外部进程同时写 config.json
        if (snapshot != null)
        {
            try
            {
                if (!Directory.Exists(_configDirectory))
                    Directory.CreateDirectory(_configDirectory);

                EncryptSensitiveFields(snapshot);
                // 锁外做 JSON 序列化——这才是 req-057-004 要求"移出锁内"的真正耗时点。
                var json = JsonSerializer.Serialize(snapshot, s_writeOptions);

                using var crossProcessMutex = new Mutex(false, "Global\\UsageMonitor-ConfigService");
                bool mutexAcquired = false;
                try
                {
                    mutexAcquired = crossProcessMutex.WaitOne(TimeSpan.FromSeconds(5));
                    var tmpPath = _configFilePath + ".tmp";
                    File.WriteAllText(tmpPath, json, Encoding.UTF8);
                    if (new FileInfo(tmpPath).Length <= 0)
                        throw new IOException("写入临时配置文件后大小为 0，放弃替换以保护原配置。");
                    if (File.Exists(_configFilePath))
                        File.Replace(tmpPath, _configFilePath, _configFilePath + ".bak", ignoreMetadataErrors: true);
                    else
                        File.Move(tmpPath, _configFilePath);
                    changed = true;
                }
                finally
                {
                    if (mutexAcquired) crossProcessMutex.ReleaseMutex();
                }
            }
            catch (Exception ex)
            {
                LastSaveError = $"{ex.GetType().Name}: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"保存配置失败(写入阶段): {ex.Message}");
            }
        }

        // REQ-089 Bug fix: Save 路径上的 MakeSnapshot() 是浅拷贝 (ConfigService.cs:516
        // ProviderConfigs = new Dictionary<...>(_settings.ProviderConfigs))，
        // 导致 EncryptSensitiveFields(snapshot) 原地改写了 _settings 里的明文 Cookie 为
        // DPAPI 密文。下次 GetUsageAsync 读 config.GetValue("Cookie") 拿到的是密文，
        // cookie.Contains("_token=") 返回 false，传入 MiniMax 服务端后被 1016 invalid api key
        // 拒绝。修复：写盘完成后立即把 _settings 还原成明文，保证业务代码读到的永远是可用明文。
        // DecryptSensitiveFields 内部 catch 单个字段失败，不影响其他字段。
        if (changed)
        {
            try
            {
                DecryptSensitiveFields();
            }
            catch (Exception ex)
            {
                // 还原失败不应阻塞 ConfigChanged 通知；记录告警供诊断。
                FileLogger.Warn("ConfigService",
                    $"Save 写盘后还原明文失败，下次读取 config 可能拿到密文: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (changed)
            ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// req-057-004：锁内调用的浅快照方法。对顶层字典/集合做 O(n) 复制，不做任何 JSON round-trip。
    /// 返回的 snapshot 与 _settings 解耦，后续在锁外对 snapshot 做 JSON 序列化是安全的。
    /// <para>调用方必须保证已持有 <c>_ioLock</c>，否则与并发 <see cref="UpdateProviderConfig"/> /
    /// <see cref="UpdateSettings"/> 会产生字典迭代期间的修改异常。</para>
    /// <para>深拷贝责任由 Phase 2 的 <c>JsonSerializer.Serialize + Deserialize</c> 承担：
    /// 这里只保证顶层集合是独立副本。ProviderConfig 内部的 <c>Values</c> 已是
    /// <c>ConcurrentDictionary</c>，序列化期间其枚举安全；其他不可变值类型
    /// （string/enum/数值）引用复制即可。</para>
    /// </summary>
    private AppSettings MakeSnapshot()
    {
        // 直接构造一个 AppSettings 并赋值所有字段（不调 JSON 序列化）。
        // 顶层字典/集合全部 ToDictionary/ToList 复制为独立实例。
        var snapshot = new AppSettings
        {
            RefreshIntervalSeconds = _settings.RefreshIntervalSeconds,
            ShowInTaskbar = _settings.ShowInTaskbar,
            AutoStart = _settings.AutoStart,
            MinimizeToTray = _settings.MinimizeToTray,
            TaskbarDisplayProviders = new List<string>(_settings.TaskbarDisplayProviders),
            ProviderConfigs = new Dictionary<string, ProviderConfig>(_settings.ProviderConfigs),
            PluginEnabled = new Dictionary<string, bool>(_settings.PluginEnabled),
            HistoryPointCount = _settings.HistoryPointCount,
            ShowTrayTooltip = _settings.ShowTrayTooltip,
            TrayTooltipHideDelayMs = _settings.TrayTooltipHideDelayMs,
            TrayTriggerWidth = _settings.TrayTriggerWidth,
            TrayTriggerHeight = _settings.TrayTriggerHeight,
            TrayTooltipWindowWidth = _settings.TrayTooltipWindowWidth,
            TaskbarMiniChartConfigs = new Dictionary<string, Models.TaskbarMiniChartConfig>(),
            ProviderTaskbarModes = new Dictionary<string, TaskbarDisplayMode>(_settings.ProviderTaskbarModes),
            ProviderEnabledRingChartMetrics = new Dictionary<string, List<string>>(),
            GlobalEnabledRingChartMetrics = new List<string>(_settings.GlobalEnabledRingChartMetrics),
            GlobalTaskbarMode = _settings.GlobalTaskbarMode,
            RingChartWarningThreshold = _settings.RingChartWarningThreshold,
            RingChartDangerThreshold = _settings.RingChartDangerThreshold,
            Theme = _settings.Theme,
            ProviderCardCharts = new Dictionary<string, CardChartKind>(_settings.ProviderCardCharts),
            ProviderCardChartKinds = new Dictionary<string, List<CardChartKind>>(),
            TrayTooltipPosition = _settings.TrayTooltipPosition == null
                ? null
                : new TrayTooltipPosition { X = _settings.TrayTooltipPosition.X, Y = _settings.TrayTooltipPosition.Y },
            TaskbarRelativeX = _settings.TaskbarRelativeX,
            TaskbarWidth = _settings.TaskbarWidth,
            UsageTierConfig = _settings.UsageTierConfig == null
                ? new List<UsageMonitor.Core.Models.UsageTierConfig>()
                : new List<UsageMonitor.Core.Models.UsageTierConfig>(_settings.UsageTierConfig),
            ProviderHeatMapTiers = new Dictionary<string, System.Collections.Generic.IList<UsageMonitor.Core.Models.HeatMapTierConfig>>(_settings.ProviderHeatMapTiers),
            UninstalledProviderChoices = new Dictionary<string, string>(_settings.UninstalledProviderChoices, StringComparer.OrdinalIgnoreCase),
            LastKnownInstalledPluginIds = new List<string>(_settings.LastKnownInstalledPluginIds),
            RingChartMetricOrder = new List<string>(_settings.RingChartMetricOrder),
            RingChartStickySeconds = _settings.RingChartStickySeconds,
            RingChartSwitchAnimationMs = _settings.RingChartSwitchAnimationMs,
            TrayTooltipTriggerRect = _settings.TrayTooltipTriggerRect,
            CookieRetentionDays = _settings.CookieRetentionDays,
            ProviderCardOrder = new List<string>(_settings.ProviderCardOrder),
            SelectedProgressFields = new Dictionary<string, List<string>>(),
            SelectedMetricFields = new Dictionary<string, List<string>>(),
            ProviderChartOrder = new Dictionary<string, List<CardChartKind>>(),
        };

        // 顶层值为 List<T> 的字典需要逐项深拷贝到新 List 中（避免快照与 _settings 共享 List 引用）。
        foreach (var kvp in _settings.ProviderEnabledRingChartMetrics)
            snapshot.ProviderEnabledRingChartMetrics[kvp.Key] = new List<string>(kvp.Value);
        foreach (var kvp in _settings.ProviderCardChartKinds)
            snapshot.ProviderCardChartKinds[kvp.Key] = new List<CardChartKind>(kvp.Value);
        foreach (var kvp in _settings.SelectedProgressFields)
            snapshot.SelectedProgressFields[kvp.Key] = new List<string>(kvp.Value);
        foreach (var kvp in _settings.SelectedMetricFields)
            snapshot.SelectedMetricFields[kvp.Key] = new List<string>(kvp.Value);
        foreach (var kvp in _settings.ProviderChartOrder)
            snapshot.ProviderChartOrder[kvp.Key] = new List<CardChartKind>(kvp.Value);
        // req-098：TaskbarMiniChartConfig 是引用类型，逐项 new 一个独立副本避免快照与 _settings 共享引用。
        foreach (var kvp in _settings.TaskbarMiniChartConfigs)
            snapshot.TaskbarMiniChartConfigs[kvp.Key] = new Models.TaskbarMiniChartConfig
            {
                IsVisible = kvp.Value.IsVisible,
                ChartKind = kvp.Value.ChartKind,
                ContentKind = kvp.Value.ContentKind,
                SecondaryKind = kvp.Value.SecondaryKind,
                ShowLogo = kvp.Value.ShowLogo
            };

        // Phase 2 修复：补齐 Accounts 深拷贝（逐项 new Account 复制全部属性）。
        snapshot.Accounts = new List<Models.Account>(_settings.Accounts.Count);
        foreach (var acc in _settings.Accounts)
        {
            snapshot.Accounts.Add(new Models.Account
            {
                ProviderId = acc.ProviderId,
                AccountId = acc.AccountId,
                Nickname = acc.Nickname,
                UseNickname = acc.UseNickname,
                CreatedAt = acc.CreatedAt,
                IsDefault = acc.IsDefault,
                Enabled = acc.Enabled
            });
        }

        // Phase 2 修复：补齐 PersistedLoginStates 深拷贝（逐项 new LoginStateInfo 复制全部属性）。
        snapshot.PersistedLoginStates = new List<LoginStateInfo>(_settings.PersistedLoginStates.Count);
        foreach (var ls in _settings.PersistedLoginStates)
        {
            snapshot.PersistedLoginStates.Add(new LoginStateInfo
            {
                AcquiredAt = ls.AcquiredAt,
                AccountId = ls.AccountId,
                ProviderId = ls.ProviderId
            });
        }

        // Phase 2 修复：补齐 AccountCustomizations 深拷贝（保留原字典比较器，值调用 Clone()）。
        snapshot.AccountCustomizations = new Dictionary<string, AccountCustomization>(
            _settings.AccountCustomizations.Count, System.StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _settings.AccountCustomizations)
            snapshot.AccountCustomizations[kvp.Key] = kvp.Value.Clone();

        return snapshot;
    }

    /// <summary>
    /// Re-load provider config from disk into memory. Used after external tools (e.g. the
    /// BrowserLoginService) write <c>config.json</c> directly without going through
    /// <see cref="UpdateProviderConfig"/>, so the in-memory <see cref="ProviderConfig"/>
    /// for the same provider must be refreshed.
    /// <para>
    /// Implementation: re-read the file, replace <c>_settings.ProviderConfigs</c> with the
    /// freshly-loaded provider configs (preserves any other app settings). Triggers
    /// <see cref="ConfigChanged"/> so subscribers re-read state.
    /// </para>
    /// </summary>
    public void ReloadProviderConfigsFromDisk()
    {
        bool changed = false;
        // 读文件 + 替换字典加锁；事件锁外触发。
        lock (_ioLock)
        {
            try
            {
                if (!File.Exists(_configFilePath)) return;
                var json = File.ReadAllText(_configFilePath, Encoding.UTF8);
                var fresh = JsonSerializer.Deserialize<AppSettings>(json, s_readOptions);
                if (fresh?.ProviderConfigs != null)
                {
                    // Replace only the ProviderConfigs dict, keep other settings
                    _settings.ProviderConfigs = fresh.ProviderConfigs;
                    // req-068 F-21：磁盘上敏感字段是 DPAPI 密文，必须解密后再赋值给内存
                    DecryptSensitiveFields();
                    FileLogger.Info("ConfigService",
                        $"Reloaded ProviderConfigs from disk. Count={fresh.ProviderConfigs.Count}");
                }
                changed = true;
            }
            catch (Exception ex)
            {
                FileLogger.Error("ConfigService",
                    $"ReloadProviderConfigsFromDisk failed: {ex.Message}", ex);
            }
        }

        if (changed)
            ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 获取指定服务商的配置（不存在则创建默认配置）
    /// </summary>
    public ProviderConfig GetProviderConfig(string providerId, IUsageProvider? provider = null)
    {
        if (!_settings.ProviderConfigs.TryGetValue(providerId, out var config))
        {
            config = new ProviderConfig { ProviderId = providerId };

            // 使用插件定义的默认值填充
            if (provider != null)
            {
                foreach (var field in provider.ConfigFields)
                {
                    if (!string.IsNullOrEmpty(field.DefaultValue))
                        config.SetValue(field.Key, field.DefaultValue);
                }
            }

            _settings.ProviderConfigs[providerId] = config;
        }
        return config;
    }

    /// <summary>
    /// 更新指定服务商的配置
    /// </summary>
    public void UpdateProviderConfig(string providerId, ProviderConfig config)
    {
        // 先在锁内更新内存字典，再调用 Save()（Save 自身加锁并在锁外触发 ConfigChanged）。
        lock (_ioLock)
        {
            _settings.ProviderConfigs[providerId] = config;
        }
        Save();
    }

    /// <summary>
    /// 获取当前生效的用量色阶配置（<see cref="AppSettings.UsageTierConfig"/> 为空时返回出厂默认 4 档）。
    /// <para>
    /// 返回一个新 List（不返回内部引用），避免调用方直接修改内部集合引发不一致；序列化时由 <c>JsonSerializer</c> 负责写回。
    /// </para>
    /// </summary>
    public List<UsageMonitor.Core.Models.UsageTierConfig> GetEffectiveUsageTierConfig()
    {
        lock (_ioLock)
        {
            if (_settings.UsageTierConfig != null && _settings.UsageTierConfig.Count > 0)
                return new List<UsageMonitor.Core.Models.UsageTierConfig>(_settings.UsageTierConfig);
            return UsageMonitor.Core.Models.UsageTierConfig.Defaults();
        }
    }

    /// <summary>
    /// 写入用量色阶配置（仅更新内存，不自动 Save；调用方控制持久化时机以实现"先预览后保存"语义）。
    /// </summary>
    /// <param name="tiers">新的档位集合（按调用方意愿的顺序传入；运行时会再按 MinPercent 升序排序）。</param>
    public void SetUsageTierConfig(IReadOnlyList<UsageMonitor.Core.Models.UsageTierConfig> tiers)
    {
        lock (_ioLock)
        {
            _settings.UsageTierConfig = tiers != null
                ? new List<UsageMonitor.Core.Models.UsageTierConfig>(tiers)
                : new List<UsageMonitor.Core.Models.UsageTierConfig>();
        }
    }

    /// <summary>
    /// 获取指定 Provider 当前的「卡片图表类型集合」（多选）。
    /// <para>
    /// 兼容旧配置：若多选字典 <see cref="AppSettings.ProviderCardChartKinds"/> 尚无该 Provider，
    /// 但旧单选 <see cref="AppSettings.ProviderCardCharts"/> 中存在且非 None，则把单值迁移为单元素列表并写回内存（下次 Save 一并持久化）。
    /// 返回列表为副本，避免调用方直接改动内部集合。
    /// </para>
    /// </summary>
    public List<CardChartKind> GetProviderCardChartKinds(string providerId)
    {
        lock (_ioLock)
        {
            if (_settings.ProviderCardChartKinds.TryGetValue(providerId, out var list) && list != null)
                return new List<CardChartKind>(list);

            // 迁移旧单选值（非 None 时包装为单元素列表）
            if (_settings.ProviderCardCharts.TryGetValue(providerId, out var single)
                && single != CardChartKind.None)
            {
                var migrated = new List<CardChartKind> { single };
                _settings.ProviderCardChartKinds[providerId] = migrated;
                return new List<CardChartKind>(migrated);
            }
            return new List<CardChartKind>();
        }
    }

    /// <summary>
    /// req-109：获取指定 (ProviderId, AccountId, CardId) 的有效账号定制（合并默认 + 旧字典兼容层）。
    /// <para>
    /// 返回 <see cref="AccountCustomization"/>：先读 <see cref="AppSettings.AccountCustomizations"/>
    /// （key = <c>ProviderId:AccountId:CardId</c>），再用旧字典（<see cref="AppSettings.ProviderCardChartKinds"/>、
    /// <see cref="AppSettings.ProviderChartOrder"/>、<see cref="AppSettings.SelectedProgressFields"/>、
    /// <see cref="AppSettings.SelectedMetricFields"/>）填充未在账号定制中设置的字段。
    /// 保证 App 端统一读“合并后视图”，逐步迁移旧数据。
    /// </para>
    /// <para>不返回 null：调用方可直接使用合并结果。</para>
    /// </summary>
    public AccountCustomization GetEffectiveAccountCustomization(string providerId, string accountId = "default", string cardId = "default-card")
    {
        lock (_ioLock)
        {
            var key = AccountCustomization.MakeKey(providerId, accountId, cardId);
            // 先拷贝账号定制（避免调用方修改内部）
            var effective = new AccountCustomization();
            if (_settings.AccountCustomizations.TryGetValue(key, out var acct) && acct != null)
            {
                effective.VisibleCharts = acct.VisibleCharts != null ? new List<string>(acct.VisibleCharts) : null;
                effective.ChartOrders = new Dictionary<string, int>(acct.ChartOrders);
                effective.CurrentDataGroupIds = new Dictionary<string, string>(acct.CurrentDataGroupIds);
                effective.ChartColorTierSources = new Dictionary<string, string>(acct.ChartColorTierSources);
                effective.Nickname = acct.Nickname;
                effective.UseNickname = acct.UseNickname;
                effective.ChartTitles = new Dictionary<string, string>(acct.ChartTitles);
                effective.VisibleProgressFields = acct.VisibleProgressFields != null ? new List<string>(acct.VisibleProgressFields) : null;
                effective.VisibleMetricFields = acct.VisibleMetricFields != null ? new List<string>(acct.VisibleMetricFields) : null;
                effective.VisibleDataGroups = CopyStringToListDict(acct.VisibleDataGroups);
                effective.DataGroupOrders = CopyStringToIntDictDict(acct.DataGroupOrders);
                // req-105 + Mini 任务栏同构（req-109）
                effective.VisibleTooltipFields = CopyStringToListDict(acct.VisibleTooltipFields);
                effective.VisibleMiniCharts = acct.VisibleMiniCharts != null ? new List<string>(acct.VisibleMiniCharts) : null;
                effective.VisibleMiniDataGroups = CopyStringToListDict(acct.VisibleMiniDataGroups);
                effective.MiniDataGroupOrders = CopyStringToIntDictDict(acct.MiniDataGroupOrders);
            }

            // 旧字典兼容填充：仅在账号定制未设置时填入（避免覆盖用户主动的选择）
            if (effective.VisibleCharts == null && _settings.ProviderCardChartKinds.TryGetValue(providerId, out var kinds) && kinds.Count > 0)
            {
                effective.VisibleCharts = kinds.Select(k => k.ToString()).ToList();
            }
            if (effective.ChartOrders.Count == 0 && _settings.ProviderChartOrder.TryGetValue(providerId, out var order) && order.Count > 0)
            {
                for (var i = 0; i < order.Count; i++) effective.ChartOrders[order[i].ToString()] = i;
            }
            if (effective.VisibleProgressFields == null && _settings.SelectedProgressFields.TryGetValue(providerId, out var prog) && prog.Count > 0)
            {
                effective.VisibleProgressFields = new List<string>(prog);
            }
            if (effective.VisibleMetricFields == null && _settings.SelectedMetricFields.TryGetValue(providerId, out var met) && met.Count > 0)
            {
                effective.VisibleMetricFields = new List<string>(met);
            }
            return effective;
        }
    }

    /// <summary>深拷贝 string→List&lt;string&gt;? 字典（用于 VisibleDataGroups）。</summary>
    private static Dictionary<string, List<string>?> CopyStringToListDict(Dictionary<string, List<string>?> src)
    {
        var dst = new Dictionary<string, List<string>?>(src.Count);
        foreach (var kv in src)
        {
            dst[kv.Key] = kv.Value != null ? new List<string>(kv.Value) : null;
        }
        return dst;
    }

    /// <summary>深拷贝 string→Dictionary&lt;string,int&gt; 字典（用于 DataGroupOrders）。</summary>
    private static Dictionary<string, Dictionary<string, int>> CopyStringToIntDictDict(Dictionary<string, Dictionary<string, int>> src)
    {
        var dst = new Dictionary<string, Dictionary<string, int>>(src.Count);
        foreach (var kv in src)
        {
            dst[kv.Key] = new Dictionary<string, int>(kv.Value);
        }
        return dst;
    }

    /// <summary>
    /// req-109 B6 演进：持久化指定 (ProviderId, AccountId, CardId) 的卡片图表配置。
    /// <para>CardId 仅在显示/配置层使用（认证层 AuthManager 仍按二段 key 鉴权）。</para>
    /// </summary>
    public void SetCardChartConfiguration(string providerId, AccountCustomization config, string accountId = "default", string cardId = "default-card")
    {
        lock (_ioLock)
        {
            var key = AccountCustomization.MakeKey(providerId, accountId, cardId);
            if (!_settings.AccountCustomizations.TryGetValue(key, out var acct) || acct == null)
            {
                acct = new AccountCustomization();
                _settings.AccountCustomizations[key] = acct;
            }
            acct.VisibleCharts = config.VisibleCharts != null ? new List<string>(config.VisibleCharts) : null;
            acct.ChartOrders = new Dictionary<string, int>(config.ChartOrders);
            acct.CurrentDataGroupIds = new Dictionary<string, string>(config.CurrentDataGroupIds);
            acct.VisibleDataGroups = CopyStringToListDict(config.VisibleDataGroups);
            acct.DataGroupOrders = CopyStringToIntDictDict(config.DataGroupOrders);
            // req-105：每张图表的 Tooltip 字段随卡片配置一起持久化
            acct.VisibleTooltipFields = CopyStringToListDict(config.VisibleTooltipFields);
        }
        Save();
    }

    /// <summary>
    /// S6：仅写入指定 (ProviderId, AccountId, CardId) 的「可见卡片图表 ID 列表」（<c>VisibleCharts</c>）并持久化。
    /// <para>与 <see cref="SetCardChartConfiguration"/>（设置窗口【卡片管理】页）同一数据落点——
    /// 均写入 <c>AccountCustomizations</c> 字典的 <c>VisibleCharts</c> 字段；
    /// 本方法只更新该单一字段，不触碰 ChartOrders / VisibleDataGroups / VisibleTooltipFields 等兄弟配置，
    /// 避免插件配置窗口的启用开关与卡片管理页互相覆盖造成双写冲突。</para>
    /// </summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    /// <param name="visibleChartIds">可见图表 ID 列表（空集合 = 不显示任何图表）。</param>
    /// <param name="accountId">账号 ID（缺省 "default"）。</param>
    /// <param name="cardId">卡片 ID（缺省 "default-card"）。</param>
    public void SetVisibleCharts(string providerId, IReadOnlyList<string>? visibleChartIds, string accountId = "default", string cardId = "default-card")
    {
        lock (_ioLock)
        {
            var key = AccountCustomization.MakeKey(providerId, accountId, cardId);
            if (!_settings.AccountCustomizations.TryGetValue(key, out var acct) || acct == null)
            {
                acct = new AccountCustomization();
                _settings.AccountCustomizations[key] = acct;
            }
            acct.VisibleCharts = visibleChartIds != null ? new List<string>(visibleChartIds) : null;
        }
        Save();
    }

    /// <summary>
    /// S6：仅写入指定 (ProviderId, AccountId, CardId) 的「可见 Mini 图表 ID 列表」（<c>VisibleMiniCharts</c>）并持久化。
    /// <para>与 <see cref="SetMiniChartConfiguration"/>（设置窗口【任务栏迷你图表】页）同一数据落点，
    /// 只更新 <c>VisibleMiniCharts</c> 单一字段，不影响 VisibleMiniDataGroups / MiniDataGroupOrders。</para>
    /// </summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    /// <param name="visibleMiniChartIds">可见 Mini 图表 ID 列表（空集合 = 不显示任何 Mini 图表）。</param>
    /// <param name="accountId">账号 ID（缺省 "default"）。</param>
    /// <param name="cardId">卡片 ID（缺省 "default-card"）。</param>
    public void SetVisibleMiniCharts(string providerId, IReadOnlyList<string>? visibleMiniChartIds, string accountId = "default", string cardId = "default-card")
    {
        lock (_ioLock)
        {
            var key = AccountCustomization.MakeKey(providerId, accountId, cardId);
            if (!_settings.AccountCustomizations.TryGetValue(key, out var acct) || acct == null)
            {
                acct = new AccountCustomization();
                _settings.AccountCustomizations[key] = acct;
            }
            acct.VisibleMiniCharts = visibleMiniChartIds != null ? new List<string>(visibleMiniChartIds) : null;
        }
        Save();
    }

    /// <summary>
    /// req-109：持久化指定 (ProviderId, AccountId, CardId) 的 Mini 图表配置（Mini 任务栏同构）。
    /// <para>仅持久化 Mini 图表专属字段（VisibleMiniCharts/VisibleMiniDataGroups/MiniDataGroupOrders）；
    /// 其它字段（VisibleCharts 等）走 <see cref="SetCardChartConfiguration"/>。</para>
    /// </summary>
    public void SetMiniChartConfiguration(string providerId, AccountCustomization config, string accountId = "default", string cardId = "default-card")
    {
        lock (_ioLock)
        {
            var key = AccountCustomization.MakeKey(providerId, accountId, cardId);
            if (!_settings.AccountCustomizations.TryGetValue(key, out var acct) || acct == null)
            {
                acct = new AccountCustomization();
                _settings.AccountCustomizations[key] = acct;
            }
            acct.VisibleMiniCharts = config.VisibleMiniCharts != null ? new List<string>(config.VisibleMiniCharts) : null;
            acct.VisibleMiniDataGroups = CopyStringToListDict(config.VisibleMiniDataGroups);
            acct.MiniDataGroupOrders = CopyStringToIntDictDict(config.MiniDataGroupOrders);
        }
        Save();
    }

    /// <summary>
    /// 加密敏感字段（如Password类型的配置值）
    /// </summary>
    private void EncryptSensitiveFields(AppSettings settings)
    {
        foreach (var (_, config) in settings.ProviderConfigs)
        {
            var keysToEncrypt = config.Values.Keys.ToList();
            foreach (var key in keysToEncrypt)
            {
                if (IsSensitiveKey(key) && !string.IsNullOrEmpty(config.Values[key]))
                {
                    config.Values[key] = Encrypt(config.Values[key]);
                }
            }
        }
    }

    /// <summary>
    /// 解密敏感字段
    /// </summary>
    private void DecryptSensitiveFields()
    {
        foreach (var (_, config) in _settings.ProviderConfigs)
        {
            var keysToDecrypt = config.Values.Keys.ToList();
            foreach (var key in keysToDecrypt)
            {
                if (IsSensitiveKey(key) && !string.IsNullOrEmpty(config.Values[key]))
                {
                    try
                    {
                        config.Values[key] = Decrypt(config.Values[key]);
                    }
                    catch (Exception ex)
                    {
                        // 解密失败则保留原值（可能是未加密的旧配置）；记录告警便于诊断，绝不记明文值。
                        FileLogger.Warn("ConfigService",
                            $"解密字段失败，已保留原值。key={key}, 原因={ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>判断是否为敏感配置键</summary>
    private static bool IsSensitiveKey(string key)
    {
        var sensitiveKeywords = new[] { "apikey", "token", "secret", "password", "cookie" };
        return sensitiveKeywords.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>使用DPAPI加密字符串</summary>
    private static string Encrypt(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>使用DPAPI解密字符串</summary>
    private static string Decrypt(string cipherText)
    {
        var bytes = Convert.FromBase64String(cipherText);
        var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    // =====================================================================
    // req-109：账号 CRUD
    // =====================================================================

    /// <summary>获取指定 Provider 下的所有账号（未过滤，按 Persistence 顺序返回）。</summary>
    public IReadOnlyList<Models.Account> GetAccounts(string providerId)
    {
        lock (_ioLock)
        {
            return _settings.Accounts
                .Where(a => string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>按复合键查找账号（Provider + AccountId）。</summary>
    public Models.Account? GetAccount(string providerId, string accountId)
    {
        lock (_ioLock)
        {
            return _settings.Accounts.FirstOrDefault(a =>
                string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.AccountId, NormalizeAccountId(accountId), StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// req-088 Phase1：确保存在指定 <paramref name="accountId"/> 的账号；不存在则以该 account_id 直接创建。
    /// <para>与 <see cref="AddAccount"/> 的区别：AddAccount 自生成序号 ID；本方法使用调用方给定的
    /// account_id（由 <see cref="AccountIdHasher"/> 从网页稳定身份哈希得出），保证稳定——删除账号后重加相同网页账号
    /// 会算出相同 account_id，从而复用历史数据。首次为某 Provider 创建时置 IsDefault=true。</para>
    /// <para>幂等：每次成功刷新都会调用；已存在则直接返回、不改动用户已自定义的昵称。</para>
    /// </summary>
    /// <param name="providerId">插件 ID。</param>
    /// <param name="accountId">账号哈希 ID（稳定身份）。</param>
    /// <param name="defaultNickname">默认昵称；为空时用 <c>{providerId}_{序号}</c>。</param>
    public Models.Account EnsureAccount(string providerId, string accountId, string? defaultNickname = null)
    {
        lock (_ioLock)
        {
            var norm = NormalizeAccountId(accountId);
            var existing = _settings.Accounts.FirstOrDefault(a =>
                string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.AccountId, norm, StringComparison.Ordinal));
            if (existing != null) return existing;

            var count = _settings.Accounts.Count(a =>
                string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            var nickname = string.IsNullOrWhiteSpace(defaultNickname)
                ? $"{providerId}_{count + 1}"
                : defaultNickname.Trim();
            var account = new Models.Account
            {
                ProviderId = providerId,
                AccountId = norm,
                Nickname = nickname,
                UseNickname = true,
                CreatedAt = DateTime.Now,
                IsDefault = count == 0
            };
            _settings.Accounts.Add(account);
            Save();
            return account;
        }
    }

    /// <summary>
    /// 添加账号。第一个账号自动设 IsDefault=true；同 (ProviderId, AccountId) 已存在则抛 <see cref="InvalidOperationException"/>。
    /// <para>认证层不受影响——不创建/修改 LoginStateInfo，登录态在 AuthManager 内部维护。</para>
    /// </summary>
    public Models.Account AddAccount(string providerId, string? nickname)
    {
        lock (_ioLock)
        {
            var newId = GenerateUniqueAccountId(providerId);
            var account = new Models.Account
            {
                ProviderId = providerId,
                AccountId = newId,
                Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim(),
                CreatedAt = DateTime.Now,
                IsDefault = !_settings.Accounts.Any(a => string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            };
            _settings.Accounts.Add(account);
            Save();
            return account;
        }
    }

    /// <summary>更新账号（昵称 / UseNickname / 启用状态）。</summary>
    public void UpdateAccount(Models.Account account)
    {
        if (account == null) throw new ArgumentNullException(nameof(account));
        lock (_ioLock)
        {
            var existing = GetAccount(account.ProviderId, account.AccountId);
            if (existing == null)
                throw new InvalidOperationException($"账号不存在：{account.ProviderId}:{account.AccountId}");
            existing.Nickname = account.Nickname;
            existing.UseNickname = account.UseNickname;
            existing.IsDefault = account.IsDefault;
            // S1：持久化账号启用状态（插件管理页账号行启用 CheckBox 改动即走这里）。
            existing.Enabled = account.Enabled;
            Save();
        }
    }

    /// <summary>
    /// 删除账号。同步清理该账号下的所有 LoginStateInfo 与 AccountCustomization（认证层保持不变）。
    /// <para>若该 Provider 仅剩一个账号，则抛异常（避免误删导致无账号状态）。</para>
    /// </summary>
    public void RemoveAccount(string providerId, string accountId)
    {
        lock (_ioLock)
        {
            // 先查找目标：账号不存在则静默返回（与 AddAccount / GetAccount 行为一致）。
            var norm = NormalizeAccountId(accountId);
            var toRemove = _settings.Accounts.FirstOrDefault(a =>
                string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.AccountId, norm, StringComparison.Ordinal));
            if (toRemove == null) return;
            // 仅在确实要删时校验“仅剩一个”。
            var accounts = _settings.Accounts
                .Where(a => string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (accounts.Count <= 1)
                throw new InvalidOperationException($"Provider {providerId} 仅剩一个账号，不可删除");
            _settings.Accounts.Remove(toRemove);
            // 同步清理该账号的登录态元数据（不删加密凭据本身）
            _settings.PersistedLoginStates.RemoveAll(s =>
                string.Equals(s.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.AccountId, norm, StringComparison.Ordinal));
            Save();
        }
    }

    private string GenerateUniqueAccountId(string providerId)
    {
        var norm = NormalizeAccountId("default");
        var existing = _settings.Accounts
            .Where(a => string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.AccountId)
            .ToHashSet(StringComparer.Ordinal);
        if (!existing.Contains(norm)) return norm;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"account-{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
        return Guid.NewGuid().ToString("N");
    }

    private static string NormalizeAccountId(string id)
        => string.IsNullOrEmpty(id) ? "default" : id;

    // =====================================================================
    // req-109：卡片 CRUD（每账号多卡片基础）
    // =====================================================================

    /// <summary>获取指定账号下的所有卡片（按 DisplayOrder 升序）。</summary>
    public IReadOnlyList<Models.CardConfig> GetCards(string providerId, string accountId)
    {
        lock (_ioLock)
        {
            var acct = GetAccountCustomization(providerId, accountId);
            return (acct?.Cards ?? new List<Models.CardConfig>())
                .OrderBy(c => c.DisplayOrder)
                .ToList();
        }
    }

    /// <summary>添加卡片。自动分配唯一 CardId（首个为 "default-card"）+ DisplayOrder 追加到末尾。</summary>
    public Models.CardConfig AddCard(string providerId, string accountId, string? title)
    {
        lock (_ioLock)
        {
            var acct = GetOrCreateAccountCustomization(providerId, accountId);
            var newCardId = GenerateUniqueCardId(acct);
            var card = new Models.CardConfig
            {
                CardId = newCardId,
                Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                DisplayOrder = acct.Cards.Count
            };
            acct.Cards.Add(card);
            Save();
            return card;
        }
    }

    /// <summary>更新卡片（标题 / DisplayOrder / Customization）。</summary>
    public void UpdateCard(string providerId, string accountId, Models.CardConfig card)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        lock (_ioLock)
        {
            var acct = GetAccountCustomization(providerId, accountId);
            var existing = acct?.Cards.FirstOrDefault(c => c.CardId == card.CardId);
            if (existing == null)
                throw new InvalidOperationException($"卡片不存在：{providerId}:{accountId}:{card.CardId}");
            existing.Title = card.Title;
            existing.DisplayOrder = card.DisplayOrder;
            existing.Customization = card.Customization ?? new AccountCustomization();
            Save();
        }
    }

    /// <summary>删除卡片。同步清理该卡片下的 AccountCustomization（按三段 key 移除）。</summary>
    public void RemoveCard(string providerId, string accountId, string cardId)
    {
        lock (_ioLock)
        {
            var acct = GetAccountCustomization(providerId, accountId);
            if (acct == null) return;
            var toRemove = acct.Cards.FirstOrDefault(c => c.CardId == cardId);
            if (toRemove == null) return;
            acct.Cards.Remove(toRemove);
            // 同步清理该卡片下的扁平字段定制
            var key = AccountCustomization.MakeKey(providerId, accountId, cardId);
            _settings.AccountCustomizations.Remove(key);
            Save();
        }
    }

    /// <summary>更新卡片 DisplayOrder（拖拽排序后批量调用）。</summary>
    public void ReorderCards(string providerId, string accountId, IReadOnlyList<string> orderedCardIds)
    {
        if (orderedCardIds == null) throw new ArgumentNullException(nameof(orderedCardIds));
        lock (_ioLock)
        {
            var acct = GetAccountCustomization(providerId, accountId);
            if (acct == null) throw new InvalidOperationException($"账号定制不存在：{providerId}:{accountId}");
            for (var i = 0; i < orderedCardIds.Count; i++)
            {
                var card = acct.Cards.FirstOrDefault(c => c.CardId == orderedCardIds[i]);
                if (card != null) card.DisplayOrder = i;
            }
            Save();
        }
    }

    private string GenerateUniqueCardId(AccountCustomization acct)
    {
        var existing = acct.Cards.Select(c => c.CardId).ToHashSet(StringComparer.Ordinal);
        const string first = "default-card";
        if (!existing.Contains(first)) return first;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"card-{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
        return Guid.NewGuid().ToString("N");
    }

    private AccountCustomization GetAccountCustomization(string providerId, string accountId)
    {
        var key = AccountCustomization.MakeKey(providerId, accountId); // 二段（账号级，跨卡片共享昵称 / UseNickname）
        _settings.AccountCustomizations.TryGetValue(key, out var acct);
        return acct ?? new AccountCustomization();
    }

    private AccountCustomization GetOrCreateAccountCustomization(string providerId, string accountId)
    {
        var key = AccountCustomization.MakeKey(providerId, accountId);
        if (!_settings.AccountCustomizations.TryGetValue(key, out var acct) || acct == null)
        {
            acct = new AccountCustomization();
            _settings.AccountCustomizations[key] = acct;
        }
        return acct;
    }
}
