namespace UsageMonitor.Core.Services;

/// <summary>
/// req-090-003：Cookie 过期清理服务。
/// 启动时扫描 cookies\*.json，删除超过保留天数的文件（默认 90 天，容错窗口 7 天）。
/// </summary>
public static class CookieCleanupService
{
    /// <summary>默认保留天数</summary>
    public const int DefaultRetentionDays = 90;
    /// <summary>容错窗口：7 天内修改的不删</summary>
    public const int GracePeriodDays = 7;

    /// <summary>
    /// 清理过期 Cookie 文件。
    /// </summary>
    /// <param name="cookieDir">Cookie 目录路径</param>
    /// <param name="retentionDays">保留天数（默认 90，范围 7-365）</param>
    /// <returns>删除的文件数</returns>
    public static int CleanupExpiredCookies(string cookieDir, int retentionDays = DefaultRetentionDays)
    {
        if (!Directory.Exists(cookieDir)) return 0;
        retentionDays = Math.Clamp(retentionDays, GracePeriodDays, 365);

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(cookieDir, "*.json"))
            {
                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    if (lastWrite < cutoff)
                    {
                        File.Delete(file);
                        deleted++;
                        FileLogger.Info("CookieCleanup", $"已删除过期 Cookie: {Path.GetFileName(file)} (最后修改: {lastWrite:yyyy-MM-dd})");
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.Warn("CookieCleanup", $"删除 {Path.GetFileName(file)} 失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("CookieCleanup", "扫描 Cookie 目录失败", ex);
        }

        if (deleted > 0)
            FileLogger.Info("CookieCleanup", $"共清理 {deleted} 个过期 Cookie 文件");
        return deleted;
    }
}
