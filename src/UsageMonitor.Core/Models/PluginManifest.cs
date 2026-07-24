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
