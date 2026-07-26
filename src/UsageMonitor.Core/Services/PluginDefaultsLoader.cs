using System;
using System.IO;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 插件显示声明装载器（req-107 B7 / req-108 Task3）：从插件目录的 defaults.json 装载
/// <see cref="PluginManifest"/>（卡片 / 任务栏显示声明），并经 <see cref="PluginValidator"/> 校验。
/// <para>位于 Core 层（仅依赖 Core 类型），供主程序运行期装载与各插件 override <c>Card</c>/<c>Taskbar</c> 时复用；
/// 运行期加载与 --validate-plugin 自检共用 <see cref="PluginValidator"/> 同一份校验代码。
/// 本类不接入渲染管线——渲染切换（MainViewModel 由 <see cref="CardDeclaration"/> 驱动）属 req-107 B8。</para>
/// </summary>
public static class PluginDefaultsLoader
{
    private const string LogSource = "PluginDefaultsLoader";

    /// <summary>
    /// Stage A 声明包文件名（按优先级顺序）：三文件对应插件三部分（接入/取数/显示），
    /// defaults.json 为单文件兼容形态（优先级最低，拆分文件可覆盖它）。
    /// </summary>
    private static readonly string[] ManifestFileNames = { "plugin.json", "fetch.json", "display.json", "defaults.json" };

    /// <summary>
    /// 从插件目录装载声明清单（Stage A：支持 plugin.json / fetch.json / display.json / defaults.json 多文件合并）。
    /// <para>存在的文件逐个解析后按优先级合并，再对合并结果做语义校验（部分文件单独看可以不完整）。
    /// 任一文件 JSON 语法错误或合并后校验失败均返回 null（整包判失败）；目录无任何清单文件返回 null。</para>
    /// </summary>
    /// <param name="pluginDirectory">插件目录。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本（供 minSdkVersion 兼容校验）。</param>
    /// <returns>校验通过的合并清单；文件缺失或校验失败返回 null。</returns>
    public static PluginManifest? LoadFromDirectory(string pluginDirectory, Version? currentSdkVersion = null)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory)) return null;

        PluginManifest? merged = null;
        foreach (var fileName in ManifestFileNames)
        {
            var path = Path.Combine(pluginDirectory, fileName);
            if (!File.Exists(path)) continue;

            var part = ParseManifestFile(path);
            if (part == null) return null; // 语法错误已记日志，整包判失败
            merged = merged == null ? part : PluginManifest.Merge(merged, part);
        }
        if (merged == null) return null;

        // 对合并结果做语义校验（与 --validate-plugin 共用 PluginValidator）
        var result = new PluginValidationResult();
        PluginValidator.ValidateManifest(merged, currentSdkVersion, result);
        foreach (var warning in result.Warnings)
            FileLogger.Warn(LogSource, $"{pluginDirectory}: {warning}");
        if (!result.IsValid)
        {
            foreach (var error in result.Errors)
                FileLogger.Warn(LogSource, $"{pluginDirectory}: {error}");
            return null;
        }
        return merged;
    }

    /// <summary>解析单个清单文件为部分 <see cref="PluginManifest"/>（仅 JSON 语法层，不做语义校验）。
    /// <para>req-116：反序列化前先经 <see cref="PluginTextResolver.ResolveJson"/> 把 i18n: 键替换为当前语言译文，
    /// 使全部文案消费点零侵入获得多语言能力（语言切换后重载插件即重新解析）。</para></summary>
    /// <param name="path">清单文件绝对路径。</param>
    private static PluginManifest? ParseManifestFile(string path)
    {
        try
        {
            return PluginManifest.Load(PluginTextResolver.ResolveJson(File.ReadAllText(path)));
        }
        catch (Exception ex)
        {
            FileLogger.Warn(LogSource, $"解析 {path} 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从指定 defaults.json 文件路径装载并校验。
    /// </summary>
    /// <param name="defaultsJsonPath">defaults.json 绝对路径。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本。</param>
    /// <returns>校验通过的插件清单；解析失败或存在错误返回 null。</returns>
    public static PluginManifest? LoadFromFile(string defaultsJsonPath, Version? currentSdkVersion = null)
    {
        try
        {
            var json = File.ReadAllText(defaultsJsonPath);
            var result = PluginValidator.Validate(json, currentSdkVersion);
            foreach (var warning in result.Warnings)
                FileLogger.Warn(LogSource, $"{defaultsJsonPath}: {warning}");

            if (!result.IsValid)
            {
                foreach (var error in result.Errors)
                    FileLogger.Warn(LogSource, $"{defaultsJsonPath}: {error}");
                return null;
            }

            return PluginManifest.Load(json);
        }
        catch (Exception ex)
        {
            FileLogger.Warn(LogSource, $"装载 {defaultsJsonPath} 失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从程序集所在目录装载声明清单（供插件 override Card/Taskbar 时复用，清单文件随插件 DLL 同目录部署）。
    /// </summary>
    /// <param name="pluginAssemblyLocation">插件程序集文件路径（Assembly.Location）。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本。</param>
    public static PluginManifest? LoadFromAssemblyDirectory(string pluginAssemblyLocation, Version? currentSdkVersion = null)
    {
        if (string.IsNullOrWhiteSpace(pluginAssemblyLocation)) return null;
        var dir = Path.GetDirectoryName(pluginAssemblyLocation);
        return string.IsNullOrEmpty(dir) ? null : LoadFromDirectory(dir, currentSdkVersion);
    }
}
