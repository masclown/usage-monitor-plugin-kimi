using System.IO;
using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// Stage A（完全声明式插件架构）：声明包多文件合并加载与新增节校验测试。
/// <para>覆盖：plugin.json / fetch.json / display.json 三文件合并、单文件 defaults.json 兼容、
/// 拆分文件优先级覆盖、configFields / errorGuidance / trayTooltip / http 端点校验拦截。</para>
/// </summary>
public class PluginManifestPackageTests
{
    private const string PluginJson = """
    {
      "providerId": "Demo",
      "displayName": "Demo Provider",
      "meta": { "version": "1.0.0", "author": "tester", "description": "demo", "iconUrl": "https://demo.example/favicon.ico" },
      "loginConfig": { "providerId": "Demo", "loginUrl": "https://demo.example/login", "cookieDomainFilters": [ "demo.example" ] },
      "configFields": [
        { "key": "ApiKey", "displayName": "API 密钥", "fieldType": "Password", "isRequired": true, "placeholder": "sk-xxx" }
      ],
      "errorGuidance": [
        { "matchKeywords": [ "1004", "login fail" ], "message": "密钥无效，请进入设置检查" },
        { "matchKeywords": [], "message": "请进入设置完成配置" }
      ],
      "refresh": { "minIntervalSeconds": 60, "defaultIntervalSeconds": 300, "maxIntervalSeconds": 3600 }
    }
    """;

    private const string FetchJson = """
    {
      "fetch": {
        "endpoints": [
          { "mode": "http", "method": "GET", "urlTemplate": "https://api.demo.example/v1/usage",
            "headers": { "Authorization": "Bearer {config:ApiKey}" },
            "fields": [ { "path": "$.used_percent", "target": "used_percent", "transform": "parsePercent" } ] },
          { "urlMatch": "usage_summary",
            "fields": [ { "path": "$.active_days", "target": "active_days", "transform": "parseNumber" } ] }
        ]
      }
    }
    """;

    private const string DisplayJson = """
    {
      "card": {
        "charts": [
          { "chartId": "demo.bar", "kind": "Bar", "defaultOrder": 1,
            "dataGroups": [ { "id": "demo.bar.main", "fields": [ { "fieldName": "used_percent", "role": "Value" } ] } ] }
        ]
      },
      "trayTooltip": { "fields": [ "used_percent", "remaining_amount" ] }
    }
    """;

    /// <summary>写入声明包文件的辅助方法。</summary>
    private static void WriteFile(TempDir dir, string name, string content)
        => File.WriteAllText(dir.Combine(name), content);

    [Fact(DisplayName = "三文件声明包合并加载：plugin/fetch/display 各节齐备")]
    public void LoadFromDirectory_MergesThreeFilePackage()
    {
        using var dir = new TempDir();
        WriteFile(dir, "plugin.json", PluginJson);
        WriteFile(dir, "fetch.json", FetchJson);
        WriteFile(dir, "display.json", DisplayJson);

        var manifest = PluginDefaultsLoader.LoadFromDirectory(dir.Path);

        manifest.Should().NotBeNull();
        manifest!.ProviderId.Should().Be("Demo");
        manifest.Meta!.Version.Should().Be("1.0.0");
        manifest.LoginConfig!.LoginUrl.Should().Be("https://demo.example/login");
        manifest.ConfigFields.Should().ContainSingle(f => f.Key == "ApiKey" && f.IsRequired);
        manifest.ErrorGuidance.Should().HaveCount(2);
        manifest.Refresh!.DefaultIntervalSeconds.Should().Be(300);
        manifest.Fetch!.Endpoints.Should().HaveCount(2);
        manifest.Fetch.Endpoints[0].Mode.Should().Be("http");
        manifest.Fetch.Endpoints[0].Headers["Authorization"].Should().Contain("{config:ApiKey}");
        manifest.Card!.Charts.Should().ContainSingle(c => c.ChartId == "demo.bar");
        manifest.TrayTooltip!.Fields.Should().Contain("used_percent");
    }

    [Fact(DisplayName = "单文件 defaults.json 兼容形态仍可加载")]
    public void LoadFromDirectory_SingleDefaultsJson_StillWorks()
    {
        using var dir = new TempDir();
        WriteFile(dir, "defaults.json", """{ "providerId": "Demo", "displayName": "Demo" }""");

        var manifest = PluginDefaultsLoader.LoadFromDirectory(dir.Path);

        manifest.Should().NotBeNull();
        manifest!.ProviderId.Should().Be("Demo");
    }

    [Fact(DisplayName = "拆分文件优先级高于 defaults.json（displayName 被 plugin.json 覆盖）")]
    public void LoadFromDirectory_SplitFilesOverrideDefaults()
    {
        using var dir = new TempDir();
        WriteFile(dir, "plugin.json", """{ "providerId": "Demo", "displayName": "新名字" }""");
        WriteFile(dir, "defaults.json", """{ "providerId": "Demo", "displayName": "旧名字" }""");

        var manifest = PluginDefaultsLoader.LoadFromDirectory(dir.Path);

        manifest!.DisplayName.Should().Be("新名字");
    }

    [Fact(DisplayName = "configFields 重复 Key：整包校验失败返回 null")]
    public void LoadFromDirectory_DuplicateConfigFieldKey_Fails()
    {
        using var dir = new TempDir();
        WriteFile(dir, "plugin.json", """
        { "providerId": "Demo",
          "configFields": [
            { "key": "ApiKey", "displayName": "A", "fieldType": "Password" },
            { "key": "apikey", "displayName": "B", "fieldType": "Text" } ] }
        """);

        PluginDefaultsLoader.LoadFromDirectory(dir.Path).Should().BeNull();
    }

    [Fact(DisplayName = "errorGuidance 空 Message：整包校验失败")]
    public void LoadFromDirectory_EmptyGuidanceMessage_Fails()
    {
        using var dir = new TempDir();
        WriteFile(dir, "plugin.json", """
        { "providerId": "Demo", "errorGuidance": [ { "matchKeywords": [ "x" ], "message": "" } ] }
        """);

        PluginDefaultsLoader.LoadFromDirectory(dir.Path).Should().BeNull();
    }

    [Fact(DisplayName = "http 端点非 https 或缺 urlTemplate：整包校验失败")]
    public void LoadFromDirectory_InvalidHttpEndpoint_Fails()
    {
        using var dir = new TempDir();
        WriteFile(dir, "fetch.json", """
        { "providerId": "Demo",
          "fetch": { "endpoints": [ { "mode": "http", "urlTemplate": "http://insecure.example/api" } ] } }
        """);
        PluginDefaultsLoader.LoadFromDirectory(dir.Path).Should().BeNull();

        using var dir2 = new TempDir();
        WriteFile(dir2, "fetch.json", """
        { "providerId": "Demo", "fetch": { "endpoints": [ { "mode": "http" } ] } }
        """);
        PluginDefaultsLoader.LoadFromDirectory(dir2.Path).Should().BeNull();
    }

    [Fact(DisplayName = "trayTooltip 非法字段名：白名单校验拦截")]
    public void LoadFromDirectory_InvalidTrayTooltipField_Fails()
    {
        using var dir = new TempDir();
        WriteFile(dir, "display.json", """
        { "providerId": "Demo", "trayTooltip": { "fields": [ "not_a_sdk_field" ] } }
        """);

        PluginDefaultsLoader.LoadFromDirectory(dir.Path).Should().BeNull();
    }

    [Fact(DisplayName = "任一清单文件语法错误：整包判失败返回 null")]
    public void LoadFromDirectory_BrokenJson_FailsWholePackage()
    {
        using var dir = new TempDir();
        WriteFile(dir, "plugin.json", """{ "providerId": "Demo" }""");
        WriteFile(dir, "display.json", "{ not-valid-json ");

        PluginDefaultsLoader.LoadFromDirectory(dir.Path).Should().BeNull();
    }

    [Fact(DisplayName = "Merge 语义：first 非空优先，缺省回退 second")]
    public void Merge_PrefersFirstThenFallsBack()
    {
        var first = PluginManifest.Load("""{ "providerId": "Demo", "meta": { "version": "2.0" } }""")!;
        var second = PluginManifest.Load("""
        { "providerId": "Ignored", "displayName": "来自second",
          "meta": { "version": "1.0" },
          "refresh": { "defaultIntervalSeconds": 120 } }
        """)!;

        var merged = PluginManifest.Merge(first, second);

        merged.ProviderId.Should().Be("Demo");           // first 优先
        merged.DisplayName.Should().Be("来自second");     // first 缺省回退 second
        merged.Meta!.Version.Should().Be("2.0");          // 节级 first 优先
        merged.Refresh!.DefaultIntervalSeconds.Should().Be(120); // 节缺省回退
    }
}
