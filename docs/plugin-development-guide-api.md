# API 插件开发指南

> req-086-3.6：基于 `PluginBase` + `IUsageProvider` 从 0 到 1 开发一个 API 插件。

---

## 前置条件

- .NET 8 SDK
- 已克隆 UsageMonitor 仓库并能编译 `UsageMonitor.sln`
- 目标服务商提供 REST API（带 ApiKey 认证）

---

## 第一步：创建项目

### 1.1 复制模板项目

```bash
cp -r src/Plugins/UsageMonitor.Plugin.Template.Api src/Plugins/UsageMonitor.Plugin.YourApi
```

### 1.2 修改项目文件

编辑 `UsageMonitor.Plugin.YourApi.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>UsageMonitor.Plugin.YourApi</RootNamespace>
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

将 `TemplateApiPlugin.cs` 重命名为 `YourApiPlugin.cs`。

---

## 第二步：实现插件类

### 2.1 基本信息

```csharp
public class YourApiPlugin : PluginBase, IUsageProvider
{
    public override string ProviderId => "yourapi";            // 唯一 ID，小写
    public override string DisplayName => "Your API";           // 显示名称
    public string Version => "1.0.0";
    public string Author => "Your Name";
    public string Description => "Your API 用量监控";
    public string? IconPath => null;                            // 可选：图标路径
}
```

### 2.2 配置字段

使用 `StandardConfigFields` 工厂方法：

```csharp
public IReadOnlyList<ConfigField> ConfigFields => new[]
{
    StandardConfigFields.ApiKey(ProviderId),                           // ApiKey（Password，必填）
    StandardConfigFields.BaseUrl(ProviderId, "https://api.example.com/v1"),  // BaseUrl
    StandardConfigFields.Organization(ProviderId),                     // Organization（可选）
};
```

**可用字段工厂**：

| 方法 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `ApiKey(providerId)` | Password | 是 | API 密钥 |
| `BaseUrl(providerId, defaultUrl)` | Text | 否 | API 基础地址 |
| `Organization(providerId)` | Text | 否 | 组织 ID（OpenAI 等） |

### 2.3 核心 API 调用逻辑

```csharp
public async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
{
    try
    {
        var apiKey = config.GetValue("ApiKey")?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return UsageInfo.CreateError(ProviderId, DisplayName,
                UsageError.Auth("未配置 ApiKey，请在设置中填写"));
        }

        var baseUrl = config.GetValue("BaseUrl")?.Trim() ?? "https://api.example.com/v1";

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var response = await http.GetAsync($"{baseUrl}/usage", ct);
        if (!response.IsSuccessStatusCode)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName,
                UsageError.Network($"API 请求失败: {response.StatusCode}", (int)response.StatusCode));
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        // 解析 JSON 响应（根据实际 API 格式调整）
        // var data = JsonSerializer.Deserialize<UsageResponse>(json);

        return new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            Quantity = new Quantity(0, new CurrencyUnit("USD")),
            UsedAmount = 0,
            TotalAmount = 100,
            Unit = "USD",
            LastUpdated = DateTime.Now,
        };
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return UsageInfo.CreateError(ProviderId, DisplayName, "用户取消");
    }
    catch (Exception ex)
    {
        LogError("GetUsageAsync 异常", ex);
        return UsageInfo.CreateError(ProviderId, DisplayName,
            UsageError.Unknown($"查询异常: {ex.Message}"));
    }
}
```

### 2.4 配置验证

```csharp
public async Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
{
    var result = await GetUsageAsync(config, ct);
    return result.IsSuccess;
}
```

---

## 第三步：错误处理

使用 `UsageError` 工厂方法创建结构化错误：

| 工厂方法 | 适用场景 |
|----------|----------|
| `UsageError.Auth(message)` | 认证失败（ApiKey 无效/过期） |
| `UsageError.Network(message, httpStatus?)` | 网络错误（超时/DNS/连接拒绝） |
| `UsageError.RateLimit(message)` | 触发速率限制 |
| `UsageError.Parse(message)` | 响应解析失败 |
| `UsageError.Unknown(message)` | 其他未知错误 |

```csharp
// 示例：区分不同错误类型
return response.StatusCode switch
{
    HttpStatusCode.Unauthorized => UsageInfo.CreateError(ProviderId, DisplayName,
        UsageError.Auth("ApiKey 无效或已过期")),
    HttpStatusCode.TooManyRequests => UsageInfo.CreateError(ProviderId, DisplayName,
        UsageError.RateLimit("已触发速率限制，请稍后重试")),
    _ => UsageInfo.CreateError(ProviderId, DisplayName,
        UsageError.Network($"API 请求失败: {response.StatusCode}", (int)response.StatusCode)),
};
```

---

## 第四步：Quantity 与 UnitBase

req-086-3.4 引入了强类型数量体系：

```csharp
// 货币类
Quantity = new Quantity(12.50m, new CurrencyUnit("USD"));

// Token 类
Quantity = new Quantity(1500000, new TokenUnit("tokens"));

// 百分比
Quantity = new Quantity(66.5m, new PercentUnit());

// 积分/额度
Quantity = new Quantity(500, new CreditUnit("credits"));
```

**建议同时写入旧字段以兼容旧版主窗口**：

```csharp
return new UsageInfo
{
    Quantity = new Quantity(used, new CurrencyUnit("USD")),  // 新字段
    UsedAmount = used,      // 旧字段（兼容）
    TotalAmount = total,    // 旧字段（兼容）
    Unit = "USD",           // 旧字段（兼容）
};
```

---

## 第五步：编译与部署

```bash
dotnet restore src/Plugins/UsageMonitor.Plugin.YourApi
dotnet build src/Plugins/UsageMonitor.Plugin.YourApi --no-restore
```

将 DLL 复制到主程序 `plugins` 目录即可。

---

## 生命周期钩子（可选）

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
```

---

## 常见问题

### Q: API 返回嵌套 JSON，如何解析？

A: 定义响应 DTO 并使用 `System.Text.Json`：

```csharp
public record UsageResponse(decimal Used, decimal Total, string Unit);

var data = JsonSerializer.Deserialize<UsageResponse>(json);
```

### Q: 需要分页获取数据？

A: 在 `GetUsageAsync` 中循环请求，累加结果后返回。

### Q: 如何支持多个用量指标？

A: 将额外指标存入 `UsageInfo.Extra` 字典，主窗口图表可通过 `Extra` 读取。
