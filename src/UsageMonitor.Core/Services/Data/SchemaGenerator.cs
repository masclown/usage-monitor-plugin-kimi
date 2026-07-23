using System.Collections.Generic;
using System.Linq;
using System.Text;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services.Data;

/// <summary>
/// 数据库 Schema 由 SDK 字段反向生成（req-107 B2）。
/// <para>原则"数据库字段 = SDK 字段"：列名取自 <see cref="UsageFields"/> 常量，列类型由
/// <see cref="UsageFieldDataType"/> 推导；额外系统列 ProviderId / AccountId / PlanType / Timestamp。
/// <see cref="UsageFieldVisibility.Sensitive"/> 字段绝不生成列（永不入库）。</para>
/// <para>本类生成目标态 DDL 与列类型映射，供建表 / schema 导出（B10）/ 迁移（<see cref="SchemaMigrationService"/>）使用；
/// 与现有 req-092 字段级 diff 持久化（usage_field_versions）并存，不破坏既有数据层。</para>
/// </summary>
public static class SchemaGenerator
{
    /// <summary>系统列定义（每行必带，非 SDK 上报字段）。</summary>
    public static readonly IReadOnlyList<(string Name, string SqlType)> SystemColumns = new[]
    {
        (UsageFields.ProviderId, "TEXT"),
        (UsageFields.AccountId, "TEXT"),
        (UsageFields.PlanType, "TEXT"),
        (UsageFields.Timestamp, "TEXT")
    };

    /// <summary>
    /// 把 SDK 字段数据类型映射为 SQLite 列类型。
    /// </summary>
    public static string ToSqlType(UsageFieldDataType dataType) => dataType switch
    {
        UsageFieldDataType.Bool => "INTEGER",
        _ => "TEXT" // 百分比/数值/Token/积分/货币/计数/日期/文本统一存 TEXT（SQLite 动态类型，原始值字符串化）
    };

    /// <summary>
    /// 生成快照宽表 DDL（系统列 + 全部可入库 SDK 字段；敏感字段排除）。
    /// </summary>
    /// <param name="tableName">表名（默认 usage_snapshot）。</param>
    public static string GenerateSnapshotTableDdl(string tableName = "usage_snapshot")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE IF NOT EXISTS {Quote(tableName)} (");
        var lines = new List<string> { "    id INTEGER PRIMARY KEY AUTOINCREMENT" };
        lines.AddRange(SystemColumns.Select(c => $"    {Quote(c.Name)} {c.SqlType}"));
        foreach (var meta in UsageFieldMetadataRegistry.All)
        {
            if (meta.Visibility == UsageFieldVisibility.Sensitive) continue; // 敏感字段永不入库
            if (SystemColumns.Any(s => s.Name == meta.FieldName)) continue;  // 系统列已含
            lines.Add($"    {Quote(meta.FieldName)} {ToSqlType(meta.DataType)}");
        }
        sb.AppendLine(string.Join(",\n", lines));
        sb.Append(");");
        return sb.ToString();
    }

    /// <summary>SQL 标识符加方括号转义。</summary>
    internal static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
}
