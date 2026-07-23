using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.Declarative;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-107 B3 / #4：声明式抓取提取器（Core 内置可执行 regex + jsonpath）单元测试。
/// <para>验证 JSONPath 提取器能从 XHR JSON 响应体按声明抽取标量值 → 转换器 → SDK 标准字段，
/// 以及提取引擎（ExtractorRegistry）按工具分发的行为。css / xpath / table 依赖浏览器 DOM，不在此覆盖。</para>
/// </summary>
public class DeclarativeExtractorTests
{
    private const string SampleJson = """
    {
      "data": { "weekly": { "usedPercent": 53 }, "credits": "1,234" },
      "items": [ { "value": 42 }, { "value": 99 } ],
      "name": "Pro"
    }
    """;

    private static ExtractionContext Ctx(string content) => new() { Content = content };

    [Fact]
    public void JsonPath_MemberPath_ParsesPercent()
    {
        var extractor = new JsonPathFieldExtractor();
        var directives = new[]
        {
            new ExtractDirective { Tool = "jsonpath", Source = "$.data.weekly.usedPercent", Transform = "parsePercent", TargetField = UsageFields.FiveHourUsedPercent }
        };

        var result = extractor.Extract(directives, Ctx(SampleJson));

        result.Should().ContainKey(UsageFields.FiveHourUsedPercent);
        result[UsageFields.FiveHourUsedPercent].Should().Be(53L);
    }

    [Fact]
    public void JsonPath_ArrayIndex_ParsesNumber()
    {
        var extractor = new JsonPathFieldExtractor();
        var directives = new[]
        {
            new ExtractDirective { Tool = "jsonpath", Source = "$.items[0].value", Transform = "parseNumber", TargetField = UsageFields.WeeklyUsedPercent }
        };

        var result = extractor.Extract(directives, Ctx(SampleJson));

        result[UsageFields.WeeklyUsedPercent].Should().Be(42L);
    }

    [Fact]
    public void JsonPath_StringWithThousandsSeparator_ParsesNumber()
    {
        var extractor = new JsonPathFieldExtractor();
        var directives = new[]
        {
            new ExtractDirective { Tool = "jsonpath", Source = "$.data.credits", Transform = "parseNumber", TargetField = UsageFields.RemainingCredits }
        };

        var result = extractor.Extract(directives, Ctx(SampleJson));

        result[UsageFields.RemainingCredits].Should().Be(1234L);
    }

    [Fact]
    public void JsonPath_MultipleDirectives_AreMerged()
    {
        var extractor = new JsonPathFieldExtractor();
        var directives = new[]
        {
            new ExtractDirective { Tool = "jsonpath", Source = "$.data.weekly.usedPercent", Transform = "parsePercent", TargetField = UsageFields.FiveHourUsedPercent },
            new ExtractDirective { Tool = "jsonpath", Source = "$.items[1].value", Transform = "parseNumber", TargetField = UsageFields.WeeklyUsedPercent }
        };

        var result = extractor.Extract(directives, Ctx(SampleJson));

        result.Should().HaveCount(2);
        result[UsageFields.FiveHourUsedPercent].Should().Be(53L);
        result[UsageFields.WeeklyUsedPercent].Should().Be(99L);
    }

    [Fact]
    public void JsonPath_MissingPath_IsSkipped()
    {
        var extractor = new JsonPathFieldExtractor();
        var directives = new[]
        {
            new ExtractDirective { Tool = "jsonpath", Source = "$.does.not.exist", Transform = "parseNumber", TargetField = UsageFields.RemainingCredits }
        };

        var result = extractor.Extract(directives, Ctx(SampleJson));

        result.Should().BeEmpty();
    }

    [Fact]
    public void JsonPath_NonJsonContent_ReturnsEmpty_Tolerant()
    {
        var extractor = new JsonPathFieldExtractor();
        var directives = new[]
        {
            new ExtractDirective { Tool = "jsonpath", Source = "$.x", Transform = "parseNumber", TargetField = UsageFields.RemainingCredits }
        };

        var result = extractor.Extract(directives, Ctx("<html>not json</html>"));

        result.Should().BeEmpty();
    }

    [Fact]
    public void Registry_JsonPath_IsKnownAndExecutable()
    {
        ExtractorRegistry.IsKnownTool("jsonpath").Should().BeTrue();
        ExtractorRegistry.CanExecute("jsonpath").Should().BeTrue();
    }

    [Fact]
    public void Registry_Run_DispatchesJsonPathDirectives()
    {
        var manifest = new ExtractManifest
        {
            Extract = new[]
            {
                new ExtractDirective { Tool = "jsonpath", Source = "$.data.weekly.usedPercent", Transform = "parsePercent", TargetField = UsageFields.FiveHourUsedPercent },
                // css 指令在 Core 内不可执行，应被跳过而不报错
                new ExtractDirective { Tool = "css", Source = ".ignored", Transform = "parseNumber", TargetField = UsageFields.WeeklyUsedPercent }
            }
        };

        var result = ExtractorRegistry.Run(manifest, Ctx(SampleJson));

        result.Should().ContainKey(UsageFields.FiveHourUsedPercent);
        result[UsageFields.FiveHourUsedPercent].Should().Be(53L);
        result.Should().NotContainKey(UsageFields.WeeklyUsedPercent); // css 被跳过
    }
}
