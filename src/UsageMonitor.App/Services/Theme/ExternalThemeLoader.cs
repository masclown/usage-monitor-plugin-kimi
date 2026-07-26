using System;
using System.Windows;
using System.Windows.Media;
using UsageMonitor.Core.Modules;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Services.Display;

namespace UsageMonitor.App.Services.Theme;

/// <summary>
/// req-115：外部主题包装载器——把 themes/&lt;pack&gt;/theme.json 的设计令牌映射
/// 同步注册进 <see cref="ThemeModule"/>（描述符 + 字典工厂）。
/// <para>字典由纯代码构造（SolidColorBrush / Color），不经 XamlReader，零代码执行；
/// 缺失 token 以 <see cref="ThemePack.IsDark"/> 对应的内置主题字典打底（MergedDictionaries 层叠）。</para>
/// </summary>
public static class ExternalThemeLoader
{
    private const string LogSource = "ExternalThemeLoader";

    /// <summary>
    /// 把注册表中的全部主题包同步到 ThemeModule：先清除旧外部主题，再逐包注册（id 撞内置 dark/light 时跳过）。
    /// </summary>
    /// <param name="registry">显示资源包注册表。</param>
    /// <param name="module">主题模块（默认实例）。</param>
    public static void SyncToThemeModule(DisplayPackRegistry registry, ThemeModule module)
    {
        module.ClearExternalThemes();
        foreach (var pack in registry.ThemePacks)
        {
            var id = pack.Id!;
            if (string.Equals(id, "dark", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "light", StringComparison.OrdinalIgnoreCase))
            {
                FileLogger.Warn(LogSource, $"主题包 id 与内置主题冲突，跳过: {id}");
                continue;
            }
            var descriptor = new ThemeDescriptor(id, pack.EffectiveDisplayName, $"pack:{id}", pack.IsDark);
            // 工厂惰性执行：ApplyTheme 时按 pack 当前 token 构造（闭包捕获 pack 实例，热重载后 Sync 重注新实例）
            module.RegisterExternalTheme(descriptor, () => BuildDictionary(pack));
        }
    }

    /// <summary>
    /// 由主题包构造 WPF 资源字典：内置打底字典 + token 覆盖层。
    /// <para>键名以 "Color" 结尾 → 写入 <see cref="Color"/>；否则写入冻结的 <see cref="SolidColorBrush"/>。
    /// 色值解析失败的 token 记日志跳过，不影响其余 token。</para>
    /// </summary>
    /// <param name="pack">主题包。</param>
    private static ResourceDictionary BuildDictionary(ThemePack pack)
    {
        var baseDict = new ResourceDictionary
        {
            Source = new Uri(pack.IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative)
        };
        // 包装层：本层键优先于 MergedDictionaries，实现"打底 + 覆盖"
        var wrapper = new ResourceDictionary();
        wrapper.MergedDictionaries.Add(baseDict);

        foreach (var (key, value) in pack.Tokens)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
                if (key.EndsWith("Color", StringComparison.OrdinalIgnoreCase))
                {
                    wrapper[key] = color;
                }
                else
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    wrapper[key] = brush;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warn(LogSource, $"主题包 {pack.Id} token {key}={value} 解析失败，跳过: {ex.Message}");
            }
        }
        return wrapper;
    }
}
