namespace UsageMonitor.Core.Models;

/// <summary>
/// 迷你图表声明（req-107 B4）：任务栏 / 卡片浮窗用的小型图表（迷你圆环 / 迷你文本等）。
/// </summary>
public sealed class MiniChartDeclaration
{
    /// <summary>迷你图表稳定 ID（如 "mm.mini.ring"）。</summary>
    public required string ChartId { get; init; }

    /// <summary>迷你图表中文显示名（可空；缺省时宿主回退到从 <see cref="ChartId"/> 提取的短名）。供设置界面展示（问题6）。</summary>
    public string? Display { get; init; }

    /// <summary>图表类型（MiniRingChart / MiniText 等）。</summary>
    public DeclarativeChartKind Kind { get; init; } = DeclarativeChartKind.MiniRingChart;

    /// <summary>切片器声明（如滚轮切 5h / 周）。</summary>
    public SlicerSpec? Slicer { get; init; }

    /// <summary>图表级 Tooltip 声明。</summary>
    public TooltipSpec? Tooltip { get; init; }

    /// <summary>图表级色阶声明。</summary>
    public ColorTierSpec? ColorTiers { get; init; }

    /// <summary>数据组列表。</summary>
    public IReadOnlyList<DataGroup> DataGroups { get; init; } = System.Array.Empty<DataGroup>();

    /// <summary>图表宽度（DIP，插件声明默认值；null = 宿主默认 120）。用户可在设置中覆盖。</summary>
    public int? Width { get; init; }
}

/// <summary>
/// 任务栏基础信息区声明（req-107 B4）。
/// </summary>
public sealed class TaskbarBaseInfo
{
    /// <summary>基础信息字段引用列表（Meta / Reset 角色）。</summary>
    public IReadOnlyList<FieldReference> Fields { get; init; } = System.Array.Empty<FieldReference>();
}

/// <summary>
/// 任务栏显示声明聚合根（req-107 B4）：来自插件 defaults.json 的 taskbar 节，或插件代码 override。
/// </summary>
public sealed class TaskbarDeclaration
{
    /// <summary>任务栏基础信息区。</summary>
    public TaskbarBaseInfo? BaseInfo { get; init; }

    /// <summary>迷你图表声明列表。</summary>
    public IReadOnlyList<MiniChartDeclaration> MiniCharts { get; init; } = System.Array.Empty<MiniChartDeclaration>();
}
