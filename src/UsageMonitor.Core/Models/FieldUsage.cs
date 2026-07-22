namespace UsageMonitor.Core.Models;

/// <summary>
/// 字段用途枚举（req-100 B6）。
/// <para>用于标注 SDK 字段组契约中每个字段的用途，便于按用途分类展示、筛选与未匹配提示。</para>
/// </summary>
public enum FieldUsage
{
    /// <summary>数据字段（用于取值展示，如已用量/总量/百分比）。</summary>
    Data,

    /// <summary>主题字段（用于主题/外观设置，如颜色）。</summary>
    Theme,

    /// <summary>设置参数字段（用于功能开关/配置，如是否倒序、色阶设置）。</summary>
    Setting
}
