using System;
using System.Globalization;

namespace UsageMonitor.Core.Models;

/// <summary>
/// SDK 字段格式化器（供应商中立）——按 <see cref="UsageFieldMetadata.DataType"/> 提供统一的数值标签与格式化能力。
/// <para>
/// 设计动机：装饰型控件（迷你文本、tooltip 行、卡片管理 chip 等）需要按字段语义渲染，
/// 但 <see cref="UsageFieldMetadata"/> 已被 Core 注册为唯一真源。插件零翻译原则下，格式化逻辑不能写进每个 Provider。
/// </para>
/// <para>
/// 本层提供：
/// <list type="bullet">
///   <item><description>中英短标签（覆盖 MiniMax / DeepSeek / Kimi / Qoder 等 Provider 的常用字段，按 UsageFieldMetadata.Description 派生）；</description></item>
///   <item><description>数值格式化（百分比 / 货币 / Token / 次数 / 日期时间）；</description></item>
///   <item><description>LabelKey i18n 解析（<c>I18n.T(meta.LabelKey)</c> + Description/字段名逐级回退）。</description></item>
/// </list>
/// </para>
/// </summary>
public static class UsageFieldFormatter
{
    /// <summary>
    /// 获取字段的中英文短标签——优先使用主程序 <c>I18n.T(LabelKey)</c> 的现成翻译；
    /// 缺键时回退 <see cref="UsageFieldMetadata.Description"/>（SDK 元数据内置中文描述）；
    /// 仍无效则返回字段名本身。
    /// </summary>
    /// <param name="fieldName">SDK 标准字段名（<see cref="UsageFields"/> 常量）。</param>
    /// <returns>本地化标签字符串（永不返回 null；空值会被回退为字段名）。</returns>
    public static string GetLabel(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return string.Empty;
        var meta = UsageFieldMetadataRegistry.Get(fieldName!);
        if (meta != null)
        {
            // 修复17：优先走元数据内置中文描述（项目级描述，避免每次都报 I18n 警告）。
            // 仅当主程序主动注册了 LabelKey（如开多语言）时才走 I18n.T。SDK 元数据 Description 本身是中文商译。
            if (!string.IsNullOrWhiteSpace(meta.Description)) return meta.Description;
            var fromI18n = TryI18n(meta.LabelKey);
            if (!string.IsNullOrEmpty(fromI18n)) return fromI18n;
        }
        // 兜底：snake_case / camelCase 转人类可读（如 "monthly_cost" → "Monthly Cost"）。
        return HumanizeFieldName(fieldName!);
    }

    /// <summary>
    /// 获取字段的紧凑短标签（2-3 字，用于迷你文本等空间受限场景）。
    /// <para>对部分常用字段返回专属短词（如 "5h" / "周"），未注册的字段返回 <see cref="GetLabel"/> 的字符串。</para>
    /// </summary>
    public static string GetShortLabel(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return string.Empty;
        var fn = fieldName!;
        // 常用字段的紧凑别名（提升任务栏迷你图可读性，避免出现 "本月消费" 这样的长标签挤占布局）。
        return fn switch
        {
            UsageFields.FiveHourUsedPercent => "5h",
            UsageFields.WeeklyUsedPercent => "周",
            UsageFields.SevenDayUsedPercent => "7日",
            UsageFields.VideoQuota => "视频",
            UsageFields.VideoUsedCount => "视频",
            UsageFields.RemainingCredits => "积分",
            UsageFields.UsedTokens => "Token",
            UsageFields.BalanceAmount => "余额",
            UsageFields.MonthlyCost => "月费",
            UsageFields.TotalCost => "累计",
            UsageFields.MonthlyTokenUsage => "本月Token",
            UsageFields.CacheHitPercent => "缓存",
            UsageFields.TotalUsedPercent => "总用",
            UsageFields.UsedPercent => "用量",
            UsageFields.RequestCount => "请求",
            UsageFields.MostActiveToken => "峰值",
            UsageFields.ActiveDays => "活跃",
            UsageFields.TotalDays => "总日",
            UsageFields.FiveHourResetAt => "5h 重置",
            UsageFields.WeeklyResetAt => "周重置",
            UsageFields.SubscriptionTier => "档位",
            UsageFields.SubscriptionType => "订阅",
            _ => CompactLabel(GetLabel(fn))
        };
    }

    /// <summary>
    /// 从 extras 字典中取 double 值（供 ProviderUsageViewModel tooltip / mini chart 等需要数值兑底处使用）。
    /// <para>未取到或不可转换均返回 null；不在 DecorateTechFace。</para>
    /// </summary>
    public static double? TryGetDouble(string? fieldName, System.Collections.Generic.IReadOnlyDictionary<string, object>? extras)
    {
        if (string.IsNullOrWhiteSpace(fieldName) || extras == null) return null;
        if (!extras.TryGetValue(fieldName!, out var v) || v == null) return null;
        try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    /// <summary>
    /// 按 <see cref="UsageFieldMetadata.DataType"/> 格式化数值为显示字符串。
    /// <para>未注册字段以 <c>0.##</c> 默认格式返回。</para>
    /// </summary>
    /// <param name="fieldName">SDK 标准字段名。</param>
    /// <param name="value">原始数值。</param>
    /// <returns>本地化后的显示字符串（如 "42%" / "¥12.67" / "298.92M"）。</returns>
    public static string FormatValue(string? fieldName, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "--";
        var meta = UsageFieldMetadataRegistry.Get(fieldName ?? string.Empty);
        var dataType = meta?.DataType ?? UsageFieldDataType.Number;
        return dataType switch
        {
            UsageFieldDataType.Percent => $"{value.ToString("0.##", CultureInfo.CurrentCulture)}%",
            // 货币：固定 ¥ 前缀（短期方案；未来若引入多币种需读取 extras["currency"] 字段）。
            UsageFieldDataType.Currency => $"¥{value.ToString("0.00", CultureInfo.CurrentCulture)}",
            // Token 数：调用 Core 内置格式化（与 App 层 MiniLineChartControl.FormatTokenValue 行为一致）。
            UsageFieldDataType.Token => FormatTokenValue(value),
            // 积分：默认沿用千分位 + 后缀；与 Number 共用紧凑格式。
            UsageFieldDataType.Credit => value.ToString("0.##", CultureInfo.CurrentCulture),
            UsageFieldDataType.Count => value.ToString("0", CultureInfo.CurrentCulture),
            UsageFieldDataType.Bool => value > 0 ? "✓" : "—",
            UsageFieldDataType.DateTime => FormatDateTimeValue(value),
            _ => value.ToString("0.##", CultureInfo.CurrentCulture)
        };
    }

    /// <summary>
    /// 安全调用 <see cref="UsageMonitor.Core.Services.I18n.T"/>。
    /// <para>Core 层依赖 I18n（T 是静态公共 API）；失败/缺键时返回 null（让调用方决定回退到 Description）。</para>
    /// </summary>
    private static string? TryI18n(string labelKey)
    {
        if (string.IsNullOrWhiteSpace(labelKey)) return null;
        try
        {
            var v = UsageMonitor.Core.Services.I18n.T(labelKey);
            // I18n 缺键时回退输入 key 本身（与原 key 相同），视为未翻译。
            return string.Equals(v, labelKey, StringComparison.Ordinal) ? null : v;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把 snake_case / camelCase 字段名转人类可读（用作 tooltip 字段缺翻译兜底）：
    /// <c>monthly_cost</c> → <c>Monthly Cost</c>。
    /// </summary>
    private static string HumanizeFieldName(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return string.Empty;
        // 已有空格/中文：原样返回。
        if (fieldName.Contains(' ') || fieldName.Any(c => c > 0x7F)) return fieldName;
        var parts = fieldName.Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
        }
        return string.Join(' ', parts);
    }

    /// <summary>
    /// 把长标签压缩到 ≤ 6 字（迷你文本布局需要）。
    /// <para>中文按字符截断；英文按空格取首词；超长仍保留前 6 字符 + …</para>
    /// </summary>
    private static string CompactLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;
        var trimmed = label.Trim();
        // 中文：按字符截断
        if (trimmed.Any(c => c > 0x7F))
        {
            return trimmed.Length <= 6 ? trimmed : trimmed.Substring(0, 6) + "…";
        }
        // 英文：取第一个非停用词
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : trimmed;
    }

    /// <summary>
    /// Core 层轻量实现：把 token 数值格式化为人类可读形式（如 250.71M、4.83B）。
    /// <para>与 App 层 <c>MiniLineChartControl.FormatTokenValue</c> 行为一致；Core 不可引用 App，
    /// 故独立实现（公式相同：K / M / B 三档，仅正整数走位运）。</para>
    /// </summary>
    private static string FormatTokenValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "--";
        if (value >= 1_000_000_000)
            return $"{value / 1_000_000_000:0.00}B";
        if (value >= 1_000_000)
            return $"{value / 1_000_000:0.00}M";
        if (value >= 1_000)
            return $"{value / 1_000:0.00}K";
        return $"{value:0.##}";
    }

    /// <summary>Core 层轻量实现：把 Unix 秒时间戳格式化为本地短时间字符串（如 12-31 14:00）。</summary>
    private static string FormatDateTimeValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return "--";
        try
        {
            // value 可能是 Unix 秒或 Unix 毫秒（按数据源约定），> 1e12 一律视为毫秒
            var epoch = value > 1_000_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)value)
                : DateTimeOffset.FromUnixTimeSeconds((long)value);
            return epoch.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture);
        }
        catch
        {
            return "--";
        }
    }
}
