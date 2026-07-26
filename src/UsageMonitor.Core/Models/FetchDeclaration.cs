// SPDX-License-Identifier: Apache-2.0
// 插件 SDK 契约文件：本文件按 Apache License 2.0 授权（见仓库根目录 LICENSE-APACHE），
// 供第三方插件开发自由引用；仓库其余部分适用 BSL 1.1（见 LICENSE）。
using System.Collections.Generic;
using System.Text.Json;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-088 Phase3：取数声明根（完全声明式）。声明"从哪些网页接口/DOM 取数、如何把响应映射为 extras/SDK 字段"，
/// 由通用 <c>BrowserCaptureService</c> + <c>DeclarativeCaptureExecutor</c> 执行，替代各插件手写的抓取解析逻辑。
/// <para>随 defaults.json 的 <c>fetch</c> 节反序列化。目标：新增 Provider 只写声明、不写 C# 抓取代码。</para>
/// </summary>
public sealed class FetchDeclaration
{
    /// <summary>接口取数声明列表（按响应 URL 子串匹配捕获的 XHR/fetch JSON）。</summary>
    public IReadOnlyList<FetchEndpoint> Endpoints { get; init; } = System.Array.Empty<FetchEndpoint>();

    /// <summary>DOM 兜底取数（如订阅档位文案、账号名等接口拿不到、只在 DOM 的值）。</summary>
    public IReadOnlyList<FetchDomField> Dom { get; init; } = System.Array.Empty<FetchDomField>();

    /// <summary>聚合映射（如对 date_model_usage 按 token 加权平均缓存命中率），在端点抽取后执行。</summary>
    public IReadOnlyList<FetchAggregate> Aggregates { get; init; } = System.Array.Empty<FetchAggregate>();

    /// <summary>计算列（如视频 used = total - remains），在所有端点/聚合产出后基于 extras 键计算。</summary>
    public IReadOnlyList<FetchComputed> Computed { get; init; } = System.Array.Empty<FetchComputed>();

    /// <summary>账号身份声明：从哪个已捕获字段取平台稳定 ID，用于哈希出 account_id。</summary>
    public FetchAccountId? AccountId { get; init; }

    /// <summary>Stage E：浏览器捕获环境声明（导航 URL / Cookie 域 / 按配置字段切换的变体），供通用 DeclarativeProvider 驱动 BrowserCaptureService。</summary>
    public FetchCapture? Capture { get; init; }
}

/// <summary>
/// Stage E：浏览器捕获环境声明。把过去插件 C# 里硬编码的“导航到哪个用量页 + Cookie 挂哪个域”
/// 变为纯声明；支持按某个配置字段值（如 Region）切换变体（如 CN/Global 不同域名）。
/// </summary>
public sealed class FetchCapture
{
    /// <summary>默认用量页导航 URL（如 https://platform.minimaxi.com/console/usage）。</summary>
    public string NavigateUrl { get; init; } = string.Empty;

    /// <summary>默认 Cookie 归属域（如 ".minimaxi.com"）。</summary>
    public string CookieDomain { get; init; } = string.Empty;

    /// <summary>可选：决定变体的配置字段 Key（如 "Region"）；字段值命中 <see cref="Variants"/> 键时用变体覆盖默认值。</summary>
    public string? VariantField { get; init; }

    /// <summary>变体表（key = 配置字段值，如 "Global"；大小写不敏感匹配）。</summary>
    public IReadOnlyDictionary<string, FetchCaptureVariant> Variants { get; init; }
        = new Dictionary<string, FetchCaptureVariant>();
}

/// <summary>Stage E：捕获环境变体（缺省属性回退 <see cref="FetchCapture"/> 默认值）。</summary>
public sealed class FetchCaptureVariant
{
    /// <summary>变体用量页导航 URL（可空=沿用默认）。</summary>
    public string? NavigateUrl { get; init; }

    /// <summary>变体 Cookie 归属域（可空=沿用默认）。</summary>
    public string? CookieDomain { get; init; }
}

/// <summary>req-088 Phase3：聚合映射——对数组按 <see cref="Op"/> 聚合（当前支持 weightedAvg：Ζ(value×weight)/Ζweight）。</summary>
public sealed class FetchAggregate
{
    /// <summary>提供数据的接口 URL 子串。</summary>
    public string UrlMatch { get; init; } = string.Empty;

    /// <summary>数组 jsonpath（如 "$.date_model_usage"）。</summary>
    public string ItemsPath { get; init; } = string.Empty;

    /// <summary>聚合算子（当前仅 weightedAvg）。</summary>
    public string Op { get; init; } = "weightedAvg";

    /// <summary>每项取值 jsonpath（相对项根，如 "$.cache_hit_percent"）。</summary>
    public string ValuePath { get; init; } = string.Empty;

    /// <summary>值转换器（如 parsePercent）。</summary>
    public string? ValueTransform { get; init; }

    /// <summary>权重 jsonpath（相对项根，如 "$.total_token"）；为空时权重=1。</summary>
    public string? WeightPath { get; init; }

    /// <summary>写入 extras 的键（如 "cache_hit_percent"）。</summary>
    public string Target { get; init; } = string.Empty;
}

/// <summary>
/// req-088 Phase3 / Stage E：计算列——基于已有 extras 键派生新键。支持算子：
/// <list type="bullet">
///   <item><description><c>subtract</c>：operands[0] - operands[1] - ...（数值）。</description></item>
///   <item><description><c>splitBefore</c>/<c>splitAfter</c>：取 operands[0] 字符串在首个 <see cref="Separators"/> 分隔符前/后的部分（trim）；
///   无分隔符时 splitBefore 不产出、splitAfter 产出整串（供“类型·档位”拆分类声明）。</description></item>
///   <item><description><c>constant</c>：写入 <see cref="Value"/> 字面量（配合 <see cref="WhenPresent"/>/<see cref="OnlyIfAbsent"/> 做条件默认值）。</description></item>
///   <item><description><c>coalesce</c>：取 operands 中首个已存在的键值复制到 Target。</description></item>
///   <item><description><c>template</c>：按 <see cref="Template"/> 拼接文本，<c>{键名}</c> 占位符取 extras 值（缺失补空串）。</description></item>
/// </list>
/// </summary>
public sealed class FetchComputed
{
    /// <summary>写入 extras 的键（如 "five_hour_video_used"）。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>算子（subtract / splitBefore / splitAfter / constant / coalesce / template）。</summary>
    public string Op { get; init; } = "subtract";

    /// <summary>参与计算的 extras 键列表（按顺序）。</summary>
    public IReadOnlyList<string> Operands { get; init; } = System.Array.Empty<string>();

    /// <summary>splitBefore/splitAfter 的分隔符候选集（任一字符命中即为分隔点，如 "·・•"）。</summary>
    public string? Separators { get; init; }

    /// <summary>constant 算子的字面量（支持字符串/数值/布尔）。</summary>
    public JsonElement? Value { get; init; }

    /// <summary>template 算子的模板文本（<c>{键名}</c> 占位符）。</summary>
    public string? Template { get; init; }

    /// <summary>条件执行：仅当此 extras 键存在时才运行本规则（可空=无条件）。</summary>
    public string? WhenPresent { get; init; }

    /// <summary>仅当 Target 尚不存在时才写入（默认 false=覆写），供“拆分失败时补默认值”类声明。</summary>
    public bool OnlyIfAbsent { get; init; }
}

/// <summary>req-088 Phase3：单个接口的取数声明。</summary>
public sealed class FetchEndpoint
{
    /// <summary>响应 URL 子串匹配（如 "remains_percent"、"usage_summary"、"token_plan_credit"、"group/list"）。</summary>
    public string UrlMatch { get; init; } = string.Empty;

    // ============ Stage A：http 直连模式（声明式替代插件 C# API 请求代码） ============

    /// <summary>取数模式：capture（默认，浏览器响应捕获，用 <see cref="UrlMatch"/>）或 http（宿主直接发 HTTP 请求）。</summary>
    public string Mode { get; init; } = "capture";

    /// <summary>Stage E：http 端点是否仅作回退（true = 仅当捕获路径未取到主指标时才请求；默认 false = 总是请求）。</summary>
    public bool Fallback { get; init; }

    /// <summary>http 模式：请求方法（GET/POST，默认 GET）。</summary>
    public string Method { get; init; } = "GET";

    /// <summary>http 模式：绝对 URL 模板（必须 https），支持 <c>{config:字段Key}</c> 与 <c>{cookie:Cookie名}</c> 占位符；
    /// 展开后的真实 URL 在运行期经 req-056 SSRF 防护校验。</summary>
    public string? UrlTemplate { get; init; }

    /// <summary>http 模式：请求头模板（值支持与 <see cref="UrlTemplate"/> 相同的占位符，如 Authorization: "Bearer {config:ApiKey}"）。</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>标量字段映射（jsonpath → extras 键）。</summary>
    public IReadOnlyList<FetchField> Fields { get; init; } = System.Array.Empty<FetchField>();

    /// <summary>数组按字段选取后提取（如 model_remains 里 model_name==general/video）。</summary>
    public IReadOnlyList<FetchArrayFind> Finds { get; init; } = System.Array.Empty<FetchArrayFind>();

    /// <summary>数组展开映射（如 date_model_usage[].models[] → List&lt;Dictionary&gt;）。</summary>
    public IReadOnlyList<FetchArray> Arrays { get; init; } = System.Array.Empty<FetchArray>();
}

/// <summary>req-088 Phase3：数组按字段选取——在数组里找 <see cref="MatchField"/>==<see cref="MatchValue"/> 的首项，从中提取 <see cref="Fields"/>。</summary>
public sealed class FetchArrayFind
{
    /// <summary>数组 jsonpath（如 "$.model_remains"）。</summary>
    public string ItemsPath { get; init; } = string.Empty;

    /// <summary>用于匹配的成员名（如 "model_name"）。</summary>
    public string MatchField { get; init; } = string.Empty;

    /// <summary>匹配值（如 "general" / "video"）。</summary>
    public string MatchValue { get; init; } = string.Empty;

    /// <summary>命中项内的字段映射。</summary>
    public IReadOnlyList<FetchField> Fields { get; init; } = System.Array.Empty<FetchField>();
}

/// <summary>req-088 Phase3：标量字段映射（jsonpath → extras 键，可选匹配条件 + 转换器）。</summary>
public sealed class FetchField
{
    /// <summary>相对端点根的 jsonpath（如 "$.total_days"、"$.model_remains[0].current_interval_used_percent"）。</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>写入 extras 的键（如 "five_hour_used_percent"）。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>转换器名（parsePercent / parseNumber / parseDate / trim / stripNonNumeric / identity）。</summary>
    public string? Transform { get; init; }

    /// <summary>并行数组元素类型（long / double / string，仅 <see cref="FetchArray"/> Mode=parallel 时生效）；默认 string。</summary>
    public string? ElementType { get; init; }
}

/// <summary>
/// req-088 Phase3：数组展开映射。把数组（可选二级嵌套）逐项映射为字典列表写入 extras。
/// <para>例：date_model_usage[].models[] → model_daily（List&lt;Dictionary&gt;{date,model,input_token,...}）。</para>
/// </summary>
public sealed class FetchArray
{
    /// <summary>数组 jsonpath（如 "$.date_model_usage"）。</summary>
    public string ItemsPath { get; init; } = string.Empty;

    /// <summary>展开模式：objects（默认，产出 List&lt;Dictionary&gt;）、parallel（每个 ItemField 产出一条并行强类型列表）
    /// 或 seriesPivot（系列×桶转置：产出类别标签+系列名+值矩阵）。</summary>
    public string Mode { get; init; } = "objects";

    /// <summary>可选：每项内的二级数组成员名（如 "models" / "buckets"）。为空表示只展开一级。</summary>
    public string? NestedItems { get; init; }

    /// <summary>seriesPivot 模式：系列名称字段名（如 "model"——从每个系列对象上取系列名）。</summary>
    public string? SeriesNameField { get; init; }

    /// <summary>写入 extras 的键（如 "model_daily"）。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>每项（或二级每项，含从父项继承的字段）字段映射（相对项根的成员名 → 字段键）。</summary>
    public IReadOnlyList<FetchField> ItemFields { get; init; } = System.Array.Empty<FetchField>();

    /// <summary>
    /// 可选：把父项某成员并入子项（如二级 models 每项带上父项 date）。key=父项成员名，value=写入子项字典的键。
    /// </summary>
    public IReadOnlyDictionary<string, string> InheritFromParent { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>req-088 Phase3：DOM 兜底字段（用 body 文本正则或 querySelector 文本抓取）。</summary>
public sealed class FetchDomField
{
    /// <summary>抓取方式：bodyRegex（对 document.body.innerText 做正则）或 selectorText（querySelector 文本）。</summary>
    public string Tool { get; init; } = "bodyRegex";

    /// <summary>正则或 CSS 选择器。</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>写入 extras 的键。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>转换器名（可选）。</summary>
    public string? Transform { get; init; }
}

/// <summary>req-088 Phase3：账号身份声明——从哪个已捕获接口字段取平台稳定 ID（如 group/list 默认组的 group_id）。</summary>
public sealed class FetchAccountId
{
    /// <summary>提供身份的接口 URL 子串（如 "group/list"）。</summary>
    public string UrlMatch { get; init; } = string.Empty;

    /// <summary>稳定 ID 的 jsonpath（如 "$.groups[0].group_id"）。执行器会优先默认组，其次首项。</summary>
    public string Path { get; init; } = string.Empty;
}
