using UsageMonitor.Core.Models;

namespace UsageMonitor.Plugin.Deepseek;

/// <summary>
/// req-084：DeepSeek 双模式配置。
/// 支持 API 模式和网页模式切换。
/// </summary>
public static class DeepseekConfig
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
    public static string GetQueryMode(ProviderConfig config)
    {
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
