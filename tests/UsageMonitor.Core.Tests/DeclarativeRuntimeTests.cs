using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Plugins.Declarative;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// Stage E（完全声明式插件架构）单测：
/// ① 计算列新算子（splitBefore / splitAfter / constant / coalesce / template + 条件门）；
/// ② 声明式 HTTP 取数器的占位符展开与 SSRF 拦截；
/// ③ DeclarativeProvider 由 PluginManifest 驱动的全成员映射；
/// ④ PluginManager 声明包扫描注册。
/// </summary>
public class DeclarativeRuntimeTests
{
    // ============ ① 计算列新算子 ============

    /// <summary>构造只含 computed 的 fetch 声明并执行（输入 extras 经由 DOM 兑底结果注入种子值）。</summary>
    private static IReadOnlyDictionary<string, object> RunComputed(string computedJson, Dictionary<string, object> seed)
    {
        var manifest = PluginManifest.Load(
            $$"""{ "providerId": "T", "fetch": { "computed": {{computedJson}} } }""")!;
        var (domDecl, domResults) = BuildSeed(seed);
        var merged = new FetchDeclaration { Computed = manifest.Fetch!.Computed, Dom = domDecl };
        return DeclarativeCaptureExecutor.Execute(merged, new Dictionary<string, string>(), domResults).Extras;
    }

    /// <summary>把种子值声明为 DOM 兑底字段（string 类型足够覆盖 split/template 场景）。</summary>
    private static (List<FetchDomField> Decl, Dictionary<string, string> Results) BuildSeed(Dictionary<string, object> seed)
    {
        var domDecl = new List<FetchDomField>();
        var domResults = new Dictionary<string, string>();
        foreach (var kv in seed)
        {
            domDecl.Add(new FetchDomField { Tool = "jsFunction", Source = "()", Target = kv.Key });
            domResults[kv.Key] = kv.Value.ToString()!;
        }
        return (domDecl, domResults);
    }

    [Fact]
    public void Computed_SplitBeforeAfter_SplitsOnFirstSeparator()
    {
        var extras = RunComputed("""
            [
              { "target": "sub_type", "op": "splitBefore", "operands": ["raw"], "separators": "·・•" },
              { "target": "sub_tier", "op": "splitAfter", "operands": ["raw"], "separators": "·・•" }
            ]
            """, new() { ["raw"] = "Token Plan · TokenPlanMax-年度会员" });
        extras["sub_type"].Should().Be("Token Plan");
        extras["sub_tier"].Should().Be("TokenPlanMax-年度会员");
    }

    [Fact]
    public void Computed_SplitWithoutSeparator_BeforeSkipsAfterYieldsWhole()
    {
        var extras = RunComputed("""
            [
              { "target": "sub_type", "op": "splitBefore", "operands": ["raw"], "separators": "·・•" },
              { "target": "sub_tier", "op": "splitAfter", "operands": ["raw"], "separators": "·・•" },
              { "target": "sub_type", "op": "constant", "value": "Token Plan", "whenPresent": "raw", "onlyIfAbsent": true }
            ]
            """, new() { ["raw"] = "尊享会员" });
        extras["sub_type"].Should().Be("Token Plan", "无分隔符时由 constant+onlyIfAbsent 兜底");
        extras["sub_tier"].Should().Be("尊享会员");
    }

    [Fact]
    public void Computed_ConstantWithWhenPresent_OnlyRunsWhenKeyExists()
    {
        var extras = RunComputed("""
            [ { "target": "active", "op": "constant", "value": true, "whenPresent": "raw" } ]
            """, new());
        extras.Should().NotContainKey("active", "whenPresent 键缺失时规则不执行");
    }

    [Fact]
    public void Computed_CoalesceAndTemplate_ProduceDerivedValues()
    {
        var extras = RunComputed("""
            [
              { "target": "title", "op": "coalesce", "operands": ["missing", "tier"] },
              { "target": "peak_day", "op": "template", "template": "{date} ({tokens})", "whenPresent": "date" }
            ]
            """, new() { ["tier"] = "Max", ["date"] = "2026-07-01", ["tokens"] = "552.49M" });
        extras["title"].Should().Be("Max");
        extras["peak_day"].Should().Be("2026-07-01 (552.49M)");
    }

    // ============ ② 声明式 HTTP 取数器 ============

    [Fact]
    public void HttpFetcher_ExpandPlaceholders_ResolvesConfigCookieAndHeader()
    {
        string? ConfigValue(string key) => key == "ApiKey" ? "sk-123" : null;
        const string cookie = "_token=abc; minimax_group_id_v2=g-42";

        DeclarativeHttpFetcher.ExpandPlaceholders("Bearer {config:ApiKey}", ConfigValue, cookie)
            .Should().Be("Bearer sk-123");
        DeclarativeHttpFetcher.ExpandPlaceholders("{cookie:minimax_group_id_v2}", ConfigValue, cookie)
            .Should().Be("g-42");
        DeclarativeHttpFetcher.ExpandPlaceholders("{cookieHeader}", ConfigValue, cookie)
            .Should().Be(cookie);
        DeclarativeHttpFetcher.ExpandPlaceholders("{cookie:absent}", ConfigValue, cookie)
            .Should().BeEmpty("缺失 Cookie 名展开为空串");
    }

    [Fact]
    public async Task HttpFetcher_SsrfBlockedUrl_IsSkipped()
    {
        var endpoints = new[]
        {
            new FetchEndpoint { Mode = "http", UrlMatch = "loopback", UrlTemplate = "https://127.0.0.1/api" },
            new FetchEndpoint { Mode = "http", UrlMatch = "intranet", UrlTemplate = "https://192.168.1.10/api" }
        };
        var responses = await DeclarativeHttpFetcher.FetchAsync(endpoints, _ => null, null);
        responses.Should().BeEmpty("环回/内网地址必须被 req-056 SSRF 校验拦截");
    }

    // ============ ③ DeclarativeProvider 成员映射 ============

    private const string RunnerManifestJson = """
    {
      "providerId": "DemoCloud",
      "displayName": "Demo Cloud",
      "meta": { "version": "1.2.3", "author": "UsageMonitor", "description": "demo provider" },
      "loginConfig": {
        "loginUrl": "https://example.com/login",
        "cookieDomainFilters": [ "example.com" ],
        "loginTimeout": "00:02:00"
      },
      "configFields": [
        { "key": "Cookie", "displayName": "登录态", "fieldType": "Password", "isRequired": false },
        { "key": "Region", "displayName": "区域", "fieldType": "Select", "defaultValue": "CN", "options": [ "CN", "Global" ] }
      ],
      "errorGuidance": [ { "matchKeywords": [], "message": "请进入设置界面完成配置" } ],
      "refresh": { "minIntervalSeconds": 120, "defaultIntervalSeconds": 600, "maxIntervalSeconds": 3600 },
      "card": {
        "primaryMetric": "used_percent",
        "collapseVisibleParts": [ "limitBars" ],
        "heatMapTiers": [ { "minTokens": 0, "colorHex": "#f3f4f6" } ]
      }
    }
    """;

    [Fact]
    public void DeclarativeProvider_MapsAllMembersFromManifest()
    {
        var manifest = PluginManifest.Load(RunnerManifestJson)!;
        var provider = new DeclarativeProvider(manifest, "pkg-dir");

        provider.ProviderId.Should().Be("DemoCloud");
        provider.DisplayName.Should().Be("Demo Cloud");
        provider.Version.Should().Be("1.2.3");
        provider.Author.Should().Be("UsageMonitor");
        provider.Description.Should().Be("demo provider");
        provider.ConfigFields.Should().HaveCount(2);
        provider.ConfigFields[1].Options.Should().Equal("CN", "Global");
#pragma warning disable CS0618 // LoginConfig 过时成员：声明运行器兼容通道
        provider.LoginConfig.Should().NotBeNull();
        provider.LoginConfig!.ProviderId.Should().Be("DemoCloud", "声明缺省时应自动补齐 ProviderId");
        provider.LoginConfig.LoginTimeout.Should().Be(TimeSpan.FromMinutes(2));
#pragma warning restore CS0618
        provider.ErrorGuidance.Should().ContainSingle().Which.Message.Should().Contain("设置界面");
        provider.RefreshPolicy.Should().NotBeNull();
        provider.RefreshPolicy!.DefaultIntervalSeconds.Should().Be(600);
        provider.CollapseVisibleParts.Should().Equal("limitBars");
        provider.Card!.HeatMapTiers.Should().ContainSingle();
        provider.IconPath.Should().BeNull("声明包图标由宿主 favicon 服务解析");
    }

    [Fact]
    public async Task DeclarativeProvider_WithoutFetchDeclaration_ReturnsError()
    {
        var manifest = PluginManifest.Load("""{ "providerId": "NoFetch" }""")!;
        var provider = new DeclarativeProvider(manifest);
        var usage = await provider.GetUsageAsync(new ProviderConfig { ProviderId = "NoFetch" });
        usage.IsSuccess.Should().BeFalse();
#pragma warning disable CS0618 // ErrorMessage 遗留字段：断言兼容通道文案
        usage.ErrorMessage.Should().Contain("fetch");
#pragma warning restore CS0618
    }

    // ============ ④ PluginManager 声明包扫描 ============

    [Fact]
    public void PluginManager_LoadPlugins_RegistersDeclarativePackages()
    {
        using var temp = new TempDir();
        var pkgDir = temp.Combine("UsageMonitor.Plugin.Demo");
        Directory.CreateDirectory(pkgDir);
        File.WriteAllText(Path.Combine(pkgDir, "defaults.json"), RunnerManifestJson);
        // 无清单文件的目录应被忽略
        Directory.CreateDirectory(temp.Combine("not-a-package"));
        // 校验失败的包（缺 providerId）应被跳过
        var badDir = temp.Combine("bad-package");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "defaults.json"), """{ "displayName": "no id" }""");

        var manager = new PluginManager(temp.Path);
        manager.LoadPlugins();

        manager.Plugins.Should().ContainSingle();
        var loaded = manager.Plugins[0];
        loaded.Provider.Should().BeOfType<DeclarativeProvider>();
        loaded.Provider.ProviderId.Should().Be("DemoCloud");
        loaded.FilePath.Should().Be(pkgDir, "声明包以目录为部署单元");
    }

    [Fact]
    public void PluginManager_LoadPlugins_SkipsDuplicateProviderIds()
    {
        using var temp = new TempDir();
        foreach (var name in new[] { "pkg-a", "pkg-b" })
        {
            var dir = temp.Combine(name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "defaults.json"), RunnerManifestJson);
        }

        var manager = new PluginManager(temp.Path);
        manager.LoadPlugins();

        manager.Plugins.Should().ContainSingle("重复 providerId 的声明包只注册首个");
    }
}
