using System.IO;
using UsageMonitor.Core.Services.Display;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-115：显示资源包（主题 / 图表样式 / mini 图表样式 / 悬浮窗模板）加载器与注册表测试。
/// </summary>
public class DisplayPackTests
{
    /// <summary>在 root 下写入一个主题包目录。</summary>
    private static void WriteThemePack(string root, string dirName, string json)
    {
        var dir = Path.Combine(root, dirName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "theme.json"), json);
    }

    /// <summary>验证：合法主题包正常加载并回填 PackDirectory。</summary>
    [Fact]
    public void LoadThemePacks_ValidPack_Loads()
    {
        using var temp = new TempDir();
        WriteThemePack(temp.Path, "solar",
            """{ "id": "solar", "displayName": "Solarized", "isDark": true, "tokens": { "SurfaceBrush": "#1B1F2A" } }""");

        var packs = DisplayPackLoader.LoadThemePacks(temp.Path);

        Assert.Single(packs);
        Assert.Equal("solar", packs[0].Id);
        Assert.True(packs[0].IsDark);
        Assert.Equal("Solarized", packs[0].EffectiveDisplayName);
        Assert.False(string.IsNullOrEmpty(packs[0].PackDirectory));
    }

    /// <summary>验证：坏包（JSON 语法错误 / 缺 id / 空 tokens）跳过，不影响其他包。</summary>
    [Fact]
    public void LoadThemePacks_BadPacks_SkippedWithoutAffectingOthers()
    {
        using var temp = new TempDir();
        WriteThemePack(temp.Path, "broken", "{ not json");
        WriteThemePack(temp.Path, "noid", """{ "tokens": { "SurfaceBrush": "#111111" } }""");
        WriteThemePack(temp.Path, "empty", """{ "id": "empty", "tokens": {} }""");
        WriteThemePack(temp.Path, "good", """{ "id": "good", "tokens": { "SurfaceBrush": "#222222" } }""");

        var packs = DisplayPackLoader.LoadThemePacks(temp.Path);

        Assert.Single(packs);
        Assert.Equal("good", packs[0].Id);
    }

    /// <summary>验证：图表样式包色阶阈值/颜色不等长时该项色阶被清空（保留参数）。</summary>
    [Fact]
    public void LoadChartStylePacks_MismatchedTiers_TiersCleared()
    {
        using var temp = new TempDir();
        var dir = Path.Combine(temp.Path, "pack1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "chartstyle.json"),
            """{ "id": "pack1", "chartStyles": { "usage": { "thresholds": [0, 50], "colors": ["#22C55E"], "parameters": { "lineThickness": 2 } } } }""");

        var packs = DisplayPackLoader.LoadChartStylePacks(temp.Path);

        Assert.Single(packs);
        var entry = packs[0].ChartStyles["usage"];
        Assert.Empty(entry.Thresholds);
        Assert.Empty(entry.Colors);
        Assert.Equal(2, entry.Parameters["lineThickness"]);
    }

    /// <summary>验证：悬浮窗模板包非法字段行剔除、静态文本行保留；全部非法时整包跳过。</summary>
    [Fact]
    public void LoadTrayTooltipPacks_InvalidFieldRows_Removed()
    {
        using var temp = new TempDir();
        var dir = Path.Combine(temp.Path, "tt1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "traytooltip.json"),
            """{ "id": "tt1", "rows": [ { "fieldName": "five_hour_used_percent" }, { "fieldName": "not_a_real_field" }, { "textTemplate": "---" }, {} ] }""");
        var dir2 = Path.Combine(temp.Path, "tt2");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, "traytooltip.json"),
            """{ "id": "tt2", "rows": [ { "fieldName": "bogus_field" } ] }""");

        var packs = DisplayPackLoader.LoadTrayTooltipPacks(temp.Path);

        Assert.Single(packs);
        Assert.Equal("tt1", packs[0].Id);
        Assert.Equal(2, packs[0].Rows.Count); // 合法字段行 + 静态文本行
    }

    /// <summary>验证：注册表 Reload 后可按 Id（大小写不敏感）检索四类包。</summary>
    [Fact]
    public void Registry_Reload_LookupById()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "themes"));
        WriteThemePack(Path.Combine(temp.Path, "themes"), "solar",
            """{ "id": "Solar", "tokens": { "SurfaceBrush": "#101010" } }""");

        using var registry = new DisplayPackRegistry(temp.Path);
        registry.Reload();

        Assert.NotNull(registry.GetThemePack("solar"));
        Assert.Null(registry.GetThemePack("missing"));
        Assert.Empty(registry.ChartStylePacks);
        Assert.Empty(registry.TrayTooltipPacks);
    }

    /// <summary>验证：色阶转换助手——#RRGGBB 默认不透明、#AARRGGBB 原样、非法色值返回 null。</summary>
    [Fact]
    public void Converters_ToUsageTiers_ParsesColors()
    {
        var entry = new ChartStyleEntry
        {
            Thresholds = { 0, 60, 85 },
            Colors = { "#22C55E", "#80F59E0B", "#EF4444" }
        };

        var tiers = entry.ToUsageTiers();

        Assert.NotNull(tiers);
        Assert.Equal(3, tiers!.Count);
        Assert.Equal(0xFF22C55Eu, tiers[0].ColorArgb);
        Assert.Equal(0x80F59E0Bu, tiers[1].ColorArgb);
        Assert.Equal(85, tiers[2].MinPercent);

        entry.Colors[2] = "oops";
        Assert.Null(entry.ToUsageTiers());
    }
}
