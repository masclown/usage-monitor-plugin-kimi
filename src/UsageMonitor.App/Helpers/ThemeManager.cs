using System;
using System.Windows;
using UsageMonitor.Core.Models;

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
    private const string DarkSource = "Themes/Dark.xaml";
    private const string LightSource = "Themes/Light.xaml";

    /// <summary>当前已应用的主题（默认深色）。</summary>
    public static ThemeMode Current { get; private set; } = ThemeMode.Dark;

    /// <summary>
    /// 应用指定主题：把 MergedDictionaries 中现存的主题字典替换为目标主题字典。
    /// </summary>
    /// <param name="mode">目标主题（深色 / 浅色）</param>
    public static void Apply(ThemeMode mode)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        var dicts = app.Resources.MergedDictionaries;
        var targetSource = mode == ThemeMode.Light ? LightSource : DarkSource;

        // 从后往前遍历，移除现存的主题字典（按 Source 是否含 Dark/Light 判定），记录插入位置
        int insertIndex = -1;
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.OriginalString ?? string.Empty;
            if (src.Contains("Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("Light.xaml", StringComparison.OrdinalIgnoreCase))
            {
                insertIndex = i;
                dicts.RemoveAt(i);
            }
        }

        var themeDict = new ResourceDictionary
        {
            Source = new Uri(targetSource, UriKind.Relative)
        };

        if (insertIndex >= 0 && insertIndex <= dicts.Count)
            dicts.Insert(insertIndex, themeDict);
        else
            dicts.Add(themeDict);

        Current = mode;
    }

    /// <summary>在深色 / 浅色间切换，返回切换后的主题。</summary>
    public static ThemeMode Toggle()
    {
        var next = Current == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
        Apply(next);
        return next;
    }
}
