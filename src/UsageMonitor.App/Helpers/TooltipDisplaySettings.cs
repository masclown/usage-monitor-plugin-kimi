namespace UsageMonitor.App.Helpers;

/// <summary>
/// 问题10：图表 tooltip 显示选项的静态持有者。
/// <para>图表控件（折线/堆叠柱状/面积等）为纯渲染层，不直接持有 ConfigService；
/// 由 MainViewModel 在启动与配置变更时把 <c>AppSettings.TooltipShowFieldName</c>
/// 同步到本静态属性，控件构建 tooltip 时读取，实现"数据前是否包含字段名称"的全局开关。</para>
/// </summary>
public static class TooltipDisplaySettings
{
    /// <summary>tooltip 数据前是否包含字段名称（如"用量 197.42M" vs "197.42M"），默认开启。</summary>
    public static bool ShowFieldName { get; set; } = true;
}
