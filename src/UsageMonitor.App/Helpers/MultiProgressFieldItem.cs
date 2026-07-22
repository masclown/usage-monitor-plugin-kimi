namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-104：多进度条/数字多排设置页的字段选择项。
/// </summary>
public class MultiProgressFieldItem
{
    /// <summary>Provider ID（唯一标识）。</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Provider 显示名称。</summary>
    public string ProviderDisplayName { get; set; } = string.Empty;

    /// <summary>字段名（对应 MetricBarItem.Label 或 MetricGridItem.Label）。</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>字段显示名称（用于 UI 展示）。</summary>
    public string FieldDisplayName { get; set; } = string.Empty;

    /// <summary>是否选中（绑定 CheckBox）。</summary>
    public bool IsSelected { get; set; }

    /// <summary>字段类型（"Progress" 或 "Metric"）。</summary>
    public string FieldType { get; set; } = string.Empty;
}
