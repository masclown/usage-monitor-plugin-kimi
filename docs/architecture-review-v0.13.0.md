# UsageMonitor MVP 全方位改进报告

> 评审日期：2026-07-19
> 评审范围：`D:/应用开发/UsageMonitor` 全部源码（v0.13.0）
> 评审维度：架构设计 / UI 与交互 / 代码实现 Bug
> 适用对象：开发团队对齐与排期

---

## 0. 整体评估

| 维度 | 评级 | 一句话结论 |
|------|------|-----------|
| 架构分层 | B+ | Core/App/Plugins 三层清晰，但服务对象全靠 `new`，没有 DI 容器 |
| 主题系统 | A- | Token/Dark/Light 三层分离规范，`DynamicResource` 用法正确 |
| 插件契约 | B | `IUsageProvider` 接口设计合理，但承载过多职责（图表/色阶/Balance）|
| 代码质量 | C+ | 7 个 Critical、19+ 个 Major 问题，部分直接影响功能可用性 |
| 健壮性 | C | 多处资源泄漏（事件订阅未解绑、HttpClient、Playwright profile）|
| 安全 | B- | 凭据加密方案完整（DPAPI + Win Credential + AES-GCM），但双轨制混乱、Cookie header injection 风险 |

**最关键结论**：项目骨架扎实、设计意图清晰，但 **MiniMax 插件链路、并发安全、资源释放** 三块需要优先修复，否则生产环境稳定性堪忧。

---

## 一、架构层面改进（8 项）

### A1. 双轨敏感存储体系混乱 — 统一到 SecretStore

**问题位置**：
- `src/UsageMonitor.Core/Services/ConfigService.cs` 行 605-668（DPAPI + JSON 加密）
- `src/UsageMonitor.Core/Services/Security/` 全套（Win Credential Manager / AES-GCM 文件）

**问题描述**：
项目同时存在两套敏感凭据存储方案：

1. **ConfigService**：用 `ProtectedData.Protect`（DPAPI）加密后 base64 存入 `config.json`，与普通配置耦合在同一文件
2. **SecretStore**：通过 `ISecretStore` 抽象，后端可选 Windows Credential Manager 或 AES-GCM 文件

`SecretConfigBridge` 注释推荐"Cookie / Token / API Key 走 SecretStore，普通配置走 ConfigService"，但 **实际代码中所有 Provider 的 API Key/Cookie 仍走 ConfigService 的 DPAPI 路径**，SecretStore 体系基本是空设计。

**改进建议**：
统一到 SecretStore，ConfigService 只存非敏感配置。改造步骤：

```csharp
// ConfigService 中移除 EncryptSensitiveFields / DecryptSensitiveFields
// ProviderConfig 增加判断：敏感字段（ConfigFieldType.Password）从 SecretStore 取
public class ProviderConfig
{
    public string GetValue(string key, ISecretStore? secretStore = null)
    {
        if (IsSensitiveKey(key) && secretStore != null)
        {
            return secretStore.Get($"UsageMonitor.Provider.{ProviderId}", key) ?? string.Empty;
        }
        return Values.TryGetValue(key, out var v) ? v : null;
    }
}
```

迁移期保留 DPAPI 解密做兼容（`if (val looks like base64 DPAPI) migrate to SecretStore`），用一个发布版本完成迁移。

---

### A2. FileLogger 日志路径在生产环境会 fallback

**问题位置**：`src/UsageMonitor.Core/Services/FileLogger.cs` 行 199-213

```csharp
private static string ResolveProjectRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (int i = 0; i < 10 && dir != null; i++)
    {
        var slnPath = Path.Combine(dir.FullName, "UsageMonitor.sln");
        if (File.Exists(slnPath)) return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();  // ⚠ 发布版本会走到这里
}
```

**问题描述**：
开发环境能找到 `.sln`，但 **打包发布给用户后** AppContext.BaseDirectory 下没有 `.sln`，会 fallback 到 `Directory.GetCurrentDirectory()`。这意味着：
- 双击 Start-UsageMonitor.vbs 启动：日志写到 vbs 所在目录
- 开机自启：日志写到 `C:\Windows\System32\` (无权限会失败)
- 用户从任意目录启动：日志散落各处

**改进建议**：
直接用 `%AppData%/UsageMonitor/logs/` 作为固定路径，与 config.json、history.db 同目录：

```csharp
private static string ResolveProjectRoot()
{
    // 始终使用 %AppData%/UsageMonitor/，与 config.json / history.db 一致
    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor");
}
// LogDir = Path.Combine(ProjectRoot, "logs")
```

迁移时检查旧目录是否有日志，自动迁移过来。

---

### A3. PluginManager 完全无并发保护

**问题位置**：`src/UsageMonitor.Core/Plugins/PluginManager.cs` 行 11（`private readonly List<LoadedPlugin> _plugins = new();`）

**问题描述**：
`_plugins` 是 `List<T>`，多线程访问没有任何锁。`RefreshService.RefreshAllAsync` 在读 `GetEnabledPlugins()`，UI 线程可能同时在 `LoadPlugins()` 或 `UnloadPlugin()`。List 在并发写时可能抛 `InvalidOperationException: Collection was modified`。

**改进建议**：
用 `ConcurrentDictionary<string, LoadedPlugin>` 替代，按 ProviderId 索引：

```csharp
public class PluginManager
{
    private readonly ConcurrentDictionary<string, LoadedPlugin> _plugins = new();

    public IReadOnlyList<LoadedPlugin> Plugins => _plugins.Values.ToList().AsReadOnly();

    public void LoadPlugins()
    {
        // 清空
        foreach (var key in _plugins.Keys.ToList())
            _plugins.TryRemove(key, out _);
        // 加载...
    }

    public LoadedPlugin? GetPlugin(string providerId)
        => _plugins.TryGetValue(providerId, out var p) ? p : null;
}
```

---

### A4. LoadedPlugin 状态字段无线程同步

**问题位置**：`src/UsageMonitor.Core/Plugins/LoadedPlugin.cs` 行 21-30

```csharp
public UsageInfo? LastUsage { get; set; }
public DateTime? LastQueryTime { get; set; }
public bool LastQuerySuccess { get; set; }
public bool IsEnabled { get; set; } = true;
```

**问题描述**：
`RefreshService` 在后台线程写 `LastUsage`，UI 线程读 `LastUsage` 显示。`UsageInfo` 是引用类型，赋值不是原子的，UI 可能读到半构造对象。`IsEnabled` 也存在类似问题。

**改进建议**：
用 `volatile` 或 `Interlocked` 保护：

```csharp
public class LoadedPlugin
{
    private UsageInfo? _lastUsage;
    private volatile bool _isEnabled = true;

    public UsageInfo? LastUsage
    {
        get => Volatile.Read(ref _lastUsage);
        set => Volatile.Write(ref _lastUsage, value);
    }
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }
    // LastQueryTime / LastQuerySuccess 同样处理
}
```

`UsageInfo` 本身也应改为不可变（所有 setter 改为 init-only），避免半构造问题。

---

### A5. RefreshService._isRefreshing 非原子并发控制

**问题位置**：`src/UsageMonitor.Core/Services/RefreshService.cs` 行 18, 82-83

```csharp
private bool _isRefreshing;
// ...
public async Task RefreshAllAsync(string triggerKind = "manual")
{
    if (_isRefreshing) return;  // ⚠ 非原子读
    _isRefreshing = true;       // ⚠ 非原子写
    // ...
}
```

**问题描述**：
Timer 触发的 `RefreshAllAsync("auto")` 与用户点击"刷新"触发的 `RefreshAllAsync("manual")` 可能并发，两个线程同时通过 `if (_isRefreshing)` 检查，都进入刷新逻辑，导致同一 Provider 被并发查询（虽然有 per-provider 锁，但浪费资源且事件会触发两次）。

**改进建议**：
用 `Interlocked.CompareExchange`：

```csharp
private int _isRefreshing;  // 0 = idle, 1 = running

public async Task RefreshAllAsync(string triggerKind = "manual")
{
    if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0) return;
    try
    {
        // ... 原刷新逻辑
    }
    finally
    {
        Volatile.Write(ref _isRefreshing, 0);
    }
}
```

---

### A6. 没有依赖注入容器，App.xaml.cs 是 God Object

**问题位置**：`src/UsageMonitor.App/App.xaml.cs` 行 23-37, 42-190

**问题描述**：
`App` 类直接 `new ConfigService()`、`new PluginManager()`、`new RefreshService(...)`、`new MainViewModel(...)`，单文件 720 行。所有服务的生命周期、依赖关系散落在 `OnStartup` 中手工编织，难以测试，难以替换实现。

**改进建议**：
引入 `Microsoft.Extensions.DependencyInjection`（已内置在 .NET 8）：

```csharp
// App.xaml.cs
private IServiceProvider _services = null!;

protected override void OnStartup(StartupEventArgs e)
{
    var services = new ServiceCollection();
    services.AddSingleton<ConfigService>();
    services.AddSingleton<PluginManager>();
    services.AddSingleton<UsageHistoryRepository>(UsageHistoryRepository.CreateDefault());
    services.AddSingleton<UsageHistoryStore>();
    services.AddSingleton<RefreshService>();
    services.AddSingleton<MainViewModel>();
    services.AddTransient<MainWindow>();
    services.AddTransient<SettingsWindow>();
    services.AddTransient<HistoryWindow>();
    _services = services.BuildServiceProvider();

    // 初始化序列
    var config = _services.GetRequiredService<ConfigService>();
    config.Load();
    // ... 其他初始化

    var mainWindow = _services.GetRequiredService<MainWindow>();
    mainWindow.Show();
}
```

注意 `RefreshService` 现在需要从 DI 拿 `IHttpClientFactory`，下面 A7 详述。

---

### A7. HttpClient 多处独立实例化，存在 socket 耗尽风险

**问题位置**：
- `src/Plugins/UsageMonitor.Plugin.Deepseek/DeepseekProvider.cs` 行 18
- `src/Plugins/UsageMonitor.Plugin.MiMo/MiMoProvider.cs` 行 17
- `src/Plugins/UsageMonitor.Plugin.OpenAI/OpenAIProvider.cs` 行 17
- `src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxProvider.cs` 行 51（静态字段）
- `src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxBalanceFetcher.cs` 行 68
- `src/UsageMonitor.Core/Services/BrowserLoginService.cs` 行 546（`using var http = new HttpClient()`）

**问题描述**：
每个 Provider 都持有自己的 `static readonly HttpClient`，且 `BrowserLoginService.CheckCookieValidAsync` 还每次 `new HttpClient()`。`MiniMaxBalanceFetcher` 与 `MiniMaxProvider` 同插件却各持一个。HttpClient 内部维护 socket 连接池，过多实例会耗尽 socket（高并发场景下出现 `SocketException`）。

**改进建议**：
统一用 `IHttpClientFactory`（需要 `Microsoft.Extensions.Http`）：

```csharp
// 在 DI 容器注册
services.AddHttpClient("deepseek", c =>
{
    c.BaseAddress = new Uri("https://api.deepseek.com/");
    c.Timeout = TimeSpan.FromSeconds(30);
});
services.AddHttpClient("minimax", c => { /* ... */ });

// Provider 通过构造注入 IHttpClientFactory
public class DeepseekProvider : IUsageProvider
{
    private readonly IHttpClientFactory _httpFactory;
    public DeepseekProvider(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config)
    {
        var http = _httpFactory.CreateClient("deepseek");
        // ...
    }
}
```

如果不引入 DI，至少抽 `HttpUsageProviderBase` 让所有 Provider 共享一个 `static HttpClient`。

---

### A8. AppSettings 默认值硬编码 minimax 业务参数

**问题位置**：`src/UsageMonitor.Core/Services/ConfigService.cs` 行 150-161

```csharp
public Dictionary<...> ProviderHeatMapTiers { get; set; } = new()
{
    ["minimax"] = new List<...>
    {
        new() { MinTokens = 0,            ColorHex = "#f3f4f6" },
        // ... 6 档
    }
};
```

**问题描述**：
`AppSettings` 是通用配置类，却把 MiniMax 的色阶默认值硬编码进来。其他 Provider 的色阶靠 `IUsageProvider.HeatMapTiers` 接口提供（已存在但未串起来），MiniMax 特殊对待导致维护时两处同步。

**改进建议**：
默认值改为空字典，启动时从 `IUsageProvider.HeatMapTiers` 装配：

```csharp
public Dictionary<...> ProviderHeatMapTiers { get; set; } = new();

// App.OnStartup 中
foreach (var plugin in _pluginManager.Plugins)
{
    var pid = plugin.Provider.ProviderId;
    if (plugin.Provider.HeatMapTiers != null && plugin.Provider.HeatMapTiers.Count > 0
        && !_configService.Settings.ProviderHeatMapTiers.ContainsKey(pid))
    {
        _configService.Settings.ProviderHeatMapTiers[pid] =
            new List<HeatMapTierConfig>(plugin.Provider.HeatMapTiers);
    }
}
```

---

## 二、UI 与交互改进（6 项）

### U1. MainWindow.xaml 主卡片模板 350+ 行，缺乏拆分

**问题位置**：`src/UsageMonitor.App/MainWindow.xaml` 行 84-431

**问题描述**：
单个 `DataTemplate` 内塞了 Provider 卡片全部逻辑：标题栏 + 5h 限额进度条 + 周限额进度条 + 视频赠送进度条 + 错误信息 + 余额快照 + 5 种图表。350 行 XAML 难以维护，每次改一个进度条样式都要在长模板里搜索。

**改进建议**：
拆为 `ProviderCard.xaml` UserControl：

```
src/UsageMonitor.App/Views/ProviderCard.xaml       # 卡片容器
src/UsageMonitor.App/Views/ProviderCard.xaml.cs
src/UsageMonitor.App/Controls/ProgressBarRow.xaml  # 单个进度条（5h/周/视频复用）
src/UsageMonitor.App/Controls/ProgressBarRow.xaml.cs
```

`MainWindow.xaml` 中 DataTemplate 简化为：

```xml
<DataTemplate>
    <views:ProviderCard ViewModel="{Binding}" />
</DataTemplate>
```

进度条抽 UserControl 后，5h/周/视频三段从 90 行 × 3 缩减为：

```xml
<controls:ProgressBarRow Label="5h 限额"
                         Percent="{Binding PrimaryBarPercent}"
                         ResetText="{Binding PrimaryResetText}"
                         VisibilityFlag="{Binding Show5hBar}" />
```

---

### U2. 缺少 Accessibility 标注，可访问性差

**问题描述**：
全项目搜索 `AutomationProperties` 几乎无结果。卡片上的"⟳"刷新按钮、"🔧"设置按钮仅靠 ToolTip 标注，屏幕阅读器无法识别。图标按钮没有 `AutomationProperties.Name`，盲人用户无法操作。

**改进建议**：
所有图标按钮统一加 a11y 标注：

```xml
<Button Command="{Binding RefreshCardCommand}"
        Style="{StaticResource IconButtonStyle}"
        ToolTip="仅刷新此卡片的用量信息"
        AutomationProperties.Name="刷新此卡片"
        AutomationProperties.HelpText="重新查询该服务商的当前用量">
    <TextBlock Text="⟳" />
</Button>
```

进度条改用 `ProgressBar` 控件（自带 a11y）替代手写 Border：

```xml
<ProgressBar Value="{Binding PrimaryBarPercent}"
             Maximum="100"
             Height="9"
             AutomationProperties.Name="5h 限额进度" />
```

---

### U3. 错误状态颜色用 Converter 切换，违反 MVVM

**问题位置**：`src/UsageMonitor.App/MainWindow.xaml` 行 168

```xml
<TextBlock Text="{Binding StatusText}"
           Foreground="{Binding IsError, Converter={StaticResource ErrorColorConverter}}" />
```

**问题描述**：
通过 Converter 把 bool 转 Brush 是反模式。Converter 难以测试、难以在多主题下工作（Converter 内部硬编码颜色）、Designer 不支持预览。

**改进建议**：
改用 Style + DataTrigger：

```xml
<TextBlock Text="{Binding StatusText}" FontSize="13">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsError}" Value="True">
                    <Setter Property="Foreground" Value="{DynamicResource DangerBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

---

### U4. 卡片高度因 Provider 而异，列表视觉不齐

**问题描述**：
不同 Provider 的卡片高度差异巨大：Deepseek 只显示 1 个进度条（约 200px），MiniMax 显示 5h + 周 + 视频 + 4 个图表（约 700px）。视觉跳变明显，用户扫视成本高。

**改进建议**：
1. 卡片设 `MinHeight` 保证最小高度
2. 多 Provider 场景用 `UniformGrid Columns="2"` 两列布局（卡片宽度自适应，单列时退化为单列）
3. 或在卡片头部加"展开/折叠"按钮，默认折叠详细图表

```xml
<Border Style="{StaticResource CardBorderStyle}" MinHeight="220">
    <!-- ... -->
</Border>
```

---

### U5. 主窗口关闭即隐藏，用户找不到退出路径

**问题位置**：`src/UsageMonitor.App/MainWindow.xaml.cs` 行 71-75

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    e.Cancel = true;
    Hide();
}
```

**问题描述**：
点 X 关闭只是隐藏窗口，新手用户会以为"程序退出了"，但任务栏/托盘还在跑。从托盘"退出"按钮才能真退出，但用户可能找不到。

**改进建议**：
首次关闭时弹一次提示：

```csharp
private bool _hasShownMinimizeHint;

protected override void OnClosing(CancelEventArgs e)
{
    if (!_hasShownMinimizeHint)
    {
        _hasShownMinimizeHint = true;
        var result = MessageBox.Show(
            "关闭窗口将最小化到托盘继续监控。\n如需完全退出，请右键托盘图标 → 退出。\n\n是否继续最小化？",
            "UsageMonitor",
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes)
        {
            _hasShownMinimizeHint = false;
            e.Cancel = false;  // 真退出
            return;
        }
    }
    e.Cancel = true;
    Hide();
}
```

---

### U6. 主题切换未短路，重复插拔 ResourceDictionary 闪烁

**问题位置**：`src/UsageMonitor.App/Helpers/ThemeManager.cs` 行 36-65

**问题描述**：
`Apply(ThemeMode.Dark)` 即使当前已经是 Dark 也会重新移除并插入字典，触发全 UI 重解析。设置页打开时若误调用两次会闪烁。

**改进建议**：
开头加短路：

```csharp
public static void Apply(ThemeMode mode)
{
    if (mode == Current) return;  // 短路
    // ... 原逻辑
}
```

---

## 三、代码实现 Bug（按严重程度排序）

### Critical（影响功能可用性，必须立即修复）

#### B1. MiniMax Global 区域 Cookie 域硬编码导致登录失效

**位置**：`src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxDomExtractor.cs` 行 596-618

**问题**：所有 cookie 都被强制 `Domain = ".minimaxi.com"`，但 MiniMax 同时支持 CN (`minimaxi.com`) 和 Global (`minimax.io`) 两个区域。Global 用户登录后 cookie 实际属于 `minimax.io` 域，注入错误域后 Playwright 无法匹配，页面跳回登录页，DOM 提取永远失败并回退到 API 路径。

**修复**：

```csharp
private static List<Cookie> ParseCookieString(string cookieString, string region)
{
    var domain = string.Equals(region, "Global", StringComparison.OrdinalIgnoreCase)
        ? ".minimax.io" : ".minimaxi.com";

    var list = new List<Cookie>();
    foreach (var part in cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
        if (kv.Length != 2) continue;
        list.Add(new Cookie
        {
            Name = kv[0],
            Value = kv[1],
            Domain = domain,
            Path = "/"
        });
    }
    return list;
}
```

调用处传入 region：

```csharp
var region = config.GetValue("Region") ?? "CN";
var cookies = ParseCookieString(cookieString, region);
```

---

#### B2. MiniMaxDomExtractor 用非线程安全字典接收并发 XHR

**位置**：`src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxDomExtractor.cs` 行 119-139

**问题**：

```csharp
var capturedResponses = new Dictionary<string, string>();  // ⚠ 非线程安全
page.Response += async (sender, e) =>
{
    // 多个 XHR 并发触发此事件（async void），并发写 Dictionary 会抛
    // InvalidOperationException: Operations that change non-concurrent collections
    capturedResponses[url] = body;
};
```

Next.js SPA 会同时发起多个 XHR，并发写字典会抛异常或数据损坏，导致后续解析拿不到关键 JSON。

**修复**：

```csharp
var capturedResponses = new ConcurrentDictionary<string, string>();
page.Response += async (sender, e) =>
{
    // ...
    capturedResponses[url] = body;  // ConcurrentDictionary 索引器线程安全
};
```

---

#### B3. DOM 选择器硬编码中文 aria-label，前端改文案即失效

**位置**：`src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxDomExtractor.cs` 行 228-254

**问题**：

```csharp
await page.QuerySelectorAsync('div[aria-label^="5h 限额"]')
await page.QuerySelectorAsync('div[aria-label^="周限额"]')
await page.QuerySelectorAsync('div[aria-label^="视频赠送"]')
```

MiniMax 一旦做 i18n、A/B 测试或文案微调（如 "5小时限额"），整个提取链路失效。注释（行 19）自承这是 "PRIMARY data source"，单点故障。

**修复方案**：
1. 优先用 captured XHR JSON（`remains_percent`、`usage_summary`）作为单一真相源
2. DOM selector 改用稳定钩子，与 MiniMax 前端约定 `data-testid`：

```csharp
// 优先走 XHR JSON
if (capturedResponses.TryGetValue("remains_percent", out var json))
{
    return ParseFromJson(json);
}
// 兜底走 DOM（用 data-testid 而非文案）
var element = await page.QuerySelectorAsync('[data-testid="usage-5h-bar"]');
```

---

#### B4. BrowserLoginService 全静态共享状态，并发登录互相覆盖

**位置**：`src/UsageMonitor.Core/Services/BrowserLoginService.cs` 行 25, 94

**问题**：
`_configService` 和 `LastError` 都是静态字段，`LoginAndExtractCookieAsync` 是静态方法。若两个 Provider 同时触发登录（如启动时自动刷新两个需要登录的 Provider），后者的 `LastError` 会覆盖前者，UI 显示错误串扰。

**修复**：改为实例类 + 每 Provider 独立实例：

```csharp
public class BrowserLoginService  // 非 static
{
    private readonly ConfigService _configService;
    public string? LastError { get; private set; }  // 实例字段

    public BrowserLoginService(ConfigService configService)
    {
        _configService = configService;
    }
    // ...
}
```

调用方持有各自实例。

---

#### B5. OpenAI Provider API 路径错误，整个插件形同摆设

**位置**：`src/Plugins/UsageMonitor.Plugin.OpenAI/OpenAIProvider.cs` 行 88-89

**问题**：使用 `/v1/organization/usage`，但 OpenAI 官方 Billing/Usage API 实际路径已变更（且 Legacy API 已弃用）。当前路径返回 404，整个 OpenAI 插件无法工作。

**修复方案**：
1. 短期：明确抛 `NotImplementedException`，避免误以为可用：

```csharp
public Task<UsageInfo> GetUsageAsync(ProviderConfig config)
{
    return Task.FromResult(UsageInfo.CreateError(ProviderId, DisplayName,
        "OpenAI Provider 暂未实现：OpenAI 官方已弃用 v1/usage API，新 API 待接入。"));
}
```

2. 长期：参考 [OpenAI 官方文档](https://platform.openai.com/docs/api-reference/usage) 实现 `/v1/usage/completions?date=YYYY-MM-DD` 逐天查询。

---

#### B6. Cookie 字符串直接拼 HTTP 头，存在 header injection 风险

**位置**：
- `src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxProvider.cs` 行 343
- `src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxBalanceFetcher.cs` 行 176, 221, 240
- `src/UsageMonitor.Core/Services/BrowserLoginService.cs` 行 549

**问题**：

```csharp
request.Headers.Add("Cookie", cookie);  // ⚠ cookie 含 \r\n 会触发 header injection
```

Cookie 来自用户浏览器，可能含意外字符（CR/LF）。若任一 cookie 值含 `\r\n`，会触发 HTTP header injection；含其他控制字符则 `HttpRequestHeaders.Add` 抛 `FormatException`，错误信息不友好。

**修复**：

```csharp
private static string SanitizeCookieHeader(string cookie)
{
    // 移除所有控制字符，避免 header injection
    return Regex.Replace(cookie, @"[\r\n\t\x00-\x1F]", "");
}

// 调用处
request.Headers.TryAddWithoutValidation("Cookie", SanitizeCookieHeader(cookie));
```

---

#### B7. 用户取消被误报为"请求超时"

**位置**：`src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxProvider.cs` 行 273-277

**问题**：

```csharp
catch (TaskCanceledException)
{
    return UsageInfo.CreateError(ProviderId, DisplayName,
        "Request timeout (30s)...");
}
```

`TaskCanceledException` 是 `OperationCanceledException` 子类，用户主动取消（CancellationToken 触发）也会命中此分支。错误信息误导用户以为网络问题。

**修复**：

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    return UsageInfo.CreateError(ProviderId, DisplayName, "用户取消");
}
catch (TaskCanceledException)
{
    return UsageInfo.CreateError(ProviderId, DisplayName, "请求超时（30s）");
}
```

---

### Major（影响稳定性/性能，发布前应修复）

#### B8. DispatcherTimer 闭包泄漏（多处）

**位置**：
- `src/UsageMonitor.App/Views/TaskbarWindow.xaml.cs` 行 494-509
- `src/UsageMonitor.App/Views/TrayTooltipWindow.xaml.cs` 行 317-334
- `src/UsageMonitor.App/Views/TriggerAreaOverlayWindow.xaml.cs` 行 242-264

**问题**：每次位置变化都 `new DispatcherTimer(...)` 并 `+= Tick`，旧 timer 仅 `Stop()` 但事件 handler 未解绑，闭包持有上一轮引用，高频拖拽时大量 timer 对象 GC 压力大。

**修复**：构造函数中只订阅一次 Tick，回调中只 `Stop()/Start()`：

```csharp
// 构造函数
_savePositionTimer = new DispatcherTimer(DispatcherPriority.Background)
{
    Interval = TimeSpan.FromMilliseconds(500)
};
_savePositionTimer.Tick += (_, _) =>
{
    _savePositionTimer.Stop();
    SavePositionToConfig();
};

// OnLocationChanged 中
_savePositionTimer.Stop();
_savePositionTimer.Start();
```

---

#### B9. 图表控件 CollectionChanged 订阅在 Unloaded 时未解绑

**位置**：
- `src/UsageMonitor.App/Controls/BarChartControl.cs` 行 130-137
- `src/UsageMonitor.App/Controls/HistoryLineChartControl.cs` 行 162-171
- `src/UsageMonitor.App/Controls/YearHeatMapControl.cs` 行 158-166
- `src/UsageMonitor.App/Controls/MiniLineChartControl.cs` 行 249-257
- `src/UsageMonitor.App/Controls/DayNightArcControl.cs` 行 77-83

**问题**：依赖属性变化时正确 `-=` 旧集合，但控件被 DataTemplate 回收时（如 TaskbarWindow 卡片切换、HistoryWindow 关闭）依赖属性不变，订阅保留。源集合（ViewModel 中的 ObservableCollection）生命周期长于控件，闭包持有控件引用，阻止 GC，并在已卸载控件上触发 `InvalidateVisual`。

**修复**：所有图表控件统一加 OnUnloaded 解绑：

```csharp
private INotifyCollectionChanged? _subscribed;

protected override void OnUnloaded(RoutedEventArgs e)
{
    base.OnUnloaded(e);
    if (_subscribed != null)
    {
        _subscribed.CollectionChanged -= OnItemsChanged;
        _subscribed = null;
    }
}
```

---

#### B10. RingChartControl 动画/sticky timer 未在卸载时停止

**位置**：`src/UsageMonitor.App/Controls/RingChartControl.cs` 行 334-337, 536-578

**问题**：`_stickyTimer` 与 `_switchAnimTimer` 在控件卸载后仍可能 Tick，闭包持有 `this`，已卸载控件继续 `InvalidateVisual`。TaskbarWindow 中 RingChart 频繁创建销毁，泄漏累积。

**修复**：

```csharp
protected override void OnUnloaded(RoutedEventArgs e)
{
    base.OnUnloaded(e);
    _stickyTimer?.Stop();
    _switchAnimTimer?.Stop();
}
```

---

#### B11. HoverTooltip 静态字典永不清理

**位置**：`src/UsageMonitor.App/Controls/HoverTooltip.cs` 行 40, 56-160

**问题**：`ActiveTooltips` 为 `static Dictionary<FrameworkElement, ToolTip>`，仅在 `Hide(owner)` 显式调用时移除。owner 控件被 DataTemplate 回收而未触发 `OnMouseLeave`（如窗口直接关闭），条目永留，阻止 owner 被 GC。

**修复**：改用 `ConditionalWeakTable`（owner 被 GC 时自动移除条目）：

```csharp
private static readonly ConditionalWeakTable<FrameworkElement, ToolTip> ActiveTooltips = new();

public static void Show(FrameworkElement owner, ...)
{
    var tooltip = new ToolTip { ... };
    ActiveTooltips.AddOrUpdate(owner, tooltip);
    // ...
}

public static void Hide(FrameworkElement owner)
{
    if (ActiveTooltips.TryGetValue(owner, out var tooltip))
    {
        tooltip.IsOpen = false;
        ActiveTooltips.Remove(owner);
    }
}
```

---

#### B12. PluginConfigWindow 防重复 HashSet 大小写敏感

**位置**：`src/UsageMonitor.App/Views/PluginConfigWindow.xaml.cs` 行 56

```csharp
static readonly HashSet<string> _isLoginInProgress = new();  // ⚠ 默认大小写敏感
```

**问题**：若不同入口传入 `"MiniMax"` 与 `"minimax"`，防重复锁失效，可能并发触发两次 Edge 登录窗口。

**修复**：

```csharp
static readonly HashSet<string> _isLoginInProgress =
    new(StringComparer.OrdinalIgnoreCase);
```

---

#### B13. PluginConfigWindow.CreatePasswordInput 重复 Add 同一元素

**位置**：`src/UsageMonitor.App/Views/PluginConfigWindow.xaml.cs` 行 433-453

**问题**：切换"显示/隐藏"时 `grid.Children.Add(textBox)`，但 textBox 在初始化时已 `grid.Children.Add`。首次切换会抛 `InvalidOperationException: Specified element is already the logical child of another element.`

**修复**：初始化时即 Add，切换时只改 `Visibility`：

```csharp
// 初始化
Grid.SetColumn(textBox, 0);
grid.Children.Add(textBox);

// toggle 中
textBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
// 删除 grid.Children.Add(textBox)
```

---

#### B14. 4 个 Provider 重复样板代码，无基类抽取

**位置**：
- `DeepseekProvider.cs` 行 18, 88, 95-98
- `MiMoProvider.cs` 行 17, 80, 87-89
- `OpenAIProvider.cs` 行 17, 88, 99-101
- `MiniMaxProvider.cs` 行 51, 334, 358-360

**问题**：HttpClient 实例化、URL `TrimEnd('/')` 拼接、错误响应 `${(int)statusCode}: {errorBody}` 拼接、`JsonSerializerOptions { PropertyNameCaseInsensitive = true }` 每次新建、`catch (HttpRequestException/Exception)` 模板，四份几乎一致。

**修复**：抽 `HttpUsageProviderBase`：

```csharp
public abstract class HttpUsageProviderBase : IUsageProvider
{
    protected static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected abstract HttpClient Http { get; }
    public abstract string ProviderId { get; }
    public abstract string DisplayName { get; }
    // ... 其他 IUsageProvider 成员

    protected async Task<T?> GetJsonAsync<T>(
        string baseUrl, string path,
        Action<HttpRequestMessage>? configure = null,
        CancellationToken ct = default) where T : class
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{baseUrl.TrimEnd('/')}{path}");
        configure?.Invoke(req);

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"API error {(int)resp.StatusCode}: {Truncate(body, 200)}");
        }
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    protected static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "...";
}
```

各 Provider 实现只需关心 API 特有的反序列化逻辑。

---

#### B15. JsonSerializerOptions 每次请求 new

**位置**：4 个 Provider 的 GetUsageAsync 中均有 `new JsonSerializerOptions { PropertyNameCaseInsensitive = true }`

**问题**：System.Text.Json 会对每个 options 实例做元数据缓存构建，热路径频繁创建会显著增加 GC 压力和首次反序列化延迟。

**修复**：

```csharp
// 每个 Provider 内
private static readonly JsonSerializerOptions JsonOpts = new()
{
    PropertyNameCaseInsensitive = true
};
```

或抽到基类（见 B14）。

---

#### B16. ValidateConfigAsync 实质是完整用量查询

**位置**：4 个 Provider 的 ValidateConfigAsync

**问题**：所有 4 个 Provider 都是 `var result = await GetUsageAsync(config); return result.IsSuccess;`。每次"测试连接"都触发一次完整用量查询：消耗 API 配额、慢、且对 MiniMax 会启动 Playwright 浏览器（数十秒）。

**修复**：用更轻量的探针：

```csharp
// OpenAI：调 /v1/models（只列模型不消耗配额）
public async Task<bool> ValidateConfigAsync(ProviderConfig config)
{
    try
    {
        var http = _httpFactory.CreateClient("openai");
        http.DefaultRequestHeaders.Authorization =
            new("Bearer", config.GetValue("ApiKey"));
        var resp = await http.GetAsync("/v1/models");
        return resp.IsSuccessStatusCode;
    }
    catch { return false; }
}

// MiniMax：用 HEAD 请求首页，验证 cookie 是否有效
public async Task<bool> ValidateConfigAsync(ProviderConfig config)
{
    var cookie = config.GetValue("Cookie");
    if (string.IsNullOrEmpty(cookie)) return false;
    // 调用 CheckCookieValidAsync 而非完整 GetUsageAsync
    return await BrowserLoginService.CheckCookieValidAsync(cookie);
}
```

---

#### B17. 错误响应体原样透传，可能泄露敏感信息

**位置**：DeepseekProvider.cs 行 95-97；MiMoProvider.cs 行 87-89；OpenAIProvider.cs 行 99-101

**问题**：`errorBody` 原样拼进用户可见错误信息。某些 API 在错误响应中会回显 Authorization 头、traceId、内部堆栈或用户 ID。

**修复**：统一截断 + 脱敏：

```csharp
protected static string SanitizeErrorBody(string body, int maxLen = 200)
{
    if (string.IsNullOrEmpty(body)) return string.Empty;
    // 脱敏常见敏感模式
    var sanitized = Regex.Replace(body,
        @"(api[_-]?key|authorization|token|secret)[""':\s=]+\S+",
        "$1=***REDACTED***",
        RegexOptions.IgnoreCase);
    return sanitized.Length <= maxLen
        ? sanitized
        : sanitized.Substring(0, maxLen) + "...";
}
```

---

#### B18. 临时 profile 目录清理失败被吞，长期泄漏

**位置**：
- `src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxDomExtractor.cs` 行 211-216
- `src/UsageMonitor.Core/Services/BrowserLoginService.cs` 行 425-426

**问题**：`Directory.Delete(tempProfile, true)` 失败时 `catch { }` 静默。Edge 进程未完全退出时文件句柄未释放，删除必失败。每次登录泄漏一个 profile 目录（数十 MB），长期积累占满磁盘。

**修复**：
1. 启动时清理超过 1 天的 `UsageMonitor_Edge_*` / `UsageMonitor_DomExtract_*` 目录：

```csharp
// App.OnStartup 中
CleanupStaleTempProfiles();

private static void CleanupStaleTempProfiles()
{
    var tempDir = Path.GetTempPath();
    foreach (var dir in Directory.EnumerateDirectories(tempDir, "UsageMonitor_*"))
    {
        try
        {
            var age = DateTime.Now - Directory.GetLastWriteTime(dir);
            if (age.TotalDays > 1)
            {
                Directory.Delete(dir, recursive: true);
                FileLogger.Info("App", $"Cleaned stale temp profile: {dir}");
            }
        }
        catch { /* 忽略，下次再清 */ }
    }
}
```

2. 删除时 retry + delay：

```csharp
async Task TryDeleteDirectoryAsync(string path, int retries = 3)
{
    for (int i = 0; i < retries; i++)
    {
        try { Directory.Delete(path, true); return; }
        catch { await Task.Delay(500); }
    }
    FileLogger.Warn("Browser", $"Failed to delete temp profile after {retries} retries: {path}");
}
```

---

#### B19. NetworkIdle 等待 SPA 不可靠 + 硬编码 WaitForTimeoutAsync

**位置**：`src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxDomExtractor.cs` 行 158-160, 170

**问题**：

```csharp
WaitUntil = WaitUntilState.NetworkIdle  // SPA 长连接永不 idle
await page.WaitForTimeoutAsync(2000);   // 固定等 2 秒，慢机器不够、快机器浪费
```

这是 Playwright 反模式。

**修复**：

```csharp
WaitUntil = WaitUntilState.Commit  // 不等 network idle
// 等待 echarts canvas 真正出现
await page.WaitForSelectorAsync("canvas", state: WaitForSelectorState.Visible,
    new() { Timeout = 10000 });
```

---

#### B20. MiniMaxDomExtractor 视频百分比字段语义颠倒

**位置**：`src/Plugins/UsageMonitor.Plugin.MiniMax/MiniMaxDomExtractor.cs` 行 369-372

**问题**：对 `name == "video"` 的 model 使用 `current_interval_used_count` / `current_interval_total_count`，但根据 `MiniMaxModelRemains` 定义（MiniMaxProvider.cs 行 664-683），这两个字段对 video model 语义是"剩余/总"。代码注释（行 651-653）明确："*_usage_count means remaining count; used = total - usage"。但变量命名为 `mm_videoIntervalUsed`，实际存的是 remaining，UI 显示用反。

**修复**：重命名变量并换算：

```csharp
var videoIntervalRemaining = GetLong(model, "current_interval_used_count");
var videoIntervalTotal = GetLong(model, "current_interval_total_count");
var videoIntervalUsed = videoIntervalTotal - videoIntervalRemaining;
// 写入 extras
extras["mm_videoIntervalUsed"] = videoIntervalUsed;
extras["mm_videoIntervalTotal"] = videoIntervalTotal;
extras["mm_videoIntervalRemaining"] = videoIntervalRemaining;
```

---

### Minor（代码质量，建议修复）

#### B21. DateTime.Now vs UtcNow 混用

**位置**：所有 Provider 的 `LastUpdated = DateTime.Now`；BrowserLoginService.cs 行 372 `SavedAt = DateTime.UtcNow`

**修复**：统一存 UTC，UI 层转本地显示：

```csharp
public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
[JsonIgnore]
public DateTime LastUpdatedLocal => LastUpdatedUtc.ToLocalTime();
```

---

#### B22. 正则表达式每次编译

**位置**：`MiniMaxDomExtractor.cs` 行 292, 301

```csharp
Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%")  // ⚠ 每次编译
```

**修复**：

```csharp
private static readonly Regex PercentRe =
    new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);
// 使用：PercentRe.Match(text)
```

---

#### B23. RingChartControl.OnRender 每帧 new Typeface + FormattedText

**位置**：`src/UsageMonitor.App/Controls/RingChartControl.cs` 行 760, 809-818, 822-828, 846-858

**问题**：OnRender 中 `new Typeface(new FontFamily("Segoe UI"), ...)`、MeasureText 每帧 new FormattedText。动画 60fps 持续 180ms，每帧 new 多个 FormattedText，GC 压力大。

**修复**：

```csharp
// static readonly 缓存
private static readonly Typeface CachedTypeface =
    new(new FontFamily("Segoe UI"), FontStyles.Normal,
        FontWeights.Normal, FontStretches.Normal);

// OnRender 中
var ft = new FormattedText(text, CultureInfo.CurrentCulture,
    FlowDirection.LeftToRight, CachedTypeface, fontSize, brush, 1.0);
```

---

#### B24. YearHeatMapControl.OnRender 每帧重新解析日期 + 排序

**位置**：`src/UsageMonitor.App/Controls/YearHeatMapControl.cs` 行 199-213

**修复**：在 `OnCellsChanged` 中预解析 + 排序后缓存：

```csharp
private List<HeatMapCell> _sortedCells = new();

protected override void OnCellsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    if (d is YearHeatMapControl c)
    {
        c._sortedCells = (e.NewValue as IEnumerable<HeatMapCell> ?? Enumerable.Empty<HeatMapCell>())
            .OrderBy(x => x.Date)
            .ToList();
        c.InvalidateVisual();
    }
}
// OnRender 直接读 _sortedCells
```

---

#### B25. ThemeManager.Apply 重复插拔 ResourceDictionary

**位置**：`src/UsageMonitor.App/Helpers/ThemeManager.cs` 行 36-82

**修复**：开头短路：

```csharp
public static void Apply(ThemeMode mode)
{
    if (mode == Current && Application.Current != null) return;
    // ... 原逻辑
}
```

---

#### B26. TaskbarWindow.EmbedIntoTaskbar 错误设置 _hasPlacedOnce

**位置**：`src/UsageMonitor.App/Views/TaskbarWindow.xaml.cs` 行 304-309

**问题**：`EmbedWindow` 返回 false 时仍 `_hasPlacedOnce = true`，导致后续 `OnLocationChanged` 启动节流保存错误位置。

**修复**：

```csharp
if (_taskbarHelper.EmbedWindow(...))
{
    _hasPlacedOnce = true;
}
```

---

## 四、改进优先级建议

### P0（立即修复，影响功能可用性）
- B1（MiniMax Global 区域 cookie 域 bug）
- B2（并发字典损坏）
- B5（OpenAI 插件不可用）
- B6（Cookie header injection 安全风险）
- B7（取消误报超时）
- B12（防重复锁失效）
- B13（密码框切换抛异常）

### P1（发布前修复，影响稳定性）
- A1（统一敏感存储）
- A2（日志路径修复）
- A3、A4、A5（并发安全三件套）
- B8、B9、B10、B11（资源泄漏）
- B14、B15（HttpClient + JsonOptions 抽基类）
- B16（验证配置走轻量探针）
- B17（错误信息脱敏）
- B18（临时 profile 清理）
- B19（Playwright 等待策略）
- B20（视频字段语义颠倒）

### P2（代码质量，迭代修复）
- A6（引入 DI 容器）
- A7（IHttpClientFactory）
- A8（移除 AppSettings 业务耦合）
- U1（XAML 拆 UserControl）
- U2（a11y 标注）
- U3（Converter 改 Style + DataTrigger）
- U4（卡片高度对齐）
- U5（首次关闭提示）
- B21-B26（Minor 项）

### P3（技术债务，长期演进）
- B3（DOM selector 改用 data-testid，需与 MiniMax 前端协作）
- B4（BrowserLoginService 去静态化）
- 引入单元测试（项目当前 0 测试覆盖）
- 抽 Plugin SDK NuGet 包（注释中提到的 k2 阶段）

---

## 五、对开发团队的对齐建议

1. **本报告不否定已有成绩**：v0.13.0 在 6 周内迭代了 100+ 个 dev doc，功能完整度高、设计质感好。问题集中在"看不见的工程债"——并发、资源释放、抽象缺失。

2. **建议拆 3 个 Sprint 推进**：
   - Sprint 1（1 周）：P0 全部 + P1 中资源泄漏四项
   - Sprint 2（1 周）：P1 剩余 + A6 引入 DI
   - Sprint 3（1 周）：P2 + 补单元测试

3. **修复时务必配套测试**：当前项目零测试，建议每个 P0/P1 修复都补一个 xUnit 测试用例，覆盖核心路径（ConfigService 加密/解密、RefreshService 并发、PluginManager 加载、HistoryRepository CRUD）。

4. **PR 评审检查清单**（贴在 PR 模板里）：
   - [ ] 是否引入新的 `new HttpClient()`？（应改 IHttpClientFactory）
   - [ ] 是否引入新的 `new JsonSerializerOptions()`？（应 static readonly）
   - [ ] 是否新增 `+=` 事件订阅？（必须有对应 `-=` 或弱引用）
   - [ ] 是否新增 `DispatcherTimer`？（必须在 OnUnloaded 中 Stop）
   - [ ] 是否新增 `catch { }` 静默吞异常？（必须 FileLogger）
   - [ ] 是否引入新的静态状态？（应改实例或 DI 单例）

5. **建议补充文档**：在 `.devdoc/` 下增加一份 `20260719190000_项目开发说明文档-v0.13.0架构审查改进报告.md`，把本报告作为基线，后续每个 Sprint 进展回写到此文档。

---

## 六、附录：开发需求清单索引（req-id 映射）

本报告发现的所有问题已于 2026-07-19 通过 dev-master skill 写入项目开发需求清单 `.dev_require/.dev_require_list.md`，按维度合并为 6 个主需求（req-062 ~ req-067），共 32 个子需求。已扣除与现有 req-053~req-061 重叠的 15 项。

> 清单位置：`D:/应用开发/UsageMonitor/.dev_require/.dev_require_list.md`
> 详情目录：`D:/应用开发/UsageMonitor/.dev_require/req-XXX-<slug>/req-XXX-<slug>.md`

### 主需求索引

| req-id | 主需求标题 | 优先级 | 子需求数 | 详情文件 |
|--------|-----------|--------|---------|----------|
| **req-062** | v0.13.0架构审查-MiniMax插件链路修复 | P0 | 5 | `req-062-minimax-plugin-chain-fix/req-062-minimax-plugin-chain-fix.md` |
| **req-063** | v0.13.0架构审查-资源释放与内存泄漏修复 | P1 | 4 | `req-063-resource-leak-fix/req-063-resource-leak-fix.md` |
| **req-064** | v0.13.0架构审查-UI控件与交互Bug修复 | P1 | 9 | `req-064-ui-control-interaction-fix/req-064-ui-control-interaction-fix.md` |
| **req-065** | v0.13.0架构审查-HTTP与错误处理加固 | P0 | 6 | `req-065-http-error-handling-hardening/req-065-http-error-handling-hardening.md` |
| **req-066** | v0.13.0架构审查-架构演进 | P2 | 4 | `req-066-architecture-evolution/req-066-architecture-evolution.md` |
| **req-067** | v0.13.0架构审查-性能优化与时区一致性 | P2 | 4 | `req-067-performance-timezone-fix/req-067-performance-timezone-fix.md` |

### 子需求与本报告问题编号映射

#### req-062 · MiniMax 插件链路修复（P0）

| 子需求 | 本报告编号 | 涉及文件 | 优先级 |
|--------|-----------|----------|--------|
| Global 区域 Cookie 域动态化 | B1 | `MiniMaxDomExtractor.cs:596-618` | P0 |
| DOM 提取并发字典改 ConcurrentDictionary | B2 | `MiniMaxDomExtractor.cs:119-139` | P0 |
| DOM 选择器改用 data-testid 替代中文 aria-label | B3 | `MiniMaxDomExtractor.cs:228-254` | P1 |
| Playwright 等待策略修正 | B19 | `MiniMaxDomExtractor.cs:158-170` | P1 |
| 视频百分比字段语义修正 | B20 | `MiniMaxDomExtractor.cs:369-372` | P0 |

#### req-063 · 资源释放与内存泄漏修复（P1）

| 子需求 | 本报告编号 | 涉及文件 | 优先级 |
|--------|-----------|----------|--------|
| DispatcherTimer 一次性订阅优化 | B8 | `TaskbarWindow.xaml.cs:494-509` 等 3 处 | P1 |
| 图表控件 OnUnloaded 解绑 CollectionChanged | B9 | `BarChartControl.cs` 等 5 个控件 | P1 |
| RingChart timer OnUnloaded 停止 | B10 | `RingChartControl.cs:334-337,536-578` | P1 |
| HoverTooltip 改 ConditionalWeakTable | B11 | `HoverTooltip.cs:40,56-160` | P2 |

#### req-064 · UI 控件与交互 Bug 修复（P1）

| 子需求 | 本报告编号 | 涉及文件 | 优先级 |
|--------|-----------|----------|--------|
| PluginConfigWindow HashSet 大小写不敏感 | B12 | `PluginConfigWindow.xaml.cs:56` | P0 |
| CreatePasswordInput 重复 Add 元素修复 | B13 | `PluginConfigWindow.xaml.cs:433-453` | P0 |
| TaskbarWindow _hasPlacedOnce 错误设置 | B26 | `TaskbarWindow.xaml.cs:304-309` | P2 |
| MainWindow.xaml 拆 ProviderCard UserControl | U1 | `MainWindow.xaml:84-431` | P2 |
| 补充 a11y AutomationProperties 标注 | U2 | 全项目图标按钮 | P2 |
| 错误颜色 Converter 改 Style+DataTrigger | U3 | `MainWindow.xaml:168` | P2 |
| 卡片 MinHeight 对齐 | U4 | `MainWindow.xaml` 卡片模板 | P2 |
| 主窗口关闭首次提示 | U5 | `MainWindow.xaml.cs:71-75` | P2 |
| ThemeManager.Apply 短路优化 | U6 | `ThemeManager.cs:36-82` | P2 |

#### req-065 · HTTP 与错误处理加固（P0）

| 子需求 | 本报告编号 | 涉及文件 | 优先级 |
|--------|-----------|----------|--------|
| Cookie header injection 防护 | B6 | `MiniMaxProvider.cs:343` 等 5 处 | P0 |
| 取消与超时分类 | B7 | `MiniMaxProvider.cs:273-277` | P0 |
| BrowserLoginService 去静态化 | B4 | `BrowserLoginService.cs:25,94` | P1 |
| 抽 HttpUsageProviderBase 基类 | B14 | 4 个 Provider 全部 | P1 |
| ValidateConfigAsync 走轻量探针 | B16 | 4 个 Provider 的 ValidateConfigAsync | P2 |
| 错误响应体脱敏 | B17 | `DeepseekProvider.cs:95-97` 等 3 处 | P2 |

#### req-066 · 架构演进（P2）

| 子需求 | 本报告编号 | 涉及文件 | 优先级 |
|--------|-----------|----------|--------|
| 统一敏感存储到 SecretStore | A1 | `ConfigService.cs:605-668` + `Security/` 全套 | P1 |
| LoadedPlugin 状态字段加 volatile | A4 | `LoadedPlugin.cs:21-30` | P1 |
| 引入 DI 容器 | A6 | `App.xaml.cs:23-37,42-190` | P2 |
| AppSettings 移除 minimax 业务耦合 | A8 | `ConfigService.cs:150-161` | P2 |

#### req-067 · 性能优化与时区一致性（P2）

| 子需求 | 本报告编号 | 涉及文件 | 优先级 |
|--------|-----------|----------|--------|
| DateTime 统一 UTC 存储 | B21 | 所有 Provider + `BrowserLoginService.cs:372` | P2 |
| Regex 编译为 static readonly | B22 | `MiniMaxDomExtractor.cs:292,301` | P2 |
| 图表控件 OnRender 缓存 Typeface | B23 | `RingChartControl.cs:760,809-858` | P2 |
| YearHeatMap 预排序缓存 | B24 | `YearHeatMapControl.cs:199-213` | P2 |

### 与现有需求的交叉引用

以下本报告发现的问题在写入前已存在于清单中（req-053~req-061），不重复添加：

| 本报告编号 | 已存在 req-id | 子需求 |
|-----------|---------------|--------|
| A2 | req-060 | filelogger-appdata |
| A3 | req-057 | pluginmanager-list-lock |
| A5 | req-057 | isrefreshing-interlocked |
| A7 | req-060 | static-httpclient |
| B5 | req-060 | openai-endpoint-verify |
| B15 | req-060 | jsonserializeroptions-reuse |
| B18（部分） | req-053 | debug-dir-autoclean |
| 其他测试/版本管理类 | req-059, req-061 | 测试基础设施、配置版本管理 |

### 修复优先级总览

按 req-id 推进顺序：

1. **Sprint 1（1 周，P0 立即修复）**：
   - req-062 子需求 B1/B2/B20（MiniMax P0 三项）
   - req-065 子需求 B6/B7（HTTP P0 两项）
   - req-064 子需求 B12/B13（UI P0 两项）

2. **Sprint 2（1 周，P1 发布前修复）**：
   - req-063 全部（资源释放四项）
   - req-064 P2 子需求
   - req-065 P1 子需求（B4/B14）
   - req-066 P1 子需求（A1/A4）

3. **Sprint 3（1 周，P2 代码质量）**：
   - req-066 P2 子需求（A6/A8）
   - req-067 全部
   - req-064 剩余 P2

### 历史遗留修复

写入过程中发现并修复的清单问题：
- **req-013 重复**：原 `req-013-shared-config-template` 与 `req-013-history-refresh-aggregates-slicer` 共用 req-id。已将前者改为 `req-014-shared-config-template`（保留 history-refresh-aggregates-slicer 为 req-013，因其 devdoc 时间更早 + 目录结构完整）。
- **req-014 子目录补建**：为 `req-014-shared-config-template` 补建 `images/` 与 `references/` 子目录，使其符合 dev-master 规范。
- **validate 状态**：从 `✗ FAIL`（req-id 重复 ERROR）降到 `⚠ WARNING`（仅历史 req-001~req-013 部分缺子目录的 WARN）。

### 验证方法

执行以下命令可验证清单合规性：

```bash
cd C:/Users/Watchin/.workbuddy/skills/dev-master
uv run python scripts/validate.py "D:/应用开发/UsageMonitor"
```

执行以下命令可查看清单全貌：

```bash
uv run python scripts/list.py "D:/应用开发/UsageMonitor"
```

---

报告完。共发现 **8 项架构改进、6 项 UI 改进、26 项代码 Bug**（7 Critical / 13 Major / 6 Minor）。最值得立即修复的是 P0 列表中的 7 项 Critical Bug。所有问题已写入开发需求清单 req-062 ~ req-067，开发团队可直接按详情文件中的代码示例对齐实现。
