namespace UsageMonitor.Core.Models;

/// <summary>
/// 配置字段类型枚举
/// </summary>
public enum ConfigFieldType
{
    /// <summary>普通文本</summary>
    Text,
    /// <summary>密码（输入时隐藏）</summary>
    Password,
    /// <summary>数字</summary>
    Number,
    /// <summary>布尔开关</summary>
    Boolean,
    /// <summary>下拉选择</summary>
    Select
}

/// <summary>
/// 配置字段定义 - 插件通过此类定义自身需要的配置项
/// </summary>
public class ConfigField
{
    /// <summary>字段键名（用于存储和读取）</summary>
    public string Key { get; set; }

    /// <summary>字段显示名称</summary>
    public string DisplayName { get; set; }

    /// <summary>字段类型</summary>
    public ConfigFieldType FieldType { get; set; }

    /// <summary>是否必填</summary>
    public bool IsRequired { get; set; }

    /// <summary>默认值</summary>
    public string? DefaultValue { get; set; }

    /// <summary>占位提示文本</summary>
    public string? Placeholder { get; set; }

    /// <summary>选项列表（仅当 FieldType 为 Select 时使用）</summary>
    public IReadOnlyList<string>? Options { get; set; }

    /// <summary>
    /// 创建配置字段实例
    /// </summary>
    public ConfigField(string key, string displayName, ConfigFieldType fieldType, bool isRequired = false,
        string? defaultValue = null, string? placeholder = null, IReadOnlyList<string>? options = null)
    {
        Key = key;
        DisplayName = displayName;
        FieldType = fieldType;
        IsRequired = isRequired;
        DefaultValue = defaultValue;
        Placeholder = placeholder;
        Options = options;
    }
}
