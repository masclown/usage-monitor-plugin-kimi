using System.Collections.Generic;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 插件元信息声明（Stage A 声明包：plugin.json 的 meta 节）。
/// <para>纯声明包（零 DLL）形态下，替代 <see cref="Plugins.IUsageProvider"/> 的
/// Version / Author / Description / IconPath 等 C# 属性——全部信息由声明承载。</para>
/// </summary>
public sealed class PluginMeta
{
    /// <summary>插件版本（如 "1.0.0"）。</summary>
    public string? Version { get; init; }

    /// <summary>插件作者。</summary>
    public string? Author { get; init; }

    /// <summary>插件描述。</summary>
    public string? Description { get; init; }

    /// <summary>图标来源 URL（宿主运行时抓取 favicon 缓存；缺省时回退 loginConfig.LoginUrl 域名）。</summary>
    public string? IconUrl { get; init; }
}

/// <summary>
/// 错误引导规则（Stage A：plugin.json 的 errorGuidance 节）。
/// <para>宿主在查询失败时按声明顺序匹配：错误消息包含 <see cref="MatchKeywords"/> 任一关键字即命中，
/// 显示 <see cref="Message"/>；<see cref="MatchKeywords"/> 为空的规则视为兜底（恒命中，应放最后）。
/// 替代宿主中按 ProviderId 硬编码的错误提示分支。</para>
/// </summary>
public sealed class ErrorGuidanceRule
{
    /// <summary>匹配关键字列表（错误消息包含任一即命中；空列表 = 兜底规则）。</summary>
    public IReadOnlyList<string> MatchKeywords { get; init; } = System.Array.Empty<string>();

    /// <summary>命中后显示的引导文案（必填）。</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 托盘悬浮窗显示声明（Stage A：display.json 的 trayTooltip 节）。
/// <para>首版仅声明"显示哪些 SDK 字段"，布局由宿主模板统一；缺省时宿主沿用默认行为。</para>
/// </summary>
public sealed class TrayTooltipDeclaration
{
    /// <summary>悬浮窗展示的 SDK 标准字段名列表（<see cref="UsageFields"/> 常量，经白名单校验）。</summary>
    public IReadOnlyList<string> Fields { get; init; } = System.Array.Empty<string>();
}
