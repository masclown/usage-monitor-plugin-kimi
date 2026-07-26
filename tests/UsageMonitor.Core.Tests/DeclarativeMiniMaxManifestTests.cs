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
        e["five_hour_used_percent"].Should().Be(3L);
        e["weekly_used_percent"].Should().Be(83L);
        e["five_hour_reset_at"].Should().BeOfType<System.DateTime>();
        e["weekly_reset_at"].Should().BeOfType<System.DateTime>();
        // 视频次数 + computed used=total-remains（标准字段）
        e["video_total_count"].Should().Be(3L);
        e["video_used_count"].Should().Be(0L);
        e["weekly_video_used"].Should().Be(0L);
        // 总体
        e["total_days"].Should().Be(44L);
        e["active_days"].Should().Be(39L);
        e["used_tokens_text"].Should().Be("5.85B");
        e["most_active_date"].Should().Be("2026-07-01");
        e["most_active_token"].Should().Be(552_490_000L);
        // 日级并行列表 + 模型×日
        e["daily_token_values"].Should().BeOfType<List<long>>().Which.Should().Equal(254674208L, 379039852L);
        e["daily_token_dates"].Should().BeOfType<List<string>>().Which.Should().Equal("2026-07-20", "2026-07-21");
        e["model_daily"].Should().BeOfType<List<Dictionary<string, object>>>().Which.Should().HaveCount(3);
        // 缓存 token 加权平均
        System.Convert.ToDouble(e["cache_hit_percent"]).Should().BeApproximately(96.22, 0.5);
        // 积分 + 账号身份
        e["remaining_credits"].Should().Be(0L);
        r.StableId.Should().Be("2031039003800637495");
    }

    /// <summary>
    /// Stage E：验证原 MiniMaxProvider C# 后处理（订阅拆分 / 峰值日字符串）已由 computed 新算子完整声明化。
    /// </summary>
    [Fact]
    public void RealDeclaration_ComputedOps_ReplaceLegacyCSharpPostProcessing()
    {
        var path = FindRealDefaultsJson();
        path.Should().NotBeNull();
        var manifest = PluginManifest.Load(File.ReadAllText(path!));
        var captured = new Dictionary<string, string>
        {
            ["https://www.minimaxi.com/backend/account/token_plan/usage_summary"] = UsageSummaryJson
        };
        // DOM 兑底字段：订阅原始文案（与页面实测一致）
        var dom = new Dictionary<string, string> { ["subscription_raw"] = "Token Plan · TokenPlanMax-年度会员" };
        var e = DeclarativeCaptureExecutor.Execute(manifest!.Fetch, captured, dom).Extras;

        e["subscription_type"].Should().Be("Token Plan");
        e["subscription_tier"].Should().Be("TokenPlanMax-年度会员");
        e["subscription_title"].Should().Be("TokenPlanMax-年度会员");
        e["subscription_active"].Should().Be(true);
        // 峰值日字符串：旧 C# 拼接 "日期 (值)" → template 算子等价产出
        e["most_active_day"].Should().Be("2026-07-01 (552.49M)");
    }

    /// <summary>
    /// 卡片图表管理优化：验证 defaults.json 的 card.charts 声明——
    /// remaining_number 扩展为 4 个数据组（累计/峰值/活跃天数/积分余额）、tokenplan_bar 已移除（与 usage_bar 去重）、
    /// 各图表与数据组均声明了中文 display。
    /// </summary>
    [Fact]
    public void RealCardDeclaration_RemainingNumberGroups_TokenplanRemoved_ChineseDisplay()
    {
        var path = FindRealDefaultsJson();
        path.Should().NotBeNull();
        var manifest = PluginManifest.Load(File.ReadAllText(path!));
        var charts = manifest!.Card!.Charts;

        // tokenplan_bar 已移除（与 usage_bar 消费同字段，planType 未在渲染层实现，属重复设定）
        charts.Should().NotContain(c => c.ChartId == "mm.chart.tokenplan_bar", "tokenplan_bar 与 usage_bar 重复，应已移除");

        // remaining_number 扩展为 4 个数据组
        var number = charts.FirstOrDefault(c => c.ChartId == "mm.chart.remaining_number");
        number.Should().NotBeNull("remaining_number 应声明（用量卡片数据概览）");
        number!.DataGroups.Select(g => g.Id).Should().BeEquivalentTo(new[]
        {
            "mm.number.cumulative", "mm.number.peak", "mm.number.active_days", "mm.number.remaining"
        }, "数据概览应含累计/峰值/活跃天数/积分余额四个数据组");

        // 每个图表与数据组均声明中文 display
        foreach (var chart in charts)
        {
            chart.Display.Should().NotBeNullOrWhiteSpace($"图表 {chart.ChartId} 应声明中文 display");
            foreach (var group in chart.DataGroups)
                group.Display.Should().NotBeNullOrWhiteSpace($"数据组 {group.Id} 应声明中文 display");
        }
    }
}
