using System.Globalization;
using System.IO;
using System.Text.Json;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-091：登录会话历史持久化服务。
/// <para>
/// 存储格式：JSON Lines 追加写，每段一行（一个 <see cref="LoginSession"/>）。
/// 文件位置：<c>%AppData%\UsageMonitor\session-history\&lt;ProviderId&gt;.jsonl</c>。
/// </para>
/// <para>
/// 数据约定：
/// <list type="bullet">
///   <item><description>每段最多只有一个活跃段（<c>EndDate = null</c>）。</description></item>
///   <item><description>新登录会先把旧活跃段的 <c>EndDate</c> 设为今天，再 append 新活跃段。</description></item>
///   <item><description>历史段（<c>EndDate != null</c>）永不修改，保留完整生命周期可追溯。</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class LoginSessionHistoryStore
{
    private readonly string _rootDir;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 创建历史仓库实例。
    /// </summary>
    /// <param name="rootDir">
    /// 根目录路径（默认 <c>%AppData%\UsageMonitor\session-history</c>）。
    /// 单元测试时可注入临时目录。
    /// </param>
    public LoginSessionHistoryStore(string? rootDir = null)
    {
        _rootDir = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UsageMonitor", "session-history");
        Directory.CreateDirectory(_rootDir);
    }

    /// <summary>JSONL 文件路径（每个 Provider 一个）。</summary>
    public string GetFilePath(string providerId)
        => Path.Combine(_rootDir, $"{SanitizeFileName(providerId)}.jsonl");

    /// <summary>
    /// 读取某个 Provider 的全部登录段（含活跃 + 历史），按 RecordedAt 升序。
    /// </summary>
    public IReadOnlyList<LoginSession> LoadAll(string providerId)
    {
        var path = GetFilePath(providerId);
        if (!File.Exists(path)) return Array.Empty<LoginSession>();

        var list = new List<LoginSession>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var s = JsonSerializer.Deserialize<LoginSession>(line, JsonOpts);
                if (s != null) list.Add(s);
            }
            catch (Exception ex)
            {
                FileLogger.Warn("LoginSessionHistoryStore",
                    $"Skip malformed line in {path}: {ex.Message}");
            }
        }
        return list.OrderBy(s => s.RecordedAt).ToList();
    }

    /// <summary>
    /// 获取当前活跃段（<c>EndDate = null</c>）。不存在返回 null。
    /// </summary>
    public LoginSession? GetActiveSession(string providerId)
    {
        return LoadAll(providerId).LastOrDefault(s => s.IsActive);
    }

    /// <summary>
    /// 归档当前活跃段 + 写入新活跃段（原子操作：先归档再 append）。
    /// 适用于用户重新登录、Cookie 失效后重新获取等场景。
    /// </summary>
    /// <param name="providerId">Provider 唯一标识</param>
    /// <param name="triggerSource">触发源（<see cref="LoginSessionTriggers"/> 常量）</param>
    public LoginSession ArchiveAndStartNew(string providerId, string triggerSource)
    {
        if (string.IsNullOrEmpty(providerId)) throw new ArgumentNullException(nameof(providerId));

        var all = LoadAll(providerId).ToList();

        // 1. 归档当前活跃段：EndDate 设为今天
        var activeIdx = all.FindIndex(s => s.IsActive);
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (activeIdx >= 0)
        {
            all[activeIdx] = new LoginSession
            {
                StartDate = all[activeIdx].StartDate,
                EndDate = today,
                TriggerSource = all[activeIdx].TriggerSource,
                RecordedAt = all[activeIdx].RecordedAt
            };
        }

        // 2. 写入新活跃段：StartDate = 今天，EndDate = null
        var newSession = new LoginSession
        {
            StartDate = today,
            EndDate = null,
            TriggerSource = triggerSource,
            RecordedAt = DateTime.UtcNow
        };
        all.Add(newSession);

        // 3. 序列化全部段（按时间升序）覆盖写文件
        var lines = all.Select(s => JsonSerializer.Serialize(s, JsonOpts));
        var path = GetFilePath(providerId);
        File.WriteAllLines(path, lines);

        FileLogger.Info("LoginSessionHistoryStore",
            $"ArchiveAndStartNew({providerId}): trigger={triggerSource}");
        return newSession;
    }

    /// <summary>
    /// 迁移老 Cookie 文件：把 mtime 视为首段起点，append 一个 LegacyMigration 段。
    /// 仅在该 Provider 没有任何历史段时调用（幂等性保护）。
    /// </summary>
    /// <param name="providerId">Provider 唯一标识</param>
    /// <param name="cookieFilePath">老 Cookie 文件路径</param>
    public LoginSession? MigrateLegacyCookie(string providerId, string cookieFilePath)
    {
        if (string.IsNullOrEmpty(providerId)) return null;
        if (string.IsNullOrEmpty(cookieFilePath) || !File.Exists(cookieFilePath))
            return null;

        // 幂等：已有任何段则不迁移
        var existing = LoadAll(providerId);
        if (existing.Count > 0) return null;

        var mtime = File.GetLastWriteTimeUtc(cookieFilePath);
        var startDate = mtime.ToLocalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var session = new LoginSession
        {
            StartDate = startDate,
            EndDate = null, // 活跃段
            TriggerSource = LoginSessionTriggers.LegacyMigration,
            RecordedAt = DateTime.UtcNow
        };

        var path = GetFilePath(providerId);
        File.AppendAllLines(path, new[] { JsonSerializer.Serialize(session, JsonOpts) });

        FileLogger.Info("LoginSessionHistoryStore",
            $"MigrateLegacyCookie({providerId}): start={startDate}");
        return session;
    }

    /// <summary>
    /// 清理 ProviderId 中的不安全字符（防止路径穿越）。
    /// </summary>
    private static string SanitizeFileName(string providerId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var arr = providerId.Where(c => !invalid.Contains(c)).ToArray();
        return new string(arr);
    }
}