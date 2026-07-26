using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsageMonitor.Core.Models;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// <see cref="UsageFieldFormatter"/> 单元测试——验证 SDK 字段标签与格式化能力。
/// <para>覆盖：① GetLabel/ShortLabel 按 SDK 元数据派生；② FormatValue 按 DataType 选格式；
/// ③ 未知字段兑底为人类可读字段名。</para>
/// </summary>
public class UsageFieldFormatterTests
{
    /// <summary>Percent 字段标签应取元数据 Description（“5 小时窗口已用百分比”），而非字段名。</summary>
    [Fact]
    public void GetLabel_RegisteredPercentField_UsesMetadataDescription()
    {
        var label = UsageFieldFormatter.GetLabel(UsageFields.FiveHourUsedPercent);
        label.Should().Be("5 小时窗口已用百分比");
    }

    /// <summary>Currency 字段（balance_amount/monthly_cost）应输出对应中文描述。</summary>
    [Fact]
    public void GetLabel_RegisteredCurrencyField_UsesMetadataDescription()
    {
        UsageFieldFormatter.GetLabel(UsageFields.BalanceAmount).Should().Be("充值余额");
        UsageFieldFormatter.GetLabel(UsageFields.MonthlyCost).Should().Be("月消费");
        UsageFieldFormatter.GetLabel(UsageFields.TotalCost).Should().Be("累计消费");
    }

    /// <summary>Token 字段应输出“月 Token 用量（计费制月度累计）”。</summary>
    [Fact]
    public void GetLabel_RegisteredTokenField_UsesMetadataDescription()
    {
        UsageFieldFormatter.GetLabel(UsageFields.MonthlyTokenUsage)
            .Should().Be("月 Token 用量（计费制月度累计）");
    }

    /// <summary>GetShortLabel 为 MiniMax/DeepSeek 常用字段提供中文紧凑别名。</summary>
    [Fact]
    public void GetShortLabel_BalancesAndCurrency_ReturnShortAliases()
    {
        UsageFieldFormatter.GetShortLabel(UsageFields.BalanceAmount).Should().Be("余额");
        UsageFieldFormatter.GetShortLabel(UsageFields.MonthlyCost).Should().Be("月费");
        UsageFieldFormatter.GetShortLabel(UsageFields.TotalCost).Should().Be("累计");
        UsageFieldFormatter.GetShortLabel(UsageFields.MonthlyTokenUsage).Should().Be("本月Token");
        UsageFieldFormatter.GetShortLabel(UsageFields.FiveHourUsedPercent).Should().Be("5h");
        UsageFieldFormatter.GetShortLabel(UsageFields.WeeklyUsedPercent).Should().Be("周");
    }

    /// <summary>未注册字段兑底为 snake_case / camelCase 转人类可读。</summary>
    [Fact]
    public void GetLabel_UnknownField_HumanizesFieldName()
    {
        UsageFieldFormatter.GetLabel("totally_unknown_metric")
            .Should().Be("Totally Unknown Metric");
    }

    /// <summary>FormatValue：Percent 字段返化为 “42%”。</summary>
    [Fact]
    public void FormatValue_PercentField_FormatsAsPercentString()
    {
        UsageFieldFormatter.FormatValue(UsageFields.FiveHourUsedPercent, 42.0)
            .Should().Be("42%");
    }

    /// <summary>FormatValue：Currency 字段返化为 “¥32.67”。</summary>
    [Fact]
    public void FormatValue_CurrencyField_FormatsAsYuanString()
    {
        UsageFieldFormatter.FormatValue(UsageFields.BalanceAmount, 32.67)
            .Should().Be("¥32.67");
    }

    /// <summary>FormatValue：Token 字段返化为紧凑 K/M/B 简写（298916453 → "298.92M"）。</summary>
    [Fact]
    public void FormatValue_TokenField_FormatsAsKMBString()
    {
        UsageFieldFormatter.FormatValue(UsageFields.MonthlyTokenUsage, 298_916_453.0)
            .Should().Be("298.92M");
        UsageFieldFormatter.FormatValue(UsageFields.MonthlyTokenUsage, 5_500_000_000.0)
            .Should().Be("5.50B");
        UsageFieldFormatter.FormatValue(UsageFields.MonthlyTokenUsage, 9_500.0)
            .Should().Be("9.50K");
    }

    /// <summary>FormatValue：Count 字段返化为整数字符串（无小数）。</summary>
    [Fact]
    public void FormatValue_CountField_FormatsAsIntegerString()
    {
        UsageFieldFormatter.FormatValue(UsageFields.RequestCount, 1234.56)
            .Should().Be("1235");
    }

    /// <summary>FormatValue：未知字段/NaN 兑底 --。</summary>
    [Fact]
    public void FormatValue_NaNOrUnknown_ReturnsFallback()
    {
        UsageFieldFormatter.FormatValue("unknown_field", double.NaN).Should().Be("--");
        UsageFieldFormatter.FormatValue("unknown_field", 12.3).Should().Be("12.3");
    }
}
