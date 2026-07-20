# UsageMonitor · Cookie 与登录态安全加固设计

> **作者**：Fenrir（Coder Agent）
> **日期**：2026-07-20
> **关联需求**：req-089（ACL 收紧单独一条）、req-090（其他 3 项 + LoginHelper 泛化合并一条）
> **状态**：方案文档，待王晨拍板各项优先级后拆分为具体需求子任务

---

## 0. 背景与现状

### 0.1 项目身份认证方式

UsageMonitor 通过**启动临时 Edge 浏览器引导用户登录**，**持久化 Cookie 保存登录态**，再以 Cookie 调 Provider 后台 API 拉取用量。

```text
┌─────────────┐  启动临时 Edge   ┌──────────────────┐
│ UsageMonitor├─────────────────►  provider.minimaxi │
│ BrowserLogin│  用户手动扫码登录  │  .com (登录页)    │
│  Service    │                  └────────┬──────────┘
└──────┬──────┘                           │ 登录完成
       │  Playwright 提取 Cookie          ▼
       │  ◄──────────────────────────────┘
       │
       │  DPAPI Encrypt (CurrentUser scope)
       ▼
┌──────────────────────────────┐
│ %AppData%\UsageMonitor\      │
│   config.json (Cookie 字段)  │
│   cookies\<Provider>.json     │
└──────────────────────────────┘
       │
       │  下次启动 → 读取 Cookie → 调 Provider API
       ▼
```

### 0.2 已有安全加固（基线）

| 项 | 实现 | 代码位置 |
|---|---|---|
| DPAPI 加密（cookies/*.json） | `DataProtectionScope.CurrentUser` | `BrowserLoginService.Encrypt/Decrypt` |
| DPAPI 加密（config.json 敏感字段） | 关键词匹配 `cookie/apikey/token/secret/password` | `ConfigService.EncryptSensitiveFields` |
| Cookie header 注入防护 | `CookieHeaderSanitizer.Sanitize`（清理 CR/LF） | req-065 B6 |
| 跨进程 Mutex 互斥 | `Global\\UsageMonitor-ConfigService` | req-057 |
| 临时 Edge profile 清理 | `BrowserLoginService` finally 块 | — |
| HttpClient 复用 + 15s 超时 | `_sharedHttp` 静态实例 | req-060 |

### 0.3 残留风险

| 威胁 | 风险等级 | 说明 |
|---|---|---|
| **同用户其他进程读 Cookie** | **中** | DPAPI CurrentUser scope 允许同用户任意进程解密 |
| 磁盘物理访问（已登录） | 低 | 需要已登录 Windows 账户 |
| 跨设备复制 | 极低 | DPAPI 不跨设备 |
| Cookie 文件被替换/篡改 | 中 | 未签名，未校验完整性 |
| 长期未用 Cookie 累积 | 中 | 无过期清理机制 |
| 用户不知 Cookie 被谁读取 | 中 | 无读取审计日志 |
| LoginHelper 写死 MiniMaxProvider | 中 | DeepSeek/Kimi/Qoder 网页模式无法复用 |

**核心结论**：DPAPI 是基础盘，但仅靠它无法应对恶意软件/同用户其他进程读取，且缺乏审计与完整性保护。

---

## 1. 安全加固方案（4 项 + 1 项延伸）

### 1.1 ACL 目录权限收紧（P0 · 单独立项 req-089）

**目标**：阻断同用户其他进程读取 Cookie 文件。

**方案**：

```csharp
// CookieDir = %AppData%\UsageMonitor\cookies\
// 在 BrowserLoginService 首次启动时执行一次：

var dirInfo = new DirectoryInfo(CookieDir);
var security = dirInfo.GetAccessControl();
security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
security.AddAccessRule(new FileSystemAccessRule(
    Environment.UserName,
    FileSystemRights.FullControl,
    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
    PropagationFlags.None,
    AccessControlType.Allow));
dirInfo.SetAccessControl(security);
```

**关键点**：
- `SetAccessRuleProtection(true, false)` —— 阻断继承，去掉 Users 组默认 ACL
- 只允许当前 Windows 用户账号完全控制
- 同样的规则应用到 config.json 所在目录

**威胁缓解**：
- ✅ 阻断同用户其他进程的意外读取
- ✅ 阻断非管理员进程的文件访问
- ❌ **无法**阻断能注入到 UsageMonitor 进程内的恶意代码（DPAPI 仍可在进程内解密）

**实现成本**：~30 行 C#

**验收**：
- [ ] `%AppData%\UsageMonitor\cookies\` 的 ACL 去除 Users 组继承
- [ ] 仅当前用户 SID 有 FullControl
- [ ] config.json 同等保护
- [ ] 跨用户访问测试：切换到另一 Windows 用户无法读取 Cookie 文件

---

### 1.2 Cookie 完整性签名（HMAC-SHA256）（P1 · 入 req-090）

**目标**：防止 Cookie 文件被替换/篡改。

**方案**：

```csharp
private static byte[] ComputeHmac(byte[] data)
{
    // 派生密钥：DPAPI 加密的随机 32 字节密钥，首次启动生成并存到 %AppData%\UsageMonitor\secret.key
    var key = LoadOrCreateSigningKey();  // DPAPI 加密存储
    using var hmac = new HMACSHA256(key);
    return hmac.ComputeHash(data);
}

public static void SaveCookieData(BrowserCookieData data)
{
    var json = JsonSerializer.Serialize(data, s_writeOptions);
    var encrypted = Encrypt(json);  // DPAPI
    var hmac = ComputeHmac(encrypted);  // HMAC

    // 文件格式：magic(4) + version(1) + hmac(32) + ciphertext
    using var fs = File.Create(path);
    fs.Write(MAGIC);           // "UMCK" (UsageMonitor Cookie)
    fs.WriteByte(VERSION);     // 0x01
    fs.Write(hmac);            // 32 bytes
    fs.Write(encrypted);       // DPAPI ciphertext
}

public static BrowserCookieData? LoadCookieData(string providerId)
{
    // 1) 读取并校验 magic/version
    // 2) 校验 HMAC（防止篡改）
    // 3) HMAC 通过后 DPAPI 解密
    // 4) HMAC 失败 → 视为篡改，删除文件并提示用户重新登录
}
```

**威胁缓解**：
- ✅ 防止攻击者替换 Cookie 文件（即使绕过 ACL 也无法构造有效 HMAC）
- ✅ 检测备份恢复场景下的 Cookie 失效
- ❌ **无法**防御能读取密钥文件的进程（密钥也走 DPAPI）

**实现成本**：~50 行 C#

**验收**：
- [ ] 手动修改 Cookie 文件 1 字节 → 加载失败并提示"文件被篡改，请重新登录"
- [ ] 首次启动生成 `secret.key`（DPAPI 加密）
- [ ] 跨设备复制 Cookie 文件 → 加载失败（密钥在不同设备）

---

### 1.3 Cookie 读取审计日志（P1 · 入 req-090）

**目标**：用户能查看 Cookie 最近被哪些时刻/哪些操作使用。

**方案**：

```csharp
// 在 CookieData 加载时记录：
FileLogger.Info("CookieAudit", $"ProviderId={id} Action=Load Time={now} UserAction=AutoRefresh");

// 同时写入专用审计文件 %AppData%\UsageMonitor\audit\cookie-audit.log（最近 100 条）
```

**设置页面新增「Cookie 审计」面板**：
- 表格展示最近 100 条 Cookie 使用记录
- 字段：时间、Provider、动作（Load/Validate/Login/Refresh）、触发源（Auto/Manual/Startup）
- 导出按钮（CSV）

**威胁缓解**：
- ✅ 用户能感知异常读取
- ✅ 排查"我的 Cookie 是不是被偷了"
- ❌ 审计日志本身也可被攻击者清除 → 缓解：写入 Windows Event Log（需管理员）

**实现成本**：~80 行 C#（含 UI 一页）

**验收**：
- [ ] 每次 Cookie Load/Validate 都记录审计日志
- [ ] 设置页面能看到表格
- [ ] 导出 CSV 可用

---

### 1.4 Cookie 过期清理（P1 · 入 req-090）

**目标**：自动删除长期未用的 Cookie，减少攻击面。

**方案**：

```csharp
// ConfigService 新增配置：
public int CookieRetentionDays { get; set; } = 90;  // 默认 90 天

// 启动时扫描：
public void CleanupExpiredCookies()
{
    var threshold = DateTime.UtcNow.AddDays(-CookieRetentionDays);
    foreach (var file in Directory.GetFiles(CookieDir, "*.json"))
    {
        if (File.GetLastWriteTimeUtc(file) < threshold)
        {
            var providerId = Path.GetFileNameWithoutExtension(file);
            FileLogger.Info("CookieCleanup", $"Deleting expired cookie: {providerId}");
            File.Delete(file);
        }
    }
}
```

**可配置项**：
- `CookieRetentionDays`：默认 90，范围 7-365
- 用户可在设置页面修改
- 删除前 7 天内的 Cookie 不删（保护刚登录的）

**威胁缓解**：
- ✅ 减少被遗忘 Provider Cookie 累积的攻击面
- ✅ 用户卸载后清理残留

**实现成本**：~30 行 C#（含配置项 + 设置 UI）

**验收**：
- [ ] 启动时自动扫描
- [ ] 设置页面可配置保留天数
- [ ] 删除前 7 天内的 Cookie 保留
- [ ] 删除日志可追溯

---

### 1.5 延伸：Master Password 二次加密（暂列 P2，不入 req-090）

**目标**：在 DPAPI 之上叠加 Master Password 派生密钥。

**方案**：用户首次设置 Master Password → PBKDF2 派生 Key → 用 Key + DPAPI 双重加密 Cookie 文件。

**取舍**：用户体验损失大（每次启动需输入密码），仅适合高敏感场景。**当前 UsageMonitor 场景不需要**，列为 P2 储备方案。

---

## 2. LoginHelper 泛化方案

### 2.1 现状

```csharp
// src/LoginHelper/Program.cs
var loginService = new BrowserLoginService();
var data = await loginService.LoginAndExtractCookieAsync(
    new MiniMaxProvider().LoginConfig,  // ← 写死
    cts.Token);
```

问题：仅 MiniMax 能用，DeepSeek/Kimi/Qoder 网页模式无法复用。

### 2.2 A 方案 vs C 方案

| 维度 | A 方案（CLI 参数化） | C 方案（SDK + UI 集成） |
|---|---|---|
| **核心改动** | LoginHelper.exe 加 `--provider X` 参数 | LoginHelper 重构为 `LoginHelper.Core` 库 + CLI 前端 |
| **用户体验** | 仍需切换窗口跑 CLI | **主程序内直接登录**，无需切窗口 |
| **改动量** | ~30 行（参数解析 + PluginManager 调用） | ~200 行 + Settings UI 改造 |
| **依赖** | PluginManager（已存在） | 需先完成 req-086 插件 SDK 稳定 |
| **可分阶段做** | ✅ 单独可用 | ❌ 必须完整完成才有用 |
| **推荐场景** | 快速解决当前复用问题 | 最终形态 |

**A 方案伪代码**：

```csharp
// Program.cs
if (args[0] == "--list")
{
    foreach (var p in PluginManager.GetAllProviders())
    {
        if (p.LoginConfig != null)
            Console.WriteLine($"{p.Id}\t{p.DisplayName}");
    }
    return;
}

var providerId = args[1];  // "--provider MiniMax"
var provider = PluginManager.GetProvider(providerId);
if (provider?.LoginConfig == null)
{
    Console.WriteLine($"X Provider {providerId} 不支持网页登录");
    return 1;
}

var loginService = new BrowserLoginService();
var data = await loginService.LoginAndExtractCookieAsync(provider.LoginConfig, cts.Token);
```

**C 方案伪代码**：

```csharp
// 新建 src/LoginHelper.Core/ILoginService.cs
public interface ILoginService
{
    Task<BrowserCookieData?> LoginAsync(string providerId, CancellationToken ct);
}

// LoginHelper.exe 改为薄壳，调用 ILoginService
// MainWindow 右键菜单新增「登录 X」直接调用 ILoginService
// SettingsWindow 加 Cookie 管理面板
```

### 2.3 推荐演进路径

```
A 方案（2 周）  →  C 方案（4 周，需先完成 req-086）
    ↑               ↑
    └─快速解决      └─理想形态
       当前痛点
```

**当前建议**：先 A 方案解决 LoginHelper 复用问题，C 方案放到 req-086 插件 SDK 二期后再做。

---

## 3. 拆分后的需求建议

### req-089 · Cookie 文件 ACL 收紧（P0）

**子任务**：
- B1 CookieDir 目录 ACL 移除 Users 组继承
- B2 config.json 所在目录同等处理
- B3 跨 Windows 用户访问测试用例
- B4 设置页面提示「已启用目录权限收紧」

### req-090 · 登录态安全加固 + LoginHelper 泛化（P1）

**安全子任务**（合并）：
- B1 Cookie HMAC-SHA256 完整性签名
- B2 Cookie 读取审计日志 + 设置面板
- B3 Cookie 过期清理（90 天可配置）
- B4 secret.key DPAPI 加密存储

**LoginHelper 泛化子任务**：
- B5 LoginHelper CLI 参数化（A 方案：`--provider X` + `--list`）
- B6 DeepSeekProvider / KimiProvider 接入 LoginHelper 测试
- B7 SettingsWindow 加「重新登录」按钮（可选，如做则进入 C 方案）

---

## 4. 优先级建议（待王晨拍板）

| 需求 | 优先级 | 工作量 | 建议 |
|---|---|---|---|
| req-089 ACL 收紧 | **P0** | ~30 行 | ✅ 立即做 |
| req-090 安全 3 项 + LoginHelper A 方案 | **P1** | ~150 行 | ✅ 紧随 req-087/088 后做 |

---

**变更记录**：
- 2026-07-20 v0.1：初稿（Fenrir 起草，待王晨拍板各项子任务优先级）