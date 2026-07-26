using System.IO;
using System.IO.Compression;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-111/113/114：防抖目录监视器、目录级聚合校验与插件安装器测试。
/// </summary>
public class PluginHotReloadInstallTests
{
    /// <summary>最小合法声明包 JSON（仅 providerId + displayName，语义校验可通过）。</summary>
    private const string MinimalDefaultsJson = """{ "providerId": "TestProv", "displayName": "Test Provider" }""";

    /// <summary>在指定目录写入最小合法 defaults.json，构造一个可通过校验的声明包。</summary>
    private static void WriteMinimalPackage(string dir, string providerId = "TestProv")
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "defaults.json"),
            $$"""{ "providerId": "{{providerId}}", "displayName": "{{providerId}}" }""");
    }

    // ==================== DebouncedDirectoryWatcher ====================

    /// <summary>验证：防抖窗口内的多次变更通知只触发一次回调。</summary>
    [Fact]
    public void Watcher_MultipleNotifies_CoalescedToSingleCallback()
    {
        using var temp = new TempDir();
        var count = 0;
        using var done = new ManualResetEventSlim(false);
        using var watcher = new DebouncedDirectoryWatcher(temp.Path, () =>
        {
            Interlocked.Increment(ref count);
            done.Set();
        }, debounceMs: 100);

        for (var i = 0; i < 5; i++) watcher.NotifyChanged();

        Assert.True(done.Wait(TimeSpan.FromSeconds(3)), "防抖回调未在超时时间内触发");
        // 再等一个防抖窗口，确认没有第二次回调
        Thread.Sleep(300);
        Assert.Equal(1, count);
    }

    /// <summary>验证：Pause 期间的变更通知被丢弃，Resume 后不补发。</summary>
    [Fact]
    public void Watcher_PausedNotifies_DroppedAndNotReplayed()
    {
        using var temp = new TempDir();
        var count = 0;
        using var watcher = new DebouncedDirectoryWatcher(temp.Path, () => Interlocked.Increment(ref count), debounceMs: 50);

        watcher.Pause();
        watcher.NotifyChanged();
        watcher.NotifyChanged();
        watcher.Resume();

        Thread.Sleep(300);
        Assert.Equal(0, count);
    }

    /// <summary>验证：Resume 后的新变更正常触发回调（Pause 只影响挂起期间的事件）。</summary>
    [Fact]
    public void Watcher_NotifyAfterResume_FiresCallback()
    {
        using var temp = new TempDir();
        using var done = new ManualResetEventSlim(false);
        using var watcher = new DebouncedDirectoryWatcher(temp.Path, () => done.Set(), debounceMs: 50);

        watcher.Pause();
        watcher.NotifyChanged();
        watcher.Resume();
        watcher.NotifyChanged();

        Assert.True(done.Wait(TimeSpan.FromSeconds(3)), "Resume 后的变更未触发回调");
    }

    /// <summary>验证：Dispose 后的变更通知不再触发回调。</summary>
    [Fact]
    public void Watcher_AfterDispose_NoCallback()
    {
        using var temp = new TempDir();
        var count = 0;
        var watcher = new DebouncedDirectoryWatcher(temp.Path, () => Interlocked.Increment(ref count), debounceMs: 50);
        watcher.Dispose();
        watcher.NotifyChanged();

        Thread.Sleep(200);
        Assert.Equal(0, count);
    }

    // ==================== PluginValidator.ValidatePackageDirectory ====================

    /// <summary>验证：合法声明包目录聚合校验通过。</summary>
    [Fact]
    public void ValidatePackageDirectory_ValidPackage_Passes()
    {
        using var temp = new TempDir();
        WriteMinimalPackage(temp.Path);

        var result = PluginValidator.ValidatePackageDirectory(temp.Path);
        Assert.True(result.IsValid, result.ToReport());
    }

    /// <summary>验证：目录无任何清单文件时报错。</summary>
    [Fact]
    public void ValidatePackageDirectory_NoManifest_Fails()
    {
        using var temp = new TempDir();
        var result = PluginValidator.ValidatePackageDirectory(temp.Path);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("未发现任何清单文件"));
    }

    /// <summary>验证：JSON 语法错误被收集为错误明细而非抛异常。</summary>
    [Fact]
    public void ValidatePackageDirectory_BrokenJson_CollectsError()
    {
        using var temp = new TempDir();
        File.WriteAllText(temp.Combine("defaults.json"), "{ not valid json");

        var result = PluginValidator.ValidatePackageDirectory(temp.Path);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("JSON 语法错误"));
    }

    // ==================== PluginInstaller：zip-slip 安全 ====================

    /// <summary>验证：zip 条目路径安全判定——.. / 绝对路径 / 盘符全部拒绝，正常相对路径放行。</summary>
    [Theory]
    [InlineData("../evil.txt", false)]
    [InlineData("..\\evil.txt", false)]
    [InlineData("pkg/../../evil.txt", false)]
    [InlineData("/abs/path.txt", false)]
    [InlineData("\\abs\\path.txt", false)]
    [InlineData("C:/evil.txt", false)]
    [InlineData("C:\\evil.txt", false)]
    [InlineData("", false)]
    [InlineData("pkg/defaults.json", true)]
    [InlineData("defaults.json", true)]
    [InlineData("pkg/i18n/zh-CN.json", true)]
    public void IsSafeZipEntry_RejectsTraversalAndAbsolutePaths(string entry, bool expected)
    {
        Assert.Equal(expected, PluginInstaller.IsSafeZipEntry(entry));
    }

    /// <summary>验证：含 ../ 路径条目的恶意 zip 被整包拒绝，不落任何文件到 plugins 根。</summary>
    [Fact]
    public void InstallFromZip_MaliciousEntry_Rejected()
    {
        using var temp = new TempDir();
        var zipPath = temp.Combine("evil.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("pwned");
        }

        var pluginsRoot = temp.Combine("plugins");
        var result = PluginInstaller.InstallFromZip(zipPath, pluginsRoot);

        Assert.False(result.Success);
        Assert.Contains("非法路径", result.Error);
        Assert.False(File.Exists(temp.Combine("evil.txt")));
    }

    // ==================== PluginInstaller：文件夹 / zip 安装 ====================

    /// <summary>验证：文件夹来源（根目录直接含清单）安装成功并复制到 plugins/&lt;包名&gt;/。</summary>
    [Fact]
    public void InstallFromFolder_ValidPackage_InstallsToPluginsRoot()
    {
        using var temp = new TempDir();
        var source = temp.Combine("MyPlugin");
        WriteMinimalPackage(source);
        var pluginsRoot = temp.Combine("plugins");

        var result = PluginInstaller.InstallFromFolder(source, pluginsRoot);

        Assert.True(result.Success, result.Error);
        Assert.Equal("MyPlugin", result.PackageName);
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "MyPlugin", "defaults.json")));
    }

    /// <summary>验证：一层嵌套（选中的目录里只有一个包目录）也能定位包根。</summary>
    [Fact]
    public void InstallFromFolder_NestedOneLevel_LocatesPackageRoot()
    {
        using var temp = new TempDir();
        var outer = temp.Combine("Download");
        var inner = Path.Combine(outer, "NestedPlugin");
        WriteMinimalPackage(inner);
        var pluginsRoot = temp.Combine("plugins");

        var result = PluginInstaller.InstallFromFolder(outer, pluginsRoot);

        Assert.True(result.Success, result.Error);
        Assert.Equal("NestedPlugin", result.PackageName);
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "NestedPlugin", "defaults.json")));
    }

    /// <summary>验证：校验不通过的包拒绝安装并携带校验明细。</summary>
    [Fact]
    public void InstallFromFolder_InvalidPackage_RejectedWithValidation()
    {
        using var temp = new TempDir();
        var source = temp.Combine("BadPlugin");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "defaults.json"), "{ broken");
        var pluginsRoot = temp.Combine("plugins");

        var result = PluginInstaller.InstallFromFolder(source, pluginsRoot);

        Assert.False(result.Success);
        Assert.NotNull(result.Validation);
        Assert.False(Directory.Exists(Path.Combine(pluginsRoot, "BadPlugin")));
    }

    /// <summary>验证：同名包已存在且未指定覆盖时，返回需确认结果且不改动现有包。</summary>
    [Fact]
    public void InstallFromFolder_ExistingPackage_RequiresOverwriteConfirmation()
    {
        using var temp = new TempDir();
        var source = temp.Combine("MyPlugin");
        WriteMinimalPackage(source, "NewProv");
        var pluginsRoot = temp.Combine("plugins");
        var existing = Path.Combine(pluginsRoot, "MyPlugin");
        WriteMinimalPackage(existing, "OldProv");

        var result = PluginInstaller.InstallFromFolder(source, pluginsRoot);
        Assert.False(result.Success);
        Assert.True(result.RequiresOverwriteConfirmation);
        Assert.Contains("OldProv", File.ReadAllText(Path.Combine(existing, "defaults.json")));

        // 带 overwrite 重试后覆盖成功
        var retry = PluginInstaller.InstallFromFolder(source, pluginsRoot, overwrite: true);
        Assert.True(retry.Success, retry.Error);
        Assert.Contains("NewProv", File.ReadAllText(Path.Combine(existing, "defaults.json")));
    }

    /// <summary>验证：zip 来源（含顶层包目录）安装成功。</summary>
    [Fact]
    public void InstallFromZip_ValidPackage_Installs()
    {
        using var temp = new TempDir();
        var stage = temp.Combine("stage", "ZipPlugin");
        WriteMinimalPackage(stage);
        var zipPath = temp.Combine("ZipPlugin.zip");
        ZipFile.CreateFromDirectory(temp.Combine("stage"), zipPath);
        var pluginsRoot = temp.Combine("plugins");

        var result = PluginInstaller.InstallFromZip(zipPath, pluginsRoot);

        Assert.True(result.Success, result.Error);
        Assert.Equal("ZipPlugin", result.PackageName);
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "ZipPlugin", "defaults.json")));
    }

    /// <summary>验证：zip 根直接平铺清单文件时，包名回退为 zip 文件名。</summary>
    [Fact]
    public void InstallFromZip_FlatEntries_UsesZipFileNameAsPackage()
    {
        using var temp = new TempDir();
        var stage = temp.Combine("stage");
        WriteMinimalPackage(stage);
        var zipPath = temp.Combine("FlatPlugin.zip");
        ZipFile.CreateFromDirectory(stage, zipPath);
        var pluginsRoot = temp.Combine("plugins");

        var result = PluginInstaller.InstallFromZip(zipPath, pluginsRoot);

        Assert.True(result.Success, result.Error);
        Assert.Equal("FlatPlugin", result.PackageName);
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "FlatPlugin", "defaults.json")));
    }
}
