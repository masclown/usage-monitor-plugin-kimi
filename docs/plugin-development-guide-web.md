# 网页插件开发指南

> req-086-3.6：基于 `WebPluginBase` 从 0 到 1 开发一个网页插件。

---

## 前置条件

- .NET 8 SDK
- 已克隆 UsageMonitor 仓库并能编译 `UsageMonitor.sln`
- 目标网站有可通过浏览器访问的用量页面（需要登录态）

---

## 第一步：创建项目

### 1.1 复制模板项目

```bash
# 复制模板项目
cp -r src/Plugins/UsageMonitor.Plugin.Template.Web src/Plugins/UsageMonitor.Plugin.YourService
```

### 1.2 修改项目文件

编辑 `UsageMonitor.Plugin.YourService.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>UsageMonitor.Plugin.YourService</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NoWarn>$(NoWarn);CS0618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\UsageMonitor.Core\UsageMonitor.Core.csproj" />
  </ItemGroup>
</Project>
```

### 1.3 重命名主类文件

将 `TemplateWebPlugin.cs` 重命名为 `YourServicePlugin.cs`。

---

## 第二步：实现插件类

### 2.1 基本信息

```csharp
public class YourServicePlugin : WebPluginBase
{
    public override string ProviderId => "yourservice";        // 唯一 ID，小写
    public override string DisplayName => "Your Service";       // 显示名称
    public override string Version => "1.0.0";
    public override string Author => "Your Name";
    public override string Description => "Your Service 用量监控";
    public override string? IconPath => null;                   // 可选：图标路径
}
```

### 2.2 配置字段

使用 `StandardWebConfigFields` 工厂方法声明配置字段：

```csharp
public override IReadOnlyList<ConfigField> ConfigFields => new[]
{
    StandardWebConfigFields.Cookie(ProviderId),              // Cookie（Password 类型）
    StandardWebConfigFields.Region(ProviderId, "CN", "CN", "Global"),  // 区域选择
    StandardWebConfigFields.AutoRefresh(ProviderId, true),   // 自动刷新开关
    StandardWebConfigFields.Proxy(ProviderId),               // 代理设置
    StandardWebConfigFields.Headless(ProviderId, false),     // 无头模式开关
};
```

**可用字段工厂**：

| 方法 | 类型 | 说明 |
|------|------|------|
| `Cookie(providerId)` | Password | 浏览器登录态 Cookie |
| `Region(providerId, default, options)` | Select | 服务区域选择 |
| `AutoRefresh(providerId, default)` | Boolean | 自动刷新开关 |
| `Proxy(providerId)` | Text | HTTP 代理地址 |
| `Headless(providerId, default)` | Boolean | 浏览器无头模式 |
| `ShowBar(providerId, barKey, default)` | Boolean | 进度条显示开关 |

### 2.3 登录配置

```csharp
public override BrowserLoginConfig? LoginConfig => new()
{
    LoginUrl = LoginUrl,
    CookieDomainFilters = CookieDomainFilters,
    ValidateUrl = UsageUrl,
};
```

### 2.4 抽象属性（必须实现）

```csharp
// 登录入口 URL
protected override string LoginUrl => "https://yourservice.com/login";

// 用量页面 URL
protected override string UsageUrl => "https://yourservice.com/console/usage";

// Cookie 域名过滤（用于判定登录态）
protected override string[] CookieDomainFilters => new[] { ".yourservice.com" };

// 无头模式（默认 false，调试时改 true）
protected override bool Headless => false;

// 共享 HttpClient（用于 API 回退路径）
protected override HttpClient Http { get; } = new();
```

### 2.5 核心解析逻辑（必须实现）

`ParseUsagePageAsync` 是模板方法的核心——在已登录、已导航到用量页面的 `IPage` 上提取数据：

```csharp
protected override async Task<UsageInfo> ParseUsagePageAsync(IPage page)
{
    try
    {
        // 方式一：使用 WebPageParser（推荐）
        var used = await WebPageParser.ExtractNumberAsync(
            page, ".usage-used", WebPageParser.ExtractMode.CssSelector);
        var total = await WebPageParser.ExtractNumberAsync(
            page, ".usage-total", WebPageParser.ExtractMode.CssSelector);

        if (used == null || total == null)
        {
            return CreateError("未找到用量数据元素，请检查页面结构或登录态");
        }

        // 方式二：直接使用 Playwright API
        // var element = await page.QuerySelectorAsync(".usage-used");
        // var text = await element?.InnerTextAsync();

        // 使用 req-086-3.4 新字段 Quantity 表示用量
        return new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            Quantity = new Quantity(used.Value, new CurrencyUnit("USD")),
            // 兼容旧字段（建议同时写入）
            UsedAmount = used.Value,
            TotalAmount = total.Value,
            Unit = "USD",
            LastUpdated = DateTime.Now,
        };
    }
    catch (Exception ex)
    {
        LogError("ParseUsagePageAsync 异常", ex);
        return CreateError($"解析页面异常: {ex.Message}");
    }
}
```

---

## 第三步：WebPageParser 提取模式

`WebPageParser` 提供三种提取模式：

### CSS Selector

```csharp
var text = await WebPageParser.ExtractAsync(
    page, ".usage-value", WebPageParser.ExtractMode.CssSelector);
```

### XPath

```csharp
var text = await WebPageParser.ExtractAsync(
    page, "//div[@class='usage']/span", WebPageParser.ExtractMode.XPath);
```

### Regex（从页面 HTML 中提取）

```csharp
var text = await WebPageParser.ExtractAsync(
    page, @"""usedAmount"":\s*(\d+\.?\d*)", WebPageParser.ExtractMode.Regex);
```

### 批量提取

```csharp
var results = await WebPageParser.ExtractBatchAsync(page, new()
{
    ["used"] = (".usage-used", WebPageParser.ExtractMode.CssSelector),
    ["total"] = (".usage-total", WebPageParser.ExtractMode.CssSelector),
    ["percent"] = (".usage-percent", WebPageParser.ExtractMode.CssSelector),
});
```

### 数值提取（自动解析）

```csharp
// 自动移除千分位逗号、百分号、单位后缀
var number = await WebPageParser.ExtractNumberAsync(page, ".value", WebPageParser.ExtractMode.CssSelector);

// 自动解析百分比 "66%" → 66.0
var percent = await WebPageParser.ExtractPercentAsync(page, ".percent", WebPageParser.ExtractMode.CssSelector);
```

---

## 第四步：编译与部署

### 4.1 编译

```bash
dotnet restore src/Plugins/UsageMonitor.Plugin.YourService
dotnet build src/Plugins/UsageMonitor.Plugin.YourService --no-restore
```

### 4.2 部署

将编译输出的 DLL 复制到主程序的 `plugins` 目录：

```
UsageMonitor.App/bin/Debug/net8.0-windows/plugins/UsageMonitor.Plugin.YourService.dll
```

### 4.3 验证

启动主程序，在设置页面应能看到 "Your Service" 插件，配置 Cookie 后即可查询用量。

---

## 第五步：生命周期钩子（可选）

```csharp
public override async Task InitializeAsync(PluginContext context)
{
    await base.InitializeAsync(context);
    LogInfo("插件初始化完成");
}

public override async Task StartAsync()
{
    await base.StartAsync();
    LogInfo("插件启动完成");
}

public override async Task StopAsync()
{
    await base.StopAsync();
    LogInfo("插件停止完成");
}
```

---

## 模板方法流程

```
GetUsageAsync(config, ct)
  ├── GetOrCreatePageAsync(ct)        ← 懒初始化浏览器/页面
  ├── EnsureLoginAsync(page, config)  ← 注入 Cookie 到浏览器上下文
  ├── NavigateToUsagePageAsync(page)  ← 导航到 UsageUrl
  └── ParseUsagePageAsync(page)       ← 子类实现：提取数据
```

---

## 常见问题

### Q: 页面是 SPA，CSS Selector 找不到元素？

A: 在 `ParseUsagePageAsync` 中先等待元素出现：

```csharp
await page.WaitForSelectorAsync(".usage-value", new PageWaitForSelectorOptions
{
    Timeout = 10000
});
```

### Q: 需要处理分页或多个用量指标？

A: 在 `ParseUsagePageAsync` 中多次调用 `WebPageParser.ExtractAsync`，将结果存入 `UsageInfo.Extra` 字典。

### Q: 如何调试？

A: 将 `Headless` 改为 `false`，浏览器会显示窗口，可观察页面加载过程。
