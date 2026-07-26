using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-107 B6 演进：卡片图表与数据组可见性/排序 + ConfigService 持久化测试。
/// </summary>
public class CardChartConfigTests : IDisposable
{
    private readonly TempDir _tempDir;
    private readonly string _configFilePath;

    public CardChartConfigTests()
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
    public void AccountCustomization_NewFields_DefaultToEmpty()
    {
        var c = new AccountCustomization();
        c.VisibleDataGroups.Should().BeEmpty();
        c.DataGroupOrders.Should().BeEmpty();
    }

    /// <summary>req-115：图表色阶来源（含 pack:&lt;packId&gt; 形态）随 SetCardChartConfiguration 持久化往返。</summary>
    [Fact]
    public void SetCardChartConfiguration_PersistsChartColorTierSources_IncludingPackForm()
    {
        var svc = CreateConfigService();
        var config = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "mm.chart.cache_heatmap" },
            ChartColorTierSources = new System.Collections.Generic.Dictionary<string, string>
            {
                ["mm.chart.cache_heatmap"] = "pack:ocean-tiers",
                ["mm.chart.usage_bar"] = "global:usage-tier-default"
            }
        };

        svc.SetCardChartConfiguration("minimax", config, "acct1");

        var eff = svc.GetEffectiveAccountCustomization("minimax", "acct1");
        eff.ChartColorTierSources["mm.chart.cache_heatmap"].Should().Be("pack:ocean-tiers");
        eff.ChartColorTierSources["mm.chart.usage_bar"].Should().Be("global:usage-tier-default");

        // 重新加载配置文件，确认落盘后仍可读回（非仅内存态）
        var svc2 = CreateConfigService();
        svc2.Load();
        var eff2 = svc2.GetEffectiveAccountCustomization("minimax", "acct1");
        eff2.ChartColorTierSources["mm.chart.cache_heatmap"].Should().Be("pack:ocean-tiers");
    }

    [Fact]
    public void Effective_CopiesVisibleDataGroupsAndDataGroupOrders()
    {
        var svc = CreateConfigService();
        svc.Settings.AccountCustomizations[AccountCustomization.MakeKey("minimax", "default", "default-card")] = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "Line", "HeatMap" },
            VisibleDataGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["mm.chart.usage_bar"] = new System.Collections.Generic.List<string> { "mm.bar.5h", "mm.bar.weekly" }
            },
            DataGroupOrders = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, int>>
            {
                ["mm.chart.usage_bar"] = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["mm.bar.weekly"] = 0,
                    ["mm.bar.5h"] = 1
                }
            }
        };

        var eff = svc.GetEffectiveAccountCustomization("minimax", "default", "default-card");

        eff.VisibleCharts.Should().BeEquivalentTo(new[] { "Line", "HeatMap" });
        eff.VisibleDataGroups.Should().ContainKey("mm.chart.usage_bar");
        eff.VisibleDataGroups["mm.chart.usage_bar"].Should().BeEquivalentTo(new[] { "mm.bar.5h", "mm.bar.weekly" });
        eff.DataGroupOrders["mm.chart.usage_bar"]["mm.bar.5h"].Should().Be(1);
    }

    [Fact]
    public void SetCardChartConfiguration_PersistsVisibleChartsAndDataGroups()
    {
        var svc = CreateConfigService();
        var config = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "Line", "HeatMap" },
            VisibleDataGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["Line"] = new System.Collections.Generic.List<string> { "7d", "30d" }
            }
        };
        svc.SetCardChartConfiguration("minimax", config);

        svc.Settings.AccountCustomizations.Should().ContainKey(AccountCustomization.MakeKey("minimax", "default", "default-card"));
        var stored = svc.Settings.AccountCustomizations[AccountCustomization.MakeKey("minimax", "default", "default-card")];
        stored.VisibleCharts.Should().BeEquivalentTo(new[] { "Line", "HeatMap" });
        stored.VisibleDataGroups["Line"].Should().BeEquivalentTo(new[] { "7d", "30d" });
    }

    [Fact]
    public void SetCardChartConfiguration_DoesNotMutateInputConfig()
    {
        var svc = CreateConfigService();
        var input = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "Bar" },
            VisibleDataGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["Bar"] = new System.Collections.Generic.List<string> { "5h" }
            }
        };
        var inputVisibleBefore = input.VisibleCharts[0];

        svc.SetCardChartConfiguration("minimax", input);

        // Input 不应被污染（写入时做深拷贝）
        input.VisibleCharts.Should().BeEquivalentTo(new[] { inputVisibleBefore });
    }

    [Fact]
    public void SetCardChartConfiguration_RoundTrip_PreservesOrder()
    {
        var svc = CreateConfigService();
        var input = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "C", "A", "B" },
            VisibleDataGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["C"] = new System.Collections.Generic.List<string> { "x", "y", "z" }
            }
        };
        svc.SetCardChartConfiguration("minimax", input);

        var eff = svc.GetEffectiveAccountCustomization("minimax");
        eff.VisibleCharts.Should().Equal("C", "A", "B"); // 顺序保留
        eff.VisibleDataGroups["C"].Should().Equal("x", "y", "z");
    }

    [Fact]
    public void SetCardChartConfiguration_PersistsTooltipFields()
    {
        var svc = CreateConfigService();
        var config = new AccountCustomization
        {
            VisibleTooltipFields = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["Line"] = new System.Collections.Generic.List<string> { "daily_token_value", "daily_cache_hit_value" },
                ["HeatMap"] = null // null = 沿用默认
            }
        };
        svc.SetCardChartConfiguration("minimax", config);

        var key = AccountCustomization.MakeKey("minimax", "default", "default-card");
        var stored = svc.Settings.AccountCustomizations[key];
        stored.VisibleTooltipFields.Should().ContainKey("Line");
        stored.VisibleTooltipFields["Line"].Should().Equal("daily_token_value", "daily_cache_hit_value");
        stored.VisibleTooltipFields["HeatMap"].Should().BeNull();
    }

    [Fact]
    public void SetMiniChartConfiguration_PersistsVisibleMiniCharts()
    {
        var svc = CreateConfigService();
        var config = new AccountCustomization
        {
            VisibleMiniCharts = new System.Collections.Generic.List<string> { "mm.mini.ring", "mm.mini.text" },
            VisibleMiniDataGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["mm.mini.ring"] = new System.Collections.Generic.List<string> { "mm.taskbar.5h" }
            },
            MiniDataGroupOrders = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, int>>
            {
                ["mm.mini.ring"] = new System.Collections.Generic.Dictionary<string, int>
                {
                    ["mm.taskbar.5h"] = 0
                }
            }
        };
        svc.SetMiniChartConfiguration("minimax", config);

        var key = AccountCustomization.MakeKey("minimax", "default", "default-card");
        var stored = svc.Settings.AccountCustomizations[key];
        stored.VisibleMiniCharts.Should().Equal("mm.mini.ring", "mm.mini.text");
        stored.VisibleMiniDataGroups["mm.mini.ring"].Should().Equal("mm.taskbar.5h");
        stored.MiniDataGroupOrders["mm.mini.ring"]["mm.taskbar.5h"].Should().Be(0);
    }

    [Fact]
    public void Effective_CopiesTooltipAndMiniFields()
    {
        var svc = CreateConfigService();
        var key = AccountCustomization.MakeKey("minimax", "default", "default-card");
        svc.Settings.AccountCustomizations[key] = new AccountCustomization
        {
            VisibleTooltipFields = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["Line"] = new System.Collections.Generic.List<string> { "value1" }
            },
            VisibleMiniCharts = new System.Collections.Generic.List<string> { "ring" },
            VisibleMiniDataGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["ring"] = new System.Collections.Generic.List<string> { "g1" }
            },
            MiniDataGroupOrders = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, int>>
            {
                ["ring"] = new System.Collections.Generic.Dictionary<string, int> { ["g1"] = 0 }
            }
        };

        var eff = svc.GetEffectiveAccountCustomization("minimax", "default", "default-card");

        eff.VisibleTooltipFields["Line"].Should().Equal("value1");
        eff.VisibleMiniCharts.Should().Equal("ring");
        eff.VisibleMiniDataGroups["ring"].Should().Equal("g1");
        eff.MiniDataGroupOrders["ring"]["g1"].Should().Be(0);
    }

    [Fact]
    public void AccountCustomization_CollapseDividerIndex_DefaultsToNull()
    {
        var c = new AccountCustomization();
        c.CollapseDividerIndex.Should().BeNull();
    }

    [Fact]
    public void SetCardChartConfiguration_PersistsCollapseDividerIndex()
    {
        var svc = CreateConfigService();
        var config = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string> { "mm.chart.usage_bar", "mm.chart.daily_line" },
            CollapseDividerIndex = 1
        };
        svc.SetCardChartConfiguration("minimax", config);

        var key = AccountCustomization.MakeKey("minimax", "default", "default-card");
        svc.Settings.AccountCustomizations[key].CollapseDividerIndex.Should().Be(1);
    }

    [Fact]
    public void Effective_CopiesCollapseDividerIndex()
    {
        var svc = CreateConfigService();
        var key = AccountCustomization.MakeKey("minimax", "default", "default-card");
        svc.Settings.AccountCustomizations[key] = new AccountCustomization
        {
            CollapseDividerIndex = 2
        };

        var eff = svc.GetEffectiveAccountCustomization("minimax", "default", "default-card");
        eff.CollapseDividerIndex.Should().Be(2);
    }

    [Fact]
    public void SetCardChartConfiguration_PersistsDuplicateChartInstances()
    {
        // 问题2：同一声明图表可添加多个实例（chartId#n），VisibleCharts 为有序实例 ID 列表
        var svc = CreateConfigService();
        var config = new AccountCustomization
        {
            VisibleCharts = new System.Collections.Generic.List<string>
            {
                "mm.chart.usage_bar", "mm.chart.usage_bar#2", "mm.chart.daily_line"
            },
            VisibleDataGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>?>
            {
                ["mm.chart.usage_bar"] = new System.Collections.Generic.List<string> { "mm.bar.5h", "mm.bar.weekly" },
                ["mm.chart.usage_bar#2"] = new System.Collections.Generic.List<string> { "mm.bar.video" }
            }
        };
        svc.SetCardChartConfiguration("minimax", config);

        var eff = svc.GetEffectiveAccountCustomization("minimax");
        eff.VisibleCharts.Should().Equal("mm.chart.usage_bar", "mm.chart.usage_bar#2", "mm.chart.daily_line");
        eff.VisibleDataGroups["mm.chart.usage_bar#2"].Should().Equal("mm.bar.video");
    }
}