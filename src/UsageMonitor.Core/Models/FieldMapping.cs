namespace UsageMonitor.Core.Models;

/// <summary>
/// 卡片字段映射关系 - 定义卡片显示所需的字段映射
/// <para>req-100：插件字段映射声明，卡片按映射关系从 UsageInfo 中取数。</para>
/// </summary>
public class FieldMapping
{
    /// <summary>已用量字段名（如 "UsedTokens" / "UsedAmount"）</summary>
    public string UsedAmountField { get; set; } = "UsedAmount";

    /// <summary>总量字段名（如 "TotalTokens" / "TotalAmount"）</summary>
    public string TotalAmountField { get; set; } = "TotalAmount";

    /// <summary>百分比字段名（如 "UsagePercent"）</summary>
    public string PercentField { get; set; } = "UsagePercent";

    /// <summary>重置时间字段名（如 "NextResetTime"）</summary>
    public string ResetTimeField { get; set; } = "NextResetTime";

    /// <summary>余额字段名（如 "Balance" / "Credits"）</summary>
    public string BalanceField { get; set; } = "Balance";

    /// <summary>
    /// 从 UsageInfo 中按映射关系获取字段值
    /// </summary>
    /// <typeparam name="T">字段值类型</typeparam>
    /// <param name="usageInfo">用量信息对象</param>
    /// <param name="fieldName">映射的字段名</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>字段值，未找到时返回默认值</returns>
    public T? GetFieldValue<T>(UsageInfo usageInfo, string fieldName, T? defaultValue = default)
    {
        if (usageInfo == null || string.IsNullOrWhiteSpace(fieldName))
            return defaultValue;

        // 优先从 Extra 字典中读取
        if (usageInfo.Extra != null && usageInfo.Extra.TryGetValue(fieldName, out var extraValue))
        {
            try
            {
                if (extraValue is T typedValue)
                    return typedValue;

                // 尝试类型转换
                return (T)Convert.ChangeType(extraValue, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        // 回退到 UsageInfo 的标准属性
        var property = typeof(UsageInfo).GetProperty(fieldName);
        if (property != null)
        {
            try
            {
                var value = property.GetValue(usageInfo);
                if (value is T typedValue)
                    return typedValue;

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }
}
