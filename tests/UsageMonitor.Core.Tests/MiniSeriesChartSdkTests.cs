using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.MiniChart;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// 任务栏迷你时序图表（柱状/折线/面积）SDK 契约的单元测试覆盖。
/// <para>
/// 覆盖范围：<see cref="MiniSeriesData"/> 序列模型（截断/空值/数值类型）；
/// <see cref="DataGroup.SeriesKey"/> 与 <see cref="MiniChartDeclaration.Width"/> 的 JSON 反序列化；
/// <see cref="DeclarativeChartKind"/> 新增迷你枚举值；<see cref="TaskbarMiniChartConfig.Width"/> 用户覆盖宽度。
/// </para>
/// </summary>
public class MiniSeriesChartSdkTests
{
    /// <summary>构建与 PluginManifest.Load 一致的反序列化选项（驼峰不敏感 + 字符串枚举）。</summary>
    private static JsonSerializerOptions ManifestOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    // ==================== MiniSeriesData 序列模型 ====================

    /// <summary>序列点少于上限时原样保留（不截断）。</summary>
    [Fact]
    public void Create_UnderLimit_KeepsAllPoints()
    {
        var points = Enumerable.Range(0, 10)
            .Select(i => new MiniSeriesPoint { Timestamp = DateTime.Today.AddDays(i), Value = i * 1.5 })
            .ToList();

        var series = MiniSeriesData.Create(points, MiniSeriesValueKind.Number, "credits");

        series.Points.Should().HaveCount(10);
        series.ValueKind.Should().Be(MiniSeriesValueKind.Number);
        series.Unit.Should().Be("credits");
    }

    /// <summary>序列点超出 90 上限时截断，保留最新的点（列表尾部）。</summary>
    [Fact]
    public void Create_OverLimit_TruncatesKeepingNewest()
    {
        var points = Enumerable.Range(0, 120)
            .Select(i => new MiniSeriesPoint { Timestamp = DateTime.Today.AddDays(i), Value = i })
            .ToList();

        var series = MiniSeriesData.Create(points);

        series.Points.Should().HaveCount(MiniSeriesData.MaxPointCount);
        // 保留尾部（最新）：最后一个点值应为 119，第一个点值应为 120-90=30
        series.Points[^1].Value.Should().Be(119);
        series.Points[0].Value.Should().Be(30);
    }

    /// <summary>空点列表创建空序列（不抛异常）。</summary>
    [Fact]
    public void Create_EmptyPoints_ReturnsEmptySeries()
    {
        var series = MiniSeriesData.Create(Array.Empty<MiniSeriesPoint>(), MiniSeriesValueKind.Percent, "%", 100);

        series.Points.Should().BeEmpty();
        series.MaxValue.Should().Be(100);
    }

    /// <summary>null 点列表创建空序列（不抛异常）。</summary>
    [Fact]
    public void Create_NullPoints_ReturnsEmptySeries()
    {
        var series = MiniSeriesData.Create(null!);
        series.Points.Should().BeEmpty();
    }

    /// <summary>ValueKind 默认值为 Percent。</summary>
    [Fact]
    public void MiniSeriesData_DefaultValueKind_IsPercent()
    {
        var series = new MiniSeriesData();
        series.ValueKind.Should().Be(MiniSeriesValueKind.Percent);
    }

    // ==================== DataGroup.SeriesKey / MiniChartDeclaration.Width 反序列化 ====================

    /// <summary>DataGroup 的 seriesKey 字段（camelCase）能正确反序列化到 SeriesKey 属性。</summary>
    [Fact]
    public void DataGroup_SeriesKey_DeserializesFromCamelCase()
    {
        const string json = """
            { "id": "mm.mini.bar.tokens", "display": "每日 Token", "seriesKey": "mm.daily.tokens",
              "fields": [{ "fieldName": "daily_token_value", "role": "Value" }] }
            """;

        var group = JsonSerializer.Deserialize<DataGroup>(json, ManifestOptions());

        group!.SeriesKey.Should().Be("mm.daily.tokens");
        group.Id.Should().Be("mm.mini.bar.tokens");
    }

    /// <summary>未声明 seriesKey 时 SeriesKey 为 null（单值图表兼容）。</summary>
    [Fact]
    public void DataGroup_WithoutSeriesKey_IsNull()
    {
        const string json = """
            { "id": "mm.taskbar.5h", "fields": [{ "fieldName": "five_hour_used_percent", "role": "Value" }] }
            """;

        var group = JsonSerializer.Deserialize<DataGroup>(json, ManifestOptions());

        group!.SeriesKey.Should().BeNull();
    }

    /// <summary>MiniChartDeclaration 的 width 字段能正确反序列化到 Width 属性。</summary>
    [Fact]
    public void MiniChartDeclaration_Width_DeserializesFromCamelCase()
    {
        const string json = """
            { "chartId": "mm.mini.bar", "kind": "MiniBarChart", "width": 100,
              "dataGroups": [{ "id": "g1", "seriesKey": "k1", "fields": [{ "fieldName": "daily_token_value", "role": "Value" }] }] }
            """;

        var decl = JsonSerializer.Deserialize<MiniChartDeclaration>(json, ManifestOptions());

        decl!.Width.Should().Be(100);
        decl.Kind.Should().Be(DeclarativeChartKind.MiniBarChart);
        decl.DataGroups[0].SeriesKey.Should().Be("k1");
    }

    /// <summary>未声明 width 时 Width 为 null（宿主回退默认 120）。</summary>
    [Fact]
    public void MiniChartDeclaration_WithoutWidth_IsNull()
    {
        const string json = """{ "chartId": "mm.mini.ring", "kind": "MiniRingChart" }""";

        var decl = JsonSerializer.Deserialize<MiniChartDeclaration>(json, ManifestOptions());

        decl!.Width.Should().BeNull();
    }

    // ==================== 枚举新值 ====================

    /// <summary>DeclarativeChartKind 新增三个迷你时序枚举值（字符串反序列化）。</summary>
    [Theory]
    [InlineData("MiniLineChart", DeclarativeChartKind.MiniLineChart)]
    [InlineData("MiniBarChart", DeclarativeChartKind.MiniBarChart)]
    [InlineData("MiniAreaChart", DeclarativeChartKind.MiniAreaChart)]
    public void DeclarativeChartKind_NewMiniKinds_Deserialize(string json, DeclarativeChartKind expected)
    {
        var kind = JsonSerializer.Deserialize<DeclarativeChartKind>($"\"{json}\"", ManifestOptions());
        kind.Should().Be(expected);
    }

    /// <summary>MiniChartKind 新增 MiniAreaChart 枚举值（追加序号 5，不影响既有值）。</summary>
    [Fact]
    public void MiniChartKind_MiniAreaChart_HasStableValue()
    {
        ((int)MiniChartKind.MiniAreaChart).Should().Be(5);
        ((int)MiniChartKind.MiniText).Should().Be(0);
        ((int)MiniChartKind.MiniRingChart).Should().Be(1);
        ((int)MiniChartKind.MiniLineChart).Should().Be(2);
        ((int)MiniChartKind.MiniBarChart).Should().Be(3);
    }

    // ==================== TaskbarMiniChartConfig.Width 用户覆盖 ====================

    /// <summary>TaskbarMiniChartConfig.Width 默认 null（不覆盖，用声明值/默认）。</summary>
    [Fact]
    public void TaskbarMiniChartConfig_Width_DefaultNull()
    {
        var cfg = new TaskbarMiniChartConfig();
        cfg.Width.Should().BeNull();
    }

    /// <summary>TaskbarMiniChartConfig.Width 可设置并读回（用户覆盖宽度）。</summary>
    [Fact]
    public void TaskbarMiniChartConfig_Width_SetAndGet()
    {
        var cfg = new TaskbarMiniChartConfig { Width = 160 };
        cfg.Width.Should().Be(160);
    }

    // ==================== MiniChartDescriptor.DeclaredWidth ====================

    /// <summary>MiniChartDescriptor.DeclaredWidth 默认 null；可透传声明宽度。</summary>
    [Fact]
    public void MiniChartDescriptor_DeclaredWidth_Roundtrip()
    {
        var descriptor = new MiniChartDescriptor
        {
            ProviderId = "minimax",
            Kind = MiniChartKind.MiniBarChart,
            DeclaredWidth = 100
        };

        descriptor.DeclaredWidth.Should().Be(100);

        var noWidth = MiniChartDescriptor.ForRingChart("minimax");
        noWidth.DeclaredWidth.Should().BeNull();
    }
}
