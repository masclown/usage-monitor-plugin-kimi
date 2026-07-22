using System.Collections.Generic;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-069 F-13 / F-06：App UI 文案的 i18n 注册表（zh-CN 现用 + en-US 预留结构）。
/// <para>
/// 由 <c>App.OnStartup</c> 早期调用 <see cref="Register"/>，把 App 界面文案注册进
/// <see cref="I18n"/> 合并式注册表；XAML 通过 <c>{helpers:Loc key}</c>（<see cref="LocExtension"/>）取值。
/// 语言切换（<see cref="I18n.SetLanguage"/>）后经 <see cref="LocProxy"/> 实时刷新绑定。
/// </para>
/// <para>
/// req-069-006：即使当前默认中文，也预留 en-US 结构，键集与 zh-CN 一一对应，便于后续补全语言包。
/// </para>
/// </summary>
public static class AppUiStrings
{
    private static bool _registered;

    /// <summary>把 App UI 文案（zh-CN + en-US）注册进 I18n 注册表（幂等）。</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        I18n.Register("zh-CN", ZhCn);
        I18n.Register("en-US", EnUs);
    }

    // 键命名约定：settings.<section>.<item>，与 XAML {helpers:Loc ...} 一一对应。
    private static readonly Dictionary<string, string> ZhCn = new()
    {
        // 设置窗口 - 常规设置分区
        ["settings.general.title"] = "常规设置",
        ["settings.theme.label"] = "外观主题",
        ["settings.theme.dark"] = "深色",
        ["settings.theme.light"] = "浅色",
        ["settings.refresh.interval"] = "刷新间隔",
        ["settings.refresh.unit.seconds"] = "秒",
        ["settings.refresh.hint"] = "建议 60-3600 秒（1 分钟至 1 小时）",
        ["settings.autostart"] = "开机自动启动",
        ["settings.history.title"] = "折线图历史数据",
        ["settings.history.points"] = "历史数据保留点数（30 / 60 / 120）",
        ["settings.ring.thresholdTitle"] = "圆环图阈值（达到百分比时切换颜色）",
        ["settings.ring.warning"] = "警告阈值（% 默认 60）",
        ["settings.ring.danger"] = "危险阈值（% 默认 85）",
        ["settings.ring.centerMetric"] = "环形图中心数字（单击启用/禁用，滚轮切换顺序）",
        // 设置窗口 - 插件管理分区
        ["settings.plugins.title"] = "已安装插件",
        // 插件配置窗口
        ["pluginconfig.title"] = "插件配置",
        ["pluginconfig.cardchart.title"] = "卡片图表",
        ["pluginconfig.cardchart.hint"] = "勾选要在主窗口该服务商卡片中展示的图表（可多选，下方为示例预览）",
        ["pluginconfig.getcookie"] = "获取登录态",
        ["pluginconfig.getcookie.tip"] = "自动启动独立 Edge 窗口并打开登录页，登录完成后自动获取 Cookie",
        // 通用
        ["common.cancel"] = "取消",
        ["common.save"] = "保存",
    };

    // req-069-006：英文键值预留结构（与 zh-CN 键集一致），当前仅默认中文。
    private static readonly Dictionary<string, string> EnUs = new()
    {
        ["settings.general.title"] = "General",
        ["settings.theme.label"] = "Appearance",
        ["settings.theme.dark"] = "Dark",
        ["settings.theme.light"] = "Light",
        ["settings.refresh.interval"] = "Refresh interval",
        ["settings.refresh.unit.seconds"] = "sec",
        ["settings.refresh.hint"] = "Recommended 60-3600 s (1 min to 1 h)",
        ["settings.autostart"] = "Start on system boot",
        ["settings.history.title"] = "Line chart history",
        ["settings.history.points"] = "History points to keep (30 / 60 / 120)",
        ["settings.ring.thresholdTitle"] = "Ring chart thresholds (color switches at %)",
        ["settings.ring.warning"] = "Warning threshold (% default 60)",
        ["settings.ring.danger"] = "Danger threshold (% default 85)",
        ["settings.ring.centerMetric"] = "Ring center metric (click to toggle, scroll to reorder)",
        ["settings.plugins.title"] = "Installed plugins",
        ["pluginconfig.title"] = "Plugin configuration",
        ["pluginconfig.cardchart.title"] = "Card charts",
        ["pluginconfig.cardchart.hint"] = "Select charts to show on this provider's card (multi-select; preview below)",
        ["pluginconfig.getcookie"] = "Get login state",
        ["pluginconfig.getcookie.tip"] = "Launches a standalone Edge window to the login page and captures cookies after sign-in",
        ["common.cancel"] = "Cancel",
        ["common.save"] = "Save",
    };
}
