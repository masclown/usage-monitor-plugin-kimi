using System.Threading;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services.Auth;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// AI用量提供者插件接口 - 所有服务商插件必须实现此接口
/// 定义了插件的基本信息、配置项和用量查询能力
/// </summary>
public interface IUsageProvider
{
    /// <summary>服务商唯一标识（如 "deepseek"、"mimo"）</summary>
    string ProviderId { get; }

    /// <summary>服务商显示名称（如 "Deepseek"）</summary>
    string DisplayName { get; }

    /// <summary>服务商图标路径（支持 pack:// URI 或文件路径）</summary>
    string? IconPath { get; }

    /// <summary>插件版本</summary>
    string Version { get; }

    /// <summary>插件作者</summary>
    string Author { get; }

    /// <summary>插件描述</summary>
    string Description { get; }

    /// <summary>
    /// 配置项定义列表 - 定义插件需要的配置字段（如API Key等）
    /// 设置界面会根据此列表自动生成对应的输入控件
    /// </summary>
    IReadOnlyList<ConfigField> ConfigFields { get; }

    /// <summary>
    /// 可选的浏览器登录配置 - 声明此插件是否需要通过临时 Edge 窗口获取登录态 Cookie。
    /// <para>
    /// 返回 <c>null</c> 表示此插件无需浏览器登录（如纯 API Key 鉴权）。
    /// 设置界面会据此自动显示"🌐 获取登录态"按钮，调用
    /// <see cref="Services.BrowserLoginService"/> 启动临时 Edge 窗口并提取 Cookie。
    /// </para>
    /// <para>
    /// 设计参考：销项数据助手项目的 <c>browser-cookie-manager</c> Skill 采用的通用 Cookie
    /// 获取方案；本项目在此基础上用 Edge + CDP 替代 Playwright，降低外部依赖。
    /// </para>
    /// <para>
    /// req-096：此属性已被 <see cref="SupportedAuthKinds"/> 取代，保留仅为向后兼容。
    /// 新插件应实现 <see cref="SupportedAuthKinds"/> 而非此属性。
    /// </para>
    /// </summary>
    [Obsolete("请使用 SupportedAuthKinds 属性声明鉴权方式，此属性将在未来版本移除")]
    Models.BrowserLoginConfig? LoginConfig => null;

    /// <summary>
    /// 插件支持的鉴权方式列表（req-096）。
    /// <para>
    /// 默认实现根据 <see cref="LoginConfig"/> 推断：LoginConfig != null → Cookie，否则 → ApiKey。
    /// 插件可覆盖此属性以明确声明支持的鉴权方式，例如同时支持 ApiKey 和 Cookie 的插件
    /// 可返回 <c>new[] { AuthKind.ApiKey, AuthKind.Cookie }</c>。
    /// </para>
    /// <para>
    /// AuthManager 会根据此声明自动选择鉴权方式，统一管理鉴权数据的获取、验证、刷新。
    /// </para>
    /// </summary>
    IReadOnlyList<AuthKind> SupportedAuthKinds => LoginConfig != null
        ? new[] { AuthKind.Cookie }
        : new[] { AuthKind.ApiKey };

    /// <summary>
    /// 插件运行模式（req-101）。
    /// <para>
    /// 默认返回 <see cref="ProviderMode.Api"/>（API 模式，按量付费，显示余额）。
    /// Token Plan 模式的插件（如 MiniMax）应覆盖此属性返回 <see cref="ProviderMode.TokenPlan"/>。
    /// </para>
    /// <para>
    /// 卡片根据此模式显示不同 UI：API 模式显示余额，Token Plan 模式显示订阅档位。
    /// </para>
    /// </summary>
    Models.ProviderMode Mode => Models.ProviderMode.Api;

    /// <summary>
    /// 订阅档位字段名（req-101，Token Plan 模式专用）。
    /// <para>
    /// Token Plan 模式下，插件声明订阅档位名称从 UsageInfo 的哪个字段获取。
    /// 例如 MiniMax 可返回 <c>"SubscriptionTier"</c>，卡片将从 <c>UsageInfo.Extra["SubscriptionTier"]</c> 读取档位名称。
    /// </para>
    /// <para>
    /// API 模式无需实现此属性（返回 null 即可）。
    /// </para>
    /// </summary>
    string? SubscriptionTierField => null;

    /// <summary>
    /// 插件刷新策略（req-102）。
    /// <para>
    /// 默认返回 null，表示使用全局刷新间隔（AppSettings.RefreshIntervalSeconds）。
    /// 插件可覆盖此属性以声明自己的刷新策略，例如 MiniMax 的 5h 限额需要频繁刷新。
    /// </para>
    /// <para>
    /// RefreshService 会按插件声明的策略执行刷新，用户设置的刷新间隔会被限制在策略范围内。
    /// </para>
    /// </summary>
    Models.RefreshPolicy? RefreshPolicy => null;

    /// <summary>
    /// 卡片字段映射关系（req-100）。
    /// <para>
    /// 默认返回 null，表示使用默认字段名（UsedAmount / TotalAmount / UsagePercent 等）。
    /// 插件可覆盖此属性以声明自己的字段映射关系，例如 MiniMax 使用 UsedTokens / TotalTokens。
    /// </para>
    /// <para>
    /// 卡片按映射关系从 UsageInfo 中取数，不硬编码字段名，实现泛化取数。
    /// </para>
    /// </summary>
    Models.FieldMapping? CardFieldMapping => null;

    /// <summary>
    /// 插件声明的默认渲染能力集合（在首次加载、未收到任何刷新数据前生效）。
    /// <para>
    /// 卡片可见性绑定 "插件声明 render_kind" 与 "用户开关"。如果插件未实现，
    /// 主窗口会在第一次渲染时就折叠对应区块，导致"数据未到则卡片残缺"。
    /// 由插件自己声明一组最常声明的能力，主窗口装配 VM 时立即写入 RenderKinds，
    /// 让首屏显示与数据到位后的显示保持一致；运行时真正收到的 mm_render_kinds
    /// 仍会通过 <c>UpdateFromMiniMaxDom</c> 等覆盖。
    /// </para>
    /// </summary>
    IReadOnlyList<string> DefaultRenderKinds => System.Array.Empty<string>();

    /// <summary>
    /// req-折叠插件控制：插件声明"卡片折叠状态下仍然保持可见的元素"集合（render_kind key）。
    /// <para>
    /// 默认返回 <c>null</c> —— 折叠态下仅保留卡片头部（logo + 名称 + 状态摘要 + 订阅胶囊 + 刷新 + 设置），
    /// 其余内容（限额进度条 / 余额快照 / 卡片图表）全部隐藏。</para>
    /// <para>
    /// 插件可覆盖，例如 MiniMax 可返回 <c>["primaryBar"]</c> 让折叠态也保留 5h 限额进度条；
    /// 也可返回 <c>["primaryBar", "weeklyBar"]</c> 同时保留 5h + 周限额。
    /// 渲染 key 与 <see cref="DefaultRenderKinds"/> 复用同一套约定（primaryBar / weeklyBar /
    /// videoProgress / balanceSnapshot / charts / subscriptionTitle 等）。
    /// </para>
    /// </summary>
    IReadOnlyList<string>? CollapseVisibleParts => null;

    /// <summary>
    /// 插件声明「支持在主窗口卡片中展示的图表类型」集合，供插件配置窗口生成复选框。
    /// <para>
    /// 这是「不同插件支持不同图表」框架的核心契约：配置窗口只为本集合内的图表类型
    /// 生成勾选项，用户可多选；用户的多选结果随 <c>AppSettings.ProviderCardChartKinds</c>
    /// （key = ProviderId）持久化，主窗口卡片按选择叠加展示对应图表。
    /// </para>
    /// <para>
    /// 默认返回通用三件套 <see cref="Models.CardChartKind.Line"/> / <see cref="Models.CardChartKind.Bar"/> /
    /// <see cref="Models.CardChartKind.Ring"/>（数据源为应用统一记录的历史用量百分比与当前已用百分比，
    /// 任何插件都适用）。有专属数据的插件应覆盖此属性，例如 MiniMax 覆盖为折线图 + 热力图。
    /// 返回空集合表示该插件不提供任何卡片图表，配置窗口将隐藏「卡片图表」分组。
    /// </para>
    /// </summary>
    IReadOnlyList<Models.CardChartKind> SupportedCardCharts => new[]
    {
        Models.CardChartKind.Line,
        Models.CardChartKind.Bar,
        Models.CardChartKind.Ring
    };

    /// <summary>
    /// 插件可注册的自定义图表工厂集合（REQ-005 SDK）。
    /// <para>
    /// 默认返回空数组表示只使用内置 5 种图表（折线 / 柱状 / 圆环 / 热力 / 时段）。
    /// 插件可在自己的实现里覆盖此属性，把自研图表通过 <see cref="IUsageChartFactory.Create"/>
    /// 提供给宿主；宿主在装配 Provider 时把这些工厂合并到全局图表注册表中，
    /// 第三方控件即可在 <c>MainWindow.xaml</c> 通过 <see cref="Models.IChartData.Kind"/>
    /// 自动路由到对应模板，无需修改主窗口代码。
    /// </para>
    /// <para>
    /// 设计参考：这是把「数据契约」与「UI 控件契约」解耦的关键位——插件只需声明「我能产哪种 IChartData」，
    /// 宿主用 chart-data.Kind 路由到对应 IUsageChartFactory.Create 出的实例来展示。
    /// </para>
    /// </summary>
    IReadOnlyList<IUsageChartFactory> ChartFactories => System.Array.Empty<IUsageChartFactory>();

    /// <summary>
    /// 插件可注册的自定义图表工厂集合（REQ-082 SDK v2，使用 v2 签名 + ChartContext）。
    /// <para>
    /// 默认返回空数组。与 <see cref="ChartFactories"/> 并存：
    /// <list type="bullet">
    /// <item>旧 v1 工厂使用 <see cref="IUsageChartFactory.Create()"/> 签名，仍可用 <see cref="ChartFactories"/>。</item>
    /// <item>新 v2 工厂使用 <see cref="IUsageChartFactory2.Create(ChartContext)"/> 签名，可接收 context，应声明在本属性。</item>
    /// <item>宿主装配时同时读取两者，去重后合并到全局注册表。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 返回 null 等价于返回空集合。插件返回非空集合时由宿主在装配 Provider 时合并到全局图表注册表中，
    /// 第三方控件即可在 <c>MainWindow.xaml</c> 通过 <see cref="Models.IChartData.Kind"/> 自动路由到对应模板，
    /// 无需修改主窗口代码。
    /// </para>
    /// </summary>
    IReadOnlyList<IUsageChartFactory2>? CustomChartFactories => null;

    /// <summary>
    /// 查询当前用量信息
    /// </summary>
    /// <param name="config">服务商配置（包含API Key等信息）</param>
    /// <param name="ct">取消令牌，用于区分用户主动取消与网络超时</param>
    /// <returns>用量信息</returns>
    Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default);

    /// <summary>
    /// 验证配置是否有效（如API Key是否正确）
    /// </summary>
    /// <param name="config">待验证的配置</param>
    /// <param name="ct">取消令牌，用于区分用户主动取消与网络超时</param>
    /// <returns>配置是否有效</returns>
    Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default);

    /// <summary>
    /// req-026：插件声明支持的环形图中心数字 metric key 集合。
    /// <para>
    /// 默认返回 <c>["Percent"]</c>（仅显示已用百分比）。有更多数字类型的插件可重写，
    /// 例如 MiniMax 重写为 <c>["Percent", "Credits"]</c>。
    /// </para>
    /// <para>
    /// 用户侧（设置窗口 → "环形图中心" Tab）按此集合展示 CheckBox；
    /// 用户勾选结果存到 <c>AppSettings.ProviderEnabledRingChartMetrics[ProviderId]</c>。
    /// </para>
    /// </summary>
    IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent" };

    /// <summary>
    /// 插件是否支持在主窗口卡片折线图右上角显示"近 7 天 / 近 30 天"等周期切换按钮。
    /// <para>
    /// 返回 <c>true</c> 时，宿主会在控件右上角绘制分段按钮，<see cref="SetPeriodAsync"/>
    /// 会被调用以让插件按指定周期重算数据；返回 <c>false</c>（默认）时不显示切换按钮。
    /// 仅当插件能提供带真实日期的"每日"数据源时（如 MiniMax usage_summary 返回的
    /// <c>mm_dailyTokenValues</c> + <c>mm_dailyTokenDates</c>）才应返回 <c>true</c>。
    /// </para>
    /// </summary>
    bool SupportsPeriodSwitch => false;

    /// <summary>
    /// 插件为折线图 hover tooltip 提供的扩展文本行（每行一项，UI 用换行拼接展示）。
    /// <para>
    /// 例如 MiniMax 可返回 <c>["调用 {value}", "缓存命中 {pct}%"]</c>，让 tooltip 显示更丰富
    /// 的当日附加信息。返回 <c>null</c> 或空集合时，tooltip 仅显示标题 + 数值。
    /// </para>
    /// </summary>
    IReadOnlyList<string>? ExtraTooltipLines => null;
    
        /// <summary>
        /// req-105：插件声明迷你图表（Taskbar / 卡片浮窗）Tooltip 应显示的字段列表。
        /// <para>
        /// 支持的字段名（由宿主 <c>MiniChartItemViewModel</c> 解析）：
        /// <list type="bullet">
        ///   <item><description><c>ProviderName</c>：插件显示名（<see cref="DisplayName"/>）。</description></item>
        ///   <item><description><c>DataName</c>：当前数据指标名（如 "5h 用量"）。</description></item>
        ///   <item><description><c>CurrentValue</c>：当前值（文本形式）。</description></item>
        ///   <item><description><c>RefreshCountdown</c>：下一次刷新倒计时（如 "重置倒计时：2 小时 21 分钟"）。</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// 默认实现为全部 4 项全开——保证现有插件开箱即用，宿主把未启用的字段从模板字符串中剔除。
        /// 返回空集合表示该插件不提供任何 Tooltip 字段（宿主走纯标题模式）。
        /// </para>
        /// </summary>
        IReadOnlyList<string> ToolTipFields => new[]
        {
            "ProviderName",
            "DataName",
            "CurrentValue",
            "RefreshCountdown"
        };

    /// <summary>
    /// req-098：插件声明支持的迷你图表类型（<see cref="Plugins.MiniChart.MiniChartKind"/>）。
    /// <para>
    /// 默认返回 <c>[MiniRingChart, MiniText]</c>。有额外能力的插件可重写返回更丰富的集合。
    /// 返回空集合表示该插件不参与任务栏迷你图。
    /// </para>
    /// </summary>
    IReadOnlyList<Plugins.MiniChart.MiniChartKind> SupportedMiniCharts => new[]
    {
        Plugins.MiniChart.MiniChartKind.MiniRingChart,
        Plugins.MiniChart.MiniChartKind.MiniText
    };

    /// <summary>
    /// req-098：插件能为迷你图提供的数据类型（<see cref="Plugins.MiniChart.MiniChartContentKind"/>）。
    /// <para>
    /// 默认返回 3 项基本内容（主指标 / Credits / 重置时间）。
    /// 设置页"任务栏迷你图表"配置 UI 按此集合生成下拉选项。
    /// </para>
    /// </summary>
    IReadOnlyList<Plugins.MiniChart.MiniChartContentKind> MiniChartDataTypes => new[]
    {
        Plugins.MiniChart.MiniChartContentKind.PrimaryMetric,
        Plugins.MiniChart.MiniChartContentKind.Credits,
        Plugins.MiniChart.MiniChartContentKind.ResetTime
    };

    /// <summary>
    /// 控件触发周期切换时由宿主调用，让插件按新周期重算数据。
    /// <para>
    /// 默认实现为 no-op：插件若不重写此方法，宿主会自行在 VM 端基于已缓存的"每日"数据切片。
    /// 重写此方法的插件通常需要：1) 内部记录当前 period；2) 在下一次
    /// <see cref="GetUsageAsync"/> 触发时把 Extra 中的 <c>mm_dailyTokenValues</c> /
    /// <c>mm_dailyTokenDates</c> 切片到该 period 对应的窗口。
    /// </para>
    /// </summary>
    /// <param name="period">周期字符串，取值为 "7d" / "30d"（与 App 端
    /// <c>UsageMonitor.App.Controls.ChartPeriods</c> 常量保持一致；Core 不引用 App）。</param>
    /// <param name="ct">取消令牌，宿主在卡片销毁或重新触发时可能取消。</param>
    Task SetPeriodAsync(string period, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// 插件为“余额快照”区域提供的额外数据项（req-008）。<see cref="Models.BalanceItem"/>。
    /// <para>
    /// 返回空集合（默认）时，主窗口组装 VM 会按内置默认 4 项（累计 / 峰值 / 活跃 / 积分余额）填充；
    /// 返回非空集合时由 VM 按 <c>Label</c> 匹配覆盖/追加：同名项插件胜出，
    /// 未匹配的插件项追加在默认项之后。插件可通过把项的 <c>IsVisible</c> 设为 false 隐藏默认项。
    /// </para>
    /// </summary>
    IReadOnlyList<Models.BalanceItem> BalanceItems => System.Array.Empty<Models.BalanceItem>();

    /// <summary>
    /// 插件声明的热力图色阶档位（req-009）。<see cref="Models.HeatMapTierConfig"/>。
    /// <para>
    /// 返回 null（默认）时，<c>HeatMapTierScale</c> 走通用 4 档兑底色（适配 K~M 级数据）。
    /// 返回非空集合时作为该 Provider 的默认色阶。用户设置页保存后会覆盖。
    /// </para>
    /// </summary>
    IReadOnlyList<Models.HeatMapTierConfig>? HeatMapTiers => null;

    // ============== REQ-083 SDK v2 新增可选属性 ==============

    /// <summary>
    /// 插件为"度量进度条组"（5h / 本周 / 视频赠送 等）提供的 V2 数据模型（REQ-083）。
    /// <para>
    /// 返回 null（默认）时，主窗口 <c>ChartCardTemplateSelector</c> 会回退到旧的硬编码 XAML 模板。
    /// 返回 <see cref="Models.MetricBarData"/> 时由新 <c>MetricBarControl</c> 渲染。
    /// </para>
    /// </summary>
    Models.MetricBarData? CardMetricBarData => null;

    /// <summary>
    /// 插件为"度量数字网格"（余额快照等）提供的 V2 数据模型（REQ-083）。
    /// <para>
    /// 返回 null（默认）时，主窗口 <c>ChartCardTemplateSelector</c> 会回退到旧的硬编码 XAML 模板。
    /// 返回 <see cref="Models.MetricGridData"/> 时由新 <c>MetricGridControl</c> 渲染。
    /// </para>
    /// </summary>
    Models.MetricGridData? CardMetricGridData => null;

    /// <summary>
    /// 插件为折线图 hover tooltip 提供的 V2 TooltipContent 生成委托（REQ-083）。
    /// <para>
    /// 返回 null（默认）时，主窗口沿用插件的旧 <see cref="ExtraTooltipLines"/> 拼接逻辑。
    /// 返回委托时由 <c>HoverTooltipPresenter.Show(FrameworkElement, TooltipContent)</c> 渲染。
    /// </para>
    /// </summary>
    /// <param name="dataIndex">当前 hover 的数据点索引（0..N-1）。</param>
    /// <returns>该索引对应的 TooltipContent。</returns>
    System.Func<int, Models.TooltipContent>? LineTooltipProvider => null;

    /// <summary>
    /// req-092：将插件原始数据映射为标准字段名字典。
    /// <para>
    /// 默认实现返回 null，表示使用 <see cref="UsageDataDiffService.ExtractStandardFields"/> 自动提取。
    /// 插件可覆盖此方法以自定义字段映射逻辑，例如 MiniMax 将网页 DOM 数据映射为标准字段。
    /// </para>
    /// <para>
    /// 标准字段名定义在 <see cref="Models.UsageFields"/> 常量类中，插件应使用这些常量作为字典 key。
    /// 差异检测引擎按标准字段名进行字段级对比，仅保存有变化的字段。
    /// </para>
    /// </summary>
    /// <param name="usage">插件返回的用量信息对象</param>
    /// <returns>标准字段名字典，key 为标准字段名（UsageFields 常量），value 为字段值</returns>
    IReadOnlyDictionary<string, object>? MapToStandardFields(UsageInfo usage) => null;
}
