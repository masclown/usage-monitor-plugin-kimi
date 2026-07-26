// SPDX-License-Identifier: Apache-2.0
// 插件 SDK 契约文件：本文件按 Apache License 2.0 授权（见仓库根目录 LICENSE-APACHE），
// 供第三方插件开发自由引用；仓库其余部分适用 BSL 1.1（见 LICENSE）。
using System.Threading;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services.Auth;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// AI用量提供者接口——宿主内部抽象（Stage E 降级）。
/// <para>完全声明式插件架构下，插件作者不再实现本接口：新 Provider 只需编写声明包
/// （plugins/&lt;包名&gt;/defaults.json 等），由通用 <see cref="DeclarativeProvider"/> 运行器实例化。
/// 本接口仅作为宿主（App/Core 服务）与运行器之间的内部契约保留。</para>
/// </summary>
public interface IUsageProvider : IBrowserLoginProvider, IChartSupportProvider, IRefreshPolicyProvider, IBalanceItemProvider, IDefaultRenderKindsProvider
{
    /// <summary>服务商唯一标识（如 "deepseek"、"minimax"）</summary>
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

    // Stage E：req-107 B6 标记的 5 个 [Obsolete] 成员（SupportedRingChartMetrics / SupportsPeriodSwitch /
    // ExtraTooltipLines / SupportedMiniCharts / MiniChartDataTypes）已删除——能力全部由
    // Card / Taskbar 声明聚合根（defaults.json）承载。

    /// <summary>
    /// Stage B（声明式插件架构）：错误引导声明（来自声明包 errorGuidance 节）。
    /// <para>查询失败时宿主按声明顺序匹配错误消息关键字并显示引导文案（空关键字规则为兑底）；
    /// 空集合 = 无引导，宿主显示通用失败文案。替代宿主按 ProviderId 硬编码的错误提示分支。</para>
    /// </summary>
    IReadOnlyList<Models.ErrorGuidanceRule> ErrorGuidance => System.Array.Empty<Models.ErrorGuidanceRule>();

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
