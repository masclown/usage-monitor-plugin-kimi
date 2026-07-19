# 架构 + UI 审查报告

## 一、执行摘要

UsageMonitor 项目整体架构分层合理（Core/App/Plugins 三层），插件接口设计有前瞻性，主题切换和配置持久化有较高完成度。但存在 **4 个 P0 级架构问题**：DI 容器声明了却从未使用、MainViewModel 是 2571 行的 God ViewModel 且充斥 MiniMax 专属逻辑、Core 层的 UsageHistoryRepository 硬编码了 "MiniMax" 字符串导致核心库被具体插件污染、整套 Security 模块（ISecretStore/SecretStoreFactory/SecretConfigBridge）是从未被调用的死代码。此外有 10 个 P1 问题涵盖接口膨胀、静态服务状态、跨进程配置竞争、国际化缺失等。建议优先拆分 God ViewModel 并消除 Core 层的 MiniMax 耦合。

## 二、问题总览表

| 序号 | 严重等级 | 模块 | 问题 | 文件 |
|------|---------|------|------|------|
| A-01 | P0 | 依赖注入 | DI 容器声明但从未使用，App.xaml.cs 手工 new 全部服务 | App.xaml.cs:71-157 |
| A-02 | P0 | MVVM | MainViewModel.cs 2571 行 God ViewModel，含 3+ 个 VM 类 | MainViewModel.cs:1-2571 |
| A-03 | P0 | 分层 | Core 层硬编码 "MiniMax" 字符串，核心库被具体插件污染 | UsageHistoryRepository.cs:447,602 |
| A-04 | P0 | 死代码 | 整套 Security 模块从未被调用 | Services/Security/*.cs |
| A-05 | P1 | 插件系统 | IUsageProvider 接口膨胀，15+ 成员含 MiniMax 专属默认值 | IUsageProvider.cs:48-177 |
| A-06 | P1 | 插件系统 | PluginManager 不用 AssemblyLoadContext，盲目扫描所有 DLL | PluginManager.cs:51,73 |
| A-07 | P1 | 服务层 | 核心服务无接口抽象，全部依赖具体类 | ConfigService.cs, RefreshService.cs |
| A-08 | P1 | 服务层 | BrowserLoginService 是静态类 + 静态可变状态 | BrowserLoginService.cs:24-36 |
| A-09 | P1 | 跨进程 | LoginHelper 创建独立 ConfigService 实例，与主程序存在配置竞争 | LoginHelper/Program.cs:59-65 |
| A-10 | P1 | 国际化 | XAML 中大量硬编码中文，I18n 仅覆盖插件配置字段 | Views/*.xaml |
| A-11 | P1 | 控件复用 | ResolveIconPath 硬编码 12 个 Provider 到文件名的 switch | MainViewModel.cs:197-231 |
| A-12 | P1 | MVVM | PluginConfigWindow 722 行 code-behind，违反 MVVM | PluginConfigWindow.xaml.cs |
| A-13 | P1 | 数据流 | UsageHistoryStore.AddPoint 构造 dummy UsageInfo 写库 | UsageHistoryStore.cs:105-117 |
| A-14 | P1 | 安全 | 双重加密路径并存且互不相通（DPAPI vs 未用的 ISecretStore） | ConfigService.cs:655, Security/*.cs |
| A-15 | P2 | 主题 | TriggerAreaOverlayWindow.xaml 硬编码颜色未走 Token | TriggerAreaOverlayWindow.xaml:28-101 |
| A-16 | P2 | 异步 | 4 处 async void 事件处理器，异常不可捕获 | TaskbarWindow.xaml.cs:122,689 |
| A-17 | P2 | 国际化 | I18nKeys.cs 用 const 硬编码中文字符串 | I18nKeys.cs:17-27 |
| A-18 | P2 | 插件系统 | LoadedPlugin 是可变状态袋，public setter 暴露运行时状态 | LoadedPlugin.cs:21-30 |

## 三、详细改进项

### A-01 [P0] DI 容器声明但从未使用，App.xaml.cs 手工 new 全部服务

- **文件**：`src/UsageMonitor.App/UsageMonitor.App.csproj:34`、`src/UsageMonitor.App/App.xaml.cs:71-157`
- **代码片段**：
```csharp
// csproj 声明了 DI 包但从未注册
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />

// App.xaml.cs 全部手工构造
_configService = new ConfigService();          // line 71
_pluginManager = new PluginManager();           // line 86
_historyRepository = UsageHistoryRepository.CreateDefault();  // line 102
_historyStore = new UsageHistoryStore(_historyRepository);     // line 141
_refreshService = new RefreshService(_pluginManager, _configService, _historyStore, _historyRepository);  // line 157
_viewModel = new MainViewModel(_pluginManager, _configService, _refreshService, _historyStore);           // line 160
```
- **问题**：`Microsoft.Extensions.DependencyInjection` NuGet 包被引入但零调用（全项目无 `AddSingleton`/`AddTransient`/`GetRequiredService`）。所有服务在 App.xaml.cs 的 `OnStartup` 中手工 `new` 构造，形成 717 行的 God Class。这导致：(1) 服务生命周期无法统一管理；(2) 无法轻松替换实现（如测试 mock）；(3) 服务间依赖关系隐式耦合在构造顺序中，任何重排都有 NullReferenceException 风险；(4) NuGet 包白白增加发布体积。
- **改进建议**：
```csharp
// App.xaml.cs OnStartup 中注册服务
var services = new ServiceCollection();
services.AddSingleton<ConfigService>();
services.AddSingleton<PluginManager>();
services.AddSingleton<UsageHistoryRepository>(_ => UsageHistoryRepository.CreateDefault());
services.AddSingleton<UsageHistoryStore>();
services.AddSingleton<RefreshService>();
services.AddSingleton<MainViewModel>();
var provider = services.BuildServiceProvider();

_configService = provider.GetRequiredService<ConfigService>();
_pluginManager = provider.GetRequiredService<PluginManager>();
// ... 其余服务同理解析
```
- **影响范围**：`App.xaml.cs`（主要改动）、各服务类的构造函数（可选加 `[ActivatorUtilitiesConstructor]`）、`MainViewModel` 构造函数
- **优先级理由**：P0 — 当前手工构造链已导致 App.xaml.cs 成为 717 行的 God Class，且 NuGet 包已声明却未用，说明架构意图与实现脱节，后续任何服务新增/重排都会加剧脆弱性

---

### A-02 [P0] MainViewModel.cs 2571 行 God ViewModel，含 3+ 个 VM 类与 MiniMax 专属逻辑

- **文件**：`src/UsageMonitor.App/ViewModels/MainViewModel.cs:1-2571`
- **代码片段**：
```csharp
// 文件内含多个不相关的 ViewModel 类：
public class ProviderUsageViewModel : INotifyPropertyChanged  // line 21, 约 1321 行
{
    // MiniMax 专属字段直接定义在通用 VM 中
    private string _subscriptionTitle = "Token Plan 订阅";  // line 44
    private double _primaryBarPercent;    // line 47 — MiniMax 5h 限额
    private double _weeklyBarPercent;     // line 49 — MiniMax 周限额
    private string _videoQuotaText = "--"; // line 54 — MiniMax 视频赠送
    // ... 30+ 个 backing field

    // MiniMax 专属方法
    private void UpdateFromMiniMaxDom(UsageInfo usage)  // line 778
    {
        var extra = usage.Extra!;
        double D(string k) => extra.TryGetValue(k, out var v) && v != null
            ? Convert.ToDouble(v) : -1;  // 直接读 mm_* 键
        // ...
    }

    // 直接判断 ProviderId 做分支
    if (string.Equals(usage.ProviderId, "MiniMax", StringComparison.OrdinalIgnoreCase))  // line 705
    {
        // MiniMax 专属错误提示逻辑
    }
}

public class PluginItemViewModel : INotifyPropertyChanged { ... }  // line 1326

public class MainViewModel : INotifyPropertyChanged  // line 1457, 约 1114 行
{
    // 主 VM 中也混入了设置编辑器 VM
    public TierListEditorViewModel TierEditor { get; }      // line 1498
    public HeatMapTierListEditorViewModel HeatMapTierEditor { get; }  // line 1511
    // ... 还有 RingChartMetricGroup 等内联类
}
```
- **问题**：(1) 单文件 2571 行，包含至少 3 个 ViewModel 类 + 多个编辑器 VM 类，严重违反单一职责；(2) `ProviderUsageViewModel` 本应是通用的"服务商用量展示"VM，却被 MiniMax 专属字段（5h 限额、周限额、视频赠送、订阅档位、积分余额等 15+ 个属性）和方法（`UpdateFromMiniMaxDom`）污染，导致新增任何 Provider 都要背负 MiniMax 的字段包袱；(3) 直接在 VM 中用 `if (ProviderId == "MiniMax")` 做分支，把插件耦合死在 VM 层。
- **改进建议**：
```csharp
// 1. 拆分文件：每个 VM 独立文件
// ViewModels/ProviderUsageViewModel.cs       — 通用属性
// ViewModels/PluginItemViewModel.cs          — 插件列表项
// ViewModels/MainViewModel.cs                — 主窗口编排
// ViewModels/TierListEditorViewModel.cs      — 色阶编辑器
// ViewModels/HeatMapTierListEditorViewModel.cs

// 2. MiniMax 专属数据通过策略模式注入，而非硬编码在 VM 中
public interface IProviderDataRenderer
{
    void Render(ProviderUsageViewModel vm, UsageInfo usage);
    IReadOnlyList<string> SupportedFields { get; }
}

// MiniMax 插件提供自己的 Renderer
public class MiniMaxDataRenderer : IProviderDataRenderer { ... }

// ProviderUsageViewModel 只持有通用字段
public class ProviderUsageViewModel
{
    public double UsagePercentage { get; set; }
    public string StatusText { get; set; }
    // 不再有 _primaryBarPercent / _weeklyBarPercent 等 MiniMax 专属字段
}
```
- **影响范围**：`MainViewModel.cs`（拆分为 5-6 个文件）、`MainWindow.xaml`（绑定路径可能调整）、各 View 的 DataContext
- **优先级理由**：P0 — 2571 行单文件 + 跨插件耦合，是当前最大的可维护性炸弹，任何新增 Provider 或修改 MiniMax 展示逻辑都要在这个巨型文件中搜索

---

### A-03 [P0] Core 层硬编码 "MiniMax" 字符串，核心库被具体插件污染

- **文件**：`src/UsageMonitor.Core/Services/UsageHistoryRepository.cs:447`、`:602`；`src/UsageMonitor.App/ViewModels/MainViewModel.cs:705,1063,2108`
- **代码片段**：
```csharp
// UsageHistoryRepository.cs — Core 层不应该知道任何具体 Provider
// line 447：清理逻辑只针对 MiniMax
cmd.CommandText = "DELETE FROM usage_points WHERE used_tokens = 0 AND provider_id = 'MiniMax';";

// line 602：业务指纹构建也硬编码 MiniMax
if (string.Equals(usage.ProviderId, "MiniMax", StringComparison.OrdinalIgnoreCase))
{
    fields.Add($"5h={TryGetDouble(usage.Extra, "mm_5hUsedPercent")}");
    fields.Add($"week={TryGetDouble(usage.Extra, "mm_weeklyUsedPercent")}");
    // ... MiniMax 专属指纹字段
}

// MainViewModel.cs line 705 — App 层也硬编码
if (string.Equals(usage.ProviderId, "MiniMax", StringComparison.OrdinalIgnoreCase))
{
    // MiniMax 专属错误提示
}

// MainViewModel.cs line 1063 — 热力图色阶也硬编码
? UsageMonitor.App.Helpers.HeatMapTierScale.ResolveBrush(token, "MiniMax")
```
- **问题**：Core 层（`UsageHistoryRepository`）是通用持久化仓库，理应与具体插件无关，但代码中直接硬编码了 `"MiniMax"` 字符串来做数据清理和指纹计算。这意味着：(1) 新增任何有 token=0 场景的 Provider 都无法享受清理逻辑；(2) 任何插件想自定义指纹字段都要修改 Core 库代码；(3) 违反了开闭原则和分层架构的基本约定。
- **改进建议**：
```csharp
// 方案：在 IUsageProvider 接口中声明数据管理能力，由插件自己提供指纹和清理规则

// IUsageProvider.cs 新增
public interface IUsageProvider
{
    // ... 现有成员 ...

    /// <summary>插件声明的历史数据清理规则（null 表示无需清理）</summary>
    DataCleanupRule? CleanupRule => null;

    /// <summary>插件声明的业务指纹构建器（null 表示用默认 percent-only 指纹）</summary>
    Func<UsageInfo, string>? FingerprintBuilder => null;
}

// UsageHistoryRepository.cs 改为接收插件规则
public async Task<int> CleanHistoricalZeroTokenDataAsync(IReadOnlyList<IUsageProvider> providers)
{
    int total = 0;
    foreach (var provider in providers)
    {
        var rule = provider.CleanupRule;
        if (rule == null) continue;
        // 用参数化查询，不硬编码 provider_id
        cmd.CommandText = "DELETE FROM usage_points WHERE used_tokens = 0 AND provider_id = $pid";
        cmd.Parameters.AddWithValue("$pid", provider.ProviderId);
        total += await cmd.ExecuteNonQueryAsync();
    }
    return total;
}
```
- **影响范围**：`UsageHistoryRepository.cs`、`IUsageProvider.cs`、`MiniMaxProvider.cs`（实现新接口成员）、`App.xaml.cs`（传 providers 给清理方法）
- **优先级理由**：P0 — Core 层被具体插件污染是架构腐化的核心标志，不修复则"插件式架构"的承诺形同虚设

---

### A-04 [P0] 整套 Security 模块从未被调用，是死代码

- **文件**：`src/UsageMonitor.Core/Services/Security/ISecretStore.cs`、`SecretStoreFactory.cs`、`SecretConfigBridge.cs`、`WindowsCredentialManagerStore.cs`、`AesGcmFileSecretStore.cs`、`MasterKeyMissingException.cs`
- **代码片段**：
```csharp
// SecretConfigBridge.cs — 设计了完整的凭据管理 API
public static class SecretConfigBridge
{
    public static void SaveProviderSecret(string providerId, string accountName, string secretData)
    {
        SecretStoreFactory.Current.Set(ResolveServiceName(providerId), accountName, secretData);
    }
    public static string? LoadProviderSecret(string providerId, string accountName)
    {
        return SecretStoreFactory.Current.Get(ResolveServiceName(providerId), accountName);
    }
    // ...
}

// 但全项目搜索 SecretConfigBridge. 的结果：零调用
// ConfigService.cs 仍然用自己的 DPAPI 加密路径
private static string Encrypt(string plainText)  // line 655
{
    var bytes = Encoding.UTF8.GetBytes(plainText);
    var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    return Convert.ToBase64String(encrypted);
}
```
- **问题**：Security 模块包含 6 个文件、完整的 `ISecretStore` 接口、Windows Credential Manager 实现、AES-GCM 降级方案、工厂模式、桥接类——但**全项目没有任何一处调用** `SecretConfigBridge`、`SecretStoreFactory.Current` 或 `ISecretStore`。实际加密全部走 `ConfigService` 的 DPAPI 路径。这套约 400 行的代码是纯粹的死代码，不仅增加维护负担，还误导开发者以为凭据管理走了 Credential Manager（实际没有）。
- **改进建议**：
```csharp
// 方案 A（推荐）：删除 Security 模块，ConfigService 的 DPAPI 路径已满足需求
// 移除 6 个文件 + Core.csproj 中的 System.Security.Cryptography.ProtectedData（若仅 ConfigService 用）

// 方案 B（如计划迁移到 Credential Manager）：把 ConfigService 的加密逻辑改为委托 ISecretStore
public class ConfigService
{
    private readonly ISecretStore _secretStore;

    public ConfigService(ISecretStore? secretStore = null)
    {
        _secretStore = secretStore ?? SecretStoreFactory.Current;
    }

    private void EncryptSensitiveFields(AppSettings settings)
    {
        foreach (var (providerId, config) in settings.ProviderConfigs)
        {
            foreach (var key in config.Values.Keys.ToList())
            {
                if (IsSensitiveKey(key) && !string.IsNullOrEmpty(config.Values[key]))
                {
                    // 用 SecretStore 替代 DPAPI
                    SecretConfigBridge.SaveProviderSecret(providerId, key, config.Values[key]);
                    config.Values[key] = "__STORED_IN_SECRET_STORE__"; // 占位符
                }
            }
        }
    }
}
```
- **影响范围**：`Services/Security/` 目录（删除或激活）、`ConfigService.cs`（如走方案 B 则重构加密路径）、`Core.csproj`（移除或保留 ProtectedData 依赖）
- **优先级理由**：P0 — 400+ 行死代码 + 安全架构与实际实现不一致，存在误导风险（审计者可能以为已用 Credential Manager）

---

### A-05 [P1] IUsageProvider 接口膨胀，15+ 成员含 MiniMax 专属默认值

- **文件**：`src/UsageMonitor.Core/Plugins/IUsageProvider.cs:10-178`
- **代码片段**：
```csharp
public interface IUsageProvider
{
    // 基础成员（合理）
    string ProviderId { get; }
    string DisplayName { get; }
    string? IconPath { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }
    IReadOnlyList<ConfigField> ConfigFields { get; }

    // 能力声明（开始膨胀）
    Models.BrowserLoginConfig? LoginConfig => null;                    // line 48
    IReadOnlyList<string> DefaultRenderKinds => Array.Empty<string>(); // line 60
    IReadOnlyList<CardChartKind> SupportedCardCharts => new[] { ... }; // line 76
    IReadOnlyList<IUsageChartFactory> ChartFactories => Array.Empty<>(); // line 97
    IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent" }; // line 124
    bool SupportsPeriodSwitch => false;                                 // line 135
    IReadOnlyList<string>? ExtraTooltipLines => null;                   // line 144
    Task SetPeriodAsync(string period, CancellationToken ct) => Task.CompletedTask; // line 158
    IReadOnlyList<BalanceItem> BalanceItems => Array.Empty<BalanceItem>(); // line 168
    IReadOnlyList<HeatMapTierConfig>? HeatMapTiers => null;             // line 177

    // 核心方法
    Task<UsageInfo> GetUsageAsync(ProviderConfig config);
    Task<bool> ValidateConfigAsync(ProviderConfig config);
}
```
- **问题**：接口有 15+ 个成员，其中 9 个是带默认实现的"能力声明"属性。这些默认值大多是为 MiniMax 的场景设计的（如 `DefaultRenderKinds` 的注释直接提到 MiniMax）。新增一个最简单的 Provider 也要面对这么多成员的理解成本，且接口的 default interface method 在 .NET 8 中无法被 Moq 等 mock 框架很好地覆盖。
- **改进建议**：
```csharp
// 拆分为核心接口 + 可选能力接口（ISP 原则）
public interface IUsageProvider  // 最小核心契约
{
    string ProviderId { get; }
    string DisplayName { get; }
    string? IconPath { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }
    IReadOnlyList<ConfigField> ConfigFields { get; }
    Task<UsageInfo> GetUsageAsync(ProviderConfig config);
    Task<bool> ValidateConfigAsync(ProviderConfig config);
}

// 可选能力接口（插件按需实现）
public interface IBrowserLoginProvider { BrowserLoginConfig? LoginConfig { get; } }
public interface IChartSupportProvider
{
    IReadOnlyList<CardChartKind> SupportedCardCharts { get; }
    IReadOnlyList<IUsageChartFactory> ChartFactories { get; }
}
public interface IPeriodSwitchProvider
{
    bool SupportsPeriodSwitch { get; }
    IReadOnlyList<string>? ExtraTooltipLines { get; }
    Task SetPeriodAsync(string period, CancellationToken ct);
}
public interface IBalanceItemProvider { IReadOnlyList<BalanceItem> BalanceItems { get; } }
public interface IRingChartMetricProvider { IReadOnlyList<string> SupportedRingChartMetrics { get; } }

// 宿主用 is 模式匹配检查能力
if (provider is IPeriodSwitchProvider ps) { ... }
```
- **影响范围**：`IUsageProvider.cs`（拆分）、所有 4 个 Provider 插件（按需实现新接口）、`MainViewModel.cs`（用 `is` 检查能力）、`PluginConfigWindow.xaml.cs`（同上）
- **优先级理由**：P1 — 不影响当前运行，但严重影响扩展性；每新增一个 Provider 都要面对 15+ 成员的庞大接口

---

### A-06 [P1] PluginManager 不用 AssemblyLoadContext，盲目扫描所有 DLL

- **文件**：`src/UsageMonitor.Core/Plugins/PluginManager.cs:51,73`
- **代码片段**：
```csharp
// line 51：递归扫描所有 DLL，包括 System.*.dll、Microsoft.*.dll 等
var dllFiles = Directory.GetFiles(_pluginDirectory, "*.dll", SearchOption.AllDirectories);

// line 73：用 Assembly.LoadFrom 加载，无法卸载
var assembly = Assembly.LoadFrom(dllPath);
```
- **问题**：(1) `Directory.GetFiles` 递归扫描 `plugins/` 下所有 `.dll`，包括 SQLite 依赖、System 库等非插件 DLL，每个都会被 `Assembly.LoadFrom` 加载到内存（即使最终没有 IUsageProvider 类型），浪费内存且可能加载恶意 DLL；(2) `Assembly.LoadFrom` 不支持卸载，`UnloadPlugin` 只是从列表移除但程序集仍留在内存中，无法实现真正的热插拔；(3) 无版本兼容性检查，插件用旧版 Core 编译后可能运行时 MissingMethodException。
- **改进建议**：
```csharp
// 1. 用 AssemblyLoadContext 实现可卸载加载
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 让 Core 库由默认上下文加载，避免重复
        if (assemblyName.Name == "UsageMonitor.Core") return null;
        return base.Load(assemblyName);
    }
}

// 2. 只加载声明了 IUsageProvider 的 DLL（先反射检查再加载）
private void LoadPluginFromAssembly(string dllPath)
{
    // 用 MetadataLoadContext 只读检查，不真正加载到内存
    var resolver = new PathAssemblyResolver(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));
    using var mlc = new MetadataLoadContext(resolver);
    var asm = mlc.LoadFromAssemblyPath(dllPath);
    var hasProvider = asm.GetTypes()
        .Any(t => typeof(IUsageProvider).IsAssignableFrom(t) && !t.IsInterface);
    if (!hasProvider) return; // 跳过非插件 DLL

    // 确认是插件后再真正加载
    var alc = new PluginLoadContext(dllPath);
    var realAsm = alc.LoadFromAssemblyPath(dllPath);
    // ...
}

// 3. UnloadPlugin 真正卸载
public bool UnloadPlugin(string providerId)
{
    var plugin = _plugins.FirstOrDefault(p => p.Provider.ProviderId == providerId);
    if (plugin == null) return false;
    _plugins.Remove(plugin);
    if (plugin.LoadContext != null)
    {
        plugin.LoadContext.Unload();
        plugin.LoadContext = null;
    }
    return true;
}
```
- **影响范围**：`PluginManager.cs`（重写加载逻辑）、`LoadedPlugin.cs`（增加 LoadContext 字段）
- **优先级理由**：P1 — 当前实现无法热插拔，且加载所有 DLL 有安全和性能隐患

---

### A-07 [P1] 核心服务无接口抽象，全部依赖具体类

- **文件**：`src/UsageMonitor.Core/Services/ConfigService.cs:255`、`RefreshService.cs:11`、`UsageHistoryStore.cs:29`
- **代码片段**：
```csharp
// ConfigService 是具体类，无接口
public class ConfigService  // line 255
{
    public AppSettings Settings => _settings;  // 直接暴露可变设置对象
    // ...
}

// RefreshService 直接依赖具体类
public class RefreshService : IDisposable  // line 11
{
    private readonly PluginManager _pluginManager;     // 具体类
    private readonly ConfigService _configService;      // 具体类
    private readonly UsageHistoryStore _historyStore;   // 具体类
    private readonly UsageHistoryRepository? _historyRepository;  // 具体类
}

// MainViewModel 也直接依赖具体类
public MainViewModel(PluginManager pluginManager, ConfigService configService,
    RefreshService refreshService, UsageHistoryStore? historyStore = null)  // line 1888
```
- **问题**：所有核心服务都是具体类，没有接口抽象。这导致：(1) 无法在测试中 mock 服务（如模拟配置加载失败、刷新异常等场景）；(2) 无法替换实现（如想把 SQLite 换成另一个数据库）；(3) `ConfigService.Settings` 直接返回内部可变对象，外部代码可以绕过锁直接修改 `ProviderConfigs` 等字典，破坏线程安全。
- **改进建议**：
```csharp
public interface IConfigService
{
    AppSettings Settings { get; }
    string? LastSaveError { get; }
    event EventHandler? ConfigChanged;
    void Load();
    void Save();
    ProviderConfig GetProviderConfig(string providerId, IUsageProvider? provider = null);
    void UpdateProviderConfig(string providerId, ProviderConfig config);
    // ... 只暴露必要方法
}

public interface IRefreshService : IDisposable
{
    event EventHandler<UsageRefreshedEventArgs>? UsageRefreshed;
    void Start();
    void Stop();
    Task RefreshAllAsync(string triggerKind = "manual");
    Task RefreshProviderAsync(string providerId);
}

// MainViewModel 依赖接口
public MainViewModel(
    PluginManager pluginManager,
    IConfigService configService,
    IRefreshService refreshService,
    UsageHistoryStore? historyStore = null)
```
- **影响范围**：`ConfigService.cs`、`RefreshService.cs`（提取接口）、`MainViewModel.cs`、`App.xaml.cs`（构造时不变，但注册 DI 时用接口）
- **优先级理由**：P1 — 不影响运行，但严重影响可测试性和可替换性

---

### A-08 [P1] BrowserLoginService 是静态类 + 静态可变状态

- **文件**：`src/UsageMonitor.Core/Services/BrowserLoginService.cs:22-36`
- **代码片段**：
```csharp
public static class BrowserLoginService  // line 22 — 静态类
{
    private static ConfigService? _configService;  // line 24 — 静态可变状态

    public static void RegisterConfigService(ConfigService service)  // line 33
    {
        _configService = service;
    }

    public static string? LastError { get; private set; }  // line 94 — 静态状态

    public static async Task<BrowserCookieData?> LoginAndExtractCookieAsync(
        BrowserLoginConfig config, CancellationToken cancellationToken = default)  // line 99
    {
        // ...
    }
}
```
- **问题**：(1) `BrowserLoginService` 是静态类，`_configService` 和 `LastError` 是静态字段，意味着全局唯一状态无法隔离；(2) 无法 mock（单元测试无法替换浏览器登录行为）；(3) `LastError` 是 `static` 属性，多个并发登录会互相覆盖错误信息；(4) `RegisterConfigService` 是隐式初始化模式——如果忘记调用，`PersistToMainConfig` 静默跳过（仅日志），Cookie 只存到 `cookies/*.json` 不进 config.json，问题难以排查。
- **改进建议**：
```csharp
// 改为实例类 + 接口，通过 DI 注入
public interface IBrowserLoginService
{
    string? LastError { get; }
    Task<BrowserCookieData?> LoginAndExtractCookieAsync(
        BrowserLoginConfig config, CancellationToken ct = default);
}

public class BrowserLoginService : IBrowserLoginService
{
    private readonly ConfigService _configService;  // 构造注入，不再 static

    public BrowserLoginService(ConfigService configService)
    {
        _configService = configService;
    }

    public string? LastError { get; private set; }

    public async Task<BrowserCookieData?> LoginAndExtractCookieAsync(...)
    {
        // ...
    }
}

// DI 注册
services.AddSingleton<IBrowserLoginService, BrowserLoginService>();

// 调用方
public partial class PluginConfigWindow : Window
{
    private readonly IBrowserLoginService _loginService;
    public PluginConfigWindow(..., IBrowserLoginService loginService)
    {
        _loginService = loginService;
    }
}
```
- **影响范围**：`BrowserLoginService.cs`（重构为实例类）、`App.xaml.cs`（移除 `RegisterConfigService` 调用）、`PluginConfigWindow.xaml.cs`（通过 DI 接收）、`LoginHelper/Program.cs`
- **优先级理由**：P1 — 静态可变状态是并发 bug 的温床，且阻碍测试

---

### A-09 [P1] LoginHelper 创建独立 ConfigService 实例，与主程序存在配置竞争

- **文件**：`src/LoginHelper/Program.cs:59-65`
- **代码片段**：
```csharp
// LoginHelper/Program.cs — 独立进程
var configService = new UsageMonitor.Core.Services.ConfigService();  // line 59 — 新实例
configService.Load();                                                 // line 60
var miniCfg = configService.GetProviderConfig("MiniMax", new MiniMaxProvider());
miniCfg.SetValue("Cookie", cookie);
miniCfg.SetValue("_userAgent", data?.UserAgent ?? "UsageMonitor");
configService.UpdateProviderConfig("MiniMax", miniCfg);               // line 65 — 直接写 config.json
```
- **问题**：LoginHelper 是独立进程，它创建了自己的 `ConfigService` 实例并直接写 `config.json`。如果主程序同时也在运行（用户从托盘菜单触发登录），两个进程的 `ConfigService` 都会读写同一个 `config.json`：(1) 主程序的内存配置会被 LoginHelper 的写入覆盖（因为主程序不知道文件已变，除非有文件监视）；(2) 两个进程同时 `File.Replace` 可能导致原子写入冲突；(3) `ConfigService` 的 `_ioLock` 是实例级锁，跨进程无效。实际上 `App.xaml.cs` 的 `BrowserLoginService.RegisterConfigService` 设计就是为了在主程序内调用避免此问题，但 LoginHelper 绕过了它。
- **改进建议**：
```csharp
// 方案 A（推荐）：LoginHelper 不直接写 config.json，而是通过 IPC（命名管道）把 Cookie 传给主程序
// LoginHelper/Program.cs
var data = await BrowserLoginService.LoginAndExtractCookieAsync(...);
if (data != null)
{
    // 通过命名管道发送给主程序（如主程序在运行）
    using var client = new NamedPipeClientStream(".", "UsageMonitor.ConfigPipe");
    await client.ConnectAsync(5000);
    var json = JsonSerializer.Serialize(data);
    var bytes = Encoding.UTF8.GetBytes(json);
    await client.WriteAsync(BitConverter.GetBytes(bytes.Length));
    await client.WriteAsync(bytes);
}

// 方案 B（简化）：LoginHelper 写入后发信号让主程序 ReloadProviderConfigsFromDisk
// 主程序启动一个 FileSystemWatcher 监视 config.json
// LoginHelper 写完后主程序自动 ReloadProviderConfigsFromDisk()
```
- **影响范围**：`LoginHelper/Program.cs`、`App.xaml.cs`（如方案 A 需加管道服务端）、`ConfigService.cs`（如方案 B 需加文件监视）
- **优先级理由**：P1 — 配置竞争可能导致用户 Cookie 丢失或配置文件损坏

---

### A-10 [P1] XAML 中大量硬编码中文，I18n 仅覆盖插件配置字段

- **文件**：`src/UsageMonitor.App/Views/SettingsWindow.xaml:18-147`、`HistoryWindow.xaml:24-215`、`PluginConfigWindow.xaml:34-57`
- **代码片段**：
```xml
<!-- SettingsWindow.xaml — 全部硬编码中文 -->
<TextBlock Text="常规设置" FontSize="20" FontWeight="Bold" />        <!-- line 18 -->
<TextBlock Text="外观主题" FontSize="13" />                           <!-- line 22 -->
<RadioButton Content="深色" GroupName="Theme" />                      <!-- line 27 -->
<RadioButton Content="浅色" GroupName="Theme" />                      <!-- line 30 -->
<TextBlock Text="刷新间隔（秒）" FontSize="13" />                      <!-- line 38 -->
<CheckBox Content="开机自动启动" IsChecked="{Binding AutoStart}" />   <!-- line 43 -->
<TextBlock Text="历史数据保留点数（30 / 60 / 120）" FontSize="13" />   <!-- line 50 -->
<TextBlock Text="警告阈值（% 默认 60）" FontSize="13" />               <!-- line 66 -->
<Button Content="保存设置" />                                         <!-- line 132 -->
<TextBlock Text="已安装插件" FontSize="20" FontWeight="Bold" />       <!-- line 147 -->
```
- **问题**：`I18n.cs` 的 `T()` 方法和 `Register()` 机制已实现，但只用于插件配置字段名（`plugin.MiniMax.field.ApiKey.name` 等）。所有 App UI 文案（设置窗口、历史窗口、插件配置窗口、托盘菜单等）都是硬编码中文。`I18n.cs` 自己的注释也承认"当前 UI 文案仍以中文硬编码"。这意味着任何多语言支持都需要大面积修改 XAML。同时 `I18nKeys.cs` 的常量也是硬编码中文（见 A-17）。
- **改进建议**：
```xml
<!-- 用 DynamicResource 或 x:Static 绑定 I18n 键 -->
<TextBlock Text="{DynamicResource Settings_General_Title}" FontSize="20" FontWeight="Bold" />
<RadioButton Content="{DynamicResource Settings_Theme_Dark}" GroupName="Theme" />
<CheckBox Content="{DynamicResource Settings_AutoStart}" IsChecked="{Binding AutoStart}" />

<!-- 或用 markup extension -->
<TextBlock Text="{i18n:T Key=Settings_General_Title}" />
```
- **影响范围**：所有 `Views/*.xaml`（SettingsWindow、HistoryWindow、PluginConfigWindow、TaskbarWindow、MainWindow）、`I18n.cs`（扩展词条）、`I18nKeys.cs`
- **优先级理由**：P1 — 不影响当前功能，但国际化基础设施数据缺失，未来支持英文需要大面积返工

---

### A-11 [P1] ResolveIconPath 硬编码 12 个 Provider 到文件名的 switch

- **文件**：`src/UsageMonitor.App/ViewModels/MainViewModel.cs:194-236`
- **代码片段**：
```csharp
public static string? ResolveIconPath(string providerId)
{
    var name = providerId.ToLowerInvariant() switch
    {
        "minimax" => "minimax",
        "deepseek" => "deepseek",
        "mimo" => "mimo",
        "kimi" => "kimi",
        "volcengine" => "volcengine",
        "zhipu" => "zhipu",
        "ollama" => "ollama",
        "openrouter" => "openrouter",
        "openai" => "openai",
        "anthropic" => "anthropic",
        "step" => null,       // SVG 格式不支持
        "siliconflow" => "siliconflow",
        _ => null
    };
    // 然后还有第二个 switch 硬编码扩展名
    var ext = name switch
    {
        "minimax" => ".ico",
        "deepseek" => ".png",
        "mimo" => ".jpg",
        // ... 12 个 case
    };
}
```
- **问题**：新增一个 Provider 需要修改这个 switch 语句（两处：文件名 + 扩展名），违反开闭原则。而且这个方法放在 `ProviderUsageViewModel` 中，属于 App 层硬编码了所有已知 Provider 的图标信息。`IUsageProvider.IconPath` 属性本就存在但未被使用（MiniMax 返回 null，其它插件也没用）。
- **改进建议**：
```csharp
// 方案：优先用 IUsageProvider.IconPath，回退到约定优于配置
public static string? ResolveIconPath(string providerId, IUsageProvider? provider = null)
{
    // 1. 优先用插件声明的 IconPath
    if (!string.IsNullOrEmpty(provider?.IconPath))
        return provider.IconPath;

    // 2. 约定优于配置：Assets/Providers/{providerId}.{png|ico|jpg}
    var basePath = AppDomain.CurrentDomain.BaseDirectory;
    var dir = Path.Combine(basePath, "Assets", "Providers");
    foreach (var ext in new[] { ".png", ".ico", ".jpg", ".svg" })
    {
        var filePath = Path.Combine(dir, providerId.ToLowerInvariant() + ext);
        if (File.Exists(filePath)) return filePath;
    }
    return null;
}
```
- **影响范围**：`MainViewModel.cs`（简化方法）、各 Provider 插件（可选设置 IconPath）
- **优先级理由**：P1 — 每新增 Provider 都要改 App 层代码，与"插件式架构"目标矛盾

---

### A-12 [P1] PluginConfigWindow 722 行 code-behind，违反 MVVM

- **文件**：`src/UsageMonitor.App/Views/PluginConfigWindow.xaml.cs:1-722`
- **代码片段**：
```csharp
public partial class PluginConfigWindow : Window  // 722 行
{
    private readonly IReadOnlyList<ConfigField> _configFields;
    private readonly ProviderConfig _config;
    private readonly BrowserLoginConfig? _loginConfig;
    private readonly Dictionary<string, FrameworkElement> _inputControls = new();
    private readonly HashSet<CardChartKind> _selectedCardCharts = new();
    private static readonly HashSet<string> _isLoginInProgress = new();  // 静态状态

    // code-behind 中处理登录、保存、表单生成、图表预览等全部逻辑
    private async void OnGetCookieClick(object sender, RoutedEventArgs e)  // line 513
    {
        // 直接在 code-behind 中调 BrowserLoginService、操作 UI 控件
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // 直接读 UI 控件值、写 ConfigService、关闭窗口
    }
}
```
- **问题**：722 行 code-behind 包含表单动态生成、Cookie 登录流程、图表选择、保存逻辑等全部业务。没有对应的 ViewModel，`DataContext` 未设置，所有操作通过事件处理器直接操作 UI 控件和 ConfigService。`_isLoginInProgress` 是静态 HashSet，跨窗口共享状态。这违反 MVVM 模式，无法通过绑定测试、无法复用逻辑。
- **改进建议**：
```csharp
// 提取 PluginConfigViewModel
public class PluginConfigViewModel : INotifyPropertyChanged
{
    private readonly ConfigService _configService;
    private readonly IBrowserLoginService _loginService;

    public ObservableCollection<ConfigFieldViewModel> Fields { get; } = new();
    public ObservableCollection<CardChartKindViewModel> CardCharts { get; } = new();
    public IAsyncRelayCommand GetCookieCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    // 所有业务逻辑在 VM 中，View 只做绑定
}

// PluginConfigWindow.xaml.cs 精简为
public partial class PluginConfigWindow : Window
{
    public PluginConfigWindow(PluginConfigViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
```
- **影响范围**：`PluginConfigWindow.xaml.cs`（大幅精简）、新增 `PluginConfigViewModel.cs`、`PluginConfigWindow.xaml`（改用绑定）
- **优先级理由**：P1 — 722 行 code-behind 是项目中最严重的 MVVM 违规

---

### A-13 [P1] UsageHistoryStore.AddPoint 构造 dummy UsageInfo 写库

- **文件**：`src/UsageMonitor.Core/Services/UsageHistoryStore.cs:105-117`
- **代码片段**：
```csharp
public void AddPoint(string providerId, double usagePercent)  // line 85
{
    // ...
    if (_repository != null)
    {
        // 构造最小可用的 UsageInfo，避免引入插件路径所产生的额外查询。
        // 为了让 GetUsagePercentage() 返回 usagePercent，
        // 必须让 UsedAmount / TotalAmount 形成比例 (usagePercent / 100)
        var dummy = new UsageInfo
        {
            ProviderId = providerId,
            ProviderName = providerId,
            UsedAmount = (decimal)usagePercent,   // hack：用百分比当金额
            TotalAmount = 100m,
            UsedTokens = 0,
            TotalTokens = -1,
            LastUpdated = DateTime.Now,
            IsSuccess = true,
            ErrorMessage = null
        };
        _ = Task.Run(() => _repository.UpsertPoint(dummy));
    }
}
```
- **问题**：为了复用 `UpsertPoint(UsageInfo)` 方法，构造了一个 `dummy` UsageInfo 对象，把 `usagePercent` 当作 `UsedAmount` 填入（`UsedAmount = (decimal)usagePercent`，`TotalAmount = 100m`），让 `GetUsagePercentage()` 恰好返回原始百分比。这是一个 hack：(1) 语义混乱——`UsedAmount` 字段在数据库中存的是百分比而非真实金额；(2) 如果未来 `UpsertPoint` 新增对 `UsedAmount` 的逻辑（如金额格式化），会意外破坏这个路径；(3) `AddPoint(string, double)` 和 `AddPoint(string, UsageInfo)` 两个重载走不同的写库路径（前者 `UpsertPoint`，后者 `InsertUsagePointIfChangedAsync`），行为不一致。
- **改进建议**：
```csharp
// Repository 增加一个直接接百分比的写入方法，不走 UsageInfo
public void UpsertPercentPoint(string providerId, double usedPercent, DateTime recordedAt)
{
    try
    {
        var percent = Math.Max(0, Math.Min(100, usedPercent));
        var bucketKey = recordedAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var day = recordedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        // 直接写 used_percent，不填 used_amount / total_amount
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR REPLACE INTO usage_points
            (provider_id, bucket_key, recorded_at, used_percent)
            VALUES ($pid, $bk, $rec, $up);";
        cmd.Parameters.AddWithValue("$pid", providerId);
        cmd.Parameters.AddWithValue("$bk", bucketKey);
        cmd.Parameters.AddWithValue("$rec", recordedAt);
        cmd.Parameters.AddWithValue("$up", percent);
        cmd.ExecuteNonQuery();
        UpsertDailyInternal(conn, tx, providerId, day);
        tx.Commit();
    }
    catch (Exception ex) { /* ... */ }
}

// UsageHistoryStore.AddPoint 改为
public void AddPoint(string providerId, double usagePercent)
{
    EnqueueMemoryPoint(providerId, usagePercent, isError: false);
    _repository?.UpsertPercentPoint(providerId, usagePercent, DateTime.Now);
}
```
- **影响范围**：`UsageHistoryRepository.cs`（新增方法）、`UsageHistoryStore.cs`（改调用）
- **优先级理由**：P1 — hack 性 workaround，语义混乱，两个重载走不同写库路径是隐性 bug 源

---

### A-14 [P1] 双重加密路径并存且互不相通（DPAPI vs 未用的 ISecretStore）

- **文件**：`src/UsageMonitor.Core/Services/ConfigService.cs:655-668`、`Services/Security/*.cs`
- **代码片段**：
```csharp
// ConfigService.cs — 实际使用的 DPAPI 加密
private static string Encrypt(string plainText)  // line 655
{
    var bytes = Encoding.UTF8.GetBytes(plainText);
    var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    return Convert.ToBase64String(encrypted);
}

// Security/SecretConfigBridge.cs — 从未被调用的替代方案
public static void SaveProviderSecret(string providerId, string accountName, string secretData)
{
    SecretStoreFactory.Current.Set(ResolveServiceName(providerId), accountName, secretData);
}

// SecretConfigBridge.cs line 7-8 的注释承认两套并存：
// 与 ConfigService 中基于 DPAPI + config.json 的加密方案并存，
// 调用方按场景选择
```
- **问题**：项目有两套独立的加密方案：(1) `ConfigService` 的 DPAPI 加密（实际使用），把密文 base64 存在 `config.json`；(2) `Security` 模块的 `ISecretStore`（Credential Manager / AES-GCM，从未使用）。`SecretConfigBridge` 的注释说"调用方按场景选择"，但实际没有任何调用方选择它。这导致：(1) DPAPI 加密的密文存在 JSON 文件中，与 Credential Manager 的系统级安全存储相比安全性较低；(2) 开发者不清楚该用哪套；(3) 如 A-04 所述，400+ 行死代码。
- **改进建议**：见 A-04 的改进建议（删除或激活 Security 模块）
- **影响范围**：与 A-04 相同
- **优先级理由**：P1 — 安全架构不明确，实际实现与设计意图脱节

---

### A-15 [P2] TriggerAreaOverlayWindow.xaml 硬编码颜色未走 Token

- **文件**：`src/UsageMonitor.App/Views/TriggerAreaOverlayWindow.xaml:28-101`
- **代码片段**：
```xml
<!-- line 28：半透明黑色遮罩，硬编码 -->
<Rectangle Fill="#80000000" />

<!-- line 36-37：调试矩形背景和边框，硬编码 -->
<Border Background="#201E90FF" BorderBrush="#FF1E90FF" />

<!-- line 58, 64, 70, 76, 83, 89, 95, 101：8 个 resize handle，全部硬编码 -->
<Rectangle Cursor="SizeWE" Background="White" BorderBrush="#FF1E90FF" BorderThickness="1" />
<Rectangle Cursor="SizeNS" Background="White" BorderBrush="#FF1E90FF" BorderThickness="1" />
<!-- ... 重复 8 次 -->
```
- **问题**：项目有完整的主题系统（`Tokens.xaml` + `Dark.xaml` + `Light.xaml`），所有颜色应通过 `DynamicResource` 引用。但 `TriggerAreaOverlayWindow` 硬编码了 `#80000000`、`#FF1E90FF`、`White` 等颜色，在浅色主题下遮罩和边框颜色不会适配。虽然这是个调试用覆盖窗口，但仍应遵循主题约定。
- **改进建议**：
```xml
<!-- 在 Tokens.xaml 或 Dark.xaml/Light.xaml 中新增 -->
<SolidColorBrush x:Key="OverlayMaskBrush" Color="#80000000" />
<SolidColorBrush x:Key="OverlayDebugBorderBrush" Color="{StaticResource AccentColor}" />
<SolidColorBrush x:Key="OverlayHandleBackgroundBrush" Color="White" />

<!-- XAML 中引用 -->
<Rectangle Fill="{DynamicResource OverlayMaskBrush}" />
<Border Background="{DynamicResource OverlayDebugBorderBrush}"
        BorderBrush="{DynamicResource OverlayDebugBorderBrush}" />
```
- **影响范围**：`TriggerAreaOverlayWindow.xaml`、`Tokens.xaml` 或 `Dark.xaml`/`Light.xaml`（新增资源）
- **优先级理由**：P2 — 仅影响调试窗口，但破坏了主题系统的一致性

---

### A-16 [P2] 4 处 async void 事件处理器，异常不可捕获

- **文件**：`TaskbarWindow.xaml.cs:122,689`、`PluginConfigWindow.xaml.cs:513`、`HistoryWindow.xaml.cs:45`
- **代码片段**：
```csharp
// TaskbarWindow.xaml.cs line 122
private async void OnRefreshAllClick(object sender, RoutedEventArgs e)
{
    await _refreshService.RefreshAllAsync();  // 异常会直接崩溃进程
}

// TaskbarWindow.xaml.cs line 689
private async void OnRingChartCenterClick(object sender, RoutedEventArgs e)
{
    // ...
}

// PluginConfigWindow.xaml.cs line 513
private async void OnGetCookieClick(object sender, RoutedEventArgs e)
{
    // 浏览器登录流程，可能抛出多种异常
}

// HistoryWindow.xaml.cs line 45
private async void OnRefreshClick(object sender, RoutedEventArgs e)
{
    // ...
}
```
- **问题**：`async void` 事件处理器中抛出的异常无法被 `try/catch` 捕获（会直接成为未处理异常导致进程崩溃）。WPF 的事件处理器确实需要 `async void` 签名，但应在方法体最外层包裹 `try/catch`。`OnGetCookieClick` 涉及浏览器登录流程（Playwright/Edge 启动、Cookie 提取等），异常概率较高。
- **改进建议**：
```csharp
private async void OnRefreshAllClick(object sender, RoutedEventArgs e)
{
    try
    {
        await _refreshService.RefreshAllAsync();
    }
    catch (Exception ex)
    {
        FileLogger.Error("TaskbarWindow", $"OnRefreshAllClick failed: {ex.Message}", ex);
    }
}

// 或提取为 async Task 方法 + fire-and-forget 包装
private async void OnGetCookieClick(object sender, RoutedEventArgs e)
{
    try { await GetCookieAsync(); }
    catch (Exception ex) { FileLogger.Error(...); }
}

private async Task GetCookieAsync() { /* ... */ }
```
- **影响范围**：`TaskbarWindow.xaml.cs`（2 处）、`PluginConfigWindow.xaml.cs`（1 处）、`HistoryWindow.xaml.cs`（1 处）
- **优先级理由**：P2 — 事件处理器确实需要 async void，但缺少 try/catch 是一个可导致闪退的隐患

---

### A-17 [P2] I18nKeys.cs 用 const 硬编码中文字符串

- **文件**：`src/UsageMonitor.App/Helpers/I18nKeys.cs:17-27`
- **代码片段**：
```csharp
public static class I18nKeys
{
    // 历史窗口 - 时间范围
    public const string Range_Last7Days = "最近 7 天";   // line 17
    public const string Range_Last30Days = "最近 30 天";  // line 18
    public const string Range_Last90Days = "最近 90 天";  // line 19
    public const string Range_All = "全部";               // line 20

    // 历史窗口 - 图表类型
    public const string Chart_Line = "折线图";            // line 23
    public const string Chart_Bar = "柱状图";             // line 24
    public const string Chart_HeatMap = "热力图";          // line 25
    public const string Chart_DayNightArc = "编程时段";    // line 26
}
```
- **问题**：这个类的注释说它是"i18n 扩展点占位"，但实际上它用 `const string` 硬编码了中文字符串值。`const` 意味着这些值在编译时确定，调用方直接引用字面值（`I18nKeys.Range_Last7Days` 实际就是 `"最近 7 天"`），无法在运行时切换语言。注释自己都说"二期替换方案：把每个 const 替换为读取 .resx 资源"，但这个二期从未到来。
- **改进建议**：
```csharp
// 改为 I18n 键名常量，运行时通过 I18n.T() 解析
public static class I18nKeys
{
    public const string Range_Last7Days = "history.range.last7days";
    public const string Range_Last30Days = "history.range.last30days";
    // ...

    // 便捷方法
    public static string Translate(string key) => I18n.T(key);
}

// I18n.cs 注册表新增
["history.range.last7days"] = "最近 7 天",
["history.range.last30days"] = "最近 30 天",
// ...

// 调用方
new KVP(range, I18n.T(I18nKeys.Range_Last7Days))
```
- **影响范围**：`I18nKeys.cs`、`I18n.cs`（注册新词条）、调用 `I18nKeys` 的代码（`HistoryViewModel.cs` 等）
- **优先级理由**：P2 — 当前不影响功能，但 i18n 占位名不副实

---

### A-18 [P2] LoadedPlugin 是可变状态袋，public setter 暴露运行时状态

- **文件**：`src/UsageMonitor.Core/Plugins/LoadedPlugin.cs:9-38`
- **代码片段**：
```csharp
public class LoadedPlugin
{
    public IUsageProvider Provider { get; }      // 只读 ✓
    public Assembly Assembly { get; }             // 只读 ✓
    public string FilePath { get; }               // 只读 ✓

    public UsageInfo? LastUsage { get; set; }     // 可变 ✗ — line 21
    public DateTime? LastQueryTime { get; set; }  // 可变 ✗ — line 24
    public bool LastQuerySuccess { get; set; }    // 可变 ✗ — line 27
    public bool IsEnabled { get; set; } = true;   // 可变 ✗ — line 30
}
```
- **问题**：`LoadedPlugin` 混合了不可变元数据（Provider、Assembly、FilePath）和可变运行时状态（LastUsage、LastQueryTime、IsEnabled 等）。public setter 意味着任何代码都可以随时修改这些字段，没有变更通知、没有线程安全保护。`RefreshService` 在刷新时直接写 `plugin.LastUsage = usage`（RefreshService.cs:128），而 `App.xaml.cs` 在启动时写 `plugin.IsEnabled = ...`（App.xaml.cs:97）。这种共享可变状态在多线程下（刷新线程 vs UI 线程）有竞态风险。
- **改进建议**：
```csharp
// 拆分为不可变元数据 + 可变状态（加锁或用 Interlocked）
public sealed class LoadedPlugin  // 元数据：构造后不变
{
    public IUsageProvider Provider { get; }
    public Assembly Assembly { get; }
    public string FilePath { get; }

    // 运行时状态用内部类隔离
    private readonly PluginRuntimeState _state = new();
    public PluginRuntimeState State => _state;
}

public sealed class PluginRuntimeState  // 可变状态：加锁保护
{
    private readonly object _lock = new();
    private UsageInfo? _lastUsage;
    private DateTime? _lastQueryTime;
    private bool _isEnabled = true;

    public UsageInfo? LastUsage
    {
        get { lock (_lock) return _lastUsage; }
        set { lock (_lock) _lastUsage = value; }
    }
    // ... 其他属性同理
}
```
- **影响范围**：`LoadedPlugin.cs`、`RefreshService.cs`（改为 `plugin.State.LastUsage =`）、`App.xaml.cs`、`MainViewModel.cs`（所有访问 `plugin.LastUsage` 等的地方）
- **优先级理由**：P2 — 当前单线程使用下不会立即出问题，但多线程刷新场景下是潜在竞态风险

## 四、整体改进路线图

### 第 1 个月：消除 P0 架构腐化
1. **拆分 MainViewModel.cs**（A-02）→ 按职责拆为 5-6 个独立 VM 文件，提取 MiniMax 专属逻辑为策略
2. **消除 Core 层 MiniMax 耦合**（A-03）→ 在 IUsageProvider 增加数据管理能力声明，Repository 参数化
3. **删除或激活 Security 模块**（A-04）→ 确认加密路径，删除死代码或迁移 ConfigService 到 ISecretStore
4. **启用 DI 容器**（A-01）→ 注册所有服务，App.xaml.cs 只做启动编排

### 第 2-3 个月：提升扩展性与可测试性
5. **拆分 IUsageProvider 接口**（A-05）→ 核心接口 + 可选能力接口（ISP）
6. **为核心服务提取接口**（A-07）→ IConfigService / IRefreshService / IUsageHistoryStore
7. **BrowserLoginService 实例化**（A-08）→ 改为 DI 注入的实例类
8. **PluginManager 升级**（A-06）→ AssemblyLoadContext + 按需加载
9. **修复 LoginHelper 配置竞争**（A-09）→ IPC 或文件监视方案

### 第 4-6 个月：UI/UX 与代码质量
10. **国际化基础数据补全**（A-10, A-17）→ XAML 改用 I18n 绑定，I18nKeys 改为键名
11. **PluginConfigWindow MVVM 化**（A-12）→ 提取 ViewModel
12. **消除 hack 和代码异味**（A-13, A-11, A-18）→ Repository 直写百分比、图标约定优于配置、LoadedPlugin 状态隔离
13. **主题一致性**（A-15）→ 覆盖窗口走 Token
14. **async void 安全网**（A-16）→ 4 处加 try/catch
