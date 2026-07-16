namespace UsageMonitor.Core.Models;

/// <summary>
/// 应用外观主题模式。随 <c>AppSettings.Theme</c> 持久化到 config.json，
/// 启动时由 ThemeManager 应用，设置窗口可运行时切换。
/// </summary>
public enum ThemeMode
{
    /// <summary>高级深色主题（默认）</summary>
    Dark,

    /// <summary>简约浅色主题</summary>
    Light
}
