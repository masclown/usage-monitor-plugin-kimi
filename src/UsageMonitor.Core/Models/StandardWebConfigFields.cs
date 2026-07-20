using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-086：网页插件标准配置字段工厂。
/// <para>
/// 为网页插件提供常用的配置字段声明，减少重复代码。
/// 字段 key 与 i18n key 模板：<c>plugin.{providerId}.field.{FieldKey}.{name|placeholder}</c>。
/// </para>
/// </summary>
public static class StandardWebConfigFields
{
    /// <summary>
    /// Cookie 字段（<see cref="ConfigFieldType.Password"/>，<c>isRequired: false</c>）。
    /// 用于存储浏览器登录态 Cookie。
    /// </summary>
    /// <param name="providerId">插件 ID</param>
    public static ConfigField Cookie(string providerId)
    {
        return new ConfigField(
            "Cookie",
            I18n.T($"plugin.{providerId}.field.Cookie.name"),
            ConfigFieldType.Password,
            isRequired: false,
            defaultValue: null,
            placeholder: I18n.T($"plugin.{providerId}.field.Cookie.placeholder"));
    }

    /// <summary>
    /// Region 字段（<see cref="ConfigFieldType.Select"/>，<c>isRequired: false</c>）。
    /// 用于选择服务区域（如 CN / Global）。
    /// </summary>
    /// <param name="providerId">插件 ID</param>
    /// <param name="defaultRegion">默认区域（如 "CN"）</param>
    /// <param name="options">可选区域列表</param>
    public static ConfigField Region(string providerId, string defaultRegion = "CN", params string[] options)
    {
        return new ConfigField(
            "Region",
            I18n.T($"plugin.{providerId}.field.Region.name"),
            ConfigFieldType.Select,
            isRequired: false,
            defaultValue: defaultRegion,
            options: options.Length > 0 ? options : new[] { "CN", "Global" });
    }

    /// <summary>
    /// 自动刷新开关（<see cref="ConfigFieldType.Boolean"/>，<c>isRequired: false</c>）。
    /// 用于控制是否自动刷新用量数据。
    /// </summary>
    /// <param name="providerId">插件 ID</param>
    /// <param name="defaultValue">默认值（true/false）</param>
    public static ConfigField AutoRefresh(string providerId, bool defaultValue = true)
    {
        return new ConfigField(
            "AutoRefresh",
            I18n.T($"plugin.{providerId}.field.AutoRefresh.name"),
            ConfigFieldType.Boolean,
            isRequired: false,
            defaultValue: defaultValue ? "true" : "false");
    }

    /// <summary>
    /// 代理设置字段（<see cref="ConfigFieldType.Text"/>，<c>isRequired: false</c>）。
    /// 用于配置 HTTP 代理（如 http://127.0.0.1:7890）。
    /// </summary>
    /// <param name="providerId">插件 ID</param>
    public static ConfigField Proxy(string providerId)
    {
        return new ConfigField(
            "Proxy",
            I18n.T($"plugin.{providerId}.field.Proxy.name"),
            ConfigFieldType.Text,
            isRequired: false,
            defaultValue: null,
            placeholder: I18n.T($"plugin.{providerId}.field.Proxy.placeholder"));
    }

    /// <summary>
    /// 无头模式开关（<see cref="ConfigFieldType.Boolean"/>，<c>isRequired: false</c>）。
    /// 用于控制浏览器是否以无头模式运行（调试用）。
    /// </summary>
    /// <param name="providerId">插件 ID</param>
    /// <param name="defaultValue">默认值（true/false）</param>
    public static ConfigField Headless(string providerId, bool defaultValue = false)
    {
        return new ConfigField(
            "Headless",
            I18n.T($"plugin.{providerId}.field.Headless.name"),
            ConfigFieldType.Boolean,
            isRequired: false,
            defaultValue: defaultValue ? "true" : "false");
    }

    /// <summary>
    /// 显示进度条开关（<see cref="ConfigFieldType.Boolean"/>，<c>isRequired: false</c>）。
    /// 用于控制卡片中某个进度条的显示/隐藏。
    /// </summary>
    /// <param name="providerId">插件 ID</param>
    /// <param name="barKey">进度条 key（如 "Show5hBar"）</param>
    /// <param name="defaultValue">默认值（true/false）</param>
    public static ConfigField ShowBar(string providerId, string barKey, bool defaultValue = true)
    {
        return new ConfigField(
            barKey,
            I18n.T($"plugin.{providerId}.field.{barKey}.name"),
            ConfigFieldType.Boolean,
            isRequired: false,
            defaultValue: defaultValue ? "true" : "false");
    }
}
