using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using UsageMonitor.Core.Services;
// ★ WPF/WinForms 命名冲突 alias（项目 UseWPF + UseWindowsForms + ImplicitUsings 触发 CS0104）
using Binding = System.Windows.Data.Binding;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-069 F-13：本地化 XAML markup extension。
/// <para>
/// 用法：<c>Text="{helpers:Loc settings.general.title}"</c> 或 <c>Content="{helpers:Loc Key=settings.theme.dark}"</c>。
/// 内部绑定到 <see cref="LocProxy"/> 的索引器，<see cref="I18n.SetLanguage"/> 切换语言时经
/// <c>Item[]</c> 变更通知实时刷新所有绑定 —— 满足 req-069-006“可切换语言”。
/// </para>
/// <para>
/// 相比直接 <c>{DynamicResource key}</c>（需把每条字符串塞进 ResourceDictionary），本扩展复用
/// 现有 <see cref="I18n"/> 注册表（zh-CN 内置 + 插件/App 追加 + en-US 预留），键缺失时按
/// “当前语言→默认语言→key 本身”兜底，避免白屏。
/// </para>
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    /// <summary>无参构造（配合 <c>Key=</c> 命名参数）。</summary>
    public LocExtension() { }

    /// <summary>位置参数构造（<c>{helpers:Loc some.key}</c>）。</summary>
    /// <param name="key">i18n 键名。</param>
    public LocExtension(string key) => Key = key;

    /// <summary>i18n 键名（见 <see cref="I18nKeys"/> 常量与 <see cref="I18n"/> 注册表）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return string.Empty;

        // 绑定到 LocProxy 索引器：语言切换时 Item[] 变更通知触发刷新。
        var binding = new Binding($"[{Key}]")
        {
            Source = LocProxy.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// req-069 F-13：本地化取值代理（单例）。
/// <para>
/// 暴露 <c>this[key]</c> 索引器供 XAML 绑定；订阅 <see cref="I18n.LanguageChanged"/>，
/// 语言切换时发出 <c>Item[]</c> 变更通知，令所有 <see cref="LocExtension"/> 绑定重新取值。
/// </para>
/// </summary>
public sealed class LocProxy : INotifyPropertyChanged
{
    /// <summary>进程级单例。</summary>
    public static LocProxy Instance { get; } = new();

    private LocProxy()
    {
        I18n.LanguageChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>按 i18n 键取当前语言文案（缺失时回退默认语言 / key 本身）。</summary>
    /// <param name="key">i18n 键名。</param>
    public string this[string key] => I18n.T(key);

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;
}
