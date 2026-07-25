using System.Collections.Generic;
using System.Linq;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 声明式图表类型（req-107 B5）。
/// <para>用于显示声明（defaults.json）的图表 kind；与既有 <c>ChartKind</c>/<c>CardChartKind</c>/<c>MiniChartKind</c>
/// 并存，由宿主适配层映射到具体控件。本枚举额外包含 Number / MiniRingChart / MiniText 等声明式专用类型。</para>
/// </summary>
public enum DeclarativeChartKind
{
    /// <summary>折线图（周期切片，需日期 Meta + 数值 Value）。</summary>
    Line,
    /// <summary>条形图 / 进度条（数据组切片，Value + 可选 Upper/Lower，支持色阶）。</summary>
    Bar,
    /// <summary>热力图（周期切片，日期 Meta + 数值 Value，支持色阶）。</summary>
    HeatMap,
    /// <summary>环形图（数据组切片，Value 必须为百分比，支持色阶）。</summary>
    Ring,
    /// <summary>数字（无切片器，单 Value）。</summary>
    Number,
    /// <summary>迷你圆环图（任务栏，数据组切片，Value 必须为百分比）。</summary>
    MiniRingChart,
    /// <summary>迷你文本（任务栏，Reset/Meta 文本）。</summary>
    MiniText
}

/// <summary>
/// 图表能力规格（req-107 B5）：定义一种图表类型支持的切片器模式、字段角色要求、数据类型约束与色阶支持。
/// <para>加载插件声明时由 <see cref="ChartKindSpecRegistry.Validate"/> 按本规格校验，
/// 例如"折线缺日期 Meta / 环形 Value 非百分比 / 柱状用了 Period 切片器"等错误会在加载期暴露。</para>
/// </summary>
public sealed record ChartKindSpec
{
    /// <summary>图表类型。</summary>
    public required DeclarativeChartKind Kind { get; init; }

    /// <summary>支持的切片器模式（空表示不支持切片器）。</summary>
    public IReadOnlyList<SlicerMode> SupportedSlicerModes { get; init; } = System.Array.Empty<SlicerMode>();

    /// <summary>必需的字段角色（每个数据组都必须包含）。</summary>
    public IReadOnlyList<FieldRole> RequiredRoles { get; init; } = System.Array.Empty<FieldRole>();

    /// <summary>可选字段角色。</summary>
    public IReadOnlyList<FieldRole> OptionalRoles { get; init; } = System.Array.Empty<FieldRole>();

    /// <summary>Value 字段允许的数据类型（空表示不约束）。</summary>
    public IReadOnlyList<UsageFieldDataType> AllowedValueTypes { get; init; } = System.Array.Empty<UsageFieldDataType>();

    /// <summary>是否支持色阶。</summary>
    public bool SupportsColorTiers { get; init; }
}

/// <summary>
/// 图表能力规格注册表 + 声明校验（req-107 B5）。
/// </summary>
public static class ChartKindSpecRegistry
{
    private static readonly Dictionary<DeclarativeChartKind, ChartKindSpec> Specs = BuildSpecs();

    /// <summary>
    /// 构建各图表类型的能力规格（依据 req-107 B5 约束表）。
    /// </summary>
    private static Dictionary<DeclarativeChartKind, ChartKindSpec> BuildSpecs()
    {
        var percentOnly = new[] { UsageFieldDataType.Percent };
        var numeric = new[]
        {
            UsageFieldDataType.Percent, UsageFieldDataType.Number, UsageFieldDataType.Token,
            UsageFieldDataType.Credit, UsageFieldDataType.Currency, UsageFieldDataType.Count
        };
        var list = new List<ChartKindSpec>
        {
            // 折线：Period/DataGroup 切片；需 Meta(日期) + Value(数值)；无色阶
            new()
            {
                Kind = DeclarativeChartKind.Line,
                SupportedSlicerModes = new[] { SlicerMode.Period, SlicerMode.DataGroup },
                RequiredRoles = new[] { FieldRole.Meta, FieldRole.Value },
                AllowedValueTypes = numeric,
                SupportsColorTiers = false
            },
            // 柱状/进度条：DataGroup 切片；需 Value，可选 Upper/Lower；支持色阶
            new()
            {
                Kind = DeclarativeChartKind.Bar,
                SupportedSlicerModes = new[] { SlicerMode.DataGroup },
                RequiredRoles = new[] { FieldRole.Value },
                // 问题4：允许 Reset 角色字段（进度条底部刷新倒计时）
                OptionalRoles = new[] { FieldRole.Upper, FieldRole.Lower, FieldRole.Meta, FieldRole.Reset },
                AllowedValueTypes = numeric,
                SupportsColorTiers = true
            },
            // 热力图：Period 切片；需 Meta(日期) + Value；支持色阶
            new()
            {
                Kind = DeclarativeChartKind.HeatMap,
                SupportedSlicerModes = new[] { SlicerMode.Period },
                RequiredRoles = new[] { FieldRole.Meta, FieldRole.Value },
                AllowedValueTypes = numeric,
                SupportsColorTiers = true
            },
            // 环形图：DataGroup 切片；Value 必须为百分比；支持色阶
            new()
            {
                Kind = DeclarativeChartKind.Ring,
                SupportedSlicerModes = new[] { SlicerMode.DataGroup },
                RequiredRoles = new[] { FieldRole.Value },
                AllowedValueTypes = percentOnly,
                SupportsColorTiers = true
            },
            // 数字：无切片器；单 Value
            new()
            {
                Kind = DeclarativeChartKind.Number,
                SupportedSlicerModes = System.Array.Empty<SlicerMode>(),
                RequiredRoles = new[] { FieldRole.Value },
                // 问题6/7：允许 Upper（分母，渲染为 "分子/分母"）与 Meta（备注行）角色字段
                OptionalRoles = new[] { FieldRole.Upper, FieldRole.Meta },
                AllowedValueTypes = numeric,
                SupportsColorTiers = false
            },
            // 迷你圆环：DataGroup 切片；Value 必须为百分比；支持色阶
            new()
            {
                Kind = DeclarativeChartKind.MiniRingChart,
                SupportedSlicerModes = new[] { SlicerMode.DataGroup },
                RequiredRoles = new[] { FieldRole.Value },
                AllowedValueTypes = percentOnly,
                SupportsColorTiers = true
            },
            // 迷你文本：DataGroup 切片（问题7：与迷你圆环对齐，支持 5h/周等数据组滚轮切换）；Reset/Meta/Value 文本
            new()
            {
                Kind = DeclarativeChartKind.MiniText,
                SupportedSlicerModes = new[] { SlicerMode.DataGroup },
                RequiredRoles = System.Array.Empty<FieldRole>(),
                OptionalRoles = new[] { FieldRole.Reset, FieldRole.Meta, FieldRole.Value },
                AllowedValueTypes = System.Array.Empty<UsageFieldDataType>(),
                SupportsColorTiers = false
            }
        };
        return list.ToDictionary(s => s.Kind);
    }

    /// <summary>
    /// 获取指定图表类型的能力规格；未注册返回 <c>null</c>。
    /// </summary>
    public static ChartKindSpec? GetSpec(DeclarativeChartKind kind) => Specs.TryGetValue(kind, out var s) ? s : null;

    /// <summary>
    /// 校验一个图表声明是否满足其类型的能力规格。
    /// </summary>
    /// <param name="chart">待校验的图表声明。</param>
    /// <returns>错误信息列表；空列表表示校验通过。</returns>
    public static IReadOnlyList<string> Validate(ChartDeclaration chart)
    {
        var errors = new List<string>();
        var spec = GetSpec(chart.Kind);
        if (spec == null)
        {
            errors.Add($"图表 {chart.ChartId}：未知图表类型 {chart.Kind}");
            return errors;
        }

        // 数据组非空
        if (chart.DataGroups.Count == 0)
            errors.Add($"图表 {chart.ChartId}（{chart.Kind}）：至少需要一个 dataGroup");

        // 切片器模式支持性
        if (chart.Slicer != null && !spec.SupportedSlicerModes.Contains(chart.Slicer.Mode))
            errors.Add($"图表 {chart.ChartId}（{chart.Kind}）：不支持 {chart.Slicer.Mode} 切片器模式");

        foreach (var group in chart.DataGroups)
        {
            // 必需角色齐全
            foreach (var role in spec.RequiredRoles)
            {
                if (!group.Fields.Any(f => f.Role == role))
                    errors.Add($"图表 {chart.ChartId} 数据组 {group.Id}：缺少必需的 {role} 角色字段");
            }

            // Value 字段数据类型约束
            if (spec.AllowedValueTypes.Count > 0)
            {
                foreach (var field in group.Fields.Where(f => f.Role == FieldRole.Value))
                {
                    var meta = UsageFieldMetadataRegistry.Get(field.FieldName);
                    if (meta == null)
                    {
                        errors.Add($"图表 {chart.ChartId} 数据组 {group.Id}：字段 {field.FieldName} 非 SDK 合法字段");
                    }
                    else if (!spec.AllowedValueTypes.Contains(meta.DataType))
                    {
                        errors.Add($"图表 {chart.ChartId} 数据组 {group.Id}：Value 字段 {field.FieldName} 数据类型 {meta.DataType} 不被 {chart.Kind} 允许");
                    }
                }
            }
        }

        // 色阶支持性
        if (chart.ColorTiers != null && !spec.SupportsColorTiers)
            errors.Add($"图表 {chart.ChartId}（{chart.Kind}）：不支持色阶");

        return errors;
    }
}
