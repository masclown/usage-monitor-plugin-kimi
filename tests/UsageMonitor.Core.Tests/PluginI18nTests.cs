using System.Collections.Generic;
using System.IO;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// req-116：i18n 语言包机制（Register / UnregisterByPrefix / 回退链）、PluginTextResolver、
/// 插件语言包加载器与 errorGuidance 错误码模型测试。
/// </summary>
public class PluginI18nTests
{
    /// <summary>验证：注册词条后按语言解析；缺键回退默认语言，再缺回退键本身。</summary>
    [Fact]
    public void I18n_RegisterAndFallbackChain_Works()
    {
        try
        {
            I18n.Register("zh-CN", new Dictionary<string, string> { ["plugin.T1.a"] = "甲", ["plugin.T1.b"] = "乙" });
            I18n.Register("en-US", new Dictionary<string, string> { ["plugin.T1.a"] = "Alpha" });

            I18n.SetLanguage("en-US");
            Assert.Equal("Alpha", I18n.T("plugin.T1.a"));
            // en-US 缺 b → 回退 zh-CN
            Assert.Equal("乙", I18n.T("plugin.T1.b"));
            // 两边都缺 → 回退键本身
            Assert.Equal("plugin.T1.missing", I18n.T("plugin.T1.missing"));
        }
        finally
        {
            I18n.SetLanguage(I18n.DefaultLanguage);
            I18n.UnregisterByPrefix("plugin.T1.");
        }
    }

    /// <summary>验证：UnregisterByPrefix 清除所有语言下匹配前缀的词条，不影响其他前缀。</summary>
    [Fact]
    public void I18n_UnregisterByPrefix_RemovesAcrossLanguages()
    {
        try
        {
            I18n.Register("zh-CN", new Dictionary<string, string> { ["plugin.T2.x"] = "X中", ["plugin.T2keep.y"] = "Y中" });
            I18n.Register("en-US", new Dictionary<string, string> { ["plugin.T2.x"] = "X-en" });

            I18n.UnregisterByPrefix("plugin.T2.");

            Assert.Equal("plugin.T2.x", I18n.T("plugin.T2.x")); // 已清除 → 回退键名
            Assert.Equal("Y中", I18n.T("plugin.T2keep.y"));      // 其他前缀不受影响
        }
        finally
        {
            I18n.UnregisterByPrefix("plugin.T2");
        }
    }

    /// <summary>验证：Resolve——i18n: 前缀走词条解析，字面量原样返回（旧插件兼容）。</summary>
    [Fact]
    public void TextResolver_Resolve_PrefixAndLiteral()
    {
        try
        {
            I18n.Register("zh-CN", new Dictionary<string, string> { ["plugin.T3.title"] = "标题甲" });

            Assert.Equal("标题甲", PluginTextResolver.Resolve("i18n:plugin.T3.title"));
            Assert.Equal("普通字面量", PluginTextResolver.Resolve("普通字面量"));
            Assert.Null(PluginTextResolver.Resolve(null));
        }
        finally
        {
            I18n.UnregisterByPrefix("plugin.T3.");
        }
    }

    /// <summary>验证：ResolveJson——JSON 文本级替换 i18n 键为译文，译文含引号时正确转义。</summary>
    [Fact]
    public void TextResolver_ResolveJson_ReplacesAndEscapes()
    {
        try
        {
            I18n.Register("zh-CN", new Dictionary<string, string>
            {
                ["plugin.T4.name"] = "五小时\"限额\"",
                ["plugin.T4.plain"] = "普通"
            });

            var json = """{ "displayName": "i18n:plugin.T4.name", "other": "i18n:plugin.T4.plain", "literal": "保持原样" }""";
            var resolved = PluginTextResolver.ResolveJson(json);

            using var doc = System.Text.Json.JsonDocument.Parse(resolved);
            Assert.Equal("五小时\"限额\"", doc.RootElement.GetProperty("displayName").GetString());
            Assert.Equal("普通", doc.RootElement.GetProperty("other").GetString());
            Assert.Equal("保持原样", doc.RootElement.GetProperty("literal").GetString());

            // ExtractKeys 提取全部键
            var keys = PluginTextResolver.ExtractKeys(json);
            Assert.Equal(new[] { "plugin.T4.name", "plugin.T4.plain" }, keys);
        }
        finally
        {
            I18n.UnregisterByPrefix("plugin.T4.");
        }
    }

    /// <summary>验证：语言包加载器——读取 i18n/&lt;lang&gt;.json 并过滤非 plugin. 前缀键。</summary>
    [Fact]
    public void LanguagePackLoader_LoadsAndFiltersKeys()
    {
        using var temp = new TempDir();
        var i18nDir = Path.Combine(temp.Path, "i18n");
        Directory.CreateDirectory(i18nDir);
        File.WriteAllText(Path.Combine(i18nDir, "zh-CN.json"),
            """{ "plugin.T5.a": "文案A", "history.range.last7days": "劫持宿主词条" }""");
        File.WriteAllText(Path.Combine(i18nDir, "en-US.json"),
            """{ "plugin.T5.a": "Text A" }""");

        try
        {
            var packs = PluginLanguagePackLoader.ReadLanguagePacks(temp.Path);
            Assert.Equal(2, packs.Count);

            var registered = PluginLanguagePackLoader.LoadAndRegister(temp.Path);
            Assert.Equal(2, registered);
            Assert.Equal("文案A", I18n.T("plugin.T5.a"));
            // 非 plugin. 前缀键被过滤，宿主词条不被劫持
            Assert.Equal("最近 7 天", I18n.T("history.range.last7days"));
        }
        finally
        {
            I18n.UnregisterByPrefix("plugin.T5.");
        }
    }

    /// <summary>验证：ErrorGuidanceRule.MatchCodes 反序列化与兑底判定（关键字与错误码均空才算兑底）。</summary>
    [Fact]
    public void ErrorGuidance_MatchCodes_DeserializedAndFallbackSemantics()
    {
        var manifest = PluginManifest.Load("""
        {
          "providerId": "T6",
          "errorGuidance": [
            { "matchCodes": [ "credential_missing" ], "matchKeywords": [], "message": "配置引导" },
            { "matchKeywords": [ "1004" ], "message": "Key 无效" },
            { "matchKeywords": [], "message": "兜底" }
          ]
        }
        """);

        Assert.NotNull(manifest);
        var rules = manifest!.ErrorGuidance;
        Assert.Equal(3, rules.Count);
        Assert.Equal(new[] { "credential_missing" }, rules[0].MatchCodes);
        // 第一条有错误码 → 不是兑底；第三条关键字与错误码均空 → 兑底
        Assert.False(rules[0].MatchKeywords.Count == 0 && rules[0].MatchCodes.Count == 0);
        Assert.True(rules[2].MatchKeywords.Count == 0 && rules[2].MatchCodes.Count == 0);
    }

    /// <summary>验证：UsageError.Code 随 CreateError 结构化错误传递（供 matchCodes 匹配）。</summary>
    [Fact]
    public void UsageError_Code_FlowsThroughCreateError()
    {
        var error = new UsageError(UsageErrorKind.Unknown, "未配置登录态") { Code = UsageErrorCodes.CredentialMissing };
        var info = UsageInfo.CreateError("T7", "T7", error);

        Assert.False(info.IsSuccess);
        Assert.Equal(UsageErrorCodes.CredentialMissing, info.Error?.Code);
    }
}
