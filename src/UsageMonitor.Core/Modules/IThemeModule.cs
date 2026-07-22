using System;
using System.Collections.Generic;

namespace UsageMonitor.Core.Modules;

/// <summary>
/// req-099 B3：主题描述符（WPF-无关）。承载注册与切换一个主题所需的元数据，
/// 使主题系统可由配置或插件扩展，而不依赖硬编码的 Dark/Light 二元枚举。
/// </summary>
/// <param name="Id">主题唯一标识（如 "dark" / "light" / "solarized"）。</param>
/// <param name="DisplayName">主题显示名（设置界面展示）。</param>
/// <param name="ResourceUri">
/// 主题资源字典的相对 URI（如 <c>"Themes/Dark.xaml"</c>）。由 App 层实现负责按此 URI 加载
/// WPF <c>ResourceDictionary</c> 并热替换，Core 不感知 WPF 细节。
/// </param>
/// <param name="IsDark">是否为深色系主题（供需要区分明暗的消费方参考）。</param>
public sealed record ThemeDescriptor(string Id, string DisplayName, string ResourceUri, bool IsDark);

/// <summary>
/// req-099 B3：主题模块契约。把"主题注册 / 切换 / 扩展"从主窗口与各处静态调用中解耦，
/// 支持第三方（插件或配置）注册新主题，宿主无需修改主程序代码即可增加主题。
/// <para>
/// 具体实现（<c>UsageMonitor.App.Services.Theme.ThemeModule</c>）位于 App 层，
/// 因为主题资源是 WPF <c>ResourceDictionary</c>；Core 仅定义契约与描述符。
/// </para>
/// </summary>
public interface IThemeModule
{
    /// <summary>已注册的可用主题（按注册顺序）。</summary>
    IReadOnlyList<ThemeDescriptor> AvailableThemes { get; }

    /// <summary>当前已应用的主题 Id（尚未应用时为 null）。</summary>
    string? CurrentThemeId { get; }

    /// <summary>主题应用完成事件（payload 为已应用主题的 Id）。</summary>
    event EventHandler<string>? ThemeApplied;

    /// <summary>
    /// 注册（或按 Id 覆盖更新）一个主题。同 Id 重复注册时用新描述符替换。
    /// </summary>
    /// <param name="theme">主题描述符。</param>
    void RegisterTheme(ThemeDescriptor theme);

    /// <summary>
    /// 按 Id 应用已注册的主题（热替换资源字典）。未注册的 Id 将被忽略并记录日志。
    /// </summary>
    /// <param name="themeId">目标主题 Id。</param>
    void ApplyTheme(string themeId);
}
