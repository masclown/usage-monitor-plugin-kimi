using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 插件语言包加载器（req-116）：读取声明包 <c>i18n/&lt;lang&gt;.json</c>（扁平 key→text）并注册进 <see cref="I18n"/>。
/// <para>安全约束：仅接受 <c>plugin.</c> 前缀的键（防止插件覆盖宿主 UI 词条），非法键跳过记日志。
/// 插件热重载前由 PluginManager 统一 <see cref="I18n.UnregisterByPrefix"/> 清除旧词条。</para>
/// </summary>
public static class PluginLanguagePackLoader
{
    private const string LogSource = "PluginLanguagePack";

    /// <summary>插件文案键强制前缀（防宿主词条劫持）。</summary>
    public const string RequiredKeyPrefix = "plugin.";

    /// <summary>
    /// 读取声明包目录下的全部语言包（不注册）：lang → (key → text)。
    /// <para>供校验器检查 i18n key 完整性；文件解析失败跳过记日志。</para>
    /// </summary>
    /// <param name="pluginDirectory">声明包目录。</param>
    public static Dictionary<string, Dictionary<string, string>> ReadLanguagePacks(string pluginDirectory)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var i18nDir = Path.Combine(pluginDirectory, "i18n");
        if (!Directory.Exists(i18nDir)) return result;

        foreach (var file in Directory.GetFiles(i18nDir, "*.json"))
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(lang)) continue;
            try
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                if (entries == null) continue;
                result[lang] = entries;
            }
            catch (Exception ex)
            {
                FileLogger.Warn(LogSource, $"语言包解析失败，跳过: {file} - {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// 加载并注册声明包语言包：过滤非 <c>plugin.</c> 前缀键后逐语言 <see cref="I18n.Register"/>。
    /// </summary>
    /// <param name="pluginDirectory">声明包目录。</param>
    /// <returns>注册的语言数。</returns>
    public static int LoadAndRegister(string pluginDirectory)
    {
        var packs = ReadLanguagePacks(pluginDirectory);
        var registered = 0;
        foreach (var (lang, entries) in packs)
        {
            var safe = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, text) in entries)
            {
                if (!key.StartsWith(RequiredKeyPrefix, StringComparison.Ordinal))
                {
                    FileLogger.Warn(LogSource, $"语言包键未以 {RequiredKeyPrefix} 开头，跳过: {key} ({pluginDirectory})");
                    continue;
                }
                safe[key] = text;
            }
            if (safe.Count == 0) continue;
            I18n.Register(lang, safe);
            registered++;
        }
        if (registered > 0)
            FileLogger.Info(LogSource, $"已注册语言包 {registered} 种语言: {Path.GetFileName(pluginDirectory)}");
        return registered;
    }
}
