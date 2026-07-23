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
    /// 从插件目录装载 defaults.json；文件不存在返回 null（插件走代码 override 或旧路径）。
    /// </summary>
    /// <param name="pluginDirectory">插件目录（含 defaults.json）。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本（供 minSdkVersion 兼容校验）。</param>
    /// <returns>校验通过的插件清单；文件缺失或校验失败返回 null。</returns>
    public static PluginManifest? LoadFromDirectory(string pluginDirectory, Version? currentSdkVersion = null)
    {
        var path = Path.Combine(pluginDirectory, "defaults.json");
        return File.Exists(path) ? LoadFromFile(path, currentSdkVersion) : null;
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
    /// 按 providerId 在 Plugins 目录下定位插件目录并装载其 defaults.json。
    /// </summary>
    /// <param name="providerId">插件 ID（用于匹配目录名，如 "MiniMax" 匹配 UsageMonitor.Plugin.MiniMax）。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本。</param>
    public static PluginManifest? LoadForProvider(string providerId, Version? currentSdkVersion = null)
    {
        var dir = ResolvePluginDirectory(providerId);
        return dir != null ? LoadFromDirectory(dir, currentSdkVersion) : null;
    }

    /// <summary>
    /// 从程序集所在目录装载 defaults.json（供插件 override Card/Taskbar 时复用，defaults.json 随插件 DLL 同目录部署）。
    /// </summary>
    /// <param name="pluginAssemblyLocation">插件程序集文件路径（Assembly.Location）。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本。</param>
    public static PluginManifest? LoadFromAssemblyDirectory(string pluginAssemblyLocation, Version? currentSdkVersion = null)
    {
        if (string.IsNullOrWhiteSpace(pluginAssemblyLocation)) return null;
        var dir = Path.GetDirectoryName(pluginAssemblyLocation);
        return string.IsNullOrEmpty(dir) ? null : LoadFromDirectory(dir, currentSdkVersion);
    }

    /// <summary>
    /// 定位插件目录：优先 BaseDirectory/Plugins/*{providerId}*/defaults.json，回退到 BaseDirectory/*{providerId}*/defaults.json。
    /// </summary>
    private static string? ResolvePluginDirectory(string providerId)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Plugins"),
            baseDir
        };
        foreach (var root in candidates)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (dir.IndexOf(providerId, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (File.Exists(Path.Combine(dir, "defaults.json"))) return dir;
            }
        }
        return null;
    }
}
