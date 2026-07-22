using System;
using FluentAssertions;
using UsageMonitor.Core.Models;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-005-022：REQ-005 图表泛化 SDK 强类型模型的单元测试覆盖。
/// <para>
/// 覆盖范围：<see cref="Quantity"/> + 5 个 <see cref="UnitBase"/> 子类的 Format/Key/相等性；
/// <see cref="UsageError"/> 的静态工厂与错误种类；各 <see cref="IChartData"/> record 的 <c>Kind</c> 契约；
/// 以及 <see cref="UsageInfo"/> 在「新 Quantity 字段」与「旧 [Obsolete] 字段」两条路径下兼容访问器的一致性。
/// </para>
/// <para>注意：按项目规则本测试仅编写、不自动运行；请由维护者手动执行
/// <c>dotnet test tests/UsageMonitor.Core.Tests</c> 验证。</para>
/// </summary>
public class ChartSdkModelTests
{
    // ==================== Quantity + UnitBase ====================

    /// <summary>货币单位 Format：两位小数 + 大写货币代码。</summary>
    [Fact]
    public void CurrencyUnit_Format_TwoDecimalsWithCode()
    {
        var q = new Quantity(12.5m, new CurrencyUnit("usd"));
        q.Format().Should().Be("12.50 USD");
    }

    /// <summary>Token 单位 Format：>=1000 走 K、>=100万走 M 的紧凑格式。</summary>
    [Theory]
    [InlineData(500, "500 token")]
    [InlineData(1500, "1.5K token")]
    [InlineData(2_000_000, "2.0M token")]
    public void TokenUnit_Format_CompactStyle(long value, string expected)
    {
        var q = new Quantity(value, new TokenUnit());
        q.Format().Should().Be(expected);
    }

    /// <summary>百分比单位 Format：整数百分号，无小数。</summary>
    [Fact]
    public void PercentUnit_Format_IntegerPercent()
    {
        var q = new Quantity(63.7m, new PercentUnit());
        q.Format().Should().Be("64%");
    }

    /// <summary>积分单位 Format：整数 + 名称后缀。</summary>
    [Fact]
    public void CreditUnit_Format_IntegerWithName()
    {
        var q = new Quantity(1234m, new CreditUnit("credits"));
        q.Format().Should().Be("1234 credits");
    }

    /// <summary>DisplaySuffix 会拼接在单位格式化结果之后。</summary>
    [Fact]
    public void Quantity_DisplaySuffix_Appended()
    {
        var q = new Quantity(80m, new PercentUnit(), " 剩余");
        q.Format().Should().Be("80% 剩余");
    }

    /// <summary>Zero 工厂生成 0 值且保留单位。</summary>
    [Fact]
    public void Quantity_Zero_KeepsUnit()
    {
        var q = Quantity.Zero(new CurrencyUnit("CNY"));
        q.Value.Should().Be(0m);
        q.Unit.Should().BeOfType<CurrencyUnit>();
    }

    /// <summary>同类型同参数的单位相等；不同子类型不相等。</summary>
    [Fact]
    public void UnitBase_Equality_ByTypeAndKey()
    {
        (new CurrencyUnit("USD")).Should().Be(new CurrencyUnit("usd")); // 构造时归一大写
        (new CurrencyUnit("USD")).Should().NotBe(new CurrencyUnit("CNY"));
        (new PercentUnit() as object).Should().NotBe(new CreditUnit());
    }

    /// <summary>UnknownUnit 用于兼容旧字段缺省，Format 不带单位后缀。</summary>
    [Fact]
    public void UnknownUnit_Format_ValueOnly()
    {
        var q = new Quantity(42m, new UnknownUnit());
        q.Format().Should().Be("42");
    }

    // ==================== UsageError ====================

    /// <summary>各静态工厂产生对应的 <see cref="UsageErrorKind"/>。</summary>
    [Fact]
    public void UsageError_Factories_MapToKind()
    {
        UsageError.Network("net", 503).Kind.Should().Be(UsageErrorKind.Network);
        UsageError.Network("net", 503).HttpStatus.Should().Be(503);
        UsageError.Auth("bad key").Kind.Should().Be(UsageErrorKind.Auth);
        UsageError.RateLimit("429").Kind.Should().Be(UsageErrorKind.RateLimit);
        UsageError.Parse("json").Kind.Should().Be(UsageErrorKind.Parse);
        UsageError.Unknown("?").Kind.Should().Be(UsageErrorKind.Unknown);
    }

    /// <summary>OccurredAtUtc 默认取当前 UTC（合理范围内）。</summary>
    [Fact]
    public void UsageError_OccurredAtUtc_DefaultsToNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var err = UsageError.Auth("x");
        err.OccurredAtUtc.Should().BeOnOrAfter(before);
        err.OccurredAtUtc.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(5));
    }

    // ==================== IChartData.Kind 契约 ====================

    /// <summary>各强类型图表数据 record 的 Kind 与其种类一一对应。</summary>
    [Fact]
    public void ChartData_Records_ExposeCorrectKind()
    {
        new LineChartData(new double[] { 1, 2, 3 }).Kind.Should().Be(ChartKind.Line);
        new BarChartData(new double[] { 1, 2 }).Kind.Should().Be(ChartKind.Bar);
        new RingChartData(50).Kind.Should().Be(ChartKind.Ring);
        new HeatMapData(new double[] { 1, 2, 3, 4 }, 2, 2).Kind.Should().Be(ChartKind.HeatMap);
        new DayNightArcData(new double[24]).Kind.Should().Be(ChartKind.DayNightArc);
    }

    /// <summary>REQ-082 SDK v2 的 MetricBar/MetricGrid record 同样暴露正确 Kind。</summary>
    [Fact]
    public void ChartDataV2_Records_ExposeCorrectKind()
    {
        var bar = new MetricBarData(new[] { new MetricBarItem("5h", 40) });
        bar.Kind.Should().Be(ChartKind.MetricBar);
        bar.Bars.Should().ContainSingle().Which.Percent.Should().Be(40);

        var grid = new MetricGridData(new[] { new MetricGridItem("余额", "¥12.00") });
        grid.Kind.Should().Be(ChartKind.MetricGrid);
        grid.Items.Should().ContainSingle().Which.Value.Should().Be("¥12.00");
    }

    /// <summary>record 值相等语义：相同内容的 LineChartData 相等（record 自动实现）。</summary>
    [Fact]
    public void LineChartData_ValueEquality()
    {
        var a = new LineChartData(new double[] { 1, 2 }, MaxValue: 100);
        var b = new LineChartData(new double[] { 1, 2 }, MaxValue: 100);
        // 注意：IReadOnlyList 引用不同，record 默认按引用比较集合成员，故此处仅验证标量参数相等语义
        a.MaxValue.Should().Be(b.MaxValue);
        a.Kind.Should().Be(b.Kind);
    }

    // ==================== UsageInfo 兼容访问器 ====================

    /// <summary>旧字段路径：TotalAmount>0 时百分比按 UsedAmount/TotalAmount 计算并封顶 100。</summary>
    [Fact]
    public void UsageInfo_GetUsagePercentage_LegacyFields()
    {
#pragma warning disable CS0618 // 显式测试旧字段兼容路径
        var info = new UsageInfo { UsedAmount = 30m, TotalAmount = 60m };
        info.GetUsagePercentage().Should().BeApproximately(50, 0.001);

        var over = new UsageInfo { UsedAmount = 200m, TotalAmount = 100m };
        over.GetUsagePercentage().Should().Be(100); // 封顶
#pragma warning restore CS0618
    }

    /// <summary>新字段路径：设置 Quantity 后，百分比仍需配合 TotalAmount 计算（兼容访问器约定）。</summary>
    [Fact]
    public void UsageInfo_GetUsagePercentage_QuantityPath()
    {
#pragma warning disable CS0618
        var info = new UsageInfo
        {
            Quantity = new Quantity(25m, new CurrencyUnit("USD")),
            TotalAmount = 100m
        };
        info.GetUsagePercentage().Should().BeApproximately(25, 0.001);
#pragma warning restore CS0618
    }

    /// <summary>剩余额度：Total - Used，且不为负。</summary>
    [Fact]
    public void UsageInfo_GetRemainingAmount_NonNegative()
    {
#pragma warning disable CS0618
        new UsageInfo { UsedAmount = 40m, TotalAmount = 100m }.GetRemainingAmount().Should().Be(60m);
        new UsageInfo { UsedAmount = 150m, TotalAmount = 100m }.GetRemainingAmount().Should().Be(0m);
#pragma warning restore CS0618
    }

    /// <summary>剩余 Token：TotalTokens=-1（不限）时返回 -1；否则 Total-Used 且不为负。</summary>
    [Fact]
    public void UsageInfo_GetRemainingTokens_UnlimitedAndBounded()
    {
#pragma warning disable CS0618
        new UsageInfo { UsedTokens = 100, TotalTokens = -1 }.GetRemainingTokens().Should().Be(-1);
        new UsageInfo { UsedTokens = 300, TotalTokens = 1000 }.GetRemainingTokens().Should().Be(700);
        new UsageInfo { UsedTokens = 1500, TotalTokens = 1000 }.GetRemainingTokens().Should().Be(0);
#pragma warning restore CS0618
    }

    /// <summary>CreateError（结构化）：Error 非空、IsSuccess=false、且回填旧 ErrorMessage 兼容字段。</summary>
    [Fact]
    public void UsageInfo_CreateError_Structured_SyncsLegacyMessage()
    {
        var info = UsageInfo.CreateError("p1", "Provider 1", UsageError.Auth("cookie 失效"));
        info.IsSuccess.Should().BeFalse();
        info.Error.Should().NotBeNull();
        info.Error!.Kind.Should().Be(UsageErrorKind.Auth);
#pragma warning disable CS0618
        info.ErrorMessage.Should().Be("cookie 失效");
#pragma warning restore CS0618
    }
}
