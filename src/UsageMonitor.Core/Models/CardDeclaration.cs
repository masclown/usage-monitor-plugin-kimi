namespace UsageMonitor.Core.Models;

/// <summary>
/// 切片器模式（req-107 B4）。
/// </summary>
public enum SlicerMode
{
    /// <summary>按时间周期切换（近 7 天 / 近 30 天等，选项用 <see cref="TimeRange"/> 内置翻译）。</summary>
    Period,
    /// <summary>按数据组切换（5h / 周 / 视频等，选项用主字段 SDK 标签）。</summary>
    DataGroup
}

/// <summary>
/// 切片器交互方式（req-107 B4）。
/// </summary>
public enum SlicerInteraction
{
    /// <summary>分段按钮（如折线图右上角"近 7 天 / 近 30 天"）。</summary>
    Button,
    /// <summary>滚轮切换（如任务栏半圆环滚轮切 5h / 周）。</summary>
    Scroll,
    /// <summary>下拉菜单。</summary>
    Dropdown
}

/// <summary>
/// 切片器位置（req-107 B4）。
/// </summary>
public enum SlicerPosition
{
    /// <summary>右上角。</summary>
    TopRight,
    /// <summary>底部。</summary>
    Bottom,
    /// <summary>跟随图表标题区。</summary>
    Inline
}

/// <summary>
/// 切片器声明（req-107 B4）：声明图表如何切换数据窗口 / 数据组。
/// <para>切片器选项无需插件写描述：<see cref="SlicerMode.Period"/> 用 <see cref="TimeRanges"/> 的内置翻译，
/// <see cref="SlicerMode.DataGroup"/> 用各数据组主字段的 SDK 标签。</para>
/// </summary>
public sealed class SlicerSpec
{
    /// <summary>切片模式（Period / DataGroup）。</summary>
    public SlicerMode Mode { get; init; } = SlicerMode.DataGroup;

    /// <summary>切片器显示位置。</summary>
    public SlicerPosition? Position { get; init; }

    /// <summary>交互方式（按钮 / 滚轮 / 下拉）。</summary>
    public SlicerInteraction Interaction { get; init; } = SlicerInteraction.Button;

    /// <summary>Period 模式可选时间范围列表（如 [Last7Days, Last30Days]）。</summary>
    public IReadOnlyList<TimeRange> TimeRanges { get; init; } = System.Array.Empty<TimeRange>();

    /// <summary>默认选中项：Period 模式为 <see cref="TimeRange"/> 名，DataGroup 模式为数据组 Id。</summary>
    public string? Default { get; init; }
}

/// <summary>
/// 图表级 Tooltip 声明（req-107 B4）：声明 hover 时展示哪些字段。
/// </summary>
public sealed class TooltipSpec
{
    /// <summary>Tooltip 展示的 SDK 标准字段名列表（<see cref="UsageFields"/> 常量）。</summary>
    public IReadOnlyList<string> Fields { get; init; } = System.Array.Empty<string>();
}

/// <summary>
/// 图表级色阶声明（req-107 B4）。
/// <para>两种用法：① <see cref="Ref"/> 引用全局色阶（如 "global:usage-tier-default"）；
/// ② 内联 <see cref="Thresholds"/> + <see cref="Colors"/> 覆盖。用户设置页可再覆盖（source 选择）。</para>
/// </summary>
public sealed class ColorTierSpec
{
    /// <summary>引用全局色阶的键（与内联阈值/颜色二选一）。</summary>
    public string? Ref { get; init; }

    /// <summary>色阶阈值分界点（如 [0, 30, 60, 80, 100]）。</summary>
    public IReadOnlyList<double> Thresholds { get; init; } = System.Array.Empty<double>();

    /// <summary>各档颜色（hex 字符串，如 "#FF5A3D"，与 <see cref="Thresholds"/> 对应）。</summary>
    public IReadOnlyList<string> Colors { get; init; } = System.Array.Empty<string>();
}

/// <summary>
/// 图表声明（req-107 B4）：一个图表的完整显示声明（类型 + 数据组 + 切片器 + Tooltip + 色阶）。
/// <para>图表标题不在声明里——由用户设置界面定义，<see cref="ChartId"/> 作为稳定引用键。
/// 加载时按 <see cref="ChartKindSpecRegistry.Validate"/> 校验数据组字段角色 / 数据类型 / 切片器模式约束。</para>
/// </summary>
public sealed class ChartDeclaration
{
    /// <summary>图表稳定 ID（如 "mm.chart.usage_bar"，用户设置标题/排序按此引用）。</summary>
    public required string ChartId { get; init; }

    /// <summary>图表类型。</summary>
    public DeclarativeChartKind Kind { get; init; } = DeclarativeChartKind.Bar;

    /// <summary>默认排序序号（越小越靠前）。</summary>
    public int DefaultOrder { get; init; }

    /// <summary>计划类型维度（Api / TokenPlan；用于订阅类型并存图表）。</summary>
    public string? PlanType { get; init; }

    /// <summary>切片器声明（缺省表示无切片器）。</summary>
    public SlicerSpec? Slicer { get; init; }

    /// <summary>图表级 Tooltip 声明。</summary>
    public TooltipSpec? Tooltip { get; init; }

    /// <summary>图表级色阶声明（支持 ref 引用全局或内联覆盖）。</summary>
    public ColorTierSpec? ColorTiers { get; init; }

    /// <summary>数据组列表（至少一个）。</summary>
    public IReadOnlyList<DataGroup> DataGroups { get; init; } = System.Array.Empty<DataGroup>();
}

/// <summary>
/// 卡片基础信息区声明（req-107 B4）：卡片头部的元信息字段（订阅档位 / 账号名 / 重置倒计时等）。
/// </summary>
public sealed class CardBaseInfo
{
    /// <summary>基础信息字段引用列表（Meta / Reset 角色）。</summary>
    public IReadOnlyList<FieldReference> Fields { get; init; } = System.Array.Empty<FieldReference>();
}

/// <summary>
/// 卡片显示声明聚合根（req-107 B4）：来自插件 defaults.json 的 card 节，或插件代码 override。
/// </summary>
public sealed class CardDeclaration
{
    /// <summary>卡片基础信息区。</summary>
    public CardBaseInfo? BaseInfo { get; init; }

    /// <summary>卡片图表声明列表。</summary>
    public IReadOnlyList<ChartDeclaration> Charts { get; init; } = System.Array.Empty<ChartDeclaration>();

    /// <summary>
    /// req-107 B6：卡片首屏渲染能力集合（声明式替代旧 IDefaultRenderKindsProvider.DefaultRenderKinds）。
    /// <para>在数据未到位时即声明卡片应展示的区段（如 "primaryBar"、"weeklyBar" 等），
    /// 让首屏渲染与数据到位后保持一致。空集合表示使用宿主默认行为。</para>
    /// </summary>
    public IReadOnlyList<string> RenderKinds { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// req-088 Phase2：卡片主指标字段名（声明式）。声明“卡片主数字/主环”应展示哪个 SDK 字段，
    /// 替代过去把 used_percent 硬编码为 5h 的做法（如 MiniMax 声明 "five_hour_used_percent"）。null = 沿用宿主默认。
    /// </summary>
    public string? PrimaryMetric { get; init; }

    /// <summary>
    /// Stage E：折叠态仍可见的部件集合（声明式替代 IDefaultRenderKindsProvider.CollapseVisibleParts 的 C# override）。
    /// <para>如 ["limitBars"] 表示折叠时保留限额进度条区段；空集合 = 折叠态隐藏全部区段（宿主默认）。</para>
    /// </summary>
    public IReadOnlyList<string> CollapseVisibleParts { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Stage E：Provider 默认热力图色阶（按 token 绝对值分档），声明式替代宿主按 ProviderId 硬编码的出厂色阶。
    /// <para>启动时由宿主注册为声明级兜底；用户在设置页保存的色阶始终优先。空集合 = 使用宿主通用兜底。</para>
    /// </summary>
    public IReadOnlyList<HeatMapTierConfig> HeatMapTiers { get; init; } = System.Array.Empty<HeatMapTierConfig>();
}
