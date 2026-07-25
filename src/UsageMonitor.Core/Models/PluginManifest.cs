using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 插件清单（req-107 B9）：defaults.json 反序列化的强类型根。
/// <para>一个完整插件 = plugin.json（元信息）+ extract.json（抓取声明）+ defaults.json（显示声明）。
/// 本类对应 defaults.json，含 schemaVersion / minSdkVersion / providerId / 多语言用量 URL / card / taskbar 显示声明。
/// 由 <see cref="Load"/> 反序列化后交 <c>PluginValidator</c> 校验，运行期加载与 --validate-plugin 自检共用同一份代码。</para>
/// </summary>
public sealed class PluginManifest
{
    /// <summary>声明 schema 版本。</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>所需最低 SDK 版本（如 "0.15.0"，加载时校验兼容性）。</summary>
    public string? MinSdkVersion { get; init; }

    /// <summary>插件 ID（须与 IUsageProvider.ProviderId 一致）。</summary>
    public string? ProviderId { get; init; }

    /// <summary>插件显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>多语言用量网页（key = 语言代码如 zh-CN；订阅档位名称从对应语言网页抓取）。</summary>
    public Dictionary<string, string> UsageUrls { get; init; } = new();

    // ============ Stage A 声明包扩展：第 1 部分（接入）与悬浮窗声明 ============

    /// <summary>插件元信息（版本/作者/描述/图标；纯声明包替代 C# 属性）。</summary>
    public PluginMeta? Meta { get; init; }

    /// <summary>浏览器登录声明（登录 URL / Cookie 域过滤 / 成功判定），替代插件 C# LoginConfig override。</summary>
    public BrowserLoginConfig? LoginConfig { get; init; }

    /// <summary>配置字段声明（字段名/类型/必填/文案），替代插件 C# ConfigFields 与宿主 I18n 硬编码文案。</summary>
    public IReadOnlyList<ConfigField> ConfigFields { get; init; } = System.Array.Empty<ConfigField>();

    /// <summary>错误引导声明（关键字 → 引导文案；空关键字规则为兑底），替代宿主按 ProviderId 硬编码的失败提示。</summary>
    public IReadOnlyList<ErrorGuidanceRule> ErrorGuidance { get; init; } = System.Array.Empty<ErrorGuidanceRule>();

    /// <summary>托盘悬浮窗显示声明（显示哪些 SDK 字段）。</summary>
    public TrayTooltipDeclaration? TrayTooltip { get; init; }

    /// <summary>刷新策略声明（最小/最大/默认间隔），替代 IRefreshPolicyProvider 的 C# 实现。</summary>
    public RefreshPolicy? Refresh { get; init; }

    /// <summary>卡片显示声明。</summary>
    public CardDeclaration? Card { get; init; }

    /// <summary>req-088 Phase3：取数声明（接口/DOM → extras/SDK 字段），供通用声明式抓取执行。</summary>
    public FetchDeclaration? Fetch { get; init; }

    /// <summary>任务栏显示声明。</summary>
    public TaskbarDeclaration? Taskbar { get; init; }

    /// <summary>
    /// 从 defaults.json 文本反序列化为 <see cref="PluginManifest"/>（枚举按字符串解析，属性名大小写不敏感）。
    /// </summary>
    /// <param name="json">defaults.json 内容。</param>
    public static PluginManifest? Load(string json)
        => JsonSerializer.Deserialize<PluginManifest>(json, Options());

    /// <summary>
    /// Stage A 声明包多文件合并：两份部分清单合为一份。标量与节均取 <paramref name="first"/> 的非空/非默认值，
    /// 缺省时回退 <paramref name="second"/>；供加载器按 plugin.json → fetch.json → display.json → defaults.json 顺序聚合。
    /// </summary>
    /// <param name="first">优先清单（先加载的文件）。</param>
    /// <param name="second">回退清单（后加载的文件）。</param>
    public static PluginManifest Merge(PluginManifest first, PluginManifest second)
    {
        return new PluginManifest
        {
            SchemaVersion = Math.Max(first.SchemaVersion, second.SchemaVersion),
            MinSdkVersion = first.MinSdkVersion ?? second.MinSdkVersion,
            ProviderId = first.ProviderId ?? second.ProviderId,
            DisplayName = first.DisplayName ?? second.DisplayName,
            UsageUrls = first.UsageUrls.Count > 0 ? first.UsageUrls : second.UsageUrls,
            Meta = first.Meta ?? second.Meta,
            LoginConfig = first.LoginConfig ?? second.LoginConfig,
            ConfigFields = first.ConfigFields.Count > 0 ? first.ConfigFields : second.ConfigFields,
            ErrorGuidance = first.ErrorGuidance.Count > 0 ? first.ErrorGuidance : second.ErrorGuidance,
            TrayTooltip = first.TrayTooltip ?? second.TrayTooltip,
            Refresh = first.Refresh ?? second.Refresh,
            Card = first.Card ?? second.Card,
            Fetch = first.Fetch ?? second.Fetch,
            Taskbar = first.Taskbar ?? second.Taskbar
        };
    }

    /// <summary>构建反序列化选项（驼峰命名 + 字符串枚举）。</summary>
    internal static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
