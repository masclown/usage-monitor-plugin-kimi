using System.Collections.Generic;

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

    /// <summary>写入 extras 的键（如 "mm_cacheHitPercent"）。</summary>
    public string Target { get; init; } = string.Empty;
}

/// <summary>req-088 Phase3：计算列——基于已有 extras 键计算（当前支持 subtract：operands[0] - operands[1] - ...）。</summary>
public sealed class FetchComputed
{
    /// <summary>写入 extras 的键（如 "mm_videoIntervalUsed"）。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>算子（当前仅 subtract）。</summary>
    public string Op { get; init; } = "subtract";

    /// <summary>参与计算的 extras 键列表（按顺序）。</summary>
    public IReadOnlyList<string> Operands { get; init; } = System.Array.Empty<string>();
}

/// <summary>req-088 Phase3：单个接口的取数声明。</summary>
public sealed class FetchEndpoint
{
    /// <summary>响应 URL 子串匹配（如 "remains_percent"、"usage_summary"、"token_plan_credit"、"group/list"）。</summary>
    public string UrlMatch { get; init; } = string.Empty;

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

    /// <summary>写入 extras 的键（如 "mm_5hUsedPercent"）。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>转换器名（parsePercent / parseNumber / parseDate / trim / stripNonNumeric / identity）。</summary>
    public string? Transform { get; init; }

    /// <summary>并行数组元素类型（long / double / string，仅 <see cref="FetchArray"/> Mode=parallel 时生效）；默认 string。</summary>
    public string? ElementType { get; init; }
}

/// <summary>
/// req-088 Phase3：数组展开映射。把数组（可选二级嵌套）逐项映射为字典列表写入 extras。
/// <para>例：date_model_usage[].models[] → mm_modelDaily（List&lt;Dictionary&gt;{date,model,input_token,...}）。</para>
/// </summary>
public sealed class FetchArray
{
    /// <summary>数组 jsonpath（如 "$.date_model_usage"）。</summary>
    public string ItemsPath { get; init; } = string.Empty;

    /// <summary>展开模式：objects（默认，产出 List&lt;Dictionary&gt;）或 parallel（每个 ItemField 产出一条并行强类型列表）。</summary>
    public string Mode { get; init; } = "objects";

    /// <summary>可选：每项内的二级数组成员名（如 "models"）。为空表示只展开一级。</summary>
    public string? NestedItems { get; init; }

    /// <summary>写入 extras 的键（如 "mm_modelDaily"）。</summary>
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
