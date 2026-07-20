using System.Text.Json;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-090-002：Cookie 读取审计日志。
/// JSON Lines 追加写 %AppData%\UsageMonitor\audit\cookie-audit.log，环形 buffer 保留最近 1000 条。
/// </summary>
public static class CookieAuditLog
{
    private static readonly string AuditDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "audit");
    private static readonly string AuditFilePath = Path.Combine(AuditDir, "cookie-audit.log");
    private const int MaxEntries = 1000;
    private static readonly object _lock = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>审计动作类型</summary>
    public enum AuditAction
    {
        Load,       // 读取 Cookie
        Save,       // 保存 Cookie
        Validate,   // 验证 Cookie 有效性
        Login,      // 登录提取 Cookie
        Refresh     // 刷新 Cookie
    }

    /// <summary>审计来源</summary>
    public enum AuditSource
    {
        Auto,       // 自动刷新
        Manual,     // 手动触发
        Startup     // 启动时
    }

    /// <summary>
    /// 写入一条审计记录。
    /// </summary>
    public static void Write(string providerId, AuditAction action, bool success,
        AuditSource source = AuditSource.Auto, string? error = null)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(AuditDir);
                var entry = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    providerId,
                    action = action.ToString(),
                    success,
                    source = source.ToString(),
                    error
                };
                var line = JsonSerializer.Serialize(entry, s_jsonOptions);
                File.AppendAllText(AuditFilePath, line + Environment.NewLine);

                // 环形 buffer：超出 MaxEntries 时截断
                TruncateIfNeeded();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("CookieAuditLog", $"写入审计日志失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 读取最近 N 条审计记录（用于设置页面展示）。
    /// </summary>
    public static List<string> ReadRecent(int count = 100)
    {
        var result = new List<string>();
        try
        {
            if (!File.Exists(AuditFilePath)) return result;
            var lines = File.ReadAllLines(AuditFilePath);
            var start = Math.Max(0, lines.Length - count);
            for (int i = start; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    result.Add(lines[i]);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("CookieAuditLog", $"读取审计日志失败: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// 导出审计日志为 CSV。
    /// </summary>
    public static string ExportToCsv()
    {
        var lines = ReadRecent(MaxEntries);
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,ProviderId,Action,Success,Source,Error");
        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var ts = root.GetProperty("timestamp").GetString() ?? "";
                var pid = root.GetProperty("providerId").GetString() ?? "";
                var act = root.GetProperty("action").GetString() ?? "";
                var suc = root.GetProperty("success").GetBoolean();
                var src = root.GetProperty("source").GetString() ?? "";
                var err = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
                csv.AppendLine($"\"{ts}\",\"{pid}\",\"{act}\",{suc},\"{src}\",\"{err.Replace("\"", "\"\"")}\"");
            }
            catch { /* skip malformed lines */ }
        }
        return csv.ToString();
    }

    /// <summary>审计文件路径（供设置页面展示）</summary>
    public static string GetAuditFilePath() => AuditFilePath;

    /// <summary>
    /// 环形 buffer 截断：保留最近 MaxEntries 条。
    /// </summary>
    private static void TruncateIfNeeded()
    {
        try
        {
            var lines = File.ReadAllLines(AuditFilePath);
            if (lines.Length <= MaxEntries) return;

            var keep = lines.Skip(lines.Length - MaxEntries).ToArray();
            File.WriteAllLines(AuditFilePath, keep);
        }
        catch { /* 截断失败不阻塞主流程 */ }
    }
}
