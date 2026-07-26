using System.Collections.Concurrent;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Security;

/// <summary>
/// 敏感配置键注册表：插件通过配置字段元数据（<see cref="ConfigField.Sensitive"/> 或
/// <see cref="ConfigFieldType.Password"/> 类型）显式声明敏感键，宿主在插件加载时注册；
/// <c>ConfigService</c> 落盘加密判定优先命中本注册表，关键词表（apikey/token/secret/password/cookie）
/// 降级为兜底，避免非常规命名的凭据字段（如 SessionId、Auth）被明文落盘。
/// </summary>
public static class SensitiveConfigKeyRegistry
{
    /// <summary>已注册的敏感键集合（大小写不敏感；value 无意义仅占位）。</summary>
    private static readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>兜底关键词表（与历史行为一致：键名包含任一关键词即视为敏感）。</summary>
    private static readonly string[] FallbackKeywords = { "apikey", "token", "secret", "password", "cookie" };

    /// <summary>
    /// 批量注册插件声明的配置字段：<see cref="ConfigField.Sensitive"/> 为 true 或字段类型为
    /// Password 的键进入敏感集合。空集合/空引用安全。
    /// </summary>
    /// <param name="fields">插件配置字段声明列表。</param>
    public static void RegisterFields(IEnumerable<ConfigField>? fields)
    {
        if (fields == null) return;
        foreach (var field in fields)
        {
            if (field == null) continue;
            if (field.Sensitive || field.FieldType == ConfigFieldType.Password)
                Register(field.Key);
        }
    }

    /// <summary>注册单个敏感键（空白键忽略）。</summary>
    /// <param name="key">配置键名。</param>
    public static void Register(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _keys.TryAdd(key.Trim(), 0);
    }

    /// <summary>
    /// 判断配置键是否敏感：注册表精确命中（大小写不敏感）优先，其次关键词子串兜底。
    /// </summary>
    /// <param name="key">配置键名。</param>
    public static bool IsSensitive(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (_keys.ContainsKey(key.Trim())) return true;
        return FallbackKeywords.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>当前已注册的敏感键快照（诊断/测试用）。</summary>
    public static IReadOnlyCollection<string> RegisteredKeys => _keys.Keys.ToList();
}
