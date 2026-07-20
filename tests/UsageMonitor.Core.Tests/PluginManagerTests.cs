using FluentAssertions;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-057-005 / req-059-001: PluginManager 关键场景覆盖。
/// <para>
/// 测试用 <see cref="FakeUsageProvider"/> 通过 RegisterPlugin 注入，
/// 避免依赖真实 DLL 加载（LoadPlugins + LoadFrom 路径）。
/// </para>
/// </summary>
public class PluginManagerTests
{
    [Fact]
    public void RegisterPlugin_Adds_To_Plugins_List()
    {
        var mgr = new PluginManager();
        var provider = new FakeUsageProvider("test-p1");
        mgr.RegisterPlugin(provider);

        mgr.Plugins.Should().HaveCount(1);
        mgr.Plugins[0].Provider.Should().BeSameAs(provider);
    }

    [Fact]
    public void RegisterPlugin_Duplicate_Id_Skips_Second()
    {
        var mgr = new PluginManager();
        mgr.RegisterPlugin(new FakeUsageProvider("dup"));
        mgr.RegisterPlugin(new FakeUsageProvider("dup")); // 同 ProviderId，应被忽略

        mgr.Plugins.Should().HaveCount(1);
    }

    [Fact]
    public void GetPlugin_Returns_Registered_Plugin_By_Id()
    {
        var mgr = new PluginManager();
        var provider = new FakeUsageProvider("findme");
        mgr.RegisterPlugin(provider);

        mgr.GetPlugin("findme").Should().NotBeNull();
        mgr.GetPlugin("findme")!.Provider.Should().BeSameAs(provider);
    }

    [Fact]
    public void GetPlugin_Returns_Null_For_Unknown_Id()
    {
        var mgr = new PluginManager();
        mgr.GetPlugin("not-registered").Should().BeNull();
    }

    [Fact]
    public void GetEnabledPlugins_Returns_Snapshot()
    {
        var mgr = new PluginManager();
        var provider = new FakeUsageProvider("p1");
        mgr.RegisterPlugin(provider);

        var snapshot = mgr.GetEnabledPlugins().ToList();
        snapshot.Should().HaveCount(1);

        // 修改 GetEnabledPlugins 返回的集合不应影响内部状态
        snapshot.Clear();

        mgr.GetEnabledPlugins().Should().HaveCount(1);
    }

    [Fact]
    public void Plugins_Returns_Snapshot()
    {
        // req-057-005: 公开属性 Plugins 返回 ToList 快照，不应让外部修改影响内部 List。
        var mgr = new PluginManager();
        mgr.RegisterPlugin(new FakeUsageProvider("p1"));

        var snapshot = mgr.Plugins;
        snapshot.Should().HaveCount(1);

        // snapshot 是 IReadOnlyList
        snapshot.Should().BeAssignableTo<IReadOnlyList<LoadedPlugin>>();
    }

    [Fact]
    public void UnloadPlugin_Removes_By_Id()
    {
        var mgr = new PluginManager();
        mgr.RegisterPlugin(new FakeUsageProvider("p1"));
        mgr.RegisterPlugin(new FakeUsageProvider("p2"));

        mgr.UnloadPlugin("p1").Should().BeTrue();
        mgr.Plugins.Should().HaveCount(1);
        mgr.GetPlugin("p1").Should().BeNull();
        mgr.GetPlugin("p2").Should().NotBeNull();
    }

    [Fact]
    public void UnloadPlugin_Returns_False_For_Unknown_Id()
    {
        var mgr = new PluginManager();
        mgr.UnloadPlugin("nonexistent").Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_Register_And_Get_Does_Not_Throw()
    {
        // req-057-005: 多线程并发 RegisterPlugin / GetPlugin / GetEnabledPlugins 不应抛异常。
        var mgr = new PluginManager();
        const int writerCount = 8;
        const int readerCount = 8;

        var registerTask = Task.Run(() =>
        {
            Parallel.For(0, writerCount, i =>
            {
                mgr.RegisterPlugin(new FakeUsageProvider($"p{i}"));
            });
        });

        var readTask = Task.Run(() =>
        {
            Parallel.For(0, readerCount, i =>
            {
                _ = mgr.GetEnabledPlugins().ToList();
                _ = mgr.Plugins.ToList();
            });
        });

        // 不应抛异常；使用 await 避免 xUnit1031 阻塞警告。
        await Task.WhenAll(registerTask, readTask);
    }

    [Fact]
    public void LoadPlugins_With_AllowExternalPlugins_False_Keeps_Empty_And_Fires_Event()
    {
        var mgr = new PluginManager();
        var eventFired = false;
        mgr.PluginsLoaded += (_, _) => eventFired = true;

        PluginManager.AllowExternalPlugins = false; // 默认值，确保
        mgr.LoadPlugins();

        // 没启用外部插件扫描时不加载任何东西（仅内置通过 RegisterPlugin）
        mgr.Plugins.Should().BeEmpty();
        eventFired.Should().BeTrue();
    }
}
