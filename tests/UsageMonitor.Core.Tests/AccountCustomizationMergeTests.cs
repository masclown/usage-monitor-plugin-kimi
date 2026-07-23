using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-107 B7 / #10：5 个旧字典 → AccountCustomization 合并逻辑单元测试。
/// <para>验证 <see cref="ConfigService.GetEffectiveAccountCustomization"/> 在账号定制缺失时回退旧字典，
/// 在账号定制设置时优先使用账号定制（避免覆盖用户主动选择）。</para>
/// </summary>
public class AccountCustomizationMergeTests : IDisposable
{
    private readonly TempDir _tempDir;
    private readonly string _configFilePath;

    public AccountCustomizationMergeTests()
    {
        _tempDir = new TempDir();
        _configFilePath = _tempDir.Combine("config.json");
    }

    public void Dispose() => _tempDir.Dispose();

    private ConfigService CreateConfigService()
    {
        var svc = new ConfigService();
        ReflectionHelpers.SetField(svc, "_configDirectory", _tempDir.Path);
        ReflectionHelpers.SetField(svc, "_configFilePath", _configFilePath);
        return svc;
    }

    [Fact]
    public void MakeKey_FormatsAsProviderIdColonAccountIdColonCardId()
    {
        // 三段 key：CardId 缺省 "default-card"
        AccountCustomization.MakeKey("minimax").Should().Be("minimax:default:default-card");
        AccountCustomization.MakeKey("minimax", "acc1").Should().Be("minimax:acc1:default-card");
        AccountCustomization.MakeKey("minimax", "acc1", "card-2").Should().Be("minimax:acc1:card-2");
        AccountCustomization.MakeKey("minimax", "").Should().Be("minimax:default:default-card");
    }

    [Fact]
    public void Effective_EmptySettings_ReturnsDefaultEmptyCustomization()
    {
        var svc = CreateConfigService();
        var eff = svc.GetEffectiveAccountCustomization("minimax");

        eff.Should().NotBeNull();
        eff.VisibleCharts.Should().BeNull();
        eff.ChartOrders.Should().BeEmpty();
        eff.VisibleProgressFields.Should().BeNull();
        eff.VisibleMetricFields.Should().BeNull();
        eff.Nickname.Should().BeNull();
        eff.UseNickname.Should().BeFalse();
    }

    [Fact]
    public void Effective_AccountCustomizationSet_OverridesLegacyDicts()
    {
        var svc = CreateConfigService();
        // 账号定制（用户主动选择）
        svc.Settings.AccountCustomizations[AccountCustomization.MakeKey("minimax", "default", "default-card")] = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "Line", "HeatMap" },
            VisibleProgressFields = new System.Collections.Generic.List<string> { "five_hour_used_percent" },
            Nickname = "工作号"
        };
        // 旧字典（应被覆盖）
        svc.Settings.ProviderCardChartKinds["minimax"] = new System.Collections.Generic.List<CardChartKind> { CardChartKind.Ring };
        svc.Settings.SelectedProgressFields["minimax"] = new System.Collections.Generic.List<string> { "weekly_used_percent" };

        var eff = svc.GetEffectiveAccountCustomization("minimax", "default", "default-card");

        eff.VisibleCharts.Should().BeEquivalentTo(new[] { "Line", "HeatMap" }); // 账号定制优先
        eff.VisibleProgressFields.Should().BeEquivalentTo(new[] { "five_hour_used_percent" });
        eff.Nickname.Should().Be("工作号");
    }

    [Fact]
    public void Effective_AccountCustomizationMissing_FallsBackToLegacyDicts()
    {
        var svc = CreateConfigService();
        // 账号定制缺失，仅有旧字典
        svc.Settings.ProviderCardChartKinds["kimi"] = new System.Collections.Generic.List<CardChartKind> { CardChartKind.Bar, CardChartKind.Ring };
        svc.Settings.ProviderChartOrder["kimi"] = new System.Collections.Generic.List<CardChartKind> { CardChartKind.Ring, CardChartKind.Bar };
        svc.Settings.SelectedProgressFields["kimi"] = new System.Collections.Generic.List<string> { "five_hour_used_percent", "weekly_used_percent" };
        svc.Settings.SelectedMetricFields["kimi"] = new System.Collections.Generic.List<string> { "remaining_credits" };

        var eff = svc.GetEffectiveAccountCustomization("kimi");

        eff.VisibleCharts.Should().BeEquivalentTo(new[] { "Bar", "Ring" });
        eff.ChartOrders.Should().ContainKey("Ring").WhoseValue.Should().Be(0);
        eff.ChartOrders.Should().ContainKey("Bar").WhoseValue.Should().Be(1);
        eff.VisibleProgressFields.Should().BeEquivalentTo(new[] { "five_hour_used_percent", "weekly_used_percent" });
        eff.VisibleMetricFields.Should().BeEquivalentTo(new[] { "remaining_credits" });
    }

    [Fact]
    public void Effective_DifferentAccountId_ReturnsSeparateCustomization()
    {
        var svc = CreateConfigService();
        svc.Settings.AccountCustomizations[AccountCustomization.MakeKey("deepseek", "work", "default-card")] = new AccountCustomization
        {
            Nickname = "工作号",
            UseNickname = true
        };
        svc.Settings.AccountCustomizations[AccountCustomization.MakeKey("deepseek", "personal", "default-card")] = new AccountCustomization
        {
            Nickname = "私人号",
            UseNickname = true
        };

        svc.GetEffectiveAccountCustomization("deepseek", "work", "default-card").Nickname.Should().Be("工作号");
        svc.GetEffectiveAccountCustomization("deepseek", "personal", "default-card").Nickname.Should().Be("私人号");
    }
}