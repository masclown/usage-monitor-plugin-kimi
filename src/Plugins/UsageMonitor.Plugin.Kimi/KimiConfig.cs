using UsageMonitor.Core.Models;

namespace UsageMonitor.Plugin.Kimi;

/// <summary>
/// req-085：Kimi 双模式配置。
/// 支持 API 模式和网页模式切换。
/// </summary>
public static class KimiConfig
{
    /// <summary>配置键：查询模式（api / web）</summary>
    public const string ModeKey = "QueryMode";

    /// <summary>API 模式标识</summary>
    public const string ModeApi = "api";

    /// <summary>网页模式标识</summary>
    public const string ModeWeb = "web";

    /// <summary>
    /// 获取当前配置的查询模式。
    /// 默认返回 API 模式（保持向后兼容）。
    /// </summary>
    /// <param name="config">插件配置</param>
    /// <returns>查询模式字符串</returns>
    public static string GetQueryMode(ProviderConfig? config)
    {
        // req-fix-Kimi-GetQueryModeNull：处理 null config（GetValue 抛 NRE）。
        // 场景：PluginConfigWindow 打开时调用 KimiConfig.GetQueryMode 检查模式，
        // 但 ProviderUsageViewModel.ConfigFields 是 instance property，每次访问都重新计算。
        // 如果 _currentConfigSnapshot 还未注入（装配时序问题）或 GetValue 失败，返回默认 API 模式。
        if (config == null) return ModeApi;
        var mode = config.GetValue(ModeKey)?.Trim().ToLowerInvariant();
        return mode switch
        {
            ModeWeb => ModeWeb,
            _ => ModeApi
        };
    }

    /// <summary>
    /// 判断当前是否为网页模式。
    /// </summary>
    /// <param name="config">插件配置</param>
    /// <returns>是否为网页模式</returns>
    public static bool IsWebMode(ProviderConfig config)
    {
        return GetQueryMode(config) == ModeWeb;
    }

    /// <summary>
    /// 判断当前是否为 API 模式。
    /// </summary>
    /// <param name="config">插件配置</param>
    /// <returns>是否为 API 模式</returns>
    public static bool IsApiMode(ProviderConfig config)
    {
        return GetQueryMode(config) == ModeApi;
    }

    /// <summary>
    /// 创建模式选择配置字段。
    /// </summary>
    /// <param name="providerId">插件 ID</param>
    /// <returns>配置字段</returns>
    public static ConfigField CreateModeField(string providerId)
    {
        return new ConfigField(
            ModeKey,
            "查询模式",
            ConfigFieldType.Select,
            isRequired: false,
            defaultValue: ModeApi,
            options: new[] { ModeApi, ModeWeb });
    }
}
