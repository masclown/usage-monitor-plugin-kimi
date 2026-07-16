using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using UsageMonitor.Core.Models;

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
            Cache = SqliteCacheMode.Shared
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
            FileLogger.Error("UsageHistoryRepository",
                $"UpsertPoint({usage.ProviderId}) failed", ex);
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

    public void Dispose()
    {
        // Microsoft.Data.Sqlite 在 using 后会自动释放连接；这里无需主动释放长连接对象
        GC.SuppressFinalize(this);
    }
}
