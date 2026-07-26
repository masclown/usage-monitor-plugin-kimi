using System;
using System.Collections.Generic;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-110：凭据配置探测助手——判断 Provider 是否已配置可用凭据。
/// <para>供 RefreshService（Q1 刷新门控④“账号未配置凭据不刷新”）与 App 层 DisplayModule
/// （待命卡“未配置”引导文案）共用同一份判定逻辑，避免两处口径漂移。</para>
/// <para>Phase 1 凭据为 Provider 级（Cookie/ApiKey 存 ProviderConfigs + cookies/&lt;Provider&gt;.json）；
/// Phase 2 凭据下沉账号级后本判定同步扩展账号维度。</para>
/// </summary>
public static class CredentialProbe
{
    /// <summary>
    /// 判断是否已配置凭据：配置中 Cookie / ApiKey 任一非空，或插件声明的 Password 型凭据字段任一非空，
    /// 或已保存浏览器登录态文件。
    /// <para>req-110 P2：传入 <paramref name="accountId"/> 时按账号维度探测——
    /// cookie 文件优先 <c>cookies/{Provider}.{Account}.json</c>，缺失回退 Provider 级旧文件。</para>
    /// <para>声明式插件（如 DeepSeek 的 UserToken）凭据字段名非 Cookie/ApiKey，需传入
    /// <paramref name="configFields"/>（插件 ConfigFields）以识别 Password 型凭据字段。</para>
    /// </summary>
    /// <param name="providerId">Provider ID。</param>
    /// <param name="config">生效配置（Provider 级或账号生效配置，已解密）。</param>
    /// <param name="accountId">账号 ID（可空 = Provider 级探测）。</param>
    /// <param name="configFields">插件声明的配置字段（可空）；非空时其中 Password 型字段任一非空即视为已配凭据。</param>
    public static bool HasConfiguredCredential(string providerId, ProviderConfig config, string? accountId = null,
        IReadOnlyList<ConfigField>? configFields = null)
    {
        if (!string.IsNullOrWhiteSpace(config.GetValue("Cookie")) ||
            !string.IsNullOrWhiteSpace(config.GetValue("ApiKey")))
            return true;
        // 插件声明的 Password 型凭据字段（如 DeepSeek UserToken）任一非空即视为已配凭据。
        if (configFields != null)
        {
            foreach (var field in configFields)
            {
                if (field.FieldType != ConfigFieldType.Password) continue;
                if (!string.IsNullOrWhiteSpace(config.GetValue(field.Key))) return true;
            }
        }
        try
        {
            var saved = BrowserLoginService.LoadCookieData(providerId, accountId);
            return saved != null && !string.IsNullOrWhiteSpace(saved.Cookie);
        }
        catch
        {
            return false;
        }
    }
}
