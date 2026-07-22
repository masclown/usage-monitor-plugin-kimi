using System;
using System.Windows;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// 运行时主题切换管理器。
/// <para>
/// 维护 <see cref="Application.Resources"/> 的 MergedDictionaries 中"主题字典"这一层：
/// 切换时移除现存的 Dark/Light 字典并在原位插入目标主题字典（保持 Tokens 在前、Styles 在后）。
/// 因所有消费方均以 <c>{DynamicResource}</c> 引用主题画笔，替换后 UI 立即换肤，无需重建窗口。
/// </para>
/// </summary>
public static class ThemeManager
{
    // req-099 B3：主题资源 URI 与切换逻辑已下沉到可插拔的 ThemeModule（支持注册第三方主题）。
    // ThemeManager 保留为兼容门面：维护 ThemeMode 语义 + ThemeChanged 事件，内部委托到 ThemeModule.Default。

    /// <summary>
    /// 当前已应用的主题（默认深色）。
    /// </summary>
    public static ThemeMode Current { get; private set; } = ThemeMode.Dark;

    /// <summary>
    /// req-016：主题切换事件。每次 <see cref="Apply(ThemeMode)"/> 完成且当前主题变化时触发。
    /// 通知订阅方（如 LogoProvider）刷新非主题字典资源（托盘图标、窗口 Icon 等）。
    /// </summary>
    public static event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>
    /// 应用指定主题：把 MergedDictionaries 中现存的主题字典替换为目标主题字典。
    /// </summary>
    /// <param name="mode">目标主题（深色 / 浅色）</param>
    public static void Apply(ThemeMode mode)
    {
        // req-064 U6：当前已是目标主题时短路返回，避免重复移除/插入字典导致 UI 闪烁
        if (mode == Current && System.Windows.Application.Current != null) return;

        // req-099 B3：字典切换逻辑已抽离到可插拔的 ThemeModule；ThemeManager 仅保留 ThemeMode 语义与 ThemeChanged 事件。
        var previous = Current;
        UsageMonitor.App.Services.Theme.ThemeModule.Default.ApplyTheme(mode == ThemeMode.Light ? "light" : "dark");
        Current = mode;

        // 仅在主题实际变化时触发事件（避免 Apply 同主题的副作用）
        if (previous != mode)
        {
            try
            {
                ThemeChanged?.Invoke(typeof(ThemeManager), new ThemeChangedEventArgs(mode));
            }
            catch (Exception ex)
            {
                FileLogger.Error("ThemeManager", $"ThemeChanged handler threw: {ex.Message}", ex);
            }
        }
    }

    /// <summary>在深色 / 浅色间切换，返回切换后的主题。</summary>
    public static ThemeMode Toggle()
    {
        var next = Current == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
        Apply(next);
        return next;
    }
}

/// <summary>
/// req-016：主题切换事件参数。仅传当前主题，订阅方自行调用 <see cref="ThemeManager.Current"/> 取最新值。
/// </summary>
public sealed class ThemeChangedEventArgs : EventArgs
{
    /// <summary>切换后的主题</summary>
    public UsageMonitor.Core.Models.ThemeMode NewTheme { get; }

    public ThemeChangedEventArgs(UsageMonitor.Core.Models.ThemeMode newTheme)
    {
        NewTheme = newTheme;
    }
}
