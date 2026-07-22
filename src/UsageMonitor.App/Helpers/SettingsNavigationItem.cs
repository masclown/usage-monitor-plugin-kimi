namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-073：设置窗口左侧导航项（分组 + 中文显示文本 + 枚举值）。
/// </summary>
public class SettingsNavigationItem
{
    /// <summary>导航项对应的分区枚举。</summary>
    public SettingsSection Section { get; set; }

    /// <summary>中文显示文本（如「常规设置」「插件管理」）。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>分组名（通用 / 显示 / 高级），用于 UI 分组标题。</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>是否为分组标题（true 时仅显示 GroupName，不可点击）。</summary>
    public bool IsGroupHeader { get; set; }

    /// <summary>创建分组标题项。</summary>
    public static SettingsNavigationItem CreateGroupHeader(string groupName) => new()
    {
        IsGroupHeader = true,
        GroupName = groupName,
        DisplayName = groupName,
    };

    /// <summary>创建可点击导航项。</summary>
    public static SettingsNavigationItem CreateItem(SettingsSection section, string displayName, string groupName) => new()
    {
        IsGroupHeader = false,
        Section = section,
        DisplayName = displayName,
        GroupName = groupName,
    };
}
