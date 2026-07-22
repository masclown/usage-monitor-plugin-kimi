using System.Text.Json;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 字段变更类型枚举
/// </summary>
public enum ChangeType
{
    /// <summary>新增字段</summary>
    Added,

    /// <summary>字段值修改</summary>
    Modified,

    /// <summary>字段删除（暂未使用，保留扩展）</summary>
    Deleted
}

/// <summary>
/// 字段变更记录
/// </summary>
public class FieldChange
{
    /// <summary>字段名</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>旧值（JSON 序列化后的字符串）</summary>
    public string? OldValue { get; set; }

    /// <summary>新值（JSON 序列化后的字符串）</summary>
    public string? NewValue { get; set; }

    /// <summary>变更类型</summary>
    public ChangeType Type { get; set; }

    /// <summary>值类型（string/number/bool/datetime/json）</summary>
    public string ValueType { get; set; } = "string";

    public FieldChange(string fieldName, object? oldValue, object? newValue, ChangeType type)
    {
        FieldName = fieldName;
        Type = type;
        OldValue = SerializeValue(oldValue);
        NewValue = SerializeValue(newValue);
        ValueType = DetermineValueType(newValue ?? oldValue);
    }

    private static string? SerializeValue(object? value)
    {
        if (value == null) return null;
        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch
        {
            return value.ToString();
        }
    }

    private static string DetermineValueType(object? value)
    {
        if (value == null) return "null";
        return value switch
        {
            string => "string",
            int or long or float or double or decimal => "number",
            bool => "bool",
            DateTime => "datetime",
            _ => "json"
        };
    }
}

/// <summary>
/// 字段级差异检测服务接口
/// </summary>
public interface IUsageDataDiffService
{
    /// <summary>
    /// 检测新旧数据之间的字段级差异
    /// </summary>
    /// <param name="oldData">旧数据（标准字段名字典）</param>
    /// <param name="newData">新数据（标准字段名字典）</param>
    /// <returns>字段变更列表</returns>
    FieldChange[] DetectChanges(
        IReadOnlyDictionary<string, object> oldData,
        IReadOnlyDictionary<string, object> newData);

    /// <summary>
    /// 从 UsageInfo 提取标准字段字典
    /// </summary>
    /// <param name="usage">用量信息对象</param>
    /// <returns>标准字段名字典</returns>
    IReadOnlyDictionary<string, object> ExtractStandardFields(UsageInfo usage);
}

/// <summary>
/// 字段级差异检测引擎 - 对比新旧数据，仅识别有变化的字段
/// <para>req-092：用量数据差异持久化，字段级差异检测。</para>
/// <para>支持数值类型精度容差（0.0001 内视为相同），支持嵌套对象和集合类型的字段级对比。</para>
/// </summary>
public class UsageDataDiffService : IUsageDataDiffService
{
    /// <summary>数值类型精度容差</summary>
    private const double NumericTolerance = 0.0001;

    /// <summary>
    /// 检测新旧数据之间的字段级差异
    /// </summary>
    public FieldChange[] DetectChanges(
        IReadOnlyDictionary<string, object> oldData,
        IReadOnlyDictionary<string, object> newData)
    {
        var changes = new List<FieldChange>();

        if (newData == null) return changes.ToArray();

        foreach (var (key, newValue) in newData)
        {
            if (oldData == null || !oldData.TryGetValue(key, out var oldValue))
            {
                // 新字段
                changes.Add(new FieldChange(key, null, newValue, ChangeType.Added));
            }
            else if (!ValuesEqual(oldValue, newValue))
            {
                // 字段值变化
                changes.Add(new FieldChange(key, oldValue, newValue, ChangeType.Modified));
            }
            // 相同则跳过，不记录
        }

        return changes.ToArray();
    }

    /// <summary>
    /// 从 UsageInfo 提取标准字段字典
    /// </summary>
    public IReadOnlyDictionary<string, object> ExtractStandardFields(UsageInfo usage)
    {
        if (usage == null) return new Dictionary<string, object>();

        var fields = new Dictionary<string, object>
        {
            [UsageFields.UsedPercent] = usage.GetUsagePercentage(),
            [UsageFields.IsSuccess] = usage.IsSuccess,
            [UsageFields.LastUpdated] = usage.LastUpdated
        };

        // 旧字段（向后兼容）
#pragma warning disable CS0618
        if (usage.UsedAmount != 0) fields[UsageFields.UsedAmount] = usage.UsedAmount;
        if (usage.TotalAmount != 0) fields[UsageFields.TotalAmount] = usage.TotalAmount;
        if (usage.UsedTokens != 0) fields[UsageFields.UsedTokens] = usage.UsedTokens;
        if (usage.TotalTokens != -1) fields[UsageFields.TotalTokens] = usage.TotalTokens;
        if (!string.IsNullOrEmpty(usage.Unit)) fields[UsageFields.Unit] = usage.Unit;
        if (!string.IsNullOrEmpty(usage.ErrorMessage)) fields[UsageFields.ErrorMessage] = usage.ErrorMessage;
#pragma warning restore CS0618

        if (usage.ExpireDate.HasValue)
            fields[UsageFields.ExpireDate] = usage.ExpireDate.Value;

        // 新字段（req-086）
        if (usage.Quantity.HasValue)
        {
            fields[UsageFields.UsedAmount] = usage.Quantity.Value.Value;
            fields[UsageFields.Unit] = usage.Quantity.Value.Unit?.ToString() ?? string.Empty;
        }

        if (usage.Error != null)
        {
            fields[UsageFields.IsSuccess] = false;
            fields[UsageFields.ErrorMessage] = usage.Error.Message;
        }

        // Extra 字典中的扩展字段
        if (usage.Extra != null)
        {
            foreach (var (key, value) in usage.Extra)
            {
                // 将 extras 字段映射为标准字段名（如果已存在映射）
                var standardKey = UsageFields.MapToStandardFieldName(key);
                fields[standardKey] = value;
            }
        }

        return fields;
    }

    /// <summary>
    /// 比较两个值是否相等（支持数值类型精度容差）
    /// </summary>
    private bool ValuesEqual(object? oldVal, object? newVal)
    {
        if (oldVal == null && newVal == null) return true;
        if (oldVal == null || newVal == null) return false;

        // 数值类型支持精度容差
        if (IsNumericType(oldVal) && IsNumericType(newVal))
        {
            var oldNum = Convert.ToDouble(oldVal);
            var newNum = Convert.ToDouble(newVal);
            return Math.Abs(oldNum - newNum) < NumericTolerance;
        }

        // DateTime 类型比较（忽略毫秒）
        if (oldVal is DateTime oldDt && newVal is DateTime newDt)
        {
            return Math.Abs((oldDt - newDt).TotalSeconds) < 1;
        }

        // 字符串类型比较（忽略大小写）
        if (oldVal is string oldStr && newVal is string newStr)
        {
            return string.Equals(oldStr, newStr, StringComparison.OrdinalIgnoreCase);
        }

        // 复杂类型（字典、列表等）使用 JSON 序列化比较
        if (IsComplexType(oldVal) || IsComplexType(newVal))
        {
            try
            {
                var oldJson = JsonSerializer.Serialize(oldVal);
                var newJson = JsonSerializer.Serialize(newVal);
                return oldJson == newJson;
            }
            catch
            {
                return Equals(oldVal, newVal);
            }
        }

        return Equals(oldVal, newVal);
    }

    /// <summary>
    /// 判断是否为数值类型
    /// </summary>
    private static bool IsNumericType(object value)
    {
        return value is int or long or float or double or decimal or short or byte;
    }

    /// <summary>
    /// 判断是否为复杂类型（字典、列表、数组等）
    /// </summary>
    private static bool IsComplexType(object value)
    {
        return value is System.Collections.IDictionary or System.Collections.IList or Array;
    }
}
