using UsageMonitor.Core.Services;

namespace UsageMonitor.Core.Models;

/// <summary>
/// req-013：通用 <see cref="ConfigField"/> 字段工厂。
/// <para>
/// 把 Deepseek / OpenAI 等 API 插件重复声明的 <c>ApiKey / BaseUrl / Organization</c>
/// 字段集中到一处工厂方法，三个插件的 <see cref="IUsageProvider.ConfigFields"/> 实现
/// 改为单行调用，减少约 30 行模板代码 + i18n key 拼接。
/// </para>
/// <para>
/// <b>设计原则：仅做"声明集中化"，不改 i18n key 模板与字段 key 名称</b>，确保用户已保存的
/// <c>config.json</c> 不需重新配置：
/// <list type="bullet">
///   <item><description>字段 key 与重构前完全一致：<c>ApiKey / BaseUrl / Organization</c></description></item>
///   <item><description>i18n key 模板：<c>plugin.{providerId}.field.{FieldKey}.{name|placeholder}</c></description></item>
///   <item><description>字段类型 / isRequired / defaultValue / placeholder 全部对齐</description></item>
/// </list>
/// </para>
/// <para>
/// MiniMax 走特殊路径（Cookie + Region + 4 个进度条开关），不参与本次抽象；保持其自定义
/// <see cref="ConfigField"/> 序列。
/// </para>
/// </summary>
public static class StandardConfigFields
{
    /// <summary>
    /// 标准 <c>ApiKey</c> 字段（<see cref="ConfigFieldType.Password"/>，<c>isRequired: true</c>）。
    /// i18n key = <c>plugin.{providerId}.field.ApiKey.{name,placeholder}</c>。
    /// </summary>
    /// <param name="providerId">插件 ID（用于拼接 i18n key），按惯例应传入 <see cref="IUsageProvider.ProviderId"/>。</param>
    public static ConfigField ApiKey(string providerId)
    {
        return new ConfigField(
            "ApiKey",
            I18n.T($"plugin.{providerId}.field.ApiKey.name"),
            ConfigFieldType.Password,
            isRequired: true,
            defaultValue: null,
            placeholder: I18n.T($"plugin.{providerId}.field.ApiKey.placeholder"));
    }

    /// <summary>
    /// 标准 <c>BaseUrl</c> 字段（<see cref="ConfigFieldType.Text"/>，<c>isRequired: false</c>，带 <c>defaultValue</c>）。
    /// i18n key = <c>plugin.{providerId}.field.BaseUrl.{name,placeholder}</c>。
    /// </summary>
    /// <param name="providerId">插件 ID（用于拼接 i18n key）。</param>
    /// <param name="defaultUrl">BaseUrl 的默认值（不同插件指向不同官方 API 地址，例如 Deepseek / OpenAI）。</param>
    public static ConfigField BaseUrl(string providerId, string defaultUrl)
    {
        return new ConfigField(
            "BaseUrl",
            I18n.T($"plugin.{providerId}.field.BaseUrl.name"),
            ConfigFieldType.Text,
            isRequired: false,
            defaultValue: defaultUrl,
            placeholder: I18n.T($"plugin.{providerId}.field.BaseUrl.placeholder"));
    }

    /// <summary>
    /// 标准 <c>Organization</c> 字段（<see cref="ConfigFieldType.Text"/>，<c>isRequired: false</c>）。
    /// 主要给 OpenAI 使用（OpenAI 支持 organization 级账单）；其它插件通常不调用。
    /// i18n key = <c>plugin.{providerId}.field.Organization.{name,placeholder}</c>。
    /// </summary>
    public static ConfigField Organization(string providerId)
    {
        return new ConfigField(
            "Organization",
            I18n.T($"plugin.{providerId}.field.Organization.name"),
            ConfigFieldType.Text,
            isRequired: false,
            defaultValue: null,
            placeholder: I18n.T($"plugin.{providerId}.field.Organization.placeholder"));
    }
}
