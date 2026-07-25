using FluentAssertions;
using UsageMonitor.App.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.App.Tests;

/// <summary>
/// app-layer-zero-test: Provider 启用过滤路径覆盖（DisplayModule 经 MainViewModel 门面）。
/// <para>验证卡片装配、启用/禁用过滤（EnabledUsages）与空状态（IsEmpty）联动。</para>
/// </summary>
public class ProviderFilterTests
{
    // -----------------------------------------------------------------
    // 卡片装配：每个已注册 Provider 至少产生一张卡片与一个插件项
    // -----------------------------------------------------------------

    [Fact]
    public void Build_Creates_Cards_And_PluginItems_For_Registered_Providers()
    {
        using var h = new MainViewModelHarness("prov-a", "prov-b");

        h.ViewModel.PluginItems.Should().HaveCount(2);
        h.ViewModel.Usages.Should().NotBeEmpty();
        h.ViewModel.Usages.Select(u => u.ProviderId).Distinct()
            .Should().BeEquivalentTo(new[] { "prov-a", "prov-b" });
    }

    // -----------------------------------------------------------------
    // 禁用 Provider：EnabledUsages 移除、全量 Usages 保留（状态不丢失）
    // -----------------------------------------------------------------

    [Fact]
    public void Disable_Provider_Removes_From_EnabledUsages_But_Keeps_In_Usages()
    {
        using var h = new MainViewModelHarness("prov-a", "prov-b");

        h.ViewModel.UpdatePluginEnabled("prov-a", false);

        h.ViewModel.EnabledUsages.Select(u => u.ProviderId)
            .Should().NotContain("prov-a");
        h.ViewModel.Usages.Select(u => u.ProviderId)
            .Should().Contain("prov-a");
        h.ViewModel.EnabledUsages.Select(u => u.ProviderId)
            .Should().Contain("prov-b");
    }

    // -----------------------------------------------------------------
    // 重新启用：卡片回到 EnabledUsages
    // -----------------------------------------------------------------

    [Fact]
    public void ReEnable_Provider_Restores_To_EnabledUsages()
    {
        using var h = new MainViewModelHarness("prov-a");

        h.ViewModel.UpdatePluginEnabled("prov-a", false);
        h.ViewModel.EnabledUsages.Should().BeEmpty();

        h.ViewModel.UpdatePluginEnabled("prov-a", true);
        h.ViewModel.EnabledUsages.Select(u => u.ProviderId)
            .Should().Contain("prov-a");
    }

    // -----------------------------------------------------------------
    // 空状态：全部禁用后 IsEmpty 为 true
    // -----------------------------------------------------------------

    [Fact]
    public void All_Providers_Disabled_Makes_IsEmpty_True()
    {
        using var h = new MainViewModelHarness("prov-a");

        h.ViewModel.IsEmpty.Should().BeFalse();

        h.ViewModel.UpdatePluginEnabled("prov-a", false);

        h.ViewModel.IsEmpty.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    // 无插件场景：装配后集合为空且 IsEmpty 为 true（不抛异常）
    // -----------------------------------------------------------------

    [Fact]
    public void No_Providers_Yields_Empty_Collections()
    {
        using var h = new MainViewModelHarness();

        h.ViewModel.Usages.Should().BeEmpty();
        h.ViewModel.EnabledUsages.Should().BeEmpty();
        h.ViewModel.PluginItems.Should().BeEmpty();
        h.ViewModel.IsEmpty.Should().BeTrue();
    }
}
