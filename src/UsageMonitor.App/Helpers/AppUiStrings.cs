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
        // S6：旧卡片图表多选区已删除，改为按插件声明 chartId 的启用开关（卡片 / 迷你图表各一组）
        ["pluginconfig.charts.title"] = "卡片图表",
        ["pluginconfig.charts.hint"] = "按插件声明的图表开关显示（与设置窗口【卡片管理】页同一配置落点）",
        ["pluginconfig.minicharts.title"] = "任务栏迷你图表",
        ["pluginconfig.minicharts.hint"] = "按插件声明的迷你图表开关显示（与设置窗口【任务栏迷你图表】页同一配置落点）",
        ["pluginconfig.getcookie"] = "获取登录态",
        ["pluginconfig.getcookie.tip"] = "自动启动独立 Edge 窗口并打开登录页，登录完成后自动获取 Cookie",
        // 通用
        ["common.cancel"] = "取消",
        ["common.save"] = "保存",
        // 历史窗口（req-069-005/006 i18n）
        ["history.window.title"] = "UsageMonitor - 历史用量",
        ["history.title"] = "历史用量",
        ["history.subtitle"] = "按 Provider、时间范围与图表类型回看用量趋势",
        ["history.providers.label"] = "已启用 Provider",
        ["history.range.label"] = "范围",
        ["history.chartkind.label"] = "图表",
        ["history.refresh"] = "刷新",
        ["history.export"] = "导出 CSV",
        ["history.empty.title"] = "暂无历史数据",
        ["history.empty.hint"] = "请先刷新数据或调整时间范围",
        ["history.empty.action"] = "去刷新数据",
        ["history.summary.title"] = "当前 Provider 摘要",
        ["history.summary.samplecount.format"] = "共 {0} 个采样点",
        ["history.stats.activedays"] = "活跃天数",
        ["history.stats.peak"] = "单日峰值用量",
        ["history.stats.average"] = "平均用量",
        ["history.slicer.label"] = "切片",
        ["history.col.date"] = "日期",
        ["history.col.refreshed"] = "刷新时间",
        ["history.col.maxpercent"] = "最高%",
        ["history.col.minpercent"] = "最低%",
        ["history.col.endpercent"] = "结束%",
        ["history.col.avgpercent"] = "平均%",
        ["history.col.snapshots"] = "采样数",
        ["history.status.loadingproviders"] = "正在加载 Provider 列表…",
        ["history.status.loading"] = "正在加载…",
        ["history.status.noprovider"] = "未勾选任何 Provider",
        ["history.status.nodata"] = "暂无任何历史数据，先在主窗口点几下'刷新'",
        ["history.status.loaded.format"] = "已加载 {0} 个 Provider，{1} 行刷新聚合",
        ["history.status.loadfailed.prefix"] = "加载失败：",
        ["history.export.dialog.title"] = "导出历史为 CSV",
        ["history.export.dialog.filter"] = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
        ["history.export.filename.format"] = "UsageMonitor-历史-{0:yyyyMMdd-HHmmss}.csv",
        ["history.export.success.message.format"] = "已导出到 {0}",
        ["history.export.fail.message"] = "导出失败，请查看 logs/UsageMonitor-*.log",
        ["history.export.success.title"] = "导出成功",
        ["history.export.fail.title"] = "导出失败",
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
        ["pluginconfig.charts.title"] = "Card charts",
        ["pluginconfig.charts.hint"] = "Toggle declared card charts on/off (same setting as the Card management page)",
        ["pluginconfig.minicharts.title"] = "Taskbar mini charts",
        ["pluginconfig.minicharts.hint"] = "Toggle declared mini charts on/off (same setting as the Taskbar mini charts page)",
        ["pluginconfig.getcookie"] = "Get login state",
        ["pluginconfig.getcookie.tip"] = "Launches a standalone Edge window to the login page and captures cookies after sign-in",
        ["common.cancel"] = "Cancel",
        ["common.save"] = "Save",
        // History window (req-069-005/006)
        ["history.window.title"] = "UsageMonitor - Usage History",
        ["history.title"] = "Usage History",
        ["history.subtitle"] = "Review usage trends by provider, time range and chart type",
        ["history.providers.label"] = "Enabled providers",
        ["history.range.label"] = "Range",
        ["history.chartkind.label"] = "Chart",
        ["history.refresh"] = "Refresh",
        ["history.export"] = "Export CSV",
        ["history.empty.title"] = "No history data yet",
        ["history.empty.hint"] = "Refresh data or adjust the time range",
        ["history.empty.action"] = "Refresh now",
        ["history.summary.title"] = "Current provider summary",
        ["history.summary.samplecount.format"] = "{0} sample points",
        ["history.stats.activedays"] = "Active days",
        ["history.stats.peak"] = "Daily peak usage",
        ["history.stats.average"] = "Average usage",
        ["history.slicer.label"] = "Slicer",
        ["history.col.date"] = "Date",
        ["history.col.refreshed"] = "Refreshed at",
        ["history.col.maxpercent"] = "Max %",
        ["history.col.minpercent"] = "Min %",
        ["history.col.endpercent"] = "End %",
        ["history.col.avgpercent"] = "Avg %",
        ["history.col.snapshots"] = "Samples",
        ["history.status.loadingproviders"] = "Loading provider list…",
        ["history.status.loading"] = "Loading…",
        ["history.status.noprovider"] = "No provider selected",
        ["history.status.nodata"] = "No history data yet — hit Refresh a few times on the main window",
        ["history.status.loaded.format"] = "Loaded {0} provider(s), {1} refresh aggregate row(s)",
        ["history.status.loadfailed.prefix"] = "Load failed: ",
        ["history.export.dialog.title"] = "Export history as CSV",
        ["history.export.dialog.filter"] = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
        ["history.export.filename.format"] = "UsageMonitor-history-{0:yyyyMMdd-HHmmss}.csv",
        ["history.export.success.message.format"] = "Exported to {0}",
        ["history.export.fail.message"] = "Export failed, see logs/UsageMonitor-*.log",
        ["history.export.success.title"] = "Export succeeded",
        ["history.export.fail.title"] = "Export failed",
    };
}
