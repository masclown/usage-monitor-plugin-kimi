# UsageMonitor 全方位改进报告

> **审查范围**：架构 / UI / 代码实现 bug / 代码质量 / 安全
> **审查对象**：`D:\应用开发\UsageMonitor`（WPF + .NET 桌面应用，115 个源文件，~20,645 行 C#）
> **审查日期**：2026-07-19
> **审查团队**：架构师 高见远、工程师 寇豆码，主理人 齐活林 汇编
> **报告版本**：v1.0（合并版）

---

## 一、执行摘要

UsageMonitor 项目整体分层合理（Core / App / Plugins 三层），插件接口设计有前瞻性，主题切换、配置持久化、自绘图表控件完成度较高，异常处理与日志覆盖面较广。但审查发现 **35 个独立问题**，分布在三个系统性维度：

1. **架构腐化（7 个 P0）**：DI 容器声明却从未使用、2571 行 God ViewModel、Core 层硬编码 `"MiniMax"` 字符串、整套 Security 模块是死代码、ConfigService 并发 Dictionary 无锁访问、Cookie 明文落盘、GDI 句柄泄漏。这些问题叠加起来意味着"插件式架构"承诺尚未兑现，且存在生产环境崩溃与会话劫持风险。
2. **扩展性与可测试性（17 个 P1）**：核心服务无接口抽象、IUsageProvider 接口膨胀（15+ 成员）、BrowserLoginService 静态可变状态、LoginHelper 跨进程配置竞争、XAML 硬编码中文、PluginConfigWindow 722 行 code-behind 违反 MVVM、debug dump 明文落盘不清理等。
3. **代码质量与边界（11 个 P2）**：fire-and-forget 异常不可观察、`catch { }` 吞异常、`Dispose()` 空实现、I18nKeys 硬编码中文值、LoadedPlugin 可变状态袋等。

**改进路线建议**：第 1 个月修 P0（安全 + 并发 + God VM 拆分），第 2-3 个月修 P1（接口抽象 + DI + 跨进程协调 + MVVM 化），第 4-6 个月清理 P2 + 补国际化。

---

## 二、问题总览表（35 个，去重合并后）

### P0 级（必须立即修，7 个）

| 序号 | 模块 | 类型 | 文件 | 简述 |
|------|------|------|------|------|
| F-01 | 依赖注入 | 架构 | `App.xaml.cs:71-157` | DI 容器声明但从未使用，717 行手工 new 全部服务 |
| F-02 | MVVM | 架构+质量 | `MainViewModel.cs:1-2571` | God ViewModel 2571 行，含 3+ VM 类与 MiniMax 专属逻辑 |
| F-03 | 分层 | 架构 | `UsageHistoryRepository.cs:447,602` | Core 层硬编码 "MiniMax" 字符串，核心库被插件污染 |
| F-04 | 死代码/安全 | 架构 | `Services/Security/*.cs` | 整套 Security 模块（6 文件 ~400 行）从未被调用 |
| F-05 | 并发 | Bug | `ConfigService.cs:496-515` | ProviderConfigs Dictionary 无锁并发访问，可致数据结构损坏 |
| F-06 | 敏感数据 | 安全 | `BrowserLoginService.cs:510-523` | Cookie 明文 JSON 落盘，任何同用户进程可读取 |
| F-07 | 资源泄漏 | Bug | `App.xaml.cs:338-354` | GetHicon 句柄从不 DestroyIcon，GDI 句柄泄漏 |

### P1 级（本季度修，17 个）

| 序号 | 模块 | 类型 | 文件 | 简述 |
|------|------|------|------|------|
| F-08 | 插件系统 | 架构 | `IUsageProvider.cs:10-178` | 接口膨胀，15+ 成员含 MiniMax 专属默认值 |
| F-09 | 插件系统 | 架构 | `PluginManager.cs:51,73` | 不用 AssemblyLoadContext，盲目扫描所有 DLL |
| F-10 | 服务层 | 架构 | `ConfigService.cs`, `RefreshService.cs` | 核心服务无接口抽象，无法 mock 测试 |
| F-11 | 服务层 | 架构 | `BrowserLoginService.cs:22-36` | 静态类 + 静态可变状态，无法测试，并发覆盖错误 |
| F-12 | 跨进程 | 架构 | `LoginHelper/Program.cs:59-65` | 独立 ConfigService 实例直接写 config.json，跨进程竞争 |
| F-13 | 国际化 | UI | `Views/*.xaml` | XAML 大量硬编码中文，I18n 仅覆盖插件配置字段 |
| F-14 | 控件复用 | 架构 | `MainViewModel.cs:194-236` | ResolveIconPath 硬编码 12 个 Provider 到文件名 switch |
| F-15 | MVVM | UI | `PluginConfigWindow.xaml.cs:1-722` | 722 行 code-behind 全业务逻辑在事件处理器，无 ViewModel |
| F-16 | 数据流 | 架构 | `UsageHistoryStore.cs:105-117` | AddPoint 构造 dummy UsageInfo（百分比当金额）写库 |
| F-17 | 并发 | Bug | `RefreshService.cs:82-83` | _isRefreshing 非原子 check-then-set 竞态 |
| F-18 | 日志 | Bug | `FileLogger.cs:199-213` | 部署环境无 .sln 时日志目录回退到 GetCurrentDirectory() |
| F-19 | 敏感数据 | 安全 | `MiniMaxProvider.cs:609-623` | WriteDebugResponse 原始 API JSON（含敏感数据）写磁盘不清理 |
| F-20 | 敏感数据 | 安全 | `BrowserLoginService.cs:299-313` | 诊断日志写入 document.cookie 前 500 字符到磁盘 |
| F-21 | 加密 | Bug | `ConfigService.cs:461-491` | ReloadProviderConfigsFromDisk 未解密敏感字段（latent P0） |
| F-22 | 资源泄漏 | Bug | `App.xaml.cs:589-611` | AttachTaskbarWindowResizeHandlers 事件订阅不取消，重建时叠加 |
| F-23 | 性能 | 质量 | `UsageHistoryStore.cs:172-192` | AddErrorPoint 中 queue.Last() 对 Queue 做 O(n) 全遍历 |
| F-24 | 敏感数据 | 安全 | `LoginHelper/Program.cs:54` | Cookie 前 60 字符打印到控制台 |
| F-25 | 加密 | 质量 | `ConfigService.cs:648-652` | IsSensitiveKey 含 "key" 关键词过宽匹配 |

### P2 级（机会性改进，11 个）

| 序号 | 模块 | 类型 | 文件 | 简述 |
|------|------|------|------|------|
| F-26 | 主题 | UI | `TriggerAreaOverlayWindow.xaml:28-101` | 硬编码颜色（#FF1E90FF 等）未走主题 Token |
| F-27 | 异步 | 异常 | `TaskbarWindow.xaml.cs:122,689` 等 | async void 事件处理器 + fire-and-forget 缺 try/catch（4 处合并） |
| F-28 | 国际化 | 质量 | `I18nKeys.cs:17-27` | 用 const 硬编码中文字符串值，不是 i18n 键名 |
| F-29 | 插件系统 | 并发 | `LoadedPlugin.cs:21-30` | 混合不可变元数据和可变运行时状态，public setter 无线程保护 |
| F-30 | 代码质量 | 健壮性 | `UsageHistoryRepository.cs:619-660` | TryGet* 未处理 JsonElement 类型 |
| F-31 | 代码质量 | 性能 | `UsageHistoryRepository.cs:870-911` | DeleteProviderDataAsync 不必要 Task.Run 包裹同步事务 |
| F-32 | 代码质量 | 清洁度 | `UsageHistoryRepository.cs:1006-1010` | Dispose() 空实现，IDisposable 误导 |
| F-33 | 异常处理 | 诊断 | `UsageHistoryRepository.cs:544` | catch { } 完全吞掉异常无日志 |
| F-34 | 边界条件 | 健壮性 | `WindowsCredentialManagerStore.cs:85-123` | 空字符串 secret 未校验，AllocHGlobal(0) 行为不确定 |
| F-35 | 边界条件 | 健壮性 | `RefreshService.cs:63` | RefreshIntervalSeconds * 1000 潜在 int 溢出 |

---

## 三、P0 详细改进项（必须立即修）

### F-01 [P0] DI 容器声明但从未使用，App.xaml.cs 手工 new 全部服务

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
- **问题**：`Microsoft.Extensions.DependencyInjection` NuGet 包被引入但零调用（全项目无 `AddSingleton`/`AddTransient`/`GetRequiredService`）。所有服务在 `App.xaml.cs` 的 `OnStartup` 中手工 `new` 构造，形成 717 行的 God Class。后果：(1) 服务生命周期无法统一管理；(2) 无法轻松替换实现（如测试 mock）；(3) 服务间依赖关系隐式耦合在构造顺序中，任何重排都有 NullReferenceException 风险；(4) NuGet 包白白增加发布体积。
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
- **优先级理由**：当前手工构造链已导致 App.xaml.cs 成为 717 行 God Class，NuGet 包已声明却未用，说明架构意图与实现脱节，后续服务新增/重排都会加剧脆弱性

---

### F-02 [P0] MainViewModel.cs 2571 行 God ViewModel，含 MiniMax 专属逻辑

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
}
```
- **问题**：(1) 单文件 2571 行，包含至少 3 个 ViewModel 类 + 多个编辑器 VM，严重违反单一职责；(2) `ProviderUsageViewModel` 本应是通用"服务商用量展示"VM，却被 MiniMax 专属字段（5h 限额、周限额、视频赠送、订阅档位、积分余额等 15+ 属性）和方法（`UpdateFromMiniMaxDom`）污染，新增任何 Provider 都要背负 MiniMax 字段包袱；(3) 直接用 `if (ProviderId == "MiniMax")` 分支把插件耦合死在 VM 层。
- **改进建议**：
```csharp
// 1. 拆分文件：每个 VM 独立文件
// ViewModels/ProviderUsageViewModel.cs       — 通用属性
// ViewModels/PluginItemViewModel.cs          — 插件列表项
// ViewModels/MainViewModel.cs                — 主窗口编排
// ViewModels/TierListEditorViewModel.cs      — 色阶编辑器
// ViewModels/HeatMapTierListEditorViewModel.cs

// 2. MiniMax 专属数据通过策略模式注入，而非硬编码
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
- **优先级理由**：2571 行单文件 + 跨插件耦合是当前最大的可维护性炸弹，任何新增 Provider 或修改 MiniMax 展示逻辑都要在这个巨型文件中搜索

---

### F-03 [P0] Core 层硬编码 "MiniMax" 字符串，核心库被具体插件污染

- **文件**：`src/UsageMonitor.Core/Services/UsageHistoryRepository.cs:447,602`、`src/UsageMonitor.App/ViewModels/MainViewModel.cs:705,1063,2108`
- **代码片段**：
```csharp
// UsageHistoryRepository.cs — Core 层不应知道任何具体 Provider
// line 447：清理逻辑只针对 MiniMax
cmd.CommandText = "DELETE FROM usage_points WHERE used_tokens = 0 AND provider_id = 'MiniMax';";

// line 602：业务指纹构建也硬编码 MiniMax
if (string.Equals(usage.ProviderId, "MiniMax", StringComparison.OrdinalIgnoreCase))
{
    fields.Add($"5h={TryGetDouble(usage.Extra, "mm_5hUsedPercent")}");
    fields.Add($"week={TryGetDouble(usage.Extra, "mm_weeklyUsedPercent")}");
}

// MainViewModel.cs line 705, 1063 — App 层也硬编码
if (string.Equals(usage.ProviderId, "MiniMax", StringComparison.OrdinalIgnoreCase)) { ... }
? UsageMonitor.App.Helpers.HeatMapTierScale.ResolveBrush(token, "MiniMax")
```
- **问题**：Core 层是通用持久化仓库，理应与具体插件无关，但代码中直接硬编码 `"MiniMax"` 字符串做数据清理和指纹计算。后果：(1) 新增任何有 token=0 场景的 Provider 都无法享受清理逻辑；(2) 任何插件想自定义指纹字段都要修改 Core 库代码；(3) 违反开闭原则和分层架构基本约定。
- **改进建议**：
```csharp
// 在 IUsageProvider 接口中声明数据管理能力，由插件自己提供指纹和清理规则
public interface IUsageProvider
{
    // ... 现有成员 ...
    DataCleanupRule? CleanupRule => null;
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
- **优先级理由**：Core 层被具体插件污染是架构腐化核心标志，不修复则"插件式架构"承诺形同虚设

---

### F-04 [P0] 整套 Security 模块从未被调用，是死代码

- **文件**：`src/UsageMonitor.Core/Services/Security/ISecretStore.cs`、`SecretStoreFactory.cs`、`SecretConfigBridge.cs`、`WindowsCredentialManagerStore.cs`、`AesGcmFileSecretStore.cs`、`MasterKeyMissingException.cs`、`ConfigService.cs:655-668`
- **代码片段**：
```csharp
// SecretConfigBridge.cs — 设计了完整的凭据管理 API
public static class SecretConfigBridge
{
    public static void SaveProviderSecret(string providerId, string accountName, string secretData)
    {
        SecretStoreFactory.Current.Set(ResolveServiceName(providerId), accountName, secretData);
    }
    // ... 但全项目搜索 SecretConfigBridge. 的结果：零调用
}

// ConfigService.cs 仍用自己的 DPAPI 加密路径
private static string Encrypt(string plainText)  // line 655
{
    var bytes = Encoding.UTF8.GetBytes(plainText);
    var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    return Convert.ToBase64String(encrypted);
}
```
- **问题**：Security 模块包含 6 个文件、完整 `ISecretStore` 接口、Windows Credential Manager 实现、AES-GCM 降级方案、工厂模式、桥接类——但**全项目没有任何一处调用** `SecretConfigBridge`、`SecretStoreFactory.Current` 或 `ISecretStore`。实际加密全部走 `ConfigService` 的 DPAPI 路径。这套约 400 行代码是纯粹死代码，不仅增加维护负担，还误导开发者（包括安全审计者）以为凭据管理走了 Credential Manager（实际没有）。
- **改进建议**：
```csharp
// 方案 A（推荐）：删除 Security 模块，ConfigService 的 DPAPI 路径已满足需求
// 移除 6 个文件 + Core.csproj 中的 System.Security.Cryptography.ProtectedData（若仅 ConfigService 用）

// 方案 B（如计划迁移到 Credential Manager）：把 ConfigService 加密逻辑改为委托 ISecretStore
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
                    SecretConfigBridge.SaveProviderSecret(providerId, key, config.Values[key]);
                    config.Values[key] = "__STORED_IN_SECRET_STORE__";
                }
            }
        }
    }
}
```
- **影响范围**：`Services/Security/` 目录（删除或激活）、`ConfigService.cs`（如走方案 B 重构加密路径）、`Core.csproj`（移除或保留 ProtectedData 依赖）
- **优先级理由**：400+ 行死代码 + 安全架构与实际实现不一致，存在误导风险（审计者可能以为已用 Credential Manager）

---

### F-05 [P0] ConfigService.ProviderConfigs Dictionary 无锁并发访问

- **文件**：`src/UsageMonitor.Core/Services/ConfigService.cs:496-515`
- **代码片段**：
```csharp
// GetProviderConfig — 无锁读取 + 写入
public ProviderConfig GetProviderConfig(string providerId, IUsageProvider? provider = null)
{
    if (!_settings.ProviderConfigs.TryGetValue(providerId, out var config)) // 无锁读
    {
        config = new ProviderConfig { ProviderId = providerId };
        // ...
        _settings.ProviderConfigs[providerId] = config; // 无锁写
    }
    return config;
}

// UpdateProviderConfig — 有锁写入
public void UpdateProviderConfig(string providerId, ProviderConfig config)
{
    lock (_ioLock)
    {
        _settings.ProviderConfigs[providerId] = config; // 有锁写
    }
    Save();
}
```
- **问题**：`_settings.ProviderConfigs` 是 `Dictionary<string, ProviderConfig>`，非线程安全。`RefreshService.RefreshPluginAsync` 在 ThreadPool 线程调用 `GetProviderConfig`（无锁），而 UI 线程通过 `PluginConfigWindow` 保存配置时调用 `UpdateProviderConfig`（有锁）。并发的读+写可导致 `Dictionary` 内部桶链表损坏，表现为 `InvalidOperationException`（Collection was modified）、死循环（桶链表成环）或返回错误值。在 .NET 中 Dictionary 并发写损坏是**不可恢复**的，进程必须重启。
- **改进建议**：
```csharp
// 方案一：所有 ProviderConfigs 访问加 _ioLock
public ProviderConfig GetProviderConfig(string providerId, IUsageProvider? provider = null)
{
    lock (_ioLock)
    {
        if (!_settings.ProviderConfigs.TryGetValue(providerId, out var config))
        {
            config = new ProviderConfig { ProviderId = providerId };
            if (provider != null)
            {
                foreach (var field in provider.ConfigFields)
                {
                    if (!string.IsNullOrEmpty(field.DefaultValue))
                        config.SetValue(field.Key, field.DefaultValue);
                }
            }
            _settings.ProviderConfigs[providerId] = config;
        }
        return config;
    }
}

// 方案二（更优）：改用 ConcurrentDictionary<string, ProviderConfig>
```
- **影响范围**：`ConfigService` 所有读写 ProviderConfigs 路径（`GetProviderConfig`、`UpdateProviderConfig`、`EncryptSensitiveFields`、`DecryptSensitiveFields`、`ReloadProviderConfigsFromDisk`）
- **优先级理由**：Dictionary 并发损坏会导致进程崩溃或配置丢失，属 P0

---

### F-06 [P0] BrowserLoginService.SaveCookieData 明文存储 Cookie

- **文件**：`src/UsageMonitor.Core/Services/BrowserLoginService.cs:510-523`
- **代码片段**：
```csharp
public static void SaveCookieData(BrowserCookieData data)
{
    if (data == null) throw new ArgumentNullException(nameof(data));
    Directory.CreateDirectory(CookieDir);
    var path = GetCookieFilePath(data.ProviderId);
    var options = new JsonSerializerOptions { WriteIndented = true, /* ... */ };
    // 明文 JSON 直接写入 — Cookie 含 session token，任何同用户进程可直接读取
    File.WriteAllText(path, JsonSerializer.Serialize(data, options), Encoding.UTF8);
}
```
- **问题**：Cookie 文件 `%AppData%/UsageMonitor/cookies/MiniMax.json` 以明文 JSON 存储，包含完整 Cookie 字符串（含 `_token` JWT 会话令牌）。同一 Windows 用户下任何进程均可读取此文件进行会话劫持。虽然 `PersistToMainConfig` 同时将 Cookie 加密写入 config.json，但明文副本仍然存在且被 `MiniMaxProvider.GetUsageAsync` 的自愈逻辑（line 206）读取回填。
- **改进建议**：
```csharp
public static void SaveCookieData(BrowserCookieData data)
{
    if (data == null) throw new ArgumentNullException(nameof(data));
    Directory.CreateDirectory(CookieDir);
    var path = GetCookieFilePath(data.ProviderId);

    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
    // 使用 DPAPI 加密后写入（与 ConfigService.Encrypt 一致）
    var plainBytes = Encoding.UTF8.GetBytes(json);
    var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
    File.WriteAllBytes(path, encrypted);
}

public static BrowserCookieData? LoadCookieData(string providerId)
{
    var path = GetCookieFilePath(providerId);
    if (!File.Exists(path)) return null;
    try
    {
        var encrypted = File.ReadAllBytes(path);
        var plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(plainBytes);
        return JsonSerializer.Deserialize<BrowserCookieData>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch { return null; }
}
```
- **影响范围**：`BrowserLoginService.SaveCookieData`、`LoadCookieData`、`DeleteCookieData`、`CheckCookieValidAsync`；`MiniMaxProvider` 的 Cookie 自愈回填逻辑
- **优先级理由**：明文存储会话令牌构成安全漏洞，可被利用进行会话劫持

---

### F-07 [P0] LoadTrayIconFromLogo GDI 句柄泄漏

- **文件**：`src/UsageMonitor.App/App.xaml.cs:338-354`
- **代码片段**：
```csharp
private static System.Drawing.Icon LoadTrayIconFromLogo()
{
    try
    {
        var path = Helpers.LogoProvider.GetLogoPath();
        using var bmp = new System.Drawing.Bitmap(path);
        var hIcon = bmp.GetHicon();           // 分配 GDI icon handle
        // Icon.FromHandle 不接管 hIcon 的所有权；Clone 出独立 Icon 后
        // 原始 hIcon 永远不会被 DestroyIcon 释放
        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
        return icon;
        // ← hIcon 泄漏！缺少 DestroyIcon(hIcon)
    }
    catch (Exception ex) { /* ... */ return SystemIcons.Application; }
}
```
- **问题**：`Bitmap.GetHicon()` 分配的 GDI icon handle 不会被 GC 回收，必须显式调用 `DestroyIcon(hIcon)`。当前代码只 Clone 出独立 Icon 但从未释放原始 handle。每次主题切换（`ThemeManager.ThemeChanged` 事件，line 307-314）都调用此方法，每次泄漏一个 GDI handle。GDI handle 默认配额 10000/进程，长期运行（如开机自启常驻）后耗尽会导致 UI 渲染异常（黑框、控件不绘制）。
- **改进建议**：
```csharp
[System.Runtime.InteropServices.DllImport("user32.dll")]
[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
private static extern bool DestroyIcon(IntPtr handle);

private static System.Drawing.Icon LoadTrayIconFromLogo()
{
    try
    {
        var path = Helpers.LogoProvider.GetLogoPath();
        using var bmp = new System.Drawing.Bitmap(path);
        var hIcon = bmp.GetHicon();
        try
        {
            var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
            return icon;
        }
        finally
        {
            DestroyIcon(hIcon); // 显式释放原始 GDI handle
        }
    }
    catch (Exception ex)
    {
        FileLogger.Error("App", $"LoadTrayIconFromLogo() failed: {ex.Message}", ex);
        return SystemIcons.Application;
    }
}
```
- **影响范围**：`App.LoadTrayIconFromLogo`、`App.InitializeTrayIcon`、`ThemeManager.ThemeChanged` 回调
- **优先级理由**：GDI 句柄泄漏在长驻任务栏应用中会导致 UI 渲染崩溃

---

## 四、P1 详细改进项（本季度修）

### F-08 [P1] IUsageProvider 接口膨胀，15+ 成员含 MiniMax 专属默认值

- **文件**：`src/UsageMonitor.Core/Plugins/IUsageProvider.cs:10-178`
- **代码片段**：
```csharp
public interface IUsageProvider
{
    // 基础成员（合理）
    string ProviderId { get; }
    string DisplayName { get; }
    // ... 7 个基础成员

    // 能力声明（开始膨胀，9 个带默认实现）
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
- **问题**：接口有 15+ 成员，其中 9 个是带默认实现的"能力声明"属性。这些默认值大多为 MiniMax 场景设计（`DefaultRenderKinds` 注释直接提到 MiniMax）。新增最简单 Provider 也要面对这么多成员理解成本，且 default interface method 在 .NET 8 中无法被 Moq 等 mock 框架很好覆盖。
- **改进建议**：拆分为核心接口 + 可选能力接口（ISP 原则）
```csharp
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
- **影响范围**：`IUsageProvider.cs`（拆分）、所有 4 个 Provider 插件（按需实现新接口）、`MainViewModel.cs`（用 `is` 检查能力）、`PluginConfigWindow.xaml.cs`
- **优先级理由**：不影响当前运行，但严重影响扩展性；每新增 Provider 都要面对 15+ 成员的庞大接口

---

### F-09 [P1] PluginManager 不用 AssemblyLoadContext，盲目扫描所有 DLL

- **文件**：`src/UsageMonitor.Core/Plugins/PluginManager.cs:51,73`
- **代码片段**：
```csharp
// line 51：递归扫描所有 DLL，包括 System.*.dll、Microsoft.*.dll 等
var dllFiles = Directory.GetFiles(_pluginDirectory, "*.dll", SearchOption.AllDirectories);

// line 73：用 Assembly.LoadFrom 加载，无法卸载
var assembly = Assembly.LoadFrom(dllPath);
```
- **问题**：(1) 递归扫描 `plugins/` 下所有 `.dll`（包括 SQLite 依赖、System 库等非插件 DLL），每个都被 `Assembly.LoadFrom` 加载到内存（即使最终无 IUsageProvider 类型），浪费内存且可能加载恶意 DLL；(2) `Assembly.LoadFrom` 不支持卸载，`UnloadPlugin` 只是从列表移除但程序集仍留在内存中，无法真正热插拔；(3) 无版本兼容性检查，插件用旧版 Core 编译后可能运行时 `MissingMethodException`。
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
        if (assemblyName.Name == "UsageMonitor.Core") return null; // 让 Core 由默认上下文加载
        return base.Load(assemblyName);
    }
}

// 2. 只加载声明了 IUsageProvider 的 DLL（先 MetadataLoadContext 只读检查再加载）
private void LoadPluginFromAssembly(string dllPath)
{
    var resolver = new PathAssemblyResolver(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));
    using var mlc = new MetadataLoadContext(resolver);
    var asm = mlc.LoadFromAssemblyPath(dllPath);
    var hasProvider = asm.GetTypes()
        .Any(t => typeof(IUsageProvider).IsAssignableFrom(t) && !t.IsInterface);
    if (!hasProvider) return;

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
    plugin.LoadContext?.Unload();
    return true;
}
```
- **影响范围**：`PluginManager.cs`（重写加载逻辑）、`LoadedPlugin.cs`（增加 LoadContext 字段）
- **优先级理由**：当前实现无法热插拔，且加载所有 DLL 有安全和性能隐患

---

### F-10 [P1] 核心服务无接口抽象，全部依赖具体类

- **文件**：`src/UsageMonitor.Core/Services/ConfigService.cs:255`、`RefreshService.cs:11`、`UsageHistoryStore.cs:29`
- **代码片段**：
```csharp
// ConfigService 是具体类，无接口
public class ConfigService  // line 255
{
    public AppSettings Settings => _settings;  // 直接暴露可变设置对象
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
- **问题**：所有核心服务都是具体类，没有接口抽象。后果：(1) 无法在测试中 mock 服务（如模拟配置加载失败、刷新异常）；(2) 无法替换实现（如想把 SQLite 换成另一个数据库）；(3) `ConfigService.Settings` 直接返回内部可变对象，外部代码可绕过锁直接修改 `ProviderConfigs` 等字典，破坏线程安全。
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
- **影响范围**：`ConfigService.cs`、`RefreshService.cs`（提取接口）、`MainViewModel.cs`、`App.xaml.cs`（DI 时用接口）
- **优先级理由**：不影响运行，但严重影响可测试性和可替换性

---

### F-11 [P1] BrowserLoginService 是静态类 + 静态可变状态

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
- **问题**：(1) 静态类 + 静态字段意味着全局唯一状态无法隔离；(2) 无法 mock（单元测试无法替换浏览器登录行为）；(3) `LastError` 是 `static` 属性，多个并发登录会互相覆盖错误信息；(4) `RegisterConfigService` 是隐式初始化——如果忘记调用，`PersistToMainConfig` 静默跳过（仅日志），Cookie 只存到 `cookies/*.json` 不进 config.json，问题难以排查。
- **改进建议**：
```csharp
public interface IBrowserLoginService
{
    string? LastError { get; }
    Task<BrowserCookieData?> LoginAndExtractCookieAsync(
        BrowserLoginConfig config, CancellationToken ct = default);
}

public class BrowserLoginService : IBrowserLoginService
{
    private readonly ConfigService _configService;

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
- **影响范围**：`BrowserLoginService.cs`（重构为实例类）、`App.xaml.cs`（移除 `RegisterConfigService` 调用）、`PluginConfigWindow.xaml.cs`（DI 接收）、`LoginHelper/Program.cs`
- **优先级理由**：静态可变状态是并发 bug 温床，且阻碍测试

---

### F-12 [P1] LoginHelper 创建独立 ConfigService 实例，与主程序存在配置竞争

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
- **问题**：LoginHelper 是独立进程，它创建了自己的 `ConfigService` 实例并直接写 `config.json`。如果主程序同时运行（用户从托盘菜单触发登录），两个进程的 `ConfigService` 都会读写同一个 `config.json`：(1) 主程序内存配置会被 LoginHelper 写入覆盖（主程序不知文件已变，除非有文件监视）；(2) 两个进程同时 `File.Replace` 可能导致原子写入冲突；(3) `ConfigService._ioLock` 是实例级锁，跨进程无效。`App.xaml.cs` 的 `BrowserLoginService.RegisterConfigService` 设计就是为了主程序内调用避免此问题，但 LoginHelper 绕过了它。
- **改进建议**：
```csharp
// 方案 A（推荐）：LoginHelper 不直接写 config.json，通过 IPC（命名管道）把 Cookie 传给主程序
// LoginHelper/Program.cs
var data = await BrowserLoginService.LoginAndExtractCookieAsync(...);
if (data != null)
{
    using var client = new NamedPipeClientStream(".", "UsageMonitor.ConfigPipe");
    await client.ConnectAsync(5000);
    var json = JsonSerializer.Serialize(data);
    var bytes = Encoding.UTF8.GetBytes(json);
    await client.WriteAsync(BitConverter.GetBytes(bytes.Length));
    await client.WriteAsync(bytes);
}

// 方案 B（简化）：LoginHelper 写入后发信号让主程序 ReloadProviderConfigsFromDisk
// 主程序启动 FileSystemWatcher 监视 config.json
```
- **影响范围**：`LoginHelper/Program.cs`、`App.xaml.cs`（方案 A 需加管道服务端）、`ConfigService.cs`（方案 B 需加文件监视）
- **优先级理由**：配置竞争可能导致用户 Cookie 丢失或配置文件损坏

---

### F-13 [P1] XAML 中大量硬编码中文，I18n 仅覆盖插件配置字段

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
<Button Content="保存设置" />                                         <!-- line 132 -->
```
- **问题**：`I18n.cs` 的 `T()` 方法和 `Register()` 机制已实现，但只用于插件配置字段名（`plugin.MiniMax.field.ApiKey.name` 等）。所有 App UI 文案（设置窗口、历史窗口、插件配置窗口、托盘菜单等）都是硬编码中文。`I18n.cs` 自己注释承认"当前 UI 文案仍以中文硬编码"。任何多语言支持都需要大面积修改 XAML。
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
- **优先级理由**：不影响当前功能，但国际化基础设施数据缺失，未来支持英文需要大面积返工

---

### F-14 [P1] ResolveIconPath 硬编码 12 个 Provider 到文件名 switch

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
    var ext = name switch
    {
        "minimax" => ".ico",
        "deepseek" => ".png",
        // ... 12 个 case
    };
}
```
- **问题**：新增 Provider 需修改这个 switch 语句（两处：文件名 + 扩展名），违反开闭原则。方法放在 `ProviderUsageViewModel` 中，App 层硬编码了所有已知 Provider 图标信息。`IUsageProvider.IconPath` 属性本就存在但未被使用（MiniMax 返回 null，其它插件也没用）。
- **改进建议**：
```csharp
// 优先用 IUsageProvider.IconPath，回退到约定优于配置
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
- **优先级理由**：每新增 Provider 都要改 App 层代码，与"插件式架构"目标矛盾

---

### F-15 [P1] PluginConfigWindow 722 行 code-behind，违反 MVVM

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
- **问题**：722 行 code-behind 包含表单动态生成、Cookie 登录流程、图表选择、保存逻辑等全部业务。没有对应 ViewModel，`DataContext` 未设置，所有操作通过事件处理器直接操作 UI 控件和 ConfigService。`_isLoginInProgress` 是静态 HashSet，跨窗口共享状态。违反 MVVM，无法通过绑定测试、无法复用逻辑。
- **改进建议**：
```csharp
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
- **优先级理由**：722 行 code-behind 是项目中最严重的 MVVM 违规

---

### F-16 [P1] UsageHistoryStore.AddPoint 构造 dummy UsageInfo 写库

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
- **问题**：为复用 `UpsertPoint(UsageInfo)`，构造 dummy UsageInfo，把 `usagePercent` 当 `UsedAmount` 填入（`UsedAmount = (decimal)usagePercent`，`TotalAmount = 100m`），让 `GetUsagePercentage()` 恰好返回原始百分比。这是 hack：(1) 语义混乱——`UsedAmount` 字段在数据库中存的是百分比而非真实金额；(2) 若未来 `UpsertPoint` 新增对 `UsedAmount` 的逻辑（如金额格式化），会意外破坏此路径；(3) `AddPoint(string, double)` 和 `AddPoint(string, UsageInfo)` 两个重载走不同写库路径（前者 `UpsertPoint`，后者 `InsertUsagePointIfChangedAsync`），行为不一致。
- **改进建议**：
```csharp
// Repository 增加直接接百分比的写入方法，不走 UsageInfo
public void UpsertPercentPoint(string providerId, double usedPercent, DateTime recordedAt)
{
    try
    {
        var percent = Math.Max(0, Math.Min(100, usedPercent));
        var bucketKey = recordedAt.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var day = recordedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
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
- **优先级理由**：hack 性 workaround，语义混乱，两个重载走不同写库路径是隐性 bug 源

---

### F-17 [P1] RefreshService._isRefreshing 非原子竞态

- **文件**：`src/UsageMonitor.Core/Services/RefreshService.cs:82-83`
- **代码片段**：
```csharp
private bool _isRefreshing; // 非 volatile，无 Interlocked

public async Task RefreshAllAsync(string triggerKind = "manual")
{
    if (_isRefreshing) return; // check
    _isRefreshing = true;      // then set — 两步之间可被其他线程穿插
    // ...
}
```
- **问题**：`_isRefreshing` 的 check-then-set 不是原子操作。定时器回调（ThreadPool 线程）和用户手动刷新（UI 线程）可同时读到 `false` 并同时进入刷新流程。虽然 per-provider 锁防止了同一 Provider 的并发查询，但 `UsageRefreshed` 事件可能被触发两次，导致 UI 双重更新。`RefreshStarted` 也会被触发两次。
- **改进建议**：
```csharp
private int _isRefreshing; // 0 = idle, 1 = refreshing

public async Task RefreshAllAsync(string triggerKind = "manual")
{
    if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0) return;
    try
    {
        // ... 原有逻辑
    }
    finally
    {
        _isRefreshing = 0;
    }
}
```
- **影响范围**：`RefreshService.RefreshAllAsync`
- **优先级理由**：竞态不会崩溃但会导致重复 API 调用和 UI 闪烁

---

### F-18 [P1] FileLogger 日志目录在部署环境解析不可靠

- **文件**：`src/UsageMonitor.Core/Services/FileLogger.cs:199-213`
- **代码片段**：
```csharp
private static string ResolveProjectRoot()
{
    try
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            var slnPath = Path.Combine(dir.FullName, "UsageMonitor.sln");
            if (File.Exists(slnPath)) return dir.FullName; // 开发环境找到 .sln
            dir = dir.Parent;
        }
    }
    catch { }
    return Directory.GetCurrentDirectory(); // 部署环境回退到工作目录
}
```
- **问题**：安装/部署的应用没有 `UsageMonitor.sln`，回退到 `Directory.GetCurrentDirectory()`。开机自启场景下工作目录可能是 `C:\Windows\System32`，导致日志写入 `System32\logs\` 因权限不足而静默失败（`WriteEntry` 的 `catch { }` 吞掉异常）。`MiniMaxProvider.DebugDir` 也使用 `FileLogger.LogDir`，导致 debug dump 同样失败。项目其他部分使用 `%AppData%/UsageMonitor/`，此处不一致。
- **改进建议**：
```csharp
private static string ResolveProjectRoot()
{
    // 优先使用 %AppData%/UsageMonitor/logs，与 config.json / history.db 一致
    var appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "logs");
    return appDataDir;
}

// 或保留开发环境 .sln 查找，但回退改为 AppData
private static string ResolveProjectRoot()
{
    try
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            var slnPath = Path.Combine(dir.FullName, "UsageMonitor.sln");
            if (File.Exists(slnPath)) return dir.FullName;
            dir = dir.Parent;
        }
    }
    catch { }
    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageMonitor", "logs");
}
```
- **影响范围**：`FileLogger.LogDir`、`FileLogger.GetCurrentLogPath`、`MiniMaxProvider.DebugDir`、`MiniMaxDomExtractor.DebugDir`
- **优先级理由**：日志丢失导致生产环境无法排查问题

---

### F-19 [P1] MiniMaxProvider.WriteDebugResponse 原始 API JSON 写磁盘不清理

- **文件**：`src/Plugins/UsageMonitor.Plugin.MiMo/MiniMaxProvider.cs:609-623`
- **代码片段**：
```csharp
private static void WriteDebugResponse(string baseUrl, string json)
{
    try
    {
        Directory.CreateDirectory(DebugDir);
        var fileName = $"MiniMax-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
        var path = Path.Combine(DebugDir, fileName);
        var content = $"// baseUrl: {baseUrl}\n// time: {DateTime.Now:O}\n\n{json}";
        File.WriteAllText(path, content); // 原始 API 响应可能含账户 ID、订阅信息
    }
    catch { /* Silently ignore */ }
}
```
- **问题**：每次 API 调用都把完整响应 JSON 写到 `%AppData%/UsageMonitor/debug/`（或开发环境 `logs/debug/`），文件无限累积，从不清理。响应中可能包含账户 ID、订阅状态、用量明细等敏感信息。按 5 分钟刷新间隔，每天约 288 个文件。
- **改进建议**：
```csharp
private static void WriteDebugResponse(string baseUrl, string json)
{
    try
    {
        Directory.CreateDirectory(DebugDir);
        // 清理超过 50 个的旧文件
        var existing = Directory.GetFiles(DebugDir, "MiniMax-*.json")
            .OrderByDescending(f => f).Skip(50).ToArray();
        foreach (var old in existing) { try { File.Delete(old); } catch { } }

        var fileName = $"MiniMax-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
        var path = Path.Combine(DebugDir, fileName);
        var content = $"// baseUrl: {baseUrl}\n// time: {DateTime.Now:O}\n\n{json}";
        File.WriteAllText(path, content);
    }
    catch { /* Silently ignore */ }
}
```
- **影响范围**：`MiniMaxProvider.WriteDebugResponse`、`MiniMaxProvider.QueryRemainsAsync`
- **优先级理由**：敏感数据无限累积磁盘，存在信息泄露和磁盘占满风险

---

### F-20 [P1] BrowserLoginService 诊断日志写入 document.cookie

- **文件**：`src/UsageMonitor.Core/Services/BrowserLoginService.cs:299-313`
- **代码片段**：
```csharp
// 当 cookies.Count == 0 时的诊断逻辑
var cur = context.Pages[0];
diagInfo = await cur.EvaluateAsync<string>(
    "() => JSON.stringify({ url: window.location.href, title: document.title, " +
    "cookies: document.cookie.substring(0, 500) })"); // ← 读取前 500 字符 Cookie
// ...
var diagContent = "[BrowserLoginService] cookies.Count=0 diagnostic" + Environment.NewLine
    + "  page=" + diagInfo + Environment.NewLine  // ← diagInfo 含 document.cookie 内容
    + "  TempProfile=" + tempProfile + Environment.NewLine
    + "  LoginUrl=" + config.LoginUrl + Environment.NewLine
    + "  LoginUrlKeywords=" + string.Join(",", config.LoginUrlKeywords ?? new List<string>()) + Environment.NewLine
    + "  LoginSuccessHost=" + config.LoginSuccessHost;
File.WriteAllText(diagPath, diagContent); // ← 明文写入磁盘
```
- **问题**：诊断日志将 `document.cookie` 前 500 字符明文写入 `%AppData%/UsageMonitor/debug/login-diag-*.log`。Cookie 可能包含 `_token`（JWT 会话令牌）。该文件不加密、不清理，任何同用户进程可读取。
- **改进建议**：
```csharp
// 移除 document.cookie，仅记录非敏感诊断信息
diagInfo = await cur.EvaluateAsync<string>(
    "() => JSON.stringify({ url: window.location.href, title: document.title, " +
    "cookieNames: document.cookie.split(';').map(c => c.split('=')[0].trim()).join(',') })");
// 只记录 cookie 名称，不记录值
```
- **影响范围**：`BrowserLoginService.LoginAndExtractCookieAsync` 的 Cookie 提取失败诊断路径
- **优先级理由**：会话令牌明文落盘构成安全漏洞

---

### F-21 [P1] ConfigService.ReloadProviderConfigsFromDisk 未解密敏感字段（latent P0）

- **文件**：`src/UsageMonitor.Core/Services/ConfigService.cs:461-491`
- **代码片段**：
```csharp
public void ReloadProviderConfigsFromDisk()
{
    lock (_ioLock)
    {
        try
        {
            if (!File.Exists(_configFilePath)) return;
            var json = File.ReadAllText(_configFilePath, Encoding.UTF8);
            var fresh = JsonSerializer.Deserialize<AppSettings>(json, /* ... */);
            if (fresh?.ProviderConfigs != null)
            {
                _settings.ProviderConfigs = fresh.ProviderConfigs;
                // ← 缺少 DecryptSensitiveFields()！
                // fresh.ProviderConfigs 中的 ApiKey / Cookie 是加密的 base64 密文
            }
            changed = true;
        }
        // ...
    }
}
```
- **问题**：`Save()` 在写入前对敏感字段做 DPAPI 加密，所以磁盘上 `config.json` 中 ApiKey/Cookie 是密文。`ReloadProviderConfigsFromDisk` 从磁盘读取后直接赋值给内存 `_settings.ProviderConfigs`，**没有调用 `DecryptSensitiveFields()`**。若此方法被调用，后续所有 `GetValue("ApiKey")` / `GetValue("Cookie")` 返回的是 base64 密文而非明文，导致所有 Provider 查询失败。当前代码中此方法未被调用（latent bug），但作为 public API 一旦被调用即触发。
- **改进建议**：
```csharp
if (fresh?.ProviderConfigs != null)
{
    _settings.ProviderConfigs = fresh.ProviderConfigs;
    DecryptSensitiveFields(); // 补上解密步骤
    FileLogger.Info("ConfigService",
        $"Reloaded ProviderConfigs from disk. Count={fresh.ProviderConfigs.Count}");
}
```
- **影响范围**：`ConfigService.ReloadProviderConfigsFromDisk`（当前未被调用，但为 public API）
- **优先级理由**：latent P0 bug，一旦调用即导致全部认证失败

---

### F-22 [P1] AttachTaskbarWindowResizeHandlers 事件订阅泄漏

- **文件**：`src/UsageMonitor.App/App.xaml.cs:589-611`
- **代码片段**：
```csharp
private void AttachTaskbarWindowResizeHandlers()
{
    _viewModel.Usages.CollectionChanged += (_, _) => _taskbarWindow?.RecalculateSize();
    foreach (var usage in _viewModel.Usages)
    {
        usage.PropertyChanged += (_, ev) =>  // ← 每次重建都追加新订阅
        {
            if (ev.PropertyName == nameof(ProviderUsageViewModel.DisplayMode))
                _taskbarWindow?.RecalculateSize();
        };
    }
    _viewModel.TaskbarModeChanged += (_, _) => { /* ... */ };
}
```
- **问题**：`DisposeTaskbarWindow` 销毁窗口后，`InitializeTaskbarWindow` 重建时再次调用 `AttachTaskbarWindowResizeHandlers`，在已有订阅上叠加新订阅。Lambda 闭包捕获 `_taskbarWindow`，旧订阅持有的旧窗口引用不会被 GC 回收（虽然 `?.` 空检查保护了调用安全，但订阅数量无限增长）。每次重建后 `RecalculateSize` 被调用 N 次（N = 重建次数）。
- **改进建议**：
```csharp
// 保存订阅的 handler 以便取消
private PropertyChangedEventHandler? _taskbarUsageHandler;
private EventHandler? _taskbarCollectionHandler;
private EventHandler? _taskbarModeHandler;

private void AttachTaskbarWindowResizeHandlers()
{
    DetachTaskbarWindowResizeHandlers(); // 先取消旧订阅

    _taskbarCollectionHandler = (_, _) => _taskbarWindow?.RecalculateSize();
    _viewModel.Usages.CollectionChanged += _taskbarCollectionHandler;

    _taskbarUsageHandler = (_, ev) =>
    {
        if (ev.PropertyName == nameof(ProviderUsageViewModel.DisplayMode))
            _taskbarWindow?.RecalculateSize();
    };
    foreach (var usage in _viewModel.Usages)
        usage.PropertyChanged += _taskbarUsageHandler;

    _taskbarModeHandler = (_, _) => { /* ... */ };
    _viewModel.TaskbarModeChanged += _taskbarModeHandler;
}

private void DetachTaskbarWindowResizeHandlers()
{
    if (_taskbarCollectionHandler != null)
        _viewModel.Usages.CollectionChanged -= _taskbarCollectionHandler;
    if (_taskbarUsageHandler != null)
        foreach (var usage in _viewModel.Usages)
            usage.PropertyChanged -= _taskbarUsageHandler;
    if (_taskbarModeHandler != null)
        _viewModel.TaskbarModeChanged -= _taskbarModeHandler;
}
// 在 DisposeTaskbarWindow 中调用 DetachTaskbarWindowResizeHandlers()
```
- **影响范围**：`App.AttachTaskbarWindowResizeHandlers`、`App.DisposeTaskbarWindow`、`App.InitializeTaskbarWindow`
- **优先级理由**：事件订阅泄漏导致内存增长和重复回调

---

### F-23 [P1] UsageHistoryStore.AddErrorPoint 中 queue.Last() 性能问题

- **文件**：`src/UsageMonitor.Core/Services/UsageHistoryStore.cs:172-192`
- **代码片段**：
```csharp
public void AddErrorPoint(string providerId)
{
    // ...
    var queue = _histories.GetOrAdd(providerId, _ => new Queue<HistoryPoint>());
    lock (queue)
    {
        var last = queue.Count > 0 ? queue.Last().UsagePercent : 0; // ← O(n) LINQ 全遍历
        queue.Enqueue(new HistoryPoint { /* ... */ });
        // ...
    }
}
```
- **问题**：`Queue<T>.Last()` 是 LINQ 扩展方法，需要从头遍历整个队列到最后一个元素，时间复杂度 O(n)。`AddErrorPoint` 在每次刷新失败时调用（默认每 5 分钟），队列最多 60-120 个点，单次开销小但属于不必要的浪费。`Queue<T>` 不支持 O(1) 取尾部元素。
- **改进建议**：
```csharp
// 推荐方案：在 EnqueueMemoryPoint 中同步更新 _lastPercent 字段，
// AddErrorPoint 直接读字段
private readonly ConcurrentDictionary<string, (Queue<HistoryPoint> queue, double lastPercent)> _histories = new();

public void AddErrorPoint(string providerId)
{
    var entry = _histories.GetOrAdd(providerId, _ => (new Queue<HistoryPoint>(), 0));
    lock (entry.queue)
    {
        var lastPercent = entry.lastPercent; // O(1) 读取
        entry.queue.Enqueue(new HistoryPoint { /* ... */ });
    }
}
```
- **影响范围**：`UsageHistoryStore.AddErrorPoint`、`UsageHistoryStore.EnqueueMemoryPoint`（需同步更新 lastPercent）
- **优先级理由**：性能问题但影响小（n≤120），P1 下限

---

### F-24 [P1] LoginHelper 打印 Cookie 前缀到控制台

- **文件**：`src/LoginHelper/Program.cs:54`
- **代码片段**：
```csharp
Console.WriteLine($"  Cookie prefix: {cookie.Substring(0, Math.Min(60, cookie.Length))}...");
```
- **问题**：Cookie 前 60 字符包含 JWT token 的 header 部分（`eyJ...`），打印到控制台可能被重定向到日志文件或被其他进程读取。虽然 LoginHelper 是 CLI 工具，但在自动化脚本场景下 stdout 重定向常见。
- **改进建议**：
```csharp
Console.WriteLine($"  Cookie length: {cookie.Length} chars");
// 不打印 Cookie 任何部分；如需调试可只打印长度
```
- **影响范围**：`LoginHelper/Program.cs`
- **优先级理由**：安全信息泄露，但仅 CLI 工具场景

---

### F-25 [P1] ConfigService.IsSensitiveKey 匹配范围过宽

- **文件**：`src/UsageMonitor.Core/Services/ConfigService.cs:648-652`
- **代码片段**：
```csharp
private static bool IsSensitiveKey(string key)
{
    var sensitiveKeywords = new[] { "apikey", "token", "secret", "password", "key", "cookie" };
    return sensitiveKeywords.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase));
}
```
- **问题**：`"key"` 作为子串匹配关键词过于宽泛。任何含 "key" 的字段名（如 `Hotkey`、`PrimaryKey`、`Turnkey`、`Monkey`）都会被判定为敏感并尝试 DPAPI 加密/解密。当前项目字段未触发问题，但新增字段时存在隐患。若字段值碰巧是合法 base64，`Decrypt` 不会抛异常但会返回乱码。
- **改进建议**：
```csharp
private static bool IsSensitiveKey(string key)
{
    // 移除过宽的 "key"，改为精确匹配或使用 ConfigField.IsSensitive 标记
    var sensitiveKeywords = new[] { "apikey", "token", "secret", "password", "cookie" };
    return sensitiveKeywords.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase));
}
```
- **影响范围**：`ConfigService.EncryptSensitiveFields`、`ConfigService.DecryptSensitiveFields`
- **优先级理由**：潜在 bug，当前未触发但新增字段时会出问题

---

## 五、P2 详细改进项（机会性改进）

### F-26 [P2] TriggerAreaOverlayWindow.xaml 硬编码颜色未走 Token

- **文件**：`src/UsageMonitor.App/Views/TriggerAreaOverlayWindow.xaml:28-101`
- **代码片段**：
```xml
<Rectangle Fill="#80000000" />  <!-- line 28：半透明黑色遮罩，硬编码 -->
<Border Background="#201E90FF" BorderBrush="#FF1E90FF" />  <!-- line 36-37 -->
<Rectangle Cursor="SizeWE" Background="White" BorderBrush="#FF1E90FF" BorderThickness="1" />  <!-- 8 个 handle 重复 -->
```
- **问题**：项目有完整主题系统（`Tokens.xaml` + `Dark.xaml` + `Light.xaml`），所有颜色应通过 `DynamicResource` 引用。但此窗口硬编码了 `#80000000`、`#FF1E90FF`、`White` 等颜色，在浅色主题下遮罩和边框颜色不会适配。
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
- **优先级理由**：仅影响调试窗口，但破坏主题系统一致性

---

### F-27 [P2] async void 事件处理器 + fire-and-forget 缺 try/catch（4 处合并）

- **文件**：
  - `TaskbarWindow.xaml.cs:122,689`
  - `PluginConfigWindow.xaml.cs:513`
  - `HistoryWindow.xaml.cs:45`
  - `App.xaml.cs:321`（托盘右键菜单 async void lambda）
  - `RefreshService.cs:183-186`（OnTimerTick fire-and-forget）
  - `MainViewModel.cs:613`（HandlePeriodChanged fire-and-forget）
- **代码片段**：
```csharp
// TaskbarWindow.xaml.cs line 122
private async void OnRefreshAllClick(object sender, RoutedEventArgs e)
{
    await _refreshService.RefreshAllAsync();  // 异常会直接崩溃进程
}

// App.xaml.cs line 321 — 托盘右键菜单
contextMenu.Items.Add("立即刷新", null, async (_, _) => await _refreshService.RefreshAllAsync());

// RefreshService.cs line 183
private void OnTimerTick(object? state)
{
    _ = RefreshAllAsync("auto"); // fire-and-forget，异常不可观察
}
```
- **问题**：`async void` 事件处理器中抛出的异常无法被 `try/catch` 捕获（会直接成为未处理异常导致进程崩溃）。WPF 事件处理器确实需要 `async void` 签名，但应在方法体最外层包裹 `try/catch`。`OnGetCookieClick` 涉及浏览器登录流程（Playwright/Edge 启动、Cookie 提取等），异常概率较高。fire-and-forget 模式下异常成为 unobserved task exception。
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

// App.xaml.cs 托盘菜单
contextMenu.Items.Add("立即刷新", null, (_, _) =>
{
    _ = Task.Run(async () =>
    {
        try { await _refreshService.RefreshAllAsync(); }
        catch (Exception ex) { FileLogger.Error("App", $"Tray refresh failed: {ex.Message}", ex); }
    });
});

// RefreshService.OnTimerTick
private async void OnTimerTick(object? state)
{
    try { await RefreshAllAsync("auto"); }
    catch (Exception ex) { FileLogger.Error("RefreshService", $"OnTimerTick failed: {ex.Message}", ex); }
}
```
- **影响范围**：`TaskbarWindow.xaml.cs`（2 处）、`PluginConfigWindow.xaml.cs`（1 处）、`HistoryWindow.xaml.cs`（1 处）、`App.xaml.cs`（1 处）、`RefreshService.cs`（1 处）、`MainViewModel.cs`（1 处）
- **优先级理由**：事件处理器确实需要 async void，但缺少 try/catch 是可导致闪退的隐患

---

### F-28 [P2] I18nKeys.cs 用 const 硬编码中文字符串

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
- **问题**：这个类注释说它是"i18n 扩展点占位"，但实际用 `const string` 硬编码了中文字符串值。`const` 意味着这些值在编译时确定，调用方直接引用字面值（`I18nKeys.Range_Last7Days` 实际就是 `"最近 7 天"`），无法在运行时切换语言。注释自己都说"二期替换方案：把每个 const 替换为读取 .resx 资源"，但这个二期从未到来。
- **改进建议**：
```csharp
// 改为 I18n 键名常量，运行时通过 I18n.T() 解析
public static class I18nKeys
{
    public const string Range_Last7Days = "history.range.last7days";
    public const string Range_Last30Days = "history.range.last30days";
    // ...
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
- **优先级理由**：当前不影响功能，但 i18n 占位名不副实

---

### F-29 [P2] LoadedPlugin 是可变状态袋，public setter 暴露运行时状态

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
- **问题**：`LoadedPlugin` 混合了不可变元数据（Provider、Assembly、FilePath）和可变运行时状态（LastUsage、LastQueryTime、IsEnabled 等）。public setter 意味着任何代码都可随时修改这些字段，没有变更通知、没有线程安全保护。`RefreshService` 在刷新时直接写 `plugin.LastUsage = usage`（RefreshService.cs:128），而 `App.xaml.cs` 在启动时写 `plugin.IsEnabled = ...`（App.xaml.cs:97）。这种共享可变状态在多线程下（刷新线程 vs UI 线程）有竞态风险。
- **改进建议**：
```csharp
// 拆分为不可变元数据 + 可变状态（加锁或用 Interlocked）
public sealed class LoadedPlugin  // 元数据：构造后不变
{
    public IUsageProvider Provider { get; }
    public Assembly Assembly { get; }
    public string FilePath { get; }

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
- **影响范围**：`LoadedPlugin.cs`、`RefreshService.cs`（改为 `plugin.State.LastUsage =`）、`App.xaml.cs`、`MainViewModel.cs`
- **优先级理由**：当前单线程使用下不会立即出问题，但多线程刷新场景下是潜在竞态风险

---

### F-30 [P2] UsageHistoryRepository TryGet* 未处理 JsonElement 类型

- **文件**：`src/UsageMonitor.Core/Services/UsageHistoryRepository.cs:619-660`
- **代码片段**：
```csharp
private static double TryGetDouble(IReadOnlyDictionary<string, object> extras, string key)
{
    if (extras.TryGetValue(key, out var v) && v != null)
    {
        if (v is double d) return d;
        if (v is float f) return f;
        if (v is int i) return i;
        if (v is long l) return l;
        if (v is decimal m) return (double)m;
        // ← 缺少 JsonElement 分支！
        // JsonSerializer.Deserialize<Dictionary<string, object>> 产出的值类型是 JsonElement
        if (double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
    }
    return 0;
}
```
- **问题**：`ToUsageInfo`（line 686）用 `JsonSerializer.Deserialize<Dictionary<string, object>>` 反序列化 `extra_json`，产出值是 `JsonElement` 类型。`TryGetDouble`/`TryGetLong`/`TryGetString`/`TryGetBool` 均无 `JsonElement` 分支，靠 `v.ToString()` + `TryParse` 兜底。对数字 `JsonElement.ToString()` 返回原始数字字符串，`TryParse` 能成功，功能正确但路径迂回且脆弱。
- **改进建议**：
```csharp
private static double TryGetDouble(IReadOnlyDictionary<string, object> extras, string key)
{
    if (extras.TryGetValue(key, out var v) && v != null)
    {
        if (v is double d) return d;
        if (v is float f) return f;
        if (v is int i) return i;
        if (v is long l) return l;
        if (v is decimal m) return (double)m;
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
            return je.GetDouble();
        if (double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
    }
    return 0;
}
```
- **影响范围**：`UsageHistoryRepository.TryGetDouble`、`TryGetLong`、`TryGetString`、`TryGetBool`、`BuildBusinessFingerprint`、`ToUsageInfo`
- **优先级理由**：代码健壮性，当前功能正确但依赖隐式转换链

---

### F-31 [P2] DeleteProviderDataAsync 不必要 Task.Run 包裹

- **文件**：`src/UsageMonitor.Core/Services/UsageHistoryRepository.cs:870-911`
- **代码片段**：
```csharp
public async Task DeleteProviderDataAsync(string providerId)
{
    // ...
    await using var conn = OpenConnection();
    await using var cmd = conn.CreateCommand();
    var deleteAsync = Task.Run(async () =>  // ← 不必要的 Task.Run
    {
        await using var tx = conn.BeginTransaction();
        cmd.CommandText = "DELETE FROM usage_points WHERE provider_id = $id";
        // ...
        await tx.CommitAsync();
        return (n1, n2, n3);
    });
    var (n1Final, n2Final, n3Final) = await deleteAsync;
}
```
- **问题**：整个删除逻辑被包在 `Task.Run(async () => ...)` 中，但外部已经是 `async Task` 方法。`conn` 和 `cmd` 在 Task.Run 外创建、内使用，虽然 `await deleteAsync` 保证了生命周期，但增加了不必要的 ThreadPool 调度和闭包捕获。
- **改进建议**：
```csharp
public async Task DeleteProviderDataAsync(string providerId)
{
    if (string.IsNullOrWhiteSpace(providerId)) return;
    try
    {
        await using var conn = OpenConnection();
        await using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM usage_points WHERE provider_id = $id";
            cmd.Parameters.AddWithValue("$id", providerId);
            var n1 = await cmd.ExecuteNonQueryAsync();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM usage_daily WHERE provider_id = $id";
            cmd.Parameters.AddWithValue("$id", providerId);
            var n2 = await cmd.ExecuteNonQueryAsync();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM usage_refresh_aggregates WHERE provider_id = $id";
            cmd.Parameters.AddWithValue("$id", providerId);
            var n3 = await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
    catch (Exception ex) { /* ... */ }
}
```
- **影响范围**：`UsageHistoryRepository.DeleteProviderDataAsync`
- **优先级理由**：代码可读性和不必要的 ThreadPool 开销

---

### F-32 [P2] UsageHistoryRepository.Dispose() 空实现，IDisposable 误导

- **文件**：`src/UsageMonitor.Core/Services/UsageHistoryRepository.cs:1006-1010`
- **代码片段**：
```csharp
public void Dispose()
{
    // Microsoft.Data.Sqlite 在 using 后会自动释放连接；这里无需主动释放长连接对象
    GC.SuppressFinalize(this);
}
```
- **问题**：类实现了 `IDisposable` 但 `Dispose()` 不做任何资源释放。该类每个操作都创建新的 `SqliteConnection`（用 `using`/`await using` 释放），不持有长生命周期的 disposable 对象。`App.OnExit` 调用 `_historyRepository?.Dispose()` 期望释放资源，但实际什么都没做。实现 `IDisposable` 误导调用方认为有资源需要释放。
- **改进建议**：移除 `IDisposable` 实现，或在注释中明确说明这是 no-op 占位。如果未来引入连接池，再实现真正的 Dispose。
- **影响范围**：`UsageHistoryRepository`、`App.OnExit`
- **优先级理由**：代码清洁度

---

### F-33 [P2] InsertUsagePointIfChangedAsync catch { } 完全吞异常

- **文件**：`src/UsageMonitor.Core/Services/UsageHistoryRepository.cs:544`
- **代码片段**：
```csharp
catch (Exception ex)
{
    FileLogger.Warn("UsageHistoryRepository",
        $"InsertUsagePointIfChangedAsync({usage.ProviderId}) fingerprint compare failed, falling back to write", ex);
    try { UpsertPoint(usage); } catch { /* swallow to not break refresh */ } // ← 完全吞掉
    return true;
}
```
- **问题**：内层 `catch { }` 完全吞掉 `UpsertPoint` 的异常，无任何日志。如果 `UpsertPoint` 因数据库损坏等原因持续失败，开发者无法从日志中发现问题。虽然 `UpsertPoint` 内部有 try-catch + 日志，但如果异常发生在 try 之前（如 `usage == null` 检查之后的 `usage.ProviderId` 访问），则无日志。
- **改进建议**：
```csharp
try { UpsertPoint(usage); }
catch (Exception innerEx)
{
    FileLogger.Warn("UsageHistoryRepository",
        $"InsertUsagePointIfChangedAsync fallback UpsertPoint also failed", innerEx);
}
```
- **影响范围**：`UsageHistoryRepository.InsertUsagePointIfChangedAsync`
- **优先级理由**：诊断困难，静默丢失数据写入失败信息

---

### F-34 [P2] WindowsCredentialManagerStore 空字符串 secret 未校验

- **文件**：`src/UsageMonitor.Core/Services/Security/WindowsCredentialManagerStore.cs:85-123`
- **代码片段**：
```csharp
public void Set(string serviceName, string accountName, string secretData)
{
    ValidateArgs(serviceName, accountName);
    if (secretData == null) throw new ArgumentNullException(nameof(secretData));
    // ← 未校验 secretData == ""

    var blobBytes = Encoding.UTF8.GetBytes(secretData); // 长度 0
    var blobPtr = Marshal.AllocHGlobal(blobBytes.Length); // AllocHGlobal(0)
    // ...
    var credential = new CREDENTIAL
    {
        CredentialBlobSize = (uint)blobBytes.Length, // 0
        CredentialBlob = blobPtr, // 非零指针但指向 0 字节
    };
    if (!CredWrite(ref credential, 0)) { /* ... */ }
}
```
- **问题**：空字符串 secret 会导致 `CredentialBlobSize=0` 的凭据被写入。`CredWrite` 对 0 长度 blob 的行为未文档化，可能成功但后续 `CredRead` 返回空字符串而非 null，与"不存在"语义混淆。
- **改进建议**：
```csharp
if (string.IsNullOrEmpty(secretData))
    throw new ArgumentException("secretData 不能为空字符串", nameof(secretData));
```
- **影响范围**：`WindowsCredentialManagerStore.Set`
- **优先级理由**：边界条件健壮性

> 注：此模块在 F-04 中被识别为死代码。如选择保留 Security 模块（方案 B），则需修复此问题；如选择删除（方案 A），此问题自动消除。

---

### F-35 [P2] RefreshIntervalSeconds * 1000 潜在 int 溢出

- **文件**：`src/UsageMonitor.Core/Services/RefreshService.cs:63`
- **代码片段**：
```csharp
var intervalMs = _configService.Settings.RefreshIntervalSeconds * 1000;
_timer = new Timer(OnTimerTick, null, 0, intervalMs); // Timer 接收 int
```
- **问题**：`RefreshIntervalSeconds` 是 `int`，`* 1000` 结果也是 `int`。当 `RefreshIntervalSeconds > 2147483`（约 24.8 天）时溢出为负数，`Timer` 构造函数接收负 `period` 会抛 `ArgumentOutOfRangeException`。虽然 UI 可能限制了输入范围，但 `AppSettings` 无 `[Range]` 验证。
- **改进建议**：
```csharp
var intervalSec = Math.Max(1, _configService.Settings.RefreshIntervalSeconds);
var intervalMs = Math.Min(intervalSec * 1000, int.MaxValue / 2); // 安全上限
_timer = new Timer(OnTimerTick, null, 0, intervalMs);
```
- **影响范围**：`RefreshService.Start`、`AppSettings`（建议加 `[Range(1, 86400)]` 验证）
- **优先级理由**：边界条件，当前 UI 可能已限制

---

## 六、整体改进路线图（3-6 个月）

### 第 1 个月：消除 P0 安全与架构腐化

| 周 | 任务 | 涉及问题 | 责任模块 |
|----|------|---------|---------|
| W1 | ConfigService 并发安全改造（统一加锁或换 ConcurrentDictionary） | F-05 | Core/Services |
| W2 | Cookie 明文存储修复（DPAPI 加密落盘） | F-06 | Core/Services |
| W3 | GDI 句柄泄漏修复（DestroyIcon P/Invoke） | F-07 | App |
| W4 | 启用 DI 容器 + 决定 Security 模块去留 | F-01, F-04 | App + Core |

并行任务（可分配给不同开发者）：
- 拆分 MainViewModel.cs 为 5-6 个独立 VM 文件，提取 MiniMax 专属逻辑为 IProviderDataRenderer 策（F-02）
- 消除 Core 层 "MiniMax" 硬编码，在 IUsageProvider 增加数据管理能力声明（F-03）

### 第 2-3 个月：提升扩展性与可测试性

| 周 | 任务 | 涉及问题 |
|----|------|---------|
| W5-6 | IUsageProvider 接口拆分（ISP 原则） | F-08 |
| W7-8 | 核心服务提取接口（IConfigService / IRefreshService / IUsageHistoryStore） | F-10 |
| W9 | BrowserLoginService 实例化 + DI 注入 | F-11 |
| W10 | PluginManager 升级（AssemblyLoadContext + 按需加载） | F-09 |
| W11 | LoginHelper 跨进程协调（IPC 命名管道 或 FileSystemWatcher） | F-12 |
| W12 | 异常安全加固（RefreshService 竞态 + FileLogger 日志目录 + ReloadProviderConfigsFromDisk 解密 + 事件订阅泄漏） | F-17, F-18, F-21, F-22 |

并行任务：
- 安全加固：MiniMaxProvider debug dump 清理 + BrowserLoginService 诊断日志去 Cookie + LoginHelper Cookie 前缀打印 + IsSensitiveKey 匹配范围（F-19, F-20, F-24, F-25）
- 数据流改造：UsageHistoryStore.AddPoint 直写百分比 + AddErrorPoint 性能优化（F-16, F-23）
- UI 改造：ResolveIconPath 约定优于配置 + PluginConfigWindow MVVM 化（F-14, F-15）

### 第 4-6 个月：UI/UX 与代码质量

| 月 | 任务 | 涉及问题 |
|----|------|---------|
| M4 | 国际化基础数据补全（XAML 改用 I18n 绑定 + I18nKeys 改为键名） | F-13, F-28 |
| M5 | 主题一致性（TriggerAreaOverlayWindow 走 Token）+ async void 安全网（4 处加 try/catch） | F-26, F-27 |
| M6 | LoadedPlugin 状态隔离 + Repository 代码质量（JsonElement / Task.Run / Dispose / catch{}）+ 边界条件（空字符串 / int 溢出） | F-29, F-30, F-31, F-32, F-33, F-34, F-35 |

### 持续任务（贯穿全程）
- 引入单元测试覆盖：ConfigService 并发场景、Repository 指纹比对、RefreshService 竞态、BrowserLoginService（mock 后）
- 为所有 public API 补充 XML 文档注释
- 建立代码审查 checklist：新增 Provider 不准硬编码到 Core/App 层、新加字段需检查 IsSensitiveKey 兼容性、新加 XAML 必须用 I18n 键

---

## 七、附录

### A. 健康指标

审查中观察到以下良好实践，建议保持：
- 项目分层清晰（Core/App/Plugins）
- 没有 `GC.Collect` 滥用
- 没有 `Thread.Sleep` 滥用
- 没有 `Dispatcher.Invoke` 同步阻塞滥用
- 没有 `TODO`/`FIXME`/`HACK` 标记污染代码
- 异常处理与日志覆盖面较广（除了少数 `catch { }` 吞异常）
- 主题系统有完整 Token + Dark/Light 切换基础设施
- 插件接口设计有前瞻性（即使当前实现未完全到位）

### B. 改进优先级矩阵

```
紧急程度 →
↑                P0(立即)          P1(本季度)        P2(机会性)
影响  高       F-05/F-06/F-07    F-17/F-21/F-22    F-33/F-34
范围  中       F-02/F-03         F-12/F-15/F-16    F-29/F-30
      低       F-01/F-04         F-08/F-09/F-10    F-26/F-28
                                   F-11/F-13/F-14
                                   F-18/F-19/F-20
                                   F-23/F-24/F-25
```

### C. 改进工作量预估（人天）

| 优先级 | 问题数 | 估总人天 | 备注 |
|--------|--------|---------|------|
| P0 | 7 | 18-25 | F-02 拆分 God VM 最重（~8 人天），其余各 1-3 人天 |
| P1 | 17 | 30-45 | F-08/F-09/F-12/F-15 各 3-5 人天，其余 1-2 人天 |
| P2 | 11 | 8-12 | 大多 <1 人天，F-27/F-29 各 1-2 人天 |
| **总计** | **35** | **56-82** | 约 3 个月（2 人并行） |

### D. 配套子报告

- 架构师高见远独立报告：`docs/architecture-review.md`（含 18 个架构/UI 问题）
- 工程师寇豆码代码 bug/质量报告：本汇编报告已合并其全部 22 个发现（去重后 35 个独立问题）

### E. 开发需求映射表（req-id）

本次审查发现的 35 个问题中，**15 个为新发现、未被现有 req-053~req-067 覆盖**的问题，已录入项目开发需求清单（`.dev_require/.dev_require_list.md`），req-id 为 req-068 ~ req-070。其余 20 个与现有需求重叠，由开发团队在实现现有 req 时参考本报告对应章节即可。

| 本次编号 | 优先级 | 录入状态 | 对应 req-id（新录入或现有） |
|---------|--------|----------|----------------------------|
| F-01 | P0 | 与现有需求重叠 | req-066 子「引入DI容器(P2)」 |
| F-02 | P0 | 与现有需求重叠 | req-060 子「拆分MainViewModel.cs中ProviderUsageViewModel(P2)」 |
| F-03 | P0 | 与现有需求重叠 | req-066 子「AppSettings移除minimax业务耦合(P2)」 |
| F-04 | P0 | 与现有需求重叠 | req-061 子「Security基础设施接入ConfigService或标注未启用(P2)」 |
| F-05 | P0 | 与现有需求重叠 | req-057 子「ProviderConfig.Values改ConcurrentDictionary(P1)」等 |
| F-06 | P0 | 与现有需求重叠 | req-053 子「Cookie明文存储加密(P0)」 |
| **F-07** | P0 | ✅ **新录入** | **req-068 子1** |
| **F-08** | P1 | ✅ **新录入** | **req-069 子1** |
| F-09 | P1 | 与现有需求重叠 | req-054 子「移除或禁用LoadPlugins外部DLL扫描(P0)」等 |
| **F-10** | P1 | ✅ **新录入** | **req-069 子2** |
| F-11 | P1 | 与现有需求重叠 | req-065 子「BrowserLoginService去静态化(P1)」 |
| F-12 | P1 | 与现有需求重叠 | req-057 子「LoginHelper与主程序跨进程Mutex互斥(P1)」 |
| **F-13** | P1 | ✅ **新录入** | **req-069 子3** |
| **F-14** | P1 | ✅ **新录入** | **req-069 子4** |
| **F-15** | P1 | ✅ **新录入** | **req-069 子5** |
| **F-16** | P1 | ✅ **新录入** | **req-069 子6** |
| F-17 | P1 | 与现有需求重叠 | req-057 子「_isRefreshing改Interlocked.CompareExchange(P0)」 |
| F-18 | P1 | 与现有需求重叠 | req-060 子「FileLogger日志目录改AppData(P1)」 |
| F-19 | P1 | 与现有需求重叠 | req-053 子「debug目录自动清理(P2)」 |
| F-20 | P1 | 与现有需求重叠 | req-053 子「日志Cookie名称脱敏(P2)」 |
| **F-21** | P1（latent P0） | ✅ **新录入** | **req-068 子2** |
| F-22 | P1 | 与现有需求重叠（部分） | req-063 子「DispatcherTimer一次性订阅优化(P1)」等 |
| **F-23** | P1 | ✅ **新录入** | **req-069 子7** |
| F-24 | P1 | 与现有需求重叠 | req-053 子「移除LoginHelper控制台Cookie输出(P1)」 |
| F-25 | P1 | 与现有需求重叠 | req-053 子「IsSensitiveKey关键词收窄(P1)」 |
| **F-26** | P2 | ✅ **新录入** | **req-070 子1** |
| F-27 | P2 | 与现有需求重叠（部分） | req-058 子「fire-and-forget Task加faulted回调(P1)」 |
| **F-28** | P2 | ✅ **新录入** | **req-070 子2** |
| F-29 | P2 | 与现有需求重叠（部分） | req-066 子「LoadedPlugin状态字段加volatile(P1)」 |
| **F-30** | P2 | ✅ **新录入** | **req-070 子3** |
| **F-31** | P2 | ✅ **新录入** | **req-070 子4** |
| **F-32** | P2 | ✅ **新录入** | **req-070 子5** |
| F-33 | P2 | 与现有需求重叠 | req-060 子「catch静默吞异常补FileLogger.Debug(P2)」 |
| **F-34** | P2 | ✅ **新录入** | **req-070 子6** |
| F-35 | P2 | 与现有需求重叠 | req-060 子「RefreshIntervalSeconds加上限钳制(P2)」 |

**统计**：35 个问题中 15 个新录入（req-068 ~ req-070），20 个与现有需求重叠。

#### 新录入需求清单（req-068 ~ req-070）

```
req-068  v0.14.0架构审查-P0资源泄漏与latent Bug修复（父需求，P0）
├── req-068 子1：GDI句柄泄漏修复-LoadTrayIconFromLogo补DestroyIcon(P0)            ← F-07
└── req-068 子2：ReloadProviderConfigsFromDisk补DecryptSensitiveFields(P0 latent) ← F-21

req-069  v0.14.0架构审查-架构扩展性与MVVM改进（父需求，P1）
├── req-069 子1：IUsageProvider接口拆分-核心契约+可选能力接口(ISP)(P1)              ← F-08
├── req-069 子2：核心服务提取接口-IConfigService/IRefreshService/IUsageHistoryStore(P1) ← F-10
├── req-069 子3：XAML硬编码中文改用DynamicResource I18n绑定(P1)                   ← F-13
├── req-069 子4：ResolveIconPath改约定优于配置-移除12个Provider硬编码switch(P1)    ← F-14
├── req-069 子5：PluginConfigWindow MVVM化-提取PluginConfigViewModel精简722行code-behind(P1) ← F-15
├── req-069 子6：UsageHistoryStore.AddPoint直写百分比-消除dummy UsageInfo hack(P1)  ← F-16
└── req-069 子7：AddErrorPoint优化-queue.Last() O(n)改O(1)缓存lastPercent(P1)      ← F-23

req-070  v0.14.0架构审查-代码质量与健壮性补强（父需求，P2）
├── req-070 子1：TriggerAreaOverlayWindow硬编码颜色改走主题Token(P2)              ← F-26
├── req-070 子2：I18nKeys改键名常量-移除const硬编码中文(P2)                       ← F-28
├── req-070 子3：TryGet*辅助方法补JsonElement分支(P2)                            ← F-30
├── req-070 子4：DeleteProviderDataAsync移除不必要Task.Run包裹(P2)                ← F-31
├── req-070 子5：UsageHistoryRepository.Dispose清理-移除空实现或加注释说明(P2)     ← F-32
└── req-070 子6：WindowsCredentialManagerStore.Set空字符串secret校验(P2)          ← F-34
```

需求详情见项目 `.dev_require/req-06[8-9]-*/` 与 `.dev_require/req-070-*/` 目录。

---

**报告完**

> 本报告基于 2026-07-19 当时的代码状态生成。建议开发团队在排期前先复核各问题的当前状态（部分可能在审查后已被修复）。任何问题需要澄清可联系审查团队。
>
> **开发需求录入状态**：15 个新发现问题已录入项目开发需求清单（req-068 ~ req-070），共 3 个父需求 + 15 个子需求；20 个与现有 req-053~req-067 重叠的问题未重复录入。
