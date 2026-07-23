using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services.Data;

/// <summary>
/// 数据库字段迁移服务（req-107 B2）：SDK 字段改名后把旧列数据搬到新列并删除旧列。
/// <para>程序升级后首次启动触发：扫描目标表的现有列，经 <see cref="UsageFieldAliases"/> 把旧列名解析到现名，
/// 若新列不存在则新增、把旧列数据 UPDATE 到新列、再 DROP 旧列，保证字段改名历史数据不丢。
/// 同时加载插件声明时旧字段名也经 <see cref="UsageFieldAliases"/> 解析到现名（见 <see cref="UsageFields.MapToStandardFieldName"/>）。</para>
/// </summary>
public sealed class SchemaMigrationService
{
    private readonly string _connectionString;

    /// <summary>
    /// 创建迁移服务。
    /// </summary>
    /// <param name="connectionString">SQLite 连接字符串（与数据层同一库）。</param>
    public SchemaMigrationService(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// 对指定表执行字段改名迁移：旧列（经 alias 解析）→ 新列，搬数据 + 删旧列。
    /// </summary>
    /// <param name="tableName">目标表名（如 usage_snapshot）。</param>
    /// <returns>本次实际迁移的（旧列名 → 新列名）列表。</returns>
    public IReadOnlyList<(string OldColumn, string NewColumn)> MigrateRenamedColumns(string tableName)
    {
        var migrated = new List<(string, string)>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var existing = GetColumns(connection, tableName);
        if (existing.Count == 0) return migrated;

        foreach (var oldColumn in existing)
        {
            var newColumn = UsageFieldAliases.Resolve(oldColumn);
            // 无别名 / 解析到自身 / 新列已存在 → 跳过
            if (string.IsNullOrEmpty(newColumn) || string.Equals(newColumn, oldColumn, StringComparison.OrdinalIgnoreCase))
                continue;
            if (existing.Contains(newColumn))
                continue;

            var sqlType = SchemaGenerator.ToSqlType(
                UsageFieldMetadataRegistry.Get(newColumn)?.DataType ?? UsageFieldDataType.Text);

            using var transaction = connection.BeginTransaction();
            try
            {
                ExecuteNonQuery(connection, transaction, $"ALTER TABLE {Quote(tableName)} ADD COLUMN {Quote(newColumn)} {sqlType}");
                ExecuteNonQuery(connection, transaction, $"UPDATE {Quote(tableName)} SET {Quote(newColumn)} = {Quote(oldColumn)}");
                ExecuteNonQuery(connection, transaction, $"ALTER TABLE {Quote(tableName)} DROP COLUMN {Quote(oldColumn)}");
                transaction.Commit();
                migrated.Add((oldColumn, newColumn));
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        return migrated;
    }

    /// <summary>
    /// 读取表的现有列名集合（经 PRAGMA table_info）。
    /// </summary>
    private static HashSet<string> GetColumns(SqliteConnection connection, string tableName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(tableName)})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            set.Add(reader.GetString(1)); // 第 1 列为列名
        }
        return set;
    }

    /// <summary>执行一条非查询 SQL。</summary>
    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>SQL 标识符加方括号转义。</summary>
    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
}
