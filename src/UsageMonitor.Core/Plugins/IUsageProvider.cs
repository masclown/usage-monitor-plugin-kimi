using System.Threading;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services.Auth;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// AI用量提供者插件接口 - 所有服务商插件必须实现此接口
/// 定义了插件的基本信息、配置项和用量查询能力
/// </summary>
public interface IUsageProvider : IBrowserLoginProvider, IChartSupportProvider, IRefreshPolicyProvider, IBalanceItemProvider, IDefaultRenderKindsProvider
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

    /// <summary>插件支持的鉴权方式列表（req-096）。
    /// <para>
    /// 默认实现根据 <see cref="LoginConfig"/> 推断：LoginConfig != null → Cookie，否则 → ApiKey。
    /// 插件可覆盖此属性以明确声明支持的鉴权方式，例如同时支持 ApiKey 和 Cookie 的插件
    /// 可返回 <c>new[] { AuthKind.ApiKey, AuthKind.Cookie }</c>。
    /// </para>
    /// <para>
    /// AuthManager 会根据此声明自动选择鉴权方式，统一管理鉴权数据的获取、验证、刷新。
    /// </para>
    /// </summary>
#pragma warning disable CS0618 // LoginConfig 已过时，此处为 req-096 向后兼容推断保留
    IReadOnlyList<AuthKind> SupportedAuthKinds => LoginConfig != null
        ? new[] { AuthKind.Cookie }
        : new[] { AuthKind.ApiKey };
#pragma warning restore CS0618

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
    [Obsolete("req-107 B6：SupportedRingChartMetrics 已被 Card.Ring 数据组替代")]
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
    [Obsolete("req-107 B6：SupportsPeriodSwitch 已被 Card.Line.Slicer(Period) 替代")]
    bool SupportsPeriodSwitch => false;

    /// <summary>
    /// 插件为折线图 hover tooltip 提供的扩展文本行（每行一项，UI 用换行拼接展示）。
    /// <para>
    /// 例如 MiniMax 可返回 <c>["调用 {value}", "缓存命中 {pct}%"]</c>，让 tooltip 显示更丰富
    /// 的当日附加信息。返回 <c>null</c> 或空集合时，tooltip 仅显示标题 + 数值。
    /// </para>
    /// </summary>
    [Obsolete("req-107 B6：ExtraTooltipLines 已被 Card.Chart.Tooltip 替代")]
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
        /// <summary>
    /// req-098：插件声明支持的迷你图表类型（<see cref="Plugins.MiniChart.MiniChartKind"/>）。
    /// <para>
    /// 默认返回 <c>[MiniRingChart, MiniText]</c>。有额外能力的插件可重写返回更丰富的集合。
    /// 返回空集合表示该插件不参与任务栏迷你图。
    /// </para>
    /// </summary>
    [Obsolete("req-107 B6：SupportedMiniCharts 已被 TaskbarDeclaration.MiniCharts 替代")]
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
    [Obsolete("req-107 B6：MiniChartDataTypes 已被 TaskbarDeclaration.MiniCharts.DataGroups 替代")]
    IReadOnlyList<Plugins.MiniChart.MiniChartContentKind> MiniChartDataTypes => new[]
    {
        Plugins.MiniChart.MiniChartContentKind.PrimaryMetric,
        Plugins.MiniChart.MiniChartContentKind.Credits,
        Plugins.MiniChart.MiniChartContentKind.ResetTime
    };

    /// <summary>
    // ============== req-107 B6：聚合声明根（声明式插件框架） ==============

    /// <summary>
    /// 卡片显示声明聚合根（req-107 B6）。
    /// <para>来自插件 defaults.json（经 PluginDefaultsLoader 装载）或插件代码 override。
    /// 返回 null 表示插件尚未迁移到声明式框架，宿主回退到旧的 SupportedCardCharts / CardMetricBarData 路径（过渡期兼容）。
    /// 插件完成 req-108 迁移后由本属性驱动卡片渲染，旧零散能力属性随之收敛移除。</para>
    /// </summary>
    Models.CardDeclaration? Card => null;

    /// <summary>
    /// 任务栏显示声明聚合根（req-107 B6）。
    /// <para>来自插件 defaults.json 或插件代码 override。返回 null 时宿主回退到旧的 SupportedMiniCharts / MiniChartDataTypes 路径（过渡期兼容）。</para>
    /// </summary>
    Models.TaskbarDeclaration? Taskbar => null;
}
