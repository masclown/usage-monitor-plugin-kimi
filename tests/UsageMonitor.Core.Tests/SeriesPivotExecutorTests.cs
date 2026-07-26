using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.Declarative;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// seriesPivot 取数模式单元测试：验证"系列 × 桶"三层嵌套结构转置为图表可消费的并行结构。
/// <para>覆盖：① 模型枢轴（SeriesNameField 非空，每模型一个系列）；② 字段枢轴（SeriesNameField 为空，
/// 每 ItemField 一个系列，跨模型求和）；③ 时间占位符 {now:unix}/{now:unix-Nd} 展开。</para>
/// </summary>
public class SeriesPivotExecutorTests
{
    // DeepSeek by_api_key/cost 接口结构（裁剪）：data[0].series[].buckets[]，按模型分组。
    private const string CostJson = """
    {
      "start": 1782172800, "end": 1784764800, "bucket": 86400,
      "models": ["deepseek-chat & deepseek-reasoner", "deepseek-v4-flash"],
      "data": [{ "currency": "CNY", "series": [
        { "api_key": { "name": "Qoder", "valid": true }, "model": "deepseek-chat & deepseek-reasoner",
          "buckets": [{ "time": 1782172800, "cost": "0.5" }, { "time": 1782259200, "cost": "1.2" }] },
        { "api_key": { "name": "Qoder", "valid": true }, "model": "deepseek-v4-flash",
          "buckets": [{ "time": 1782172800, "cost": "0.3" }, { "time": 1782259200, "cost": "0.7" }] }
      ]}]
    }
    """;

    // DeepSeek by_api_key/amount 接口结构（裁剪）：series[].buckets[].usage，含多种 token 类型。
    private const string AmountJson = """
    {
      "bucket": 86400,
      "models": ["deepseek-v4-flash", "deepseek-v4-pro"],
      "series": [
        { "api_key": { "name": "Qoder" }, "model": "deepseek-v4-flash",
          "buckets": [
            { "time": 1782172800, "usage": { "RESPONSE_TOKEN": 100, "REQUEST": 5, "PROMPT_CACHE_HIT_TOKEN": 200, "PROMPT_CACHE_MISS_TOKEN": 300 } },
            { "time": 1782259200, "usage": { "RESPONSE_TOKEN": 150, "REQUEST": 8, "PROMPT_CACHE_HIT_TOKEN": 250, "PROMPT_CACHE_MISS_TOKEN": 350 } }
          ] },
        { "api_key": { "name": "Qoder" }, "model": "deepseek-v4-pro",
          "buckets": [
            { "time": 1782172800, "usage": { "RESPONSE_TOKEN": 10, "REQUEST": 1, "PROMPT_CACHE_HIT_TOKEN": 20, "PROMPT_CACHE_MISS_TOKEN": 30 } },
            { "time": 1782259200, "usage": { "RESPONSE_TOKEN": 15, "REQUEST": 2, "PROMPT_CACHE_HIT_TOKEN": 25, "PROMPT_CACHE_MISS_TOKEN": 35 } }
          ] }
      ]
    }
    """;

    /// <summary>模型枢轴：cost 接口按模型拆为多系列，类别为日期 MM-dd，值为每日消费。</summary>
    [Fact]
    public void ModelPivot_CostEndpoint_ProducesPerModelSeries()
    {
        // Arrange：与 DeepSeek defaults.json 的 cost 端点声明一致。
        var decl = new FetchDeclaration
        {
            Endpoints = new[]
            {
                new FetchEndpoint
                {
                    UrlMatch = "by_api_key/cost",
                    Arrays = new[]
                    {
                        new FetchArray
                        {
                            ItemsPath = "$.data[0].series",
                            Mode = "seriesPivot",
                            SeriesNameField = "model",
                            NestedItems = "buckets",
                            Target = "daily_cost",
                            ItemFields = new[]
                            {
                                new FetchField { Path = "$.cost", Target = "cost", Transform = "parseNumber", ElementType = "double" }
                            }
                        }
                    }
                }
            }
        };
        var captured = new Dictionary<string, string>
        {
            ["https://platform.deepseek.com/api/v0/usage/by_api_key/cost?start=1&end=2"] = CostJson
        };

        // Act
        var result = DeclarativeCaptureExecutor.Execute(decl, captured);

        // Assert
        var categories = result.Extras["daily_cost_categories"].Should().BeAssignableTo<List<string>>().Subject;
        var names = result.Extras["daily_cost_series_names"].Should().BeAssignableTo<List<string>>().Subject;
        var matrix = result.Extras["daily_cost_matrix"].Should().BeAssignableTo<List<List<double>>>().Subject;

        categories.Should().HaveCount(2);
        categories[0].Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$"); // 问题9：yyyy-MM-dd 完整日期格式
        names.Should().BeEquivalentTo(new[] { "deepseek-chat & deepseek-reasoner", "deepseek-v4-flash" },
            c => c.WithStrictOrdering());
        matrix.Should().HaveCount(2);
        matrix[0].Should().BeEquivalentTo(new[] { 0.5, 1.2 });
        matrix[1].Should().BeEquivalentTo(new[] { 0.3, 0.7 });
    }

    /// <summary>字段枢轴：amount 接口按 token 类型分层，跨模型求和。</summary>
    [Fact]
    public void FieldPivot_AmountEndpoint_SumsAcrossModels()
    {
        // Arrange：与 DeepSeek defaults.json 的 amount 端点 daily_tokens 声明一致。
        var decl = new FetchDeclaration
        {
            Endpoints = new[]
            {
                new FetchEndpoint
                {
                    UrlMatch = "by_api_key/amount",
                    Arrays = new[]
                    {
                        new FetchArray
                        {
                            ItemsPath = "$.series",
                            Mode = "seriesPivot",
                            NestedItems = "buckets",
                            Target = "daily_tokens",
                            ItemFields = new[]
                            {
                                new FetchField { Path = "$.usage.PROMPT_CACHE_MISS_TOKEN", Target = "cache_miss_token", Transform = "parseNumber", ElementType = "double" },
                                new FetchField { Path = "$.usage.PROMPT_CACHE_HIT_TOKEN", Target = "cache_hit_token", Transform = "parseNumber", ElementType = "double" },
                                new FetchField { Path = "$.usage.RESPONSE_TOKEN", Target = "response_token", Transform = "parseNumber", ElementType = "double" }
                            }
                        }
                    }
                }
            }
        };
        var captured = new Dictionary<string, string>
        {
            ["https://platform.deepseek.com/api/v0/usage/by_api_key/amount?start=1&end=2"] = AmountJson
        };

        // Act
        var result = DeclarativeCaptureExecutor.Execute(decl, captured);

        // Assert
        var names = result.Extras["daily_tokens_series_names"].Should().BeAssignableTo<List<string>>().Subject;
        var matrix = result.Extras["daily_tokens_matrix"].Should().BeAssignableTo<List<List<double>>>().Subject;

        names.Should().BeEquivalentTo(new[] { "cache_miss_token", "cache_hit_token", "response_token" },
            c => c.WithStrictOrdering());
        matrix.Should().HaveCount(3);
        // cache_miss: (300+350) 跨模型求和 → 每天 [650]... 实际是每天分别求和：day0=300+30=330, day1=350+35=385
        matrix[0].Should().BeEquivalentTo(new double[] { 330, 385 });
        // cache_hit: day0=200+20=220, day1=250+25=275
        matrix[1].Should().BeEquivalentTo(new double[] { 220, 275 });
        // response: day0=100+10=110, day1=150+15=165
        matrix[2].Should().BeEquivalentTo(new double[] { 110, 165 });
    }

    /// <summary>字段枢轴（单字段）：请求次数聚合全模型。</summary>
    [Fact]
    public void FieldPivot_SingleField_AggregatesRequests()
    {
        // Arrange
        var decl = new FetchDeclaration
        {
            Endpoints = new[]
            {
                new FetchEndpoint
                {
                    UrlMatch = "by_api_key/amount",
                    Arrays = new[]
                    {
                        new FetchArray
                        {
                            ItemsPath = "$.series",
                            Mode = "seriesPivot",
                            NestedItems = "buckets",
                            Target = "daily_requests",
                            ItemFields = new[]
                            {
                                new FetchField { Path = "$.usage.REQUEST", Target = "request_count", Transform = "parseNumber", ElementType = "double" }
                            }
                        }
                    }
                }
            }
        };
        var captured = new Dictionary<string, string>
        {
            ["https://platform.deepseek.com/api/v0/usage/by_api_key/amount?start=1&end=2"] = AmountJson
        };

        // Act
        var result = DeclarativeCaptureExecutor.Execute(decl, captured);

        // Assert：day0 = 5+1 = 6, day1 = 8+2 = 10
        var matrix = result.Extras["daily_requests_matrix"].Should().BeAssignableTo<List<List<double>>>().Subject;
        matrix.Should().HaveCount(1);
        matrix[0].Should().BeEquivalentTo(new double[] { 6, 10 });
    }

    /// <summary>时间占位符展开：{now:unix} 与 {now:unix-30d}。</summary>
    [Fact]
    public void ExpandPlaceholders_TimePlaceholders_ProduceUnixSeconds()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var url = DeclarativeHttpFetcher.ExpandPlaceholders(
            "https://platform.deepseek.com/api/v0/usage/by_api_key/cost?start={now:unix-30d}&end={now:unix}&tz=0",
            _ => null, null);

        // Assert：URL 中不再含占位符，start/end 为合理范围的 Unix 秒。
        url.Should().NotContain("{now:");
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var start = long.Parse(query["start"]!);
        var end = long.Parse(query["end"]!);
        end.Should().BeInRange(now - 5, now + 5);
        (end - start).Should().BeInRange(30 * 86400 - 10, 30 * 86400 + 10);
    }

    /// <summary>日对齐时间占位符：{now:unixDay} 为 UTC 当日零点，{now:unixDay-30d} 为 30 天前零点（DeepSeek 接口要求按日对齐）。</summary>
    [Fact]
    public void ExpandPlaceholders_DayAlignedPlaceholders_ProduceMidnightUnixSeconds()
    {
        // Act
        var url = DeclarativeHttpFetcher.ExpandPlaceholders(
            "https://platform.deepseek.com/api/v0/usage/by_api_key/cost?start={now:unixDay-30d}&end={now:unixDay}&tz=0",
            _ => null, null);

        // Assert
        url.Should().NotContain("{now:");
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var start = long.Parse(query["start"]!);
        var end = long.Parse(query["end"]!);

        // 两个时间戳都必须按日对齐（86400 的整数倍）。
        (end % 86400L).Should().Be(0, "end 应为 UTC 当日零点");
        (start % 86400L).Should().Be(0, "start 应为 UTC 零点");
        // start 恰好为 end 前 30 天。
        (end - start).Should().Be(30 * 86400L);
        // end 应为今天（与当前时间差不超过 24h）。
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        (now - end).Should().BeInRange(0, 86400);
    }

    /// <summary>DeepSeek defaults.json 清单可正确反序列化（含 seriesPivot 数组声明与新图表 Kind）。</summary>
    [Fact]
    public void DeepSeekManifest_Deserializes_WithSeriesPivotAndNewChartKinds()
    {
        // Arrange：读取实际声明包。
        var manifestPath = FindDeepSeekDefaults();
        if (manifestPath == null) return; // CI 无声明包时跳过
        var json = File.ReadAllText(manifestPath);

        // Act
        var manifest = PluginManifest.Load(json);

        // Assert
        manifest.Should().NotBeNull();
        manifest!.ProviderId.Should().Be("DeepSeek");
        manifest.Fetch!.Endpoints.Should().HaveCount(3);

        // 浏览器登录声明：localStorage 令牌提取（DeepSeek 鉴权靠 localStorage.userToken）。
        manifest.LoginConfig.Should().NotBeNull("DeepSeek 需启用浏览器登录态获取 UserToken");
        manifest.LoginConfig!.LocalStorageTokens.Should().ContainKey("UserToken");
        manifest.LoginConfig.LocalStorageTokens["UserToken"].Should().Be("userToken");

        // cost 端点的 seriesPivot 数组声明。
        var costEp = manifest.Fetch.Endpoints.First(e => e.UrlMatch.Contains("cost"));
        costEp.Mode.Should().Be("http");
        var pivot = costEp.Arrays.First();
        pivot.Mode.Should().Be("seriesPivot");
        pivot.SeriesNameField.Should().Be("model");
        pivot.NestedItems.Should().Be("buckets");

        // 卡片声明含 StackedBar 与 Area 图表。
        manifest.Card!.Charts.Should().Contain(c => c.Kind == DeclarativeChartKind.StackedBar);
        manifest.Card.Charts.Should().Contain(c => c.Kind == DeclarativeChartKind.Area);
        var stacked = manifest.Card.Charts.First(c => c.Kind == DeclarativeChartKind.StackedBar);
        stacked.CategoriesField.Should().Be("daily_cost_categories");
        stacked.ValuesMatrixField.Should().Be("daily_cost_matrix");
        // 问题7：移除自定义颜色声明，堆叠柱状图统一走宿主默认蓝色色板，保证同卡片多图颜色风格一致。
        stacked.Colors.Should().BeEmpty();
        // 问题12：时序图表声明泛化数据组（供卡片管理页展示/勾选）。
        stacked.DataGroups.Should().NotBeEmpty();
    }

    /// <summary>定位 DeepSeek 声明包路径（输出目录或源码目录）。</summary>
    private static string? FindDeepSeekDefaults()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "UsageMonitor.Plugin.DeepSeek", "defaults.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "Plugins", "UsageMonitor.Plugin.DeepSeek", "defaults.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>DeepSeek 声明包通过 PluginValidator 完整校验（字段白名单 + ChartKindSpec + 切片器约束）。</summary>
    [Fact]
    public void DeepSeekManifest_PassesPluginValidator()
    {
        // Arrange
        var manifestPath = FindDeepSeekDefaults();
        if (manifestPath == null) return; // CI 无声明包时跳过
        var json = File.ReadAllText(manifestPath);

        // Act
        var result = UsageMonitor.Core.Plugins.PluginValidator.Validate(json, new Version(0, 36, 0));

        // Assert：无错误（警告可接受）。
        result.Errors.Should().BeEmpty($"声明包应通过完整校验，实际错误：{string.Join("; ", result.Errors)}");
    }

    /// <summary>CredentialProbe 泛化：Password 型配置字段（如 DeepSeek UserToken）非空即视为已配凭据。</summary>
    [Fact]
    public void CredentialProbe_PasswordConfigField_CountsAsCredential()
    {
        // Arrange：模拟 Password 凭据字段。用不存在的 providerId，避免回退到真实 cookie 文件（测试隔离）。
        const string pid = "CredentialProbeTest_NoSuchProvider";
        var configFields = new List<UsageMonitor.Core.Models.ConfigField>
        {
            new("UserToken", "平台 UserToken", UsageMonitor.Core.Models.ConfigFieldType.Password)
        };
        var withToken = new UsageMonitor.Core.Models.ProviderConfig { ProviderId = pid };
        withToken.SetValue("UserToken", "sk-test-token");
        var empty = new UsageMonitor.Core.Models.ProviderConfig { ProviderId = pid };

        // Act & Assert：有 UserToken → 已配凭据；空配置 → 未配凭据。
        UsageMonitor.Core.Services.CredentialProbe
            .HasConfiguredCredential(pid, withToken, null, configFields).Should().BeTrue();
        UsageMonitor.Core.Services.CredentialProbe
            .HasConfiguredCredential(pid, empty, null, configFields).Should().BeFalse();
    }

    /// <summary>端到端：加载真实 defaults.json（走 JSON 反序列化路径）+ 真实结构的接口响应，
    /// 验证执行器产出余额字段、模型枢轴（cost）与字段枢轴（requests/tokens）数据。</summary>
    [Fact]
    public void DeepSeekManifest_EndToEnd_ProducesAllChartData()
    {
        // Arrange：加载真实声明包。
        var manifestPath = FindDeepSeekDefaults();
        if (manifestPath == null) return;
        var manifest = PluginManifest.Load(File.ReadAllText(manifestPath));
        manifest.Should().NotBeNull();

        // 真实结构的响应（与 chrome-devtools 实测一致：data.biz_data 包裹）。
        const string summary = """{"code":0,"msg":"","data":{"biz_code":0,"biz_msg":"","biz_data":{"normal_wallets":[{"currency":"CNY","balance":"32.66","token_estimation":"10889755"}],"monthly_costs":[{"currency":"CNY","amount":"27.92"}],"total_costs":[{"currency":"CNY","amount":"167.33"}],"monthly_token_usage":"298916453"}}}""";
        const string cost = """{"code":0,"msg":"","data":{"biz_code":0,"biz_msg":"","biz_data":{"bucket":86400,"data":[{"currency":"CNY","series":[{"model":"deepseek-v4-flash","buckets":[{"time":1782432000,"cost":"1.5"},{"time":1782518400,"cost":"0.5"}]}]}]}}}""";
        const string amount = """{"code":0,"msg":"","data":{"biz_code":0,"biz_msg":"","biz_data":{"bucket":86400,"series":[{"model":"deepseek-v4-flash","buckets":[{"time":1782432000,"usage":{"RESPONSE_TOKEN":100,"REQUEST":5,"PROMPT_CACHE_HIT_TOKEN":200,"PROMPT_CACHE_MISS_TOKEN":300}},{"time":1782518400,"usage":{"RESPONSE_TOKEN":50,"REQUEST":2,"PROMPT_CACHE_HIT_TOKEN":100,"PROMPT_CACHE_MISS_TOKEN":150}}]}]}}}""";
        var captured = new Dictionary<string, string>
        {
            ["https://platform.deepseek.com/api/v0/users/get_user_summary"] = summary,
            ["https://platform.deepseek.com/api/v0/usage/by_api_key/cost?start=1&end=2&tz=0"] = cost,
            ["https://platform.deepseek.com/api/v0/usage/by_api_key/amount?start=1&end=2&tz=0"] = amount,
        };

        // Act
        var result = DeclarativeCaptureExecutor.Execute(manifest!.Fetch, captured);

        // Assert：余额字段。
        result.Extras.Should().ContainKey("balance_amount");
        Convert.ToDouble(result.Extras["balance_amount"]).Should().BeApproximately(32.66, 0.001);
        result.Extras.Should().ContainKey("monthly_cost");

        // Assert：模型枢轴（每日消费）。
        result.Extras.Should().ContainKey("daily_cost_categories");
        result.Extras.Should().ContainKey("daily_cost_matrix");

        // Assert：字段枢轴（请求次数 + token 分层）。
        result.Extras.Should().ContainKey("daily_requests_categories", "字段枢轴应产出请求数据");
        result.Extras.Should().ContainKey("daily_requests_matrix");
        var reqMatrix = result.Extras["daily_requests_matrix"].Should().BeAssignableTo<List<List<double>>>().Subject;
        reqMatrix[0].Should().BeEquivalentTo(new double[] { 5, 2 });

        result.Extras.Should().ContainKey("daily_tokens_matrix");
        var tokMatrix = result.Extras["daily_tokens_matrix"].Should().BeAssignableTo<List<List<double>>>().Subject;
        tokMatrix.Should().HaveCount(3, "token 枢轴应有 cache_miss/cache_hit/response 三个系列");
    }
}
