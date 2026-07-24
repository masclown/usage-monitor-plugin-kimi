using System.Collections.Generic;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.Declarative;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-088 Phase3：声明式取数执行器单元测试。
/// <para>用 MiniMax 真实接口 JSON（2026-07 实测裁剪）驱动 <see cref="DeclarativeCaptureExecutor"/>，
/// 验证"接口 JSON → extras"逐字段映射（标量 / 并行强类型列表 / 对象数组嵌套 / 账号稳定身份）与旧手写提取器等价，
/// 无需真实浏览器即可核对 Phase3 声明式取数正确性。同时验证 defaults.json 的 fetch 节能反序列化。</para>
/// </summary>
public class DeclarativeCaptureExecutorTests
{
    // MiniMax defaults.json 的 fetch 节（与插件一致，用于验证反序列化 + 执行）。
    private const string ManifestJson = """
    {
      "providerId": "MiniMax",
      "fetch": {
        "endpoints": [
          {
            "urlMatch": "remains_percent",
            "fields": [
              { "path": "$.model_remains[0].current_interval_used_percent", "target": "mm_5hUsedPercent", "transform": "parsePercent" },
              { "path": "$.model_remains[0].current_weekly_used_percent", "target": "mm_weeklyUsedPercent", "transform": "parsePercent" }
            ]
          },
          {
            "urlMatch": "usage_summary",
            "fields": [
              { "path": "$.total_days", "target": "mm_totalDays", "transform": "parseNumber" },
              { "path": "$.active_days", "target": "mm_activeDays", "transform": "parseNumber" }
            ],
            "arrays": [
              {
                "itemsPath": "$.date_model_usage",
                "mode": "parallel",
                "itemFields": [
                  { "path": "$.date", "target": "mm_dailyTokenDates", "elementType": "string" },
                  { "path": "$.total_token", "target": "mm_dailyTokenValues", "elementType": "long" },
                  { "path": "$.cache_hit_percent", "target": "mm_dailyCacheHitPercents", "transform": "parsePercent", "elementType": "double" }
                ]
              },
              {
                "itemsPath": "$.date_model_usage",
                "mode": "objects",
                "nestedItems": "models",
                "target": "mm_modelDaily",
                "inheritFromParent": { "date": "date" },
                "itemFields": [
                  { "path": "$.model", "target": "model" },
                  { "path": "$.input_token", "target": "input_token", "transform": "parseNumber" },
                  { "path": "$.output_token", "target": "output_token", "transform": "parseNumber" }
                ]
              }
            ]
          },
          {
            "urlMatch": "token_plan_credit",
            "fields": [
              { "path": "$.remaining_credits", "target": "mm_remainingCredits", "transform": "parseNumber" }
            ]
          }
        ],
        "accountId": { "urlMatch": "group/list", "path": "$.groups[0].group_id" }
      }
    }
    """;

    private const string RemainsPercentJson = """
    { "model_remains": [
        { "model_name": "general", "current_interval_used_percent": "3%", "current_weekly_used_percent": "83%" },
        { "model_name": "video", "current_interval_used_percent": "0%" }
      ] }
    """;

    private const string UsageSummaryJson = """
    {
      "total_days": 44, "active_days": 39, "total_token_consumed": "5.85B",
      "date_model_usage": [
        { "date": "2026-07-20", "total_token": 254674208, "cache_hit_percent": "95.91%",
          "models": [ { "model": "MiniMax-M3-512k", "input_token": 253754259, "output_token": 919949 } ] },
        { "date": "2026-07-21", "total_token": 379039852, "cache_hit_percent": "96.43%",
          "models": [ { "model": "MiniMax-M3-512k", "input_token": 288215908, "output_token": 508663 },
                      { "model": "MiniMax-M3-1m", "input_token": 90255082, "output_token": 60199 } ] }
      ]
    }
    """;

    private const string CreditJson = """{ "remaining_credits": 0, "total_credits": 0 }""";
    private const string GroupListJson = """{ "groups": [ { "group_id": "2031039003800637495", "is_default": true } ] }""";

    private static CaptureResult RunExecute()
    {
        var manifest = PluginManifest.Load(ManifestJson);
        manifest.Should().NotBeNull();
        manifest!.Fetch.Should().NotBeNull();
        var captured = new Dictionary<string, string>
        {
            ["https://www.minimaxi.com/backend/account/token_plan/remains_percent"] = RemainsPercentJson,
            ["https://www.minimaxi.com/backend/account/token_plan/usage_summary"] = UsageSummaryJson,
            ["https://www.minimaxi.com/backend/account/token_plan_credit"] = CreditJson,
            ["https://www.minimaxi.com/backend/group/list"] = GroupListJson
        };
        return DeclarativeCaptureExecutor.Execute(manifest.Fetch, captured);
    }

    [Fact]
    public void ScalarFields_ParsedFromCapturedJson()
    {
        var r = RunExecute();
        r.Extras["mm_5hUsedPercent"].Should().Be(3L);
        r.Extras["mm_weeklyUsedPercent"].Should().Be(83L);
        r.Extras["mm_totalDays"].Should().Be(44L);
        r.Extras["mm_activeDays"].Should().Be(39L);
        r.Extras["mm_remainingCredits"].Should().Be(0L);
    }

    [Fact]
    public void ParallelArrays_ProduceAlignedTypedLists()
    {
        var r = RunExecute();
        r.Extras["mm_dailyTokenDates"].Should().BeOfType<List<string>>()
            .Which.Should().Equal("2026-07-20", "2026-07-21");
        r.Extras["mm_dailyTokenValues"].Should().BeOfType<List<long>>()
            .Which.Should().Equal(254674208L, 379039852L);
        r.Extras["mm_dailyCacheHitPercents"].Should().BeOfType<List<double>>()
            .Which.Should().HaveCount(2);
    }

    [Fact]
    public void ObjectArray_NestedModels_ExpandWithParentDate()
    {
        var r = RunExecute();
        var rows = r.Extras["mm_modelDaily"].Should().BeOfType<List<Dictionary<string, object>>>().Which;
        rows.Should().HaveCount(3); // 1 + 2 models
        rows[0]["date"].Should().Be("2026-07-20");
        rows[0]["model"].Should().Be("MiniMax-M3-512k");
        rows[0]["input_token"].Should().Be(253754259L);
        rows[2]["date"].Should().Be("2026-07-21");
        rows[2]["model"].Should().Be("MiniMax-M3-1m");
    }

    [Fact]
    public void AccountId_ResolvesDefaultGroupId()
    {
        var r = RunExecute();
        r.StableId.Should().Be("2031039003800637495");
    }

    // ===== 新能力：finds 按字段选取 + fromUnixMs + parseFormattedToken =====

    private const string FindsManifestJson = """
    {
      "providerId": "MiniMax",
      "fetch": {
        "endpoints": [
          {
            "urlMatch": "remains_percent",
            "finds": [
              {
                "itemsPath": "$.model_remains", "matchField": "model_name", "matchValue": "general",
                "fields": [
                  { "path": "$.current_interval_used_percent", "target": "mm_5hUsedPercent", "transform": "parsePercent" },
                  { "path": "$.end_time", "target": "mm_5hResetAt", "transform": "fromUnixMs" }
                ]
              },
              {
                "itemsPath": "$.model_remains", "matchField": "model_name", "matchValue": "video",
                "fields": [
                  { "path": "$.current_interval_total_count", "target": "mm_videoIntervalTotal", "transform": "parseNumber" },
                  { "path": "$.current_weekly_total_count", "target": "mm_videoWeeklyTotal", "transform": "parseNumber" }
                ]
              }
            ]
          },
          {
            "urlMatch": "usage_summary",
            "fields": [
              { "path": "$.most_active_day.date", "target": "mm_mostActiveDate" },
              { "path": "$.most_active_day.token_count", "target": "mm_mostActiveToken", "transform": "parseFormattedToken" }
            ]
          }
        ]
      }
    }
    """;

    private const string RemainsWithTimesJson = """
    { "model_remains": [
        { "model_name": "general", "current_interval_used_percent": "3%", "end_time": 1784894400000 },
        { "model_name": "video", "current_interval_total_count": 3, "current_weekly_total_count": 21 }
      ] }
    """;

    private const string MostActiveJson = """
    { "most_active_day": { "date": "2026-07-01", "token_count": "552.49M" } }
    """;

    private static CaptureResult RunFinds()
    {
        var manifest = PluginManifest.Load(FindsManifestJson);
        var captured = new Dictionary<string, string>
        {
            ["https://www.minimaxi.com/backend/account/token_plan/remains_percent"] = RemainsWithTimesJson,
            ["https://www.minimaxi.com/backend/account/token_plan/usage_summary"] = MostActiveJson
        };
        return DeclarativeCaptureExecutor.Execute(manifest!.Fetch, captured);
    }

    [Fact]
    public void Finds_SelectGeneralAndVideoByModelName()
    {
        var r = RunFinds();
        r.Extras["mm_5hUsedPercent"].Should().Be(3L);
        r.Extras["mm_videoIntervalTotal"].Should().Be(3L);
        r.Extras["mm_videoWeeklyTotal"].Should().Be(21L);
    }

    [Fact]
    public void FromUnixMs_ProducesDateTime()
    {
        var r = RunFinds();
        r.Extras["mm_5hResetAt"].Should().BeOfType<System.DateTime>();
    }

    [Fact]
    public void ParseFormattedToken_RestoresRawLong()
    {
        var r = RunFinds();
        r.Extras["mm_mostActiveDate"].Should().Be("2026-07-01");
        r.Extras["mm_mostActiveToken"].Should().Be(552_490_000L);
    }

    // ===== 新能力：aggregates 加权平均 + computed 减法计算列 =====

    private const string AggComputeManifestJson = """
    {
      "providerId": "MiniMax",
      "fetch": {
        "endpoints": [
          { "urlMatch": "remains_percent", "finds": [
            { "itemsPath": "$.model_remains", "matchField": "model_name", "matchValue": "video", "fields": [
              { "path": "$.current_interval_total_count", "target": "mm_videoIntervalTotal", "transform": "parseNumber" },
              { "path": "$.current_interval_remains_count", "target": "mm_videoIntervalRemaining", "transform": "parseNumber" }
            ]}
          ]}
        ],
        "aggregates": [
          { "urlMatch": "usage_summary", "itemsPath": "$.date_model_usage", "op": "weightedAvg", "valuePath": "$.cache_hit_percent", "valueTransform": "parsePercent", "weightPath": "$.total_token", "target": "mm_cacheHitPercent" }
        ],
        "computed": [
          { "target": "mm_videoIntervalUsed", "op": "subtract", "operands": ["mm_videoIntervalTotal", "mm_videoIntervalRemaining"] }
        ]
      }
    }
    """;

    private const string VideoRemainsJson = """{ "model_remains": [ { "model_name": "video", "current_interval_total_count": 3, "current_interval_remains_count": 2 } ] }""";
    private const string CacheAggJson = """{ "date_model_usage": [ { "total_token": 100, "cache_hit_percent": "90%" }, { "total_token": 300, "cache_hit_percent": "98%" } ] }""";

    [Fact]
    public void ComputedSubtract_And_WeightedAvg()
    {
        var manifest = PluginManifest.Load(AggComputeManifestJson);
        var captured = new Dictionary<string, string>
        {
            ["https://www.minimaxi.com/backend/account/token_plan/remains_percent"] = VideoRemainsJson,
            ["https://www.minimaxi.com/backend/account/token_plan/usage_summary"] = CacheAggJson
        };
        var r = DeclarativeCaptureExecutor.Execute(manifest!.Fetch, captured);
        // 视频 used = total(3) - remains(2) = 1
        r.Extras["mm_videoIntervalUsed"].Should().Be(1L);
        // 加权平均 = (100*90 + 300*98)/400 = 96.0
        System.Convert.ToDouble(r.Extras["mm_cacheHitPercent"]).Should().BeApproximately(96.0, 0.001);
    }
}
