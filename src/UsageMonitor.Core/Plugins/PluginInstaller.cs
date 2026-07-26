using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 插件安装结果（req-114）。
/// </summary>
public sealed class PluginInstallResult
{
    /// <summary>是否安装成功。</summary>
    public bool Success { get; init; }

    /// <summary>包名（plugins/&lt;包名&gt;/ 目标目录名）。</summary>
    public string? PackageName { get; init; }

    /// <summary>安装后的目标目录（成功时非空）。</summary>
    public string? InstalledPath { get; init; }

    /// <summary>失败原因（校验失败时另见 <see cref="Validation"/> 明细）。</summary>
    public string? Error { get; init; }

    /// <summary>同名包已存在且未指定覆盖：需要 UI 确认后带 overwrite=true 重新调用。</summary>
    public bool RequiresOverwriteConfirmation { get; init; }

    /// <summary>安装前预校验结果（供 UI 展示错误/警告明细）。</summary>
    public PluginValidationResult? Validation { get; init; }
}

/// <summary>
/// 插件安装器（req-114）：把外部位置的声明包（文件夹或 zip 压缩包）复制到 plugins/ 目录。
/// <para>流程：定位包根（含清单文件的目录，允许一层嵌套）→ PluginValidator 预校验 →
/// 复制到 plugins/&lt;包名&gt;/。zip 源先解压到临时目录（逐条目 zip-slip 路径校验）再走文件夹流程。
/// 复制期间调用方应 Pause 目录监视器，完成后 Resume 并显式触发一次重载。</para>
/// </summary>
public static class PluginInstaller
{
    /// <summary>声明包清单文件名（任一存在即视为包根，与 PluginManager 扫描约定一致）。</summary>
    private static readonly string[] ManifestFileNames = { "plugin.json", "fetch.json", "display.json", "defaults.json" };

    /// <summary>
    /// 从文件夹安装声明包：定位包根 → 预校验 → 复制到 plugins/&lt;包名&gt;/。
    /// </summary>
    /// <param name="sourceDirectory">插件来源目录（本身或其一层子目录含清单文件）。</param>
    /// <param name="pluginsRoot">plugins 根目录。</param>
    /// <param name="overwrite">同名包已存在时是否覆盖（false 时返回需确认结果）。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本（供预校验）。</param>
    public static PluginInstallResult InstallFromFolder(
        string sourceDirectory, string pluginsRoot, bool overwrite = false, Version? currentSdkVersion = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                return new PluginInstallResult { Error = $"来源目录不存在：{sourceDirectory}" };

            var packageRoot = LocatePackageRoot(sourceDirectory);
            if (packageRoot == null)
                return new PluginInstallResult { Error = "来源目录（含一层子目录）内未发现声明包清单文件（plugin.json / defaults.json 等）" };

            // 安装前预校验：错误即拒绝安装，避免坏包进入 plugins/
            var validation = PluginValidator.ValidatePackageDirectory(packageRoot, currentSdkVersion);
            if (!validation.IsValid)
                return new PluginInstallResult { Error = "声明包校验未通过，已取消安装", Validation = validation };

            var packageName = new DirectoryInfo(packageRoot).Name;
            var targetDir = Path.Combine(pluginsRoot, packageName);

            // 防御：目标路径必须仍在 plugins 根内（包名含路径分隔符等异常输入）
            var rootFull = Path.GetFullPath(pluginsRoot + Path.DirectorySeparatorChar);
            if (!Path.GetFullPath(targetDir + Path.DirectorySeparatorChar).StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return new PluginInstallResult { Error = $"非法包名：{packageName}" };

            if (Directory.Exists(targetDir) && !overwrite)
            {
                return new PluginInstallResult
                {
                    PackageName = packageName,
                    RequiresOverwriteConfirmation = true,
                    Error = $"plugins/{packageName} 已存在，需确认覆盖",
                    Validation = validation
                };
            }

            if (!Directory.Exists(pluginsRoot))
                Directory.CreateDirectory(pluginsRoot);
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
            CopyDirectory(packageRoot, targetDir);

            FileLogger.Info("PluginInstaller", $"已安装声明包：{packageName} → {targetDir}");
            return new PluginInstallResult
            {
                Success = true,
                PackageName = packageName,
                InstalledPath = targetDir,
                Validation = validation
            };
        }
        catch (Exception ex)
        {
            FileLogger.Error("PluginInstaller", $"文件夹安装失败：{sourceDirectory} - {ex.Message}", ex);
            return new PluginInstallResult { Error = $"安装失败：{ex.Message}" };
        }
    }

    /// <summary>
    /// 从 zip 压缩包安装声明包：逐条目 zip-slip 校验后解压到临时目录，再走文件夹安装流程。
    /// </summary>
    /// <param name="zipPath">zip 文件路径。</param>
    /// <param name="pluginsRoot">plugins 根目录。</param>
    /// <param name="overwrite">同名包已存在时是否覆盖。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本（供预校验）。</param>
    public static PluginInstallResult InstallFromZip(
        string zipPath, string pluginsRoot, bool overwrite = false, Version? currentSdkVersion = null)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return new PluginInstallResult { Error = $"压缩包不存在：{zipPath}" };

        var tempDir = Path.Combine(Path.GetTempPath(), "UsageMonitor.PluginInstall." + Guid.NewGuid().ToString("N"));
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                // 先全量做 zip-slip 校验，任一条目非法即整包拒绝（不落任何文件）
                foreach (var entry in archive.Entries)
                {
                    if (!IsSafeZipEntry(entry.FullName))
                        return new PluginInstallResult { Error = $"压缩包含非法路径条目，已拒绝安装：{entry.FullName}" };
                }

                Directory.CreateDirectory(tempDir);
                var tempFull = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);
                foreach (var entry in archive.Entries)
                {
                    var destPath = Path.GetFullPath(Path.Combine(tempDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    // 双保险：规范化后仍必须落在临时目录内
                    if (!destPath.StartsWith(tempFull, StringComparison.OrdinalIgnoreCase))
                        return new PluginInstallResult { Error = $"压缩包含越界路径条目，已拒绝安装：{entry.FullName}" };

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // 目录条目
                        Directory.CreateDirectory(destPath);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }

            // 解压后走文件夹流程；zip 根即多个文件时临时目录本身就是包根
            var result = InstallFromFolder(tempDir, pluginsRoot, overwrite, currentSdkVersion);

            // 包名为临时 GUID 目录名时改用 zip 文件名重装（zip 根直接平铺清单文件的场景）
            if (result.Success && result.PackageName != null
                && result.PackageName.StartsWith("UsageMonitor.PluginInstall.", StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(result.InstalledPath!, recursive: true);
                var renamed = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(zipPath));
                Directory.CreateDirectory(renamed);
                foreach (var file in Directory.GetFiles(tempDir))
                    File.Move(file, Path.Combine(renamed, Path.GetFileName(file)));
                foreach (var dir in Directory.GetDirectories(tempDir).Where(d => d != renamed))
                    Directory.Move(dir, Path.Combine(renamed, new DirectoryInfo(dir).Name));
                result = InstallFromFolder(renamed, pluginsRoot, overwrite, currentSdkVersion);
            }
            return result;
        }
        catch (InvalidDataException ex)
        {
            return new PluginInstallResult { Error = $"压缩包格式无效：{ex.Message}" };
        }
        catch (Exception ex)
        {
            FileLogger.Error("PluginInstaller", $"zip 安装失败：{zipPath} - {ex.Message}", ex);
            return new PluginInstallResult { Error = $"安装失败：{ex.Message}" };
        }
        finally
        {
            // 清理临时解压目录（失败不影响结果）
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* 临时目录清理失败可忽略 */ }
        }
    }

    /// <summary>
    /// zip-slip 条目路径安全校验：拒绝 ..、绝对路径、盘符与空路径（供单测直接验证）。
    /// </summary>
    /// <param name="entryFullName">zip 条目的原始路径。</param>
    public static bool IsSafeZipEntry(string entryFullName)
    {
        if (string.IsNullOrWhiteSpace(entryFullName)) return false;
        // 拒绝盘符（C:）与绝对路径（/foo、\foo）
        if (entryFullName.Contains(':')) return false;
        if (entryFullName.StartsWith("/") || entryFullName.StartsWith("\\")) return false;
        // 拒绝任何 .. 路径段（同时覆盖 / 与 \ 分隔符）
        var segments = entryFullName.Split('/', '\\');
        return segments.All(s => s != "..");
    }

    /// <summary>
    /// 定位包根目录：本身含清单文件即为根；否则在一层子目录中找唯一含清单文件的目录。
    /// </summary>
    /// <param name="directory">候选目录。</param>
    private static string? LocatePackageRoot(string directory)
    {
        if (ContainsManifest(directory)) return directory;

        var candidates = Directory.GetDirectories(directory).Where(ContainsManifest).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>判断目录是否直接包含任一声明包清单文件。</summary>
    /// <param name="directory">待判断目录。</param>
    private static bool ContainsManifest(string directory)
        => ManifestFileNames.Any(f => File.Exists(Path.Combine(directory, f)));

    /// <summary>
    /// 递归复制目录（含子目录与全部文件）。
    /// </summary>
    /// <param name="sourceDir">源目录。</param>
    /// <param name="targetDir">目标目录（自动创建）。</param>
    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(targetDir, new DirectoryInfo(dir).Name));
    }
}
