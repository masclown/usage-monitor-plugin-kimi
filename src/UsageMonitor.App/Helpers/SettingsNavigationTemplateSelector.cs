using System.Windows;
using System.Windows.Controls;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-073：导航项模板选择器——分组标题用 GroupHeaderTemplate，可点击项用 ItemTemplate。
/// </summary>
public class SettingsNavigationTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupHeaderTemplate { get; set; }
    public DataTemplate? ItemTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is SettingsNavigationItem navItem)
        {
            return navItem.IsGroupHeader ? GroupHeaderTemplate : ItemTemplate;
        }
        return ItemTemplate;
    }
}
