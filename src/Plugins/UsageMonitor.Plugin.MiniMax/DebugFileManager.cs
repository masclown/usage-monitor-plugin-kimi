using System;
using System.IO;

namespace UsageMonitor.Plugin.MiniMax;

/// <summary>
/// Debug 文件管理工具类 - 提供 debug 文件的清理和写入功能
/// </summary>
internal static class DebugFileManager
{
    private static readonly string DebugDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "debug");

    /// <summary>
    /// 清理 7 天前的 debug 文件，避免磁盘占用无限增长。
    /// </summary>
    public static void CleanupOldDebugFiles()
    {
        try
        {
            if (!Directory.Exists(DebugDir)) return;
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.GetFiles(DebugDir))
            {
                var info = new FileInfo(file);
                if (info.CreationTime < cutoff)
                {
                    info.Delete();
                }
            }
        }
        catch
        {
            // Silently ignore cleanup failures
        }
    }

    /// <summary>
    /// 获取 debug 目录路径
    /// </summary>
    public static string GetDebugDirectory() => DebugDir;
}
