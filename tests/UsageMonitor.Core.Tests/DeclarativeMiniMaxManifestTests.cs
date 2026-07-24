using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins.Declarative;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-088 Phase3 迁移校验：加载 <b>真实</b> MiniMax defaults.json 的 fetch 声明，用真实接口 JSON（2026-07 实测裁剪）
/// 端到端跑 <see cref="DeclarativeCaptureExecutor"/>，逐字段核对新声明路径产出与旧提取器等价（除浏览器抓取层外全覆盖）。
/// </summary>
public class DeclarativeMiniMaxManifestTests
{
    private const string RemainsPercentJson = """
    { "model_remains": [
        { "model_name": "general", "current_interval_used_percent": "3%", "current_weekly_used_percent": "83%",
          "end_time": 1784894400000, "weekly_end_time": 1785081600000 },
        { "model_name": "video", "current_interval_total_count": 3, "current_interval_remains_count": 3,
          "current_weekly_total_count": 21, "current_weekly_remains_count": 21 }
      ] }
    """;

    private const string UsageSummaryJson = """
    {
      "total_days": 44, "active_days": 39, "usage_ranking_percent": 1.27, "total_token_consumed": "5.85B",
      "most_active_day": { "date": "2026-07-01", "token_count": "552.49M" },
      "date_model_usage": [
        { "date": "2026-07-20", "total_token": 254674208, "cache_hit_percent": "95.91%",
          "models": [ { "model": "MiniMax-M3-512k", "input_token": 253754259, "output_token": 919949, "cache_read_token": 243372536, "total_token": 498046744, "cache_hit_percent": "95.91%" } ] },
        { "date": "2026-07-21", "total_token": 379039852, "cache_hit_percent": "96.43%",
          "models": [ { "model": "MiniMax-M3-512k", "input_token": 288215908, "output_token": 508663, "cache_read_token": 279689794, "total_token": 568414365, "cache_hit_percent": "97.04%" },
                      { "model": "MiniMax-M3-1m", "input_token": 90255082, "output_token": 60199, "cache_read_token": 85287736, "total_token": 175603017, "cache_hit_percent": "94.50%" } ] }
      ]
    }
    """;

    private const string CreditJson = """{ "remaining_credits": 0, "total_credits": 0, "api_key": "sk-secret" }""";
    private const string GroupListJson = """{ "groups": [ { "group_id": "2031039003800637495", "group_name": "18638649086", "is_default": true } ] }""";

    /// <summary>从测试运行目录向上查找仓库内的 MiniMax defaults.json。</summary>
    private static string? FindRealDefaultsJson()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Plugins", "UsageMonitor.Plugin.MiniMax", "defaults.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void RealDefaultsJson_LoadsWithFetchDeclaration()
    {
        var path = FindRealDefaultsJson();
        path.Should().NotBeNull("应能在仓库内定位 MiniMax defaults.json");
        var manifest = PluginManifest.Load(File.ReadAllText(path!));
        manifest.Should().NotBeNull();
        manifest!.Fetch.Should().NotBeNull("defaults.json 必须含 fetch 取数声明");
        manifest.Fetch!.Endpoints.Should().HaveCountGreaterThanOrEqualTo(3);
        manifest.Fetch.Aggregates.Should().NotBeEmpty();
        manifest.Fetch.Computed.Should().NotBeEmpty();
        manifest.Fetch.AccountId.Should().NotBeNull();
    }

    [Fact]
    public void RealDeclaration_EndToEnd_ProducesAllExpectedExtras()
    {
        var path = FindRealDefaultsJson();
        path.Should().NotBeNull();
        var manifest = PluginManifest.Load(File.ReadAllText(path!));
        var captured = new Dictionary<string, string>
        {
            ["https://www.minimaxi.com/backend/account/token_plan/remains_percent"] = RemainsPercentJson,
            ["https://www.minimaxi.com/backend/account/token_plan/usage_summary"] = UsageSummaryJson,
            ["https://www.minimaxi.com/backend/account/token_plan_credit"] = CreditJson,
            ["https://www.minimaxi.com/backend/group/list"] = GroupListJson
        };
        var r = DeclarativeCaptureExecutor.Execute(manifest!.Fetch, captured);
        var e = r.Extras;

        // 时刻级
        e["mm_5hUsedPercent"].Should().Be(3L);
        e["mm_weeklyUsedPercent"].Should().Be(83L);
        e["mm_5hResetAt"].Should().BeOfType<System.DateTime>();
        e["mm_weeklyResetAt"].Should().BeOfType<System.DateTime>();
        // 视频次数 + computed used=total-remains
        e["mm_videoIntervalTotal"].Should().Be(3L);
        e["mm_videoIntervalUsed"].Should().Be(0L);
        e["mm_videoWeeklyUsed"].Should().Be(0L);
        // 总体
        e["mm_totalDays"].Should().Be(44L);
        e["mm_activeDays"].Should().Be(39L);
        e["mm_totalTokens"].Should().Be("5.85B");
        e["mm_mostActiveDate"].Should().Be("2026-07-01");
        e["mm_mostActiveToken"].Should().Be(552_490_000L);
        // 日级并行列表 + 模型×日
        e["mm_dailyTokenValues"].Should().BeOfType<List<long>>().Which.Should().Equal(254674208L, 379039852L);
        e["mm_dailyTokenDates"].Should().BeOfType<List<string>>().Which.Should().Equal("2026-07-20", "2026-07-21");
        e["mm_modelDaily"].Should().BeOfType<List<Dictionary<string, object>>>().Which.Should().HaveCount(3);
        // 缓存 token 加权平均
        System.Convert.ToDouble(e["mm_cacheHitPercent"]).Should().BeApproximately(96.22, 0.5);
        // 积分 + 账号身份
        e["mm_remainingCredits"].Should().Be(0L);
        r.StableId.Should().Be("2031039003800637495");
    }
}
