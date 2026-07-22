using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using UsageMonitor.Core.Modules;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Services.Theme;

/// <summary>
/// req-099 B3：主题模块实现（App 层，操作 WPF <see cref="ResourceDictionary"/>）。
/// <para>
/// 可插拔主题引擎：内置注册 "dark" / "light" 两个主题，并允许通过 <see cref="RegisterTheme"/>
/// 追加第三方主题（无需修改主程序代码）。<see cref="ApplyTheme"/> 负责把
/// <see cref="Application.Resources"/> 的 MergedDictionaries 中"主题字典"这一层热替换为目标主题字典，
/// 因所有消费方均以 <c>{DynamicResource}</c> 引用主题画笔，替换后 UI 立即换肤。
/// </para>
/// <para>
/// 旧的静态 <c>ThemeManager</c> 现作为兼容门面，其 Apply 内部委托到本模块的 <see cref="Default"/> 实例，
/// 保留 <c>ThemeMode</c> 语义与 <c>ThemeChanged</c> 事件；本模块提供更通用的按 Id 扩展能力。
/// </para>
/// </summary>
public sealed class ThemeModule : IThemeModule
{
    /// <summary>
    /// 进程级默认实例（供 <c>ThemeManager</c> 兼容门面与插件注册共用）。
    /// 初始化时注册内置 dark / light 两个主题。
    /// </summary>
    public static ThemeModule Default { get; } = CreateWithBuiltins();

    private readonly Dictionary<string, ThemeDescriptor> _themes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ThemeDescriptor> _order = new();
    private string? _currentThemeId;

    /// <inheritdoc/>
    public IReadOnlyList<ThemeDescriptor> AvailableThemes => _order;

    /// <inheritdoc/>
    public string? CurrentThemeId => _currentThemeId;

    /// <inheritdoc/>
    public event EventHandler<string>? ThemeApplied;

    /// <summary>创建并注册内置 dark / light 主题的模块实例。</summary>
    private static ThemeModule CreateWithBuiltins()
    {
        var m = new ThemeModule();
        m.RegisterTheme(new ThemeDescriptor("dark", "深色", "Themes/Dark.xaml", IsDark: true));
        m.RegisterTheme(new ThemeDescriptor("light", "浅色", "Themes/Light.xaml", IsDark: false));
        return m;
    }

    /// <inheritdoc/>
    public void RegisterTheme(ThemeDescriptor theme)
    {
        if (theme == null || string.IsNullOrWhiteSpace(theme.Id)) return;
        if (_themes.ContainsKey(theme.Id))
        {
            // 覆盖更新：保持注册顺序中的原位置
            var idx = _order.FindIndex(t => string.Equals(t.Id, theme.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _order[idx] = theme;
        }
        else
        {
            _order.Add(theme);
        }
        _themes[theme.Id] = theme;
    }

    /// <inheritdoc/>
    public void ApplyTheme(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId)) return;
        if (!_themes.TryGetValue(themeId, out var target))
        {
            FileLogger.Warn("ThemeModule", $"ApplyTheme: 未注册的主题 id={themeId}");
            return;
        }

        var app = System.Windows.Application.Current;
        if (app == null) return;

        var dicts = app.Resources.MergedDictionaries;

        // 已注册主题的资源文件名集合，用于识别 MergedDictionaries 中现存的"主题字典"层。
        var themeFileNames = _themes.Values
            .Select(t => FileName(t.ResourceUri))
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 从后往前遍历，移除现存的主题字典，记录插入位置（保持 Tokens 在前、Styles 在后的层序）。
        int insertIndex = -1;
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.OriginalString ?? string.Empty;
            if (themeFileNames.Contains(FileName(src)))
            {
                insertIndex = i;
                dicts.RemoveAt(i);
            }
        }

        var themeDict = new ResourceDictionary { Source = new Uri(target.ResourceUri, UriKind.Relative) };
        if (insertIndex >= 0 && insertIndex <= dicts.Count)
            dicts.Insert(insertIndex, themeDict);
        else
            dicts.Add(themeDict);

        _currentThemeId = target.Id;
        try
        {
            ThemeApplied?.Invoke(this, target.Id);
        }
        catch (Exception ex)
        {
            FileLogger.Error("ThemeModule", $"ThemeApplied handler threw: {ex.Message}", ex);
        }
    }

    /// <summary>取相对 URI 的文件名部分（用于跨反斜杠/正斜杠的稳健比对）。</summary>
    private static string FileName(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return string.Empty;
        var s = uri.Replace('\\', '/');
        var idx = s.LastIndexOf('/');
        return idx >= 0 ? s.Substring(idx + 1) : s;
    }
}
