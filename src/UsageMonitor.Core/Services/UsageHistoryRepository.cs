using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using UsageMonitor.Core.Models;

// req-086-3.4：Core 项目内部作为兼容层继续使用旧字段，抑制 CS0618 警告
#pragma warning disable CS0618

namespace UsageMonitor.Core.Services;

/// <summary>
/// 历史数据库单条明细记录（与 usage_points 表字段一一对应）
/// </summary>
public class HistoryPointRecord
{
    /// <summary>主键 id（自增）</summary>
    public long Id { get; set; }

    /// <summary>服务商唯一标识</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>分钟桶键（yyyyMMddHHmm）</summary>
    public string BucketKey { get; set; } = string.Empty;

    /// <summary>采样时间</summary>
    public DateTime RecordedAt { get; set; }

    /// <summary>已用百分比 0-100</summary>
    public double UsedPercent { get; set; }

    /// <summary>已用金额（可选）</summary>
    public double? UsedAmount { get; set; }

    /// <summary>总额度（可选）</summary>
    public double? TotalAmount { get; set; }

    /// <summary>已用 Token（可选）</summary>
    public long? UsedTokens { get; set; }

    /// <summary>总 Token（可选）</summary>
    public long? TotalTokens { get; set; }

    /// <summary>单位</summary>
    public string? Unit { get; set; }

    /// <summary>扩展数据 JSON 文本</summary>
    public string? ExtraJson { get; set; }
}

/// <summary>
/// 日聚合数据模型（与 usage_daily 表字段一一对应）
/// </summary>
public class DailyAggregate
{
    /// <summary>服务商唯一标识</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>日期 yyyy-MM-dd</summary>
    public string Day { get; set; } = string.Empty;

    /// <summary>当日最高已用百分比</summary>
    public double MaxUsedPercent { get; set; }

    /// <summary>当日最低已用百分比</summary>
    public double MinUsedPercent { get; set; }

    /// <summary>当日最后采样百分比</summary>
    public double EndUsedPercent { get; set; }

    /// <summary>当日平均百分比</summary>
    public double AvgUsedPercent { get; set; }

    /// <summary>当日采样次数</summary>
    public int SnapshotCount { get; set; }

    /// <summary>最后聚合更新时间</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 用量历史数据持久化仓库（Microsoft.Data.Sqlite + SQLite）
/// <para>
/// 数据库文件位于 %AppData%/UsageMonitor/history.db，schema 由 EnsureSchema 幂等创建。
/// </para>
/// <para>
/// 写策略：
/// <list type="bullet">
/// <item><description>折线图原始点：分钟桶 + UNIQUE INDEX + INSERT OR REPLACE，去重刷新周期内的重复记录</description></item>
/// <item><description>热力图日聚合：(provider_id, day) 主键 + INSERT OR REPLACE，每次刷新更新到当前</description></item>
/// </list>
/// </para>
/// <para>
/// 所有 I/O 操作均 try/catch + FileLogger.Error，永不向上抛。
/// </para>
/// </summary>
public class UsageHistoryRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly string _dbFilePath;

    /// <summary>最近一次读写操作的错误信息（null 表示正常），供上层感知 DB 健康。</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 创建仓库实例。dbFilePath 通常传 %AppData%/UsageMonitor/history.db
    /// </summary>
    public UsageHistoryRepository(string dbFilePath)
    {
        _dbFilePath = dbFilePath;
        var directory = Path.GetDirectoryName(dbFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 10
        }.ToString();
    }

    /// <summary>
    /// 创建使用默认配置路径的仓库（%AppData%/UsageMonitor/history.db）
    /// </summary>
    public static UsageHistoryRepository CreateDefault()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsageMonitor");
        return new UsageHistoryRepository(Path.Combine(dir, "history.db"));
    }

    /// <summary>
    /// 幂等创建表与索引。若 DB 损坏则备份并重建。
    /// </summary>
    public void EnsureSchema()
    {
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS usage_points (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id     TEXT NOT NULL,
    bucket_key      TEXT NOT NULL,
    recorded_at     DATETIME NOT NULL,
    used_percent    REAL NOT NULL,
    used_amount     REAL,
    total_amount    REAL,
    used_tokens     INTEGER,
    total_tokens    INTEGER,
    unit            TEXT,
    extra_json      TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_usage_points_dedup
    ON usage_points(provider_id, bucket_key);
CREATE INDEX IF NOT EXISTS idx_usage_points_lookup
    ON usage_points(provider_id, recorded_at);

CREATE TABLE IF NOT EXISTS usage_daily (
    provider_id         TEXT NOT NULL,
    day                 TEXT NOT NULL,
    max_used_percent    REAL NOT NULL,
    min_used_percent    REAL NOT NULL,
    end_used_percent    REAL NOT NULL,
    avg_used_percent    REAL NOT NULL,
    snapshot_count      INTEGER NOT NULL,
    updated_at          DATETIME NOT NULL,
    PRIMARY KEY(provider_id, day)
);

-- req-013: 刷新聚合表。主窗口每次刷新（手动 + 定时）写入一行；
-- 供历史窗口 DataGrid 展示“每次刷新”级别的明细。
CREATE TABLE IF NOT EXISTS usage_refresh_aggregates (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id         TEXT NOT NULL,
    refresh_at          TEXT NOT NULL,    -- yyyy-MM-dd HH:mm:ss
    business_day        TEXT NOT NULL,    -- yyyy-MM-dd
    max_used_percent    REAL NOT NULL,
    min_used_percent    REAL NOT NULL,
    end_used_percent    REAL NOT NULL,
    avg_used_percent    REAL NOT NULL,
    snapshot_count      INTEGER NOT NULL,
    trigger_kind        TEXT NOT NULL     -- 'manual' / 'auto'
);
CREATE INDEX IF NOT EXISTS idx_ura_provider_day
    ON usage_refresh_aggregates(provider_id, refresh_at DESC);

-- req-092: 字段版本表，记录每个字段的最新值（字段级差异持久化）
CREATE TABLE IF NOT EXISTS usage_field_versions (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id     TEXT NOT NULL,
    field_name      TEXT NOT NULL,
    field_value     TEXT,           -- JSON 序列化后的值
    value_type      TEXT NOT NULL,  -- string/number/bool/datetime/json
    updated_at      DATETIME NOT NULL,
    UNIQUE(provider_id, field_name)
);
CREATE INDEX IF NOT EXISTS idx_ufv_provider
    ON usage_field_versions(provider_id, updated_at DESC);

-- req-092: 字段变更历史表（可选，用于审计）
CREATE TABLE IF NOT EXISTS usage_field_history (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id     TEXT NOT NULL,
    field_name      TEXT NOT NULL,
    old_value       TEXT,
    new_value       TEXT,
    changed_at      DATETIME NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_ufh_provider
    ON usage_field_history(provider_id, changed_at DESC);
";
            cmd.ExecuteNonQuery();
            FileLogger.Info("UsageHistoryRepository", "EnsureSchema ok");
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository", "EnsureSchema failed", ex);
            TryRecoverFromCorruptedDb(ex);
        }
    }

    /// <summary>
    /// 打开一个新的 SqliteConnection，同步 / async 操作都能复用。
    /// 会启用 WAL + synchronous=NORMAL，让刷新线程写库与 UI 读可并发执行。
    /// </summary>
    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <summary>
    /// req-069 F-16：直接写入百分比值的历史点（不经过 UsageInfo 反推）。
    /// 与 <see cref="UpsertPoint(UsageInfo)"/> 的区别：本方法直接接受 usagePercent，
    /// 避免调用方为了写库而构造 dummy UsageInfo（UsedAmount=percent, TotalAmount=100）。
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="usagePercent">已用百分比（0-100）</param>
    public void UpsertPoint(string providerId, double usagePercent)
    {
        if (string.IsNullOrEmpty(providerId)) return;

        try
        {
            var percent = Math.Max(0, Math.Min(100, usagePercent));
            var recordedAt = DateTime.Now;
            var bucketKey = recordedAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            var day = recordedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();

            // 1) 写原始点（同分钟内 INSERT OR REPLACE）
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT OR REPLACE INTO usage_points
    (provider_id, bucket_key, recorded_at, used_percent, used_amount, total_amount,
     used_tokens, total_tokens, unit, extra_json)
VALUES
    ($pid, $bk, $rec, $up, NULL, NULL, NULL, NULL, NULL, NULL);
";
                cmd.Parameters.AddWithValue("$pid", providerId);
                cmd.Parameters.AddWithValue("$bk", bucketKey);
                cmd.Parameters.AddWithValue("$rec", recordedAt);
                cmd.Parameters.AddWithValue("$up", percent);
                cmd.ExecuteNonQuery();
            }

            // 2) 从 usage_points 重新聚合当天那行
            UpsertDailyInternal(conn, tx, providerId, day);

            tx.Commit();
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            FileLogger.Error("UsageHistoryRepository",
                $"UpsertPoint({providerId}, {usagePercent}) failed", ex);
            if (ex is SqliteException)
                TryRecoverFromCorruptedDb(ex);
        }
    }

    /// <summary>
    /// 写入一条原始点，并在同一事务内重算当天聚合。
    /// 失败仅日志，不向上抛。
    /// </summary>
    public void UpsertPoint(UsageInfo usage)
    {
        if (usage == null || string.IsNullOrEmpty(usage.ProviderId)) return;
        if (!usage.IsSuccess) return; // 仅成功样本入库

        try
        {
            var percent = Math.Max(0, Math.Min(100, usage.GetUsagePercentage()));
            var recordedAt = usage.LastUpdated == default ? DateTime.Now : usage.LastUpdated;
            var bucketKey = recordedAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            var day = recordedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var extraJson = usage.Extra != null && usage.Extra.Count > 0
                ? JsonSerializer.Serialize(usage.Extra)
                : null;

            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();

            // 1) 写原始点（同分钟内 INSERT OR REPLACE）
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT OR REPLACE INTO usage_points
    (provider_id, bucket_key, recorded_at, used_percent, used_amount, total_amount,
     used_tokens, total_tokens, unit, extra_json)
VALUES
    ($pid, $bk, $rec, $up, $ua, $ta, $utk, $ttk, $unit, $extra);
";
                cmd.Parameters.AddWithValue("$pid", usage.ProviderId);
                cmd.Parameters.AddWithValue("$bk", bucketKey);
                cmd.Parameters.AddWithValue("$rec", recordedAt);
                cmd.Parameters.AddWithValue("$up", percent);
                cmd.Parameters.AddWithValue("$ua", (object?)usage.UsedAmount ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ta", (object?)usage.TotalAmount ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$utk", (object?)usage.UsedTokens ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ttk", (object?)usage.TotalTokens ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$unit", (object?)usage.Unit ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$extra", (object?)extraJson ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // 2) 从 usage_points 重新聚合当天那行
            UpsertDailyInternal(conn, tx, usage.ProviderId, day);

            tx.Commit();
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            FileLogger.Error("UsageHistoryRepository",
                $"UpsertPoint({usage.ProviderId}) failed", ex);
            // 疑似数据库损坏时备份并重建，下次 EnsureSchema 生效。
            if (ex is SqliteException)
                TryRecoverFromCorruptedDb(ex);
        }
    }

    /// <summary>
    /// 在给定事务内对指定 (provider, day) 全表重算并 INSERT OR REPLACE。
    /// 公开给测试 / 数据库修复使用；正常路径由 UpsertPoint 调用。
    /// <para>
    /// SQL 注意：<c>INSERT ... SELECT</c> 必须明确 <c>FROM usage_points</c> 子句，否则 SQLite
    /// 会报 <c>no such column: used_percent</c>。当天 0 行时 FROM WHERE 自然返回空，INSERT 不执行。
    /// </para>
    /// </summary>
    private void UpsertDailyInternal(SqliteConnection conn, SqliteTransaction tx, string providerId, string day)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT OR REPLACE INTO usage_daily
    (provider_id, day, max_used_percent, min_used_percent, end_used_percent,
     avg_used_percent, snapshot_count, updated_at)
SELECT
    $pid,
    $day,
    COALESCE(MAX(used_percent), 0),
    COALESCE(MIN(used_percent), 0),
    COALESCE((SELECT used_percent FROM usage_points
              WHERE provider_id = $pid AND substr(bucket_key, 1, 8) = $day8
              ORDER BY recorded_at DESC LIMIT 1), 0),
    COALESCE(AVG(used_percent), 0),
    COALESCE(COUNT(*), 0),
    $now
FROM usage_points
WHERE provider_id = $pid AND substr(bucket_key, 1, 8) = $day8;
";
        cmd.Parameters.AddWithValue("$pid", providerId);
        cmd.Parameters.AddWithValue("$day", day);
        cmd.Parameters.AddWithValue("$day8", day.Replace("-", ""));
        cmd.Parameters.AddWithValue("$now", DateTime.Now);
        cmd.ExecuteNonQuery();

        // 如果当天已无明细（比如很久之前同步失败），则把 daily 那行删掉，避免长期残留脏数据
        using (var cleanup = conn.CreateCommand())
        {
            cleanup.Transaction = tx;
            cleanup.CommandText = @"
DELETE FROM usage_daily
WHERE provider_id = $pid AND day = $day
  AND NOT EXISTS (SELECT 1 FROM usage_points WHERE provider_id = $pid AND substr(bucket_key, 1, 8) = $day8);
";
            cleanup.Parameters.AddWithValue("$pid", providerId);
            cleanup.Parameters.AddWithValue("$day", day);
            cleanup.Parameters.AddWithValue("$day8", day.Replace("-", ""));
            cleanup.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 异步包装。失败仅日志。
    /// </summary>
    public Task UpsertPointAsync(UsageInfo usage)
    {
        return Task.Run(() => UpsertPoint(usage));
    }

    /// <summary>
    /// req-013：插入一条刷新聚合记录。主窗口每次刷新（手动 / 定时）调用。
    /// 失败仅日志，不向上抛。
    /// </summary>
    public async Task InsertRefreshAggregateAsync(RefreshAggregate agg)
    {
        if (agg == null || string.IsNullOrEmpty(agg.ProviderId)) return;
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO usage_refresh_aggregates
(provider_id, refresh_at, business_day, max_used_percent, min_used_percent,
 end_used_percent, avg_used_percent, snapshot_count, trigger_kind)
VALUES ($pid, $rat, $bd, $mx, $mn, $en, $av, $cnt, $tk);";
            cmd.Parameters.AddWithValue("$pid", agg.ProviderId);
            cmd.Parameters.AddWithValue("$rat", agg.RefreshAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$bd", agg.BusinessDay);
            cmd.Parameters.AddWithValue("$mx", agg.MaxUsedPercent);
            cmd.Parameters.AddWithValue("$mn", agg.MinUsedPercent);
            cmd.Parameters.AddWithValue("$en", agg.EndUsedPercent);
            cmd.Parameters.AddWithValue("$av", agg.AvgUsedPercent);
            cmd.Parameters.AddWithValue("$cnt", agg.SnapshotCount);
            cmd.Parameters.AddWithValue("$tk", agg.TriggerKind);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"InsertRefreshAggregateAsync({agg.ProviderId}) failed", ex);
        }
    }

    /// <summary>
    /// req-013：查询指定 provider 列表的刷新聚合记录，默认按 refresh_at DESC 排序。
    /// from / to 可选，限定 refresh_at 范围。
    /// </summary>
    public async Task<List<RefreshAggregate>> QueryRefreshAggregatesAsync(
        IEnumerable<string> providerIds, DateTime? from = null, DateTime? to = null)
    {
        var list = new List<RefreshAggregate>();
        var ids = providerIds?.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList()
                  ?? new List<string>();
        if (ids.Count == 0) return list;
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            var placeholders = string.Join(",", ids.Select((_, i) => $"$p{i}"));
            cmd.CommandText = $@"
SELECT id, provider_id, refresh_at, business_day,
       max_used_percent, min_used_percent, end_used_percent,
       avg_used_percent, snapshot_count, trigger_kind
FROM usage_refresh_aggregates
WHERE provider_id IN ({placeholders})
  {(from.HasValue ? "AND refresh_at >= $from" : "")}
  {(to.HasValue ? "AND refresh_at <= $to" : "")}
ORDER BY refresh_at DESC, provider_id ASC;";
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue($"$p{i}", ids[i]);
            if (from.HasValue)
                cmd.Parameters.AddWithValue("$from", from.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            if (to.HasValue)
                cmd.Parameters.AddWithValue("$to", to.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new RefreshAggregate(
                    Id: reader.GetInt64(0),
                    ProviderId: reader.GetString(1),
                    RefreshAt: DateTime.ParseExact(reader.GetString(2), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    BusinessDay: reader.GetString(3),
                    MaxUsedPercent: reader.GetDouble(4),
                    MinUsedPercent: reader.GetDouble(5),
                    EndUsedPercent: reader.GetDouble(6),
                    AvgUsedPercent: reader.GetDouble(7),
                    SnapshotCount: reader.GetInt32(8),
                    TriggerKind: reader.GetString(9)));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"QueryRefreshAggregatesAsync failed", ex);
        }
        return list;
    }

    /// <summary>
    /// req-021：一次性清理历史错误数据（token=0 的所有采样点）。
    /// <para>
    /// 背景：早期版本 MiniMax DOM 抓取会偶尔产出 token=0 的点，这些点对应"无用量但被错误着色"
    /// 的热力图格子。本清理逻辑：扫描 <c>usage_points</c> 中 <c>used_tokens=0</c> 的所有记录，删除之。
    /// </para>
    /// <para>
    /// 删除而非标记的原因：<c>usage_points</c> 不存 background 字段，标记需要新增列（schema 变更）。
    /// 而 0 token 在折线图 / 热力图 / 统计上都无意义，保留反而占空间。
    /// </para>
    /// </summary>
    /// <returns>删除的记录条数</returns>
    public async Task<int> CleanHistoricalZeroTokenDataAsync()
    {
        int cleaned = 0;
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            // req-021：限定 used_tokens=0（避免误删其他 Provider 的 token=0 应保留数据）
            // 为安全起见，仅当 ProviderId 是 MiniMax 时清理（其他 Provider 0 token 可能合法）。
            cmd.CommandText = "DELETE FROM usage_points WHERE used_tokens = 0 AND provider_id = 'MiniMax';";
            cleaned = await cmd.ExecuteNonQueryAsync();
            if (cleaned > 0)
            {
                FileLogger.Info("UsageHistoryRepository",
                    $"CleanHistoricalZeroTokenDataAsync: deleted {cleaned} MiniMax rows with used_tokens=0");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageHistoryRepository",
                "CleanHistoricalZeroTokenDataAsync failed (will not block startup)", ex);
        }
        return cleaned;
    }

    /// <summary>
    /// req-021：清理后的每日聚合重算。逐 Provider × Day 重新计算 usage_daily，保证热力图色阶正确。
    /// <para>
    /// 适用场景：清理完 token=0 后，原 usage_daily 表里的 avg_used_percent / end_used_percent 仍
    /// 可能受 0 token 影响。本方法对所有受影响的 (provider, day) 行触发重算。
    /// </para>
    /// </summary>
    public async Task RecomputeDailyAggregatesAsync()
    {
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            // 查找所有受影响的 day
            cmd.CommandText = @"
SELECT DISTINCT substr(bucket_key, 1, 8), provider_id
FROM usage_points
ORDER BY provider_id;";
            await using var reader = await cmd.ExecuteReaderAsync();
            var targets = new List<(string providerId, string day8)>();
            while (await reader.ReadAsync())
            {
                targets.Add((reader.GetString(1), reader.GetString(0)));
            }
            await reader.CloseAsync();

            // 对每对 (provider, day) 重算 usage_daily
            foreach (var (pid, day8) in targets)
            {
                var day = $"{day8.Substring(0, 4)}-{day8.Substring(4, 2)}-{day8.Substring(6, 2)}";
                await using var tx = conn.BeginTransaction();
                UpsertDailyInternal(conn, tx, pid, day);
                tx.Commit();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageHistoryRepository",
                "RecomputeDailyAggregatesAsync failed", ex);
        }
    }

    /// <summary>
    /// req-015：按需写入采样点。与上次同 Provider 同日采样点比对，业务指纹一致则跳过（仅日志）。
    /// <para>
    /// 指纹字段：<c>used_percent</c> + Provider 关键 extras（MiniMax：<c>mm_5hUsedPercent</c> /
    /// <c>mm_weeklyUsedPercent</c> / <c>mm_remainingCredits</c> / <c>mm_subscriptionTitle</c> /
    /// <c>mm_subscriptionActive</c>；其他：<c>used_tokens</c> / <c>total_tokens</c>）。
    /// </para>
    /// <para>
    /// 异常时（fingerprint 比较失败）落入“决策 5B”：回退为写入 + FileLogger.Warn。不丢数据。
    /// </para>
    /// </summary>
    /// <param name="usage">本次采样点（含 extras）</param>
    /// <returns>true 表示已写入；false 表示指纹一致已跳过</returns>
    public async Task<bool> InsertUsagePointIfChangedAsync(UsageInfo usage)
    {
        if (usage == null || string.IsNullOrEmpty(usage.ProviderId)) return false;
        try
        {
            var percent = Math.Max(0, Math.Min(100, usage.GetUsagePercentage()));
            var recordedAt = usage.LastUpdated == default ? DateTime.Now : usage.LastUpdated;
            var day = recordedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var last = await GetLastUsagePointForDayAsync(usage.ProviderId, day);
            if (last != null)
            {
                var lastUsage = ToUsageInfo(last);
                if (BuildBusinessFingerprint(usage) == BuildBusinessFingerprint(lastUsage))
                {
                    FileLogger.Info("UsageHistoryRepository",
                        $"UsagePoint skipped (no change): provider={usage.ProviderId} day={day} recordedAt={recordedAt:o}");
                    return false;
                }
            }
            UpsertPoint(usage);
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("UsageHistoryRepository",
                $"InsertUsagePointIfChangedAsync({usage.ProviderId}) fingerprint compare failed, falling back to write", ex);
            try { UpsertPoint(usage); } catch { /* swallow to not break refresh */ }
            return true;
        }
    }

    /// <summary>
    /// req-015：查询指定 Provider 当日最近一条采样点。
    /// </summary>
    public async Task<HistoryPointRecord?> GetLastUsagePointForDayAsync(string providerId, string day)
    {
        if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(day)) return null;
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, provider_id, bucket_key, recorded_at, used_percent,
       used_amount, total_amount, used_tokens, total_tokens, unit, extra_json
FROM usage_points
WHERE provider_id = $pid AND substr(bucket_key, 1, 8) = $day8
ORDER BY recorded_at DESC
LIMIT 1;";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$day8", day.Replace("-", ""));
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return ReadPointRecord(reader);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"GetLastUsagePointForDayAsync({providerId}, {day}) failed", ex);
        }
        return null;
    }

    /// <summary>
    /// req-015：构造本次采样的“业务指纹”——同值指纹一致才跳过写入。
    /// <para>
    /// 指纹字段：
    /// <list type="bullet">
    /// <item><description><c>used_percent</c>（决策 2C 顶层字段）</description></item>
    /// <item><description>MiniMax：<c>mm_5hUsedPercent</c> / <c>mm_weeklyUsedPercent</c> / <c>mm_remainingCredits</c> / <c>mm_subscriptionTitle</c> / <c>mm_subscriptionActive</c></description></item>
    /// <item><description>其他 Provider：<c>used_tokens</c> / <c>total_tokens</c></description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static string BuildBusinessFingerprint(UsageInfo usage)
    {
        if (usage == null) return string.Empty;
        var fields = new List<string>
        {
            $"percent={usage.GetUsagePercentage():0.####}"
        };
        if (usage.Extra != null)
        {
            if (string.Equals(usage.ProviderId, "MiniMax", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add($"5h={TryGetDouble(usage.Extra, "mm_5hUsedPercent")}");
                fields.Add($"week={TryGetDouble(usage.Extra, "mm_weeklyUsedPercent")}");
                fields.Add($"credits={TryGetDouble(usage.Extra, "mm_remainingCredits")}");
                fields.Add($"subTitle={TryGetString(usage.Extra, "mm_subscriptionTitle")}");
                fields.Add($"subActive={TryGetBool(usage.Extra, "mm_subscriptionActive")}");
            }
            else
            {
                fields.Add($"usedTokens={TryGetLong(usage.Extra, "used_tokens")}");
                fields.Add($"totalTokens={TryGetLong(usage.Extra, "total_tokens")}");
            }
        }
        return string.Join("|", fields);
    }

    /// <summary>req-070 F-30：从 extras 字典安全提取 double，补充 JsonElement 分支。</summary>
    private static double TryGetDouble(IReadOnlyDictionary<string, object> extras, string key)
    {
        if (extras.TryGetValue(key, out var v) && v != null)
        {
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is decimal m) return (double)m;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
                return je.GetDouble();
            if (double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return 0;
    }

    /// <summary>req-070 F-30：从 extras 字典安全提取 string，补充 JsonElement 分支。</summary>
    private static string TryGetString(IReadOnlyDictionary<string, object> extras, string key)
    {
        if (extras.TryGetValue(key, out var v) && v != null)
        {
            if (v is string s) return s;
            if (v is JsonElement je)
                return je.ValueKind == JsonValueKind.String ? je.GetString() ?? string.Empty : je.ToString();
            return v.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>req-070 F-30：从 extras 字典安全提取 long，补充 JsonElement 分支。</summary>
    private static long TryGetLong(IReadOnlyDictionary<string, object> extras, string key)
    {
        if (extras.TryGetValue(key, out var v) && v != null)
        {
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is double d) return (long)d;
            if (v is decimal m) return (long)m;
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
                return je.GetInt64();
            if (long.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return 0;
    }

    /// <summary>req-070 F-30：从 extras 字典安全提取 bool，补充 JsonElement 分支。</summary>
    private static bool TryGetBool(IReadOnlyDictionary<string, object> extras, string key)
    {
        if (extras.TryGetValue(key, out var v) && v != null)
        {
            if (v is bool b) return b;
            if (v is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.True) return true;
                if (je.ValueKind == JsonValueKind.False) return false;
            }
            if (bool.TryParse(v.ToString(), out var p)) return p;
        }
        return false;
    }

    /// <summary>
    /// req-015：把持久化的 <see cref="HistoryPointRecord"/> 还原为 <see cref="UsageInfo"/> 用于指纹比对。
    /// <para>
    /// 注意：只填充指纹计算必需的字段，其他字段允许为默认。extras 从 <c>extra_json</c> 反序列化。
    /// </para>
    /// </summary>
    private static UsageInfo ToUsageInfo(HistoryPointRecord r)
    {
        var info = new UsageInfo
        {
            ProviderId = r.ProviderId,
            ProviderName = r.ProviderId,
            UsedAmount = (decimal)(r.UsedAmount ?? r.UsedPercent),
            TotalAmount = (decimal)(r.TotalAmount ?? 100),
            UsedTokens = r.UsedTokens ?? 0,
            TotalTokens = r.TotalTokens ?? 0,
            Unit = r.Unit ?? string.Empty,
            LastUpdated = r.RecordedAt,
            IsSuccess = true
        };
        if (!string.IsNullOrEmpty(r.ExtraJson))
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(r.ExtraJson);
                if (dict != null)
                {
                    foreach (var kv in dict) info.Extra[kv.Key] = kv.Value;
                }
            }
            catch { /* extras 解析失败不阻塞；指纹退化为 percent-only */ }
        }
        return info;
    }

    /// <summary>
    /// 异步加载指定 provider 最近 N 条明细（按 recorded_at 升序）。
    /// 用于启动时回填 UsageHistoryStore 内存。
    /// </summary>
    public async Task<List<HistoryPointRecord>> LoadLatestPointsAsync(string providerId, int count)
    {
        var list = new List<HistoryPointRecord>();
        if (string.IsNullOrEmpty(providerId) || count <= 0) return list;
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, provider_id, bucket_key, recorded_at, used_percent,
       used_amount, total_amount, used_tokens, total_tokens, unit, extra_json
FROM (
    SELECT *
    FROM usage_points
    WHERE provider_id = $pid
    ORDER BY recorded_at DESC
    LIMIT $cnt
)
ORDER BY recorded_at ASC;
";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$cnt", count);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(ReadPointRecord(reader));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"LoadLatestPointsAsync({providerId}, {count}) failed", ex);
        }
        return list;
    }

    /// <summary>
    /// 异步查询时间范围内某 provider 的明细，返回按时间升序。
    /// </summary>
    public async Task<List<HistoryPointRecord>> QueryPointsAsync(
        string providerId, DateTime from, DateTime to)
    {
        var list = new List<HistoryPointRecord>();
        if (string.IsNullOrEmpty(providerId)) return list;
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, provider_id, bucket_key, recorded_at, used_percent,
       used_amount, total_amount, used_tokens, total_tokens, unit, extra_json
FROM usage_points
WHERE provider_id = $pid AND recorded_at >= $from AND recorded_at <= $to
ORDER BY recorded_at ASC;
";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$from", from);
            cmd.Parameters.AddWithValue("$to", to);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(ReadPointRecord(reader));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"QueryPointsAsync({providerId}) failed", ex);
        }
        return list;
    }

    /// <summary>
    /// 异步查询多个 provider 在 [from, to] 范围内的日聚合。
    /// </summary>
    public async Task<List<DailyAggregate>> QueryDailyAsync(
        IEnumerable<string> providerIds, DateTime from, DateTime to)
    {
        var list = new List<DailyAggregate>();
        var ids = providerIds?.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList()
                  ?? new List<string>();
        if (ids.Count == 0) return list;
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            // 拼成 (id1, id2, ...) 形式
            var placeholders = string.Join(",", ids.Select((_, i) => $"$p{i}"));
            cmd.CommandText = $@"
SELECT provider_id, day, max_used_percent, min_used_percent, end_used_percent,
       avg_used_percent, snapshot_count, updated_at
FROM usage_daily
WHERE provider_id IN ({placeholders}) AND day >= $from AND day <= $to
ORDER BY day ASC, provider_id ASC;
";
            for (int i = 0; i < ids.Count; i++)
            {
                cmd.Parameters.AddWithValue($"$p{i}", ids[i]);
            }
            cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new DailyAggregate
                {
                    ProviderId = reader.GetString(0),
                    Day = reader.GetString(1),
                    MaxUsedPercent = reader.GetDouble(2),
                    MinUsedPercent = reader.GetDouble(3),
                    EndUsedPercent = reader.GetDouble(4),
                    AvgUsedPercent = reader.GetDouble(5),
                    SnapshotCount = reader.GetInt32(6),
                    UpdatedAt = reader.GetDateTime(7)
                });
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"QueryDailyAsync failed", ex);
        }
        return list;
    }

    /// <summary>
    /// 异步列出 DB 中出现过的所有 ProviderId（用于历史窗口显示"已卸载 Provider"）。
    /// </summary>
    public async Task<List<string>> GetKnownProviderIdsAsync()
    {
        var ids = new HashSet<string>();
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT provider_id FROM usage_points;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetString(0));
            }
            await using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT DISTINCT provider_id FROM usage_daily;";
            await using var reader2 = await cmd2.ExecuteReaderAsync();
            while (await reader2.ReadAsync())
            {
                ids.Add(reader2.GetString(0));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                "GetKnownProviderIdsAsync failed", ex);
        }
        return ids.ToList();
    }

    /// <summary>
    /// req-012：删除指定 Provider 的所有历史数据（usage_points + usage_daily 两张表）。
    /// <para>
    /// 启动时检测到 Provider 卸载且用户选"删除"时调用。两张表都用 <c>provider_id</c> 索引，
    /// 删除走索引扫描（百毫秒级），无须清空整个库。
    /// </para>
    /// <para>
    /// 失败仅日志（不向上抛），与仓库其他写入方法保持一致。
    /// </para>
    /// </summary>
    /// <param name="providerId">要删除数据的 Provider ID（如 "deepseek"）</param>
    public async Task DeleteProviderDataAsync(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;
        try
        {
            await using var conn = OpenConnection();
            // req-070 F-31：移除不必要的 Task.Run 包裹，直接在 async 方法中执行事务
            await using var tx = conn.BeginTransaction();

            int n1, n2, n3;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM usage_points WHERE provider_id = $id";
                cmd.Parameters.AddWithValue("$id", providerId);
                n1 = await cmd.ExecuteNonQueryAsync();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM usage_daily WHERE provider_id = $id";
                cmd.Parameters.AddWithValue("$id", providerId);
                n2 = await cmd.ExecuteNonQueryAsync();
            }
            // req-013: 同时清理刷新聚合表
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM usage_refresh_aggregates WHERE provider_id = $id";
                cmd.Parameters.AddWithValue("$id", providerId);
                n3 = await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            FileLogger.Info("UsageHistoryRepository",
                $"DeleteProviderDataAsync({providerId}): usage_points={n1} 行, usage_daily={n2} 行, usage_refresh_aggregates={n3} 行");
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"DeleteProviderDataAsync({providerId}) failed", ex);
        }
    }

    /// <summary>
    /// 异步返回指定 provider 某个日期的日聚合。失败时返回 null。
    /// </summary>
    public async Task<DailyAggregate?> GetDailyAsync(string providerId, string day)
    {
        if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(day)) return null;
        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT provider_id, day, max_used_percent, min_used_percent, end_used_percent,
       avg_used_percent, snapshot_count, updated_at
FROM usage_daily
WHERE provider_id = $pid AND day = $day LIMIT 1;
";
            cmd.Parameters.AddWithValue("$pid", providerId);
            cmd.Parameters.AddWithValue("$day", day);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new DailyAggregate
                {
                    ProviderId = reader.GetString(0),
                    Day = reader.GetString(1),
                    MaxUsedPercent = reader.GetDouble(2),
                    MinUsedPercent = reader.GetDouble(3),
                    EndUsedPercent = reader.GetDouble(4),
                    AvgUsedPercent = reader.GetDouble(5),
                    SnapshotCount = reader.GetInt32(6),
                    UpdatedAt = reader.GetDateTime(7)
                };
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"GetDailyAsync({providerId}, {day}) failed", ex);
        }
        return null;
    }

    /// <summary>
    /// 用于把当前内存中 UsageHistoryStore 的快照回填时构造 HistoryPoint。
    /// </summary>
    public static UsageMonitor.Core.Services.HistoryPoint ToInMemoryPoint(HistoryPointRecord r)
    {
        return new UsageMonitor.Core.Services.HistoryPoint
        {
            UsagePercent = r.UsedPercent,
            Timestamp = r.RecordedAt
        };
    }

    private static HistoryPointRecord ReadPointRecord(SqliteDataReader reader)
    {
        return new HistoryPointRecord
        {
            Id = reader.GetInt64(0),
            ProviderId = reader.GetString(1),
            BucketKey = reader.GetString(2),
            RecordedAt = reader.GetDateTime(3),
            UsedPercent = reader.GetDouble(4),
            UsedAmount = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            TotalAmount = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            UsedTokens = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            TotalTokens = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            Unit = reader.IsDBNull(9) ? null : reader.GetString(9),
            ExtraJson = reader.IsDBNull(10) ? null : reader.GetString(10)
        };
    }

    /// <summary>
    /// req-092：增量保存字段变更。仅保存有变化的字段，避免重复存储。
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="changes">字段变更列表</param>
    /// <returns>保存的字段数量</returns>
    public async Task<int> SaveIncrementalAsync(string providerId, FieldChange[] changes)
    {
        if (string.IsNullOrEmpty(providerId) || changes == null || changes.Length == 0)
            return 0;

        int savedCount = 0;
        try
        {
            await using var conn = OpenConnection();
            await using var tx = conn.BeginTransaction();

            var now = DateTime.Now;

            foreach (var change in changes)
            {
                // 1) 更新字段版本表（INSERT OR REPLACE）
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT OR REPLACE INTO usage_field_versions
    (provider_id, field_name, field_value, value_type, updated_at)
VALUES
    ($pid, $fname, $fvalue, $vtype, $now);
";
                    cmd.Parameters.AddWithValue("$pid", providerId);
                    cmd.Parameters.AddWithValue("$fname", change.FieldName);
                    cmd.Parameters.AddWithValue("$fvalue", (object?)change.NewValue ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$vtype", change.ValueType);
                    cmd.Parameters.AddWithValue("$now", now);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 2) 记录字段变更历史（用于审计）
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT INTO usage_field_history
    (provider_id, field_name, old_value, new_value, changed_at)
VALUES
    ($pid, $fname, $oldval, $newval, $now);
";
                    cmd.Parameters.AddWithValue("$pid", providerId);
                    cmd.Parameters.AddWithValue("$fname", change.FieldName);
                    cmd.Parameters.AddWithValue("$oldval", (object?)change.OldValue ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$newval", (object?)change.NewValue ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$now", now);
                    await cmd.ExecuteNonQueryAsync();
                }

                savedCount++;
            }

            await tx.CommitAsync();
            FileLogger.Info("UsageHistoryRepository",
                $"SaveIncrementalAsync({providerId}): saved {savedCount} field changes");
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"SaveIncrementalAsync({providerId}) failed", ex);
        }

        return savedCount;
    }

    /// <summary>
    /// req-092：查询指定 Provider 的所有字段最新值。
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <returns>字段名到最新值的字典</returns>
    public async Task<Dictionary<string, object>> GetLatestFieldsAsync(string providerId)
    {
        var fields = new Dictionary<string, object>();
        if (string.IsNullOrEmpty(providerId)) return fields;

        try
        {
            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT field_name, field_value, value_type
FROM usage_field_versions
WHERE provider_id = $pid
ORDER BY updated_at DESC;
";
            cmd.Parameters.AddWithValue("$pid", providerId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fieldName = reader.GetString(0);
                var fieldValue = reader.IsDBNull(1) ? null : reader.GetString(1);
                var valueType = reader.GetString(2);

                // 根据 value_type 反序列化字段值
                object? value = fieldValue == null ? null : DeserializeFieldValue(fieldValue, valueType);
                if (value != null)
                {
                    fields[fieldName] = value;
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"GetLatestFieldsAsync({providerId}) failed", ex);
        }

        return fields;
    }

    /// <summary>
    /// req-092：根据 value_type 反序列化字段值。
    /// </summary>
    private static object? DeserializeFieldValue(string fieldValue, string valueType)
    {
        try
        {
            return valueType switch
            {
                "string" => JsonSerializer.Deserialize<string>(fieldValue),
                "number" => JsonSerializer.Deserialize<double>(fieldValue),
                "bool" => JsonSerializer.Deserialize<bool>(fieldValue),
                "datetime" => JsonSerializer.Deserialize<DateTime>(fieldValue),
                "json" => JsonSerializer.Deserialize<Dictionary<string, object>>(fieldValue),
                _ => fieldValue
            };
        }
        catch
        {
            return fieldValue;
        }
    }

    /// <summary>
    /// req-092：删除指定 Provider 的所有字段版本数据。
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    public async Task DeleteProviderFieldVersionsAsync(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;
        try
        {
            await using var conn = OpenConnection();
            await using var tx = conn.BeginTransaction();

            int n1, n2;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM usage_field_versions WHERE provider_id = $id";
                cmd.Parameters.AddWithValue("$id", providerId);
                n1 = await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM usage_field_history WHERE provider_id = $id";
                cmd.Parameters.AddWithValue("$id", providerId);
                n2 = await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            FileLogger.Info("UsageHistoryRepository",
                $"DeleteProviderFieldVersionsAsync({providerId}): usage_field_versions={n1} 行, usage_field_history={n2} 行");
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                $"DeleteProviderFieldVersionsAsync({providerId}) failed", ex);
        }
    }

    /// <summary>
    /// DB 文件损坏时，把异常文件重命名备份，让下次 EnsureSchema 重建。
    /// </summary>
    private void TryRecoverFromCorruptedDb(Exception cause)
    {
        try
        {
            if (!File.Exists(_dbFilePath)) return;
            var backupPath = _dbFilePath + ".bak";
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(_dbFilePath, backupPath);
            FileLogger.Warn("UsageHistoryRepository",
                $"history.db backed up to {backupPath} due to {cause.GetType().Name}: {cause.Message}");
        }
        catch (Exception ex)
        {
            FileLogger.Error("UsageHistoryRepository",
                "TryRecoverFromCorruptedDb failed", ex);
        }
    }

    /// <summary>
    /// req-070 F-32：保留 IDisposable 以兼容现有调用方（App.OnExit）。
    /// 当前为 no-op：每次操作都创建新的 SqliteConnection 并用 using 释放，不持有长生命周期 disposable 对象。
    /// 如未来引入连接池或长生命周期资源，在此实现真正的释放逻辑。
    /// </summary>
    public void Dispose()
    {
        // Intentionally no-op: SqliteConnection 在每个操作中用 using 释放
        GC.SuppressFinalize(this);
    }
}
