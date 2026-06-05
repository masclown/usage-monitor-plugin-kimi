using System.Text.Json;

namespace UsageMonitor.Core.Models;

/// <summary>
/// 服务商配置模型 - 存储单个AI服务商的配置信息（如API Key等）
/// </summary>
public class ProviderConfig
{
    /// <summary>服务商唯一标识</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>是否启用此服务商</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>配置键值对（如 ApiKey=xxx）</summary>
    public Dictionary<string, string> Values { get; set; } = new();

    /// <summary>
    /// 获取指定键的配置值
    /// </summary>
    public string? GetValue(string key)
    {
        return Values.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// 设置指定键的配置值
    /// </summary>
    public void SetValue(string key, string value)
    {
        Values[key] = value;
    }

    /// <summary>
    /// 验证必填配置项是否已填写
    /// </summary>
    public bool Validate(IReadOnlyList<ConfigField> fields)
    {
        foreach (var field in fields.Where(f => f.IsRequired))
        {
            var value = GetValue(field.Key);
            if (string.IsNullOrWhiteSpace(value))
                return false;
        }
        return true;
    }
}
