using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Services.Data;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-088 Phase1/Phase4：数据层账号隔离 + 刷新快照序列可查的集成测试（临时 SQLite）。
/// <para>验证：① usage_field_versions/history 按 account_id 隔离（Phase1）；② 快照序列可按账号+时间读取（Phase4 第2层）；
/// ③ usage_request_detail / usage_daily_trend 按 account_id 隔离（Phase4 第1层 + 明细）。</para>
/// </summary>
public class UsageDataLayerAccountTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"um_test_{Guid.NewGuid():N}.db");

    private static void TryDelete(string path)
    {
        foreach (var p in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* 临时文件，忽略清理失败 */ }
        }
    }

    [Fact]
    public async Task FieldVersionsAndHistory_AreAccountIsolated_AndSeriesQueryable()
    {
        var db = TempDb();
        try
        {
            var repo = new UsageHistoryRepository(db);
            repo.EnsureSchema();

            await repo.SaveIncrementalAsync("MiniMax", "acctA",
                new[] { new FieldChange("five_hour_used_percent", null, 3L, ChangeType.Modified) });
            await repo.SaveIncrementalAsync("MiniMax", "acctA",
                new[] { new FieldChange("five_hour_used_percent", 3L, 5L, ChangeType.Modified) });
            await repo.SaveIncrementalAsync("MiniMax", "acctB",
                new[] { new FieldChange("five_hour_used_percent", null, 99L, ChangeType.Modified) });

            // 最新值按账号隔离
            var latestA = await repo.GetLatestFieldsAsync("MiniMax", "acctA");
            var latestB = await repo.GetLatestFieldsAsync("MiniMax", "acctB");
            Convert.ToDouble(latestA["five_hour_used_percent"]).Should().Be(5d);
            Convert.ToDouble(latestB["five_hour_used_percent"]).Should().Be(99d);

            // 快照序列按账号可查
            (await repo.GetFieldHistoryAsync("MiniMax", "acctA")).Should().HaveCount(2);
            (await repo.GetFieldHistoryAsync("MiniMax", "acctB")).Should().HaveCount(1);
            (await repo.GetFieldHistoryAsync("MiniMax", "acctA", "five_hour_used_percent")).Should().HaveCount(2);
        }
        finally { TryDelete(db); }
    }

    [Fact]
    public void RequestDetail_And_DailyTrend_AreAccountIsolated()
    {
        var db = TempDb();
        try
        {
            var repo = new UsageDetailRepository(db);
            repo.EnsureSchema();

            repo.UpsertRequestDetailBatch("qoder_web", "acctA",
                new[] { new RequestDetailRow("req1", "2026-07-24 21:00:00", "IDE", "m1", 100, 5.0, null, 0.1) });
            repo.UpsertDailyTrendBatch("MiniMax", "acctA",
                new[] { new DailyTrendRow("2026-07-24", 254674208, 84.1) });

            repo.GetRequestDetail("qoder_web", "acctA").Should().HaveCount(1);
            repo.GetRequestDetail("qoder_web", "acctB").Should().BeEmpty();
            repo.GetDailyTrend("MiniMax", "acctA").Should().HaveCount(1);
            repo.GetDailyTrend("MiniMax", "acctB").Should().BeEmpty();
        }
        finally { TryDelete(db); }
    }

    /// <summary>
    /// req-088 Phase4 第3层：验证 upsert 覆盖策略——同一 (provider, account, date[, model]) / request_id
    /// 再次写入时按新值覆盖（“当天行随刷新覆盖 + 允许覆盖修正”），不产生重复行，行数保持 1。
    /// </summary>
    [Fact]
    public void SameKeyUpsert_OverwritesInPlace_NoDuplicateRows()
    {
        var db = TempDb();
        try
        {
            var repo = new UsageDetailRepository(db);
            repo.EnsureSchema();

            // ① 每日趋势：同日两次刷新，第二次值不同 → 仅 1 行且为新值（当天覆盖 + 修正）。
            repo.UpsertDailyTrend("MiniMax", "acctA", "2026-07-24", 100, 50.0);
            repo.UpsertDailyTrend("MiniMax", "acctA", "2026-07-24", 254674208, 84.1);
            var daily = repo.GetDailyTrend("MiniMax", "acctA");
            daily.Should().HaveCount(1);
            daily[0].TokenTotal.Should().Be(254674208);
            daily[0].CacheHitPercent.Should().Be(84.1);

            // ② 模型×日：同 (date, model) 两次 → 仅 1 行且为新值。
            repo.UpsertModelDaily("MiniMax", "acctA", new ModelDailyRow("2026-07-24", "abab6.5s", 1, 2, 0, 0, 3, null));
            repo.UpsertModelDaily("MiniMax", "acctA", new ModelDailyRow("2026-07-24", "abab6.5s", 10, 20, 5, 0, 35, 70.0));
            var model = repo.GetModelDaily("MiniMax", "acctA");
            model.Should().HaveCount(1);
            model[0].TotalToken.Should().Be(35);
            model[0].CacheHitPercent.Should().Be(70.0);

            // ③ 逐请求：同 request_id 两次 → 仅 1 行且为新值（request_id 去重）。
            repo.UpsertRequestDetailBatch("qoder_web", "acctA",
                new[] { new RequestDetailRow("reqX", "2026-07-24 21:00:00", "IDE", "m1", 100, 5.0, null, 0.1) });
            repo.UpsertRequestDetailBatch("qoder_web", "acctA",
                new[] { new RequestDetailRow("reqX", "2026-07-24 21:05:00", "CLI", "m1", 200, 9.0, null, 0.2) });
            var reqs = repo.GetRequestDetail("qoder_web", "acctA");
            reqs.Should().HaveCount(1);
            reqs[0].Token.Should().Be(200);
            reqs[0].Channel.Should().Be("CLI");
        }
        finally { TryDelete(db); }
    }
}
