namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-073：设置窗口左侧导航的分区枚举。
/// <para>
/// S3 重构：已删除「任务栏显示 / 卡片排序 / 图表顺序 / 多进度条 / 卡片图表与数据组」5 个旧分区，
/// 后续由【插件管理重构】【卡片管理】【迷你图表同构】等新页接替。
/// </para>
/// </summary>
public enum SettingsSection
{
    // ===== 通用 =====
    /// <summary>常规设置（外观主题 / 刷新间隔 / 开机自启 / 全局任务栏模式）</summary>
    General,

    /// <summary>修复4：历史用量（圆环图阈值 / 环形图中心数字——历史窗口圆环图专属设置）</summary>
    HistoryUsage,

    /// <summary>插件管理（启用/禁用、任务栏显示模式、插件配置入口）</summary>
    Plugins,

    /// <summary>S2：卡片管理（三级折叠：账号 → 图表 → 数据组 + tooltip 字段多选）</summary>
    CardManage,

    // ===== 显示 =====
    /// <summary>悬浮窗（托盘悬浮窗开关 / 延迟 / 触发区域）</summary>
    Tray,

    /// <summary>色阶（用量色阶 + 热力图色阶）</summary>
    ColorTier,

    // ===== 高级 =====
    /// <summary>安全（NTFS ACL 收紧 / Cookie 审计 / 过期清理）</summary>
    Security,

    /// <summary>诊断 / 日志（日志路径 / 打开文件夹 / 复制日志）</summary>
    Diagnostics,

    // ===== 个性化 =====
    /// <summary>req-098：任务栏迷你图表（每个 Provider 的图表类型 / 内容 / Logo 显示配置 + Mini 图表可见性）</summary>
    TaskbarMiniChart,
}
