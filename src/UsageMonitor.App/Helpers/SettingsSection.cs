namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-073：设置窗口左侧导航的分区枚举。
/// <para>
/// 按「通用 / 显示 / 高级」三组分组，为 req-103（卡片排序）、req-104（多进度条）、
/// req-097（图表顺序）预留导航项；当前版本先实现已有的 7 个分区，预留项后续随需求加入。
/// </para>
/// </summary>
public enum SettingsSection
{
    // ===== 通用 =====
    /// <summary>常规设置（外观主题 / 刷新间隔 / 开机自启 / 历史点数 / 圆环阈值 / 环形图中心数字）</summary>
    General,

    /// <summary>插件管理（启用/禁用、任务栏显示模式、插件配置入口）</summary>
    Plugins,

    // ===== 显示 =====
    /// <summary>任务栏显示（全局默认 + 每 Provider 覆盖 + 环形图中心数字按 Provider）</summary>
    Taskbar,

    /// <summary>悬浮窗（托盘悬浮窗开关 / 延迟 / 触发区域）</summary>
    Tray,

    /// <summary>色阶（用量色阶 + 热力图色阶）</summary>
    ColorTier,

    // ===== 高级 =====
    /// <summary>安全（NTFS ACL 收紧 / Cookie 审计 / 过期清理）</summary>
    Security,

    /// <summary>诊断 / 日志（日志路径 / 打开文件夹 / 复制日志）</summary>
    Diagnostics,

    // ===== 个性化（req-103 / req-104 / req-097） =====
    /// <summary>req-103：卡片排序（拖拽调整主窗口卡片顺序）</summary>
    CardOrder,

    /// <summary>req-097：图表顺序（按 Provider 调整卡片图表顺序）</summary>
    ChartOrder,

    /// <summary>req-104：多进度条 / 数字多排显示字段选择</summary>
    MultiProgress,

    /// <summary>req-098：任务栏迷你图表（每个 Provider 的图表类型 / 内容 / Logo 显示配置）</summary>
    TaskbarMiniChart,
}
