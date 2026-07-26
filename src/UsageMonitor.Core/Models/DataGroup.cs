namespace UsageMonitor.Core.Models;

/// <summary>
/// 字段在图表数据组中的角色（req-107 B4）。
/// </summary>
public enum FieldRole
{
    /// <summary>数值（进度条当前值 / 折线 Y 值 / 环形百分比等）。</summary>
    Value,
    /// <summary>元信息（日期 / 订阅档位 / 账号名等维度）。</summary>
    Meta,
    /// <summary>上限（进度条 Upper）。</summary>
    Upper,
    /// <summary>下限（进度条 Lower）。</summary>
    Lower,
    /// <summary>重置时间（倒计时）。</summary>
    Reset
}

/// <summary>
/// 字段引用（req-107 B4）：声明"图表数据组消费哪个 SDK 标准字段、扮演什么角色"。
/// <para><see cref="FieldName"/> 必须是 <see cref="UsageFields"/> 标准字段名（加载时经白名单校验）；
/// 插件零翻译——字段的标签 / 单位由 SDK 元数据 + 主程序 i18n 提供。</para>
/// </summary>
public sealed class FieldReference
{
    /// <summary>SDK 标准字段名（<see cref="UsageFields"/> 常量）。</summary>
    public required string FieldName { get; init; }

    /// <summary>字段在本数据组中的角色。</summary>
    public FieldRole Role { get; init; } = FieldRole.Value;
}

/// <summary>
/// 查询范围（req-107 B4）：声明数据组的时间窗口。
/// <para>两种声明方式：① <see cref="Range"/> 用预定义 <see cref="TimeRange"/>；
/// ② <see cref="Type"/>=LastDays + <see cref="Days"/> 自定义天数（如热力图近 90 天）。</para>
/// </summary>
public sealed class QueryRange
{
    /// <summary>范围类型（目前支持 "LastDays"；缺省按 <see cref="Range"/> 解析）。</summary>
    public string? Type { get; init; }

    /// <summary>自定义天数（<see cref="Type"/>=LastDays 时生效）。</summary>
    public int? Days { get; init; }

    /// <summary>预定义时间范围（与 <see cref="Type"/> 二选一）。</summary>
    public TimeRange? Range { get; init; }

    /// <summary>
    /// 解析为实际天数；无法解析或不限窗口时返回 <c>null</c>。
    /// </summary>
    public int? ResolveDays()
    {
        if (Days is > 0) return Days;
        return Range?.ToDays();
    }
}

/// <summary>
/// 数据组（req-107 B4）：图表的一组字段引用 + 可选窗口/计划类型维度。
/// <para>同一图表可声明多个数据组（如条形图的 5h / 周 / 视频三组），由切片器（滚轮/按钮/下拉）切换；
/// <see cref="PlanType"/> 用于订阅类型并存（API + TokenPlan 各一组，共用通用字段）。</para>
/// </summary>
public sealed class DataGroup
{
    /// <summary>数据组稳定 ID（如 "mm.bar.5h"，切片器 default 引用它）。</summary>
    public required string Id { get; init; }

    /// <summary>数据组中文显示名（可空；缺省时宿主回退到从 <see cref="Id"/> 提取的短名）。供设置界面展示。</summary>
    public string? Display { get; init; }

    /// <summary>计划类型维度（Api / TokenPlan；缺省表示通用）。</summary>
    public string? PlanType { get; init; }

    /// <summary>查询时间窗口（缺省由图表级切片器或默认窗口决定）。</summary>
    public QueryRange? QueryRange { get; init; }

    /// <summary>本组的字段引用列表（至少一个 Value 角色字段，加载时按 ChartKindSpec 校验）。</summary>
    public IReadOnlyList<FieldReference> Fields { get; init; } = System.Array.Empty<FieldReference>();

    /// <summary>
    /// 序列数据键（迷你时序图表用）：Provider 据此将时间序列写入
    /// <c>UsageInfo.Extra["mini_series:{SeriesKey}"]</c>，宿主按当前数据组的 SeriesKey 取出渲染。
    /// <para>null = 无序列声明（单值图表如环形/文本，或回退 HistoryValues 兼容路径）。</para>
    /// </summary>
    public string? SeriesKey { get; init; }
}
