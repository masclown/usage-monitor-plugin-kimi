using System.Windows;
using System.Windows.Controls;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-073：按 <see cref="SettingsSection"/> 从宿主 Window 资源中挑选对应 DataTemplate。
/// <para>
/// 模板资源键约定为 <c>"SettingsSection_{枚举名}"</c>（如 <c>SettingsSection_General</c>）。
/// 找不到时回退到 <see cref="SettingsSection.General"/> 模板，避免导航到未实现分区时白屏。
/// </para>
/// </summary>
public class SettingsSectionSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not SettingsSection section) return null;

        var element = container as FrameworkElement;
        var key = $"SettingsSection_{section}";
        var template = element?.TryFindResource(key) as DataTemplate;

        // 回退：未实现的分区（CardOrder / ChartOrder / MultiProgress）先显示 General 模板，
        // 等 req-103/104/097 落地后各自补充独立模板即可移除此回退。
        template ??= element?.TryFindResource($"SettingsSection_{SettingsSection.General}") as DataTemplate;
        return template;
    }
}
