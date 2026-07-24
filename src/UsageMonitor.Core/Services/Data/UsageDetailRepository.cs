using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace UsageMonitor.Core.Services.Data;

/// <summary>每日趋势行（usage_daily_trend，画折线/热力）。</summary>
/// <param name="Date">日期（yyyy-MM-dd，去重键）。</param>
/// <param name="TokenTotal">当日总计 Token（day 级 = input + output）。</param>
/// <param name="CacheHitPercent">当日缓存命中率（0-100，null 表示无数据）。</param>
public sealed record DailyTrendRow(string Date, long TokenTotal, double? CacheHitPercent);

/// <summary>模型×日聚合行（usage_model_daily）。</summary>
/// <param name="Date">日期（yyyy-MM-dd）。</param>
/// <param name="ModelName">模型名。</param>
/// <param name="InputToken">输入 Token。</param>
/// <param name="OutputToken">输出 Token。</param>
/// <param name="CacheReadToken">缓存读取 Token。</param>
/// <param name="CacheMissToken">缓存未命中 Token。</param>
/// <param name="TotalToken">model 级总计 Token（含 cache_read）。</param>
/// <param name="CacheHitPercent">缓存命中率（0-100，null 表示无数据）。</param>
public sealed record ModelDailyRow(string Date, string ModelName, long InputToken, long OutputToken,
    long CacheReadToken, long CacheMissToken, long TotalToken, double? CacheHitPercent);

/// <summary>逐请求流水行（usage_request_detail，req-088 Phase4；供有请求级数据源的 Provider 如 Qoder/Kimi，MiniMax 无此源）。</summary>
/// <param name="RequestId">请求 ID（去重键）。</param>
/// <param name="OccurredAt">发生时刻（yyyy-MM-dd HH:mm:ss，支持小时/分钟级图表）。</param>
/// <param name="Channel">渠道/客户端（IDE/CLI/API 等）。</param>
/// <param name="ModelName">模型名。</param>
/// <param name="Token">该请求 Token 数。</param>
/// <param name="UsedPercent">用量百分比（可空）。</param>
/// <param name="Credits">消耗 Credits（可空）。</param>
/// <param name="Cost">费用（可空）。</param>
public sealed record RequestDetailRow(string RequestId, string OccurredAt, string? Channel, string? ModelName,
    long Token, double? UsedPercent, double? Credits, double? Cost);

/// <summary>
/// 用量明细仓储（req-107 B8 取数前置）：管理两张时序明细表，供声明式图表（折线/热力/模型×日）按字段引用 + queryRange 取数。
/// <para>表结构对应 <c>docs/sdk-unified-fields.md</c> §2：
/// ① <c>usage_daily_trend</c>（date 去重，画折线/热力）；② <c>usage_model_daily</c>（(date,model) 去重，模型×日聚合）。
/// 均含系统列 provider_id / account_id（多账号隔离，req-109），与 <see cref="UsageHistoryRepository"/> 同库并存、附加式不破坏既有表。</para>
/// </summary>
public sealed class UsageDetailRepository : IDisposable
{
    private readonly string _connectionString;

    /// <summary>创建明细仓储（与历史库同一 SQLite 文件）。</summary>
    /// <param name="dbFilePath">SQLite 数据库文件路径（通常 %AppData%/UsageMonitor/history.db）。</param>
    public UsageDetailRepository(string dbFilePath)
    {
        var directory = System.IO.Path.GetDirectoryName(dbFilePath);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 10
        }.ToString();
    }

    /// <summary>幂等创建两张明细表与索引。</summary>
    public void EnsureSchema()
    {
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS usage_daily_trend (
    provider_id        TEXT NOT NULL,
    account_id         TEXT NOT NULL DEFAULT 'default',
    date               TEXT NOT NULL,
    token_total        INTEGER NOT NULL DEFAULT 0,
    cache_hit_percent  REAL,
    updated_at         DATETIME NOT NULL,
    PRIMARY KEY(provider_id, account_id, date)
);
CREATE INDEX IF NOT EXISTS idx_udt_provider_date
    ON usage_daily_trend(provider_id, account_id, date);

CREATE TABLE IF NOT EXISTS usage_model_daily (
    provider_id        TEXT NOT NULL,
    account_id         TEXT NOT NULL DEFAULT 'default',
    date               TEXT NOT NULL,
    model_name         TEXT NOT NULL,
    input_token        INTEGER NOT NULL DEFAULT 0,
    output_token       INTEGER NOT NULL DEFAULT 0,
    cache_read_token   INTEGER NOT NULL DEFAULT 0,
    cache_miss_token   INTEGER NOT NULL DEFAULT 0,
    total_token        INTEGER NOT NULL DEFAULT 0,
    cache_hit_percent  REAL,
    updated_at         DATETIME NOT NULL,
    PRIMARY KEY(provider_id, account_id, date, model_name)
);
CREATE INDEX IF NOT EXISTS idx_umd_provider_date
    ON usage_model_daily(provider_id, account_id, date);

-- req-088 Phase4: 逐请求流水（request_id 去重，时/分级图表数据源；有请求级数据的 Provider 使用）
CREATE TABLE IF NOT EXISTS usage_request_detail (
    provider_id   TEXT NOT NULL,
    account_id    TEXT NOT NULL DEFAULT 'default',
    request_id    TEXT NOT NULL,
    occurred_at   TEXT NOT NULL,
    channel       TEXT,
    model_name    TEXT,
    token         INTEGER NOT NULL DEFAULT 0,
    used_percent  REAL,
    credits       REAL,
    cost          REAL,
    updated_at    DATETIME NOT NULL,
    PRIMARY KEY(provider_id, account_id, request_id)
);
CREATE INDEX IF NOT EXISTS idx_urd_provider_time
    ON usage_request_detail(provider_id, account_id, occurred_at);
";
            cmd.ExecuteNonQuery();
            FileLogger.Info("UsageDetailRepository", "EnsureSchema ok");
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageDetailRepository", "EnsureSchema failed", ex);
        }
    }

    /// <summary>
    /// 写入/覆盖一条每日趋势（当日行随刷新覆盖，历史完结日稳定——印证 req-092/MiniMax 手册 §4.4 累积覆盖）。
    /// </summary>
    public void UpsertDailyTrend(string providerId, string accountId, string date, long tokenTotal, double? cacheHitPercent)
    {
        if (string.IsNullOrWhiteSpace(date)) return;
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT OR REPLACE INTO usage_daily_trend
    (provider_id, account_id, date, token_total, cache_hit_percent, updated_at)
    VALUES ($pid, $aid, $date, $tt, $chp, $ua);";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$aid", string.IsNullOrEmpty(accountId) ? "default" : accountId);
            cmd.Parameters.AddWithValue("$date", date);
            cmd.Parameters.AddWithValue("$tt", tokenTotal);
            cmd.Parameters.AddWithValue("$chp", cacheHitPercent.HasValue ? (object)cacheHitPercent.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$ua", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageDetailRepository", $"UpsertDailyTrend({providerId},{date}) failed: {ex.Message}");
        }
    }

    /// <summary>批量写入每日趋势（一次刷新写入多天，单连接 + 事务复用）。</summary>
    public void UpsertDailyTrendBatch(string providerId, string accountId, IReadOnlyList<DailyTrendRow> rows)
    {
        if (rows == null || rows.Count == 0) return;
        try
        {
            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Date)) continue;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT OR REPLACE INTO usage_daily_trend
    (provider_id, account_id, date, token_total, cache_hit_percent, updated_at)
    VALUES ($pid, $aid, $date, $tt, $chp, $ua);";
                cmd.Parameters.AddWithValue("$pid", providerId);
                cmd.Parameters.AddWithValue("$aid", string.IsNullOrEmpty(accountId) ? "default" : accountId);
                cmd.Parameters.AddWithValue("$date", row.Date);
                cmd.Parameters.AddWithValue("$tt", row.TokenTotal);
                cmd.Parameters.AddWithValue("$chp", row.CacheHitPercent.HasValue ? (object)row.CacheHitPercent.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("$ua", DateTime.UtcNow);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageDetailRepository", $"UpsertDailyTrendBatch({providerId},{rows.Count} rows) failed: {ex.Message}");
        }
    }

    /// <summary>写入/覆盖一条模型×日聚合。</summary>
    public void UpsertModelDaily(string providerId, string accountId, ModelDailyRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.Date) || string.IsNullOrWhiteSpace(row.ModelName)) return;
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT OR REPLACE INTO usage_model_daily
    (provider_id, account_id, date, model_name, input_token, output_token, cache_read_token, cache_miss_token, total_token, cache_hit_percent, updated_at)
    VALUES ($pid, $aid, $date, $mn, $it, $ot, $crt, $cmt, $tt, $chp, $ua);";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$aid", string.IsNullOrEmpty(accountId) ? "default" : accountId);
            cmd.Parameters.AddWithValue("$date", row.Date);
            cmd.Parameters.AddWithValue("$mn", row.ModelName);
            cmd.Parameters.AddWithValue("$it", row.InputToken);
            cmd.Parameters.AddWithValue("$ot", row.OutputToken);
            cmd.Parameters.AddWithValue("$crt", row.CacheReadToken);
            cmd.Parameters.AddWithValue("$cmt", row.CacheMissToken);
            cmd.Parameters.AddWithValue("$tt", row.TotalToken);
            cmd.Parameters.AddWithValue("$chp", row.CacheHitPercent.HasValue ? (object)row.CacheHitPercent.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$ua", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageDetailRepository", $"UpsertModelDaily({providerId},{row.Date},{row.ModelName}) failed: {ex.Message}");
        }
    }

    /// <summary>批量写入/覆盖模型×日聚合（一次刷新写入多天多模型，单连接 + 事务复用）。</summary>
    public void UpsertModelDailyBatch(string providerId, string accountId, IReadOnlyList<ModelDailyRow> rows)
    {
        if (rows == null || rows.Count == 0) return;
        try
        {
            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Date) || string.IsNullOrWhiteSpace(row.ModelName)) continue;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT OR REPLACE INTO usage_model_daily
    (provider_id, account_id, date, model_name, input_token, output_token, cache_read_token, cache_miss_token, total_token, cache_hit_percent, updated_at)
    VALUES ($pid, $aid, $date, $mn, $it, $ot, $crt, $cmt, $tt, $chp, $ua);";
                cmd.Parameters.AddWithValue("$pid", providerId);
                cmd.Parameters.AddWithValue("$aid", string.IsNullOrEmpty(accountId) ? "default" : accountId);
                cmd.Parameters.AddWithValue("$date", row.Date);
                cmd.Parameters.AddWithValue("$mn", row.ModelName);
                cmd.Parameters.AddWithValue("$it", row.InputToken);
                cmd.Parameters.AddWithValue("$ot", row.OutputToken);
                cmd.Parameters.AddWithValue("$crt", row.CacheReadToken);
                cmd.Parameters.AddWithValue("$cmt", row.CacheMissToken);
                cmd.Parameters.AddWithValue("$tt", row.TotalToken);
                cmd.Parameters.AddWithValue("$chp", row.CacheHitPercent.HasValue ? (object)row.CacheHitPercent.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("$ua", DateTime.UtcNow);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageDetailRepository", $"UpsertModelDailyBatch({providerId},{rows.Count} rows) failed: {ex.Message}");
        }
    }

    /// <summary>req-088 Phase4：批量写入/覆盖逐请求流水（request_id 去重覆盖）。</summary>
    public void UpsertRequestDetailBatch(string providerId, string accountId, IReadOnlyList<RequestDetailRow> rows)
    {
        if (rows == null || rows.Count == 0) return;
        try
        {
            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.RequestId)) continue;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT OR REPLACE INTO usage_request_detail
    (provider_id, account_id, request_id, occurred_at, channel, model_name, token, used_percent, credits, cost, updated_at)
    VALUES ($pid,$aid,$rid,$occ,$ch,$mn,$tk,$up,$cr,$co,$ua);";
                cmd.Parameters.AddWithValue("$pid", providerId);
                cmd.Parameters.AddWithValue("$aid", string.IsNullOrEmpty(accountId) ? "default" : accountId);
                cmd.Parameters.AddWithValue("$rid", row.RequestId);
                cmd.Parameters.AddWithValue("$occ", row.OccurredAt);
                cmd.Parameters.AddWithValue("$ch", (object?)row.Channel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$mn", (object?)row.ModelName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$tk", row.Token);
                cmd.Parameters.AddWithValue("$up", row.UsedPercent.HasValue ? (object)row.UsedPercent.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("$cr", row.Credits.HasValue ? (object)row.Credits.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("$co", row.Cost.HasValue ? (object)row.Cost.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("$ua", DateTime.UtcNow);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageDetailRepository", $"UpsertRequestDetailBatch({providerId},{rows.Count} rows) failed: {ex.Message}");
        }
    }

    /// <summary>req-088 Phase4：按时间范围读取逐请求流水（occurred_at 升序）。</summary>
    public IReadOnlyList<RequestDetailRow> GetRequestDetail(string providerId, string accountId, string? fromTime = null, string? toTime = null)
    {
        var result = new List<RequestDetailRow>();
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT request_id, occurred_at, channel, model_name, token, used_percent, credits, cost FROM usage_request_detail WHERE provider_id=$pid AND account_id=$aid";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$aid", string.IsNullOrEmpty(accountId) ? "default" : accountId);
            if (!string.IsNullOrEmpty(fromTime)) { cmd.CommandText += " AND occurred_at >= $from"; cmd.Parameters.AddWithValue("$from", fromTime); }
            if (!string.IsNullOrEmpty(toTime)) { cmd.CommandText += " AND occurred_at <= $to"; cmd.Parameters.AddWithValue("$to", toTime); }
            cmd.CommandText += " ORDER BY occurred_at ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new RequestDetailRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7)));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageDetailRepository", $"GetRequestDetail({providerId}) failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>按日期范围读取每日趋势（升序）；from/to 为 null 表示不限。</summary>
    public IReadOnlyList<DailyTrendRow> GetDailyTrend(string providerId, string accountId, string? fromDate = null, string? toDate = null)
    {
        var result = new List<DailyTrendRow>();
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT date, token_total, cache_hit_percent FROM usage_daily_trend WHERE provider_id=$pid AND account_id=$aid";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$aid", string.IsNullOrEmpty(accountId) ? "default" : accountId);
            if (!string.IsNullOrEmpty(fromDate)) { cmd.CommandText += " AND date >= $from"; cmd.Parameters.AddWithValue("$from", fromDate); }
            if (!string.IsNullOrEmpty(toDate)) { cmd.CommandText += " AND date <= $to"; cmd.Parameters.AddWithValue("$to", toDate); }
            cmd.CommandText += " ORDER BY date ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DailyTrendRow(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetDouble(2)));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageDetailRepository", $"GetDailyTrend({providerId}) failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>按日期范围读取模型×日聚合（按日期、模型升序）；from/to 为 null 表示不限。</summary>
    public IReadOnlyList<ModelDailyRow> GetModelDaily(string providerId, string accountId, string? fromDate = null, string? toDate = null)
    {
        var result = new List<ModelDailyRow>();
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT date, model_name, input_token, output_token, cache_read_token, cache_miss_token, total_token, cache_hit_percent FROM usage_model_daily WHERE provider_id=$pid AND account_id=$aid";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$aid", string.IsNullOrEmpty(accountId) ? "default" : accountId);
            if (!string.IsNullOrEmpty(fromDate)) { cmd.CommandText += " AND date >= $from"; cmd.Parameters.AddWithValue("$from", fromDate); }
            if (!string.IsNullOrEmpty(toDate)) { cmd.CommandText += " AND date <= $to"; cmd.Parameters.AddWithValue("$to", toDate); }
            cmd.CommandText += " ORDER BY date ASC, model_name ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ModelDailyRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7)));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageDetailRepository", $"GetModelDaily({providerId}) failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>打开连接（WAL + synchronous=NORMAL，与历史库一致，支持读写并发）。</summary>
    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <inheritdoc />
    public void Dispose() { }
}
