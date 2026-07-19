# UsageMonitor 上线前检查报告

**日期**：2026-07-19
**场景**：上线前检查（代码审查 + 安全审计 + QA 测试）
**参与成员**：产品官（gstack-product-reviewer）+ 安全卫士（gstack-security-officer）+ 质量门神（gstack-qa-lead）
**项目路径**：`D:\应用开发\UsageMonitor`
**项目画像**：Windows 任务栏 AI 用量监控工具 / C# 12 / .NET 8 / WPF / COM Shell DeskBand / Playwright / 插件式架构（Deepseek+MiMo+OpenAI+MiniMax）

---

## 📌 TL;DR（执行摘要）

- **整体结论**：🔴 **No-Go**（必须先修复 5 项 P0 阻塞项后方可上线）
- **三位成员结论分歧**：产品官判"条件 Go"、安全卫士判"有条件通过"、QA 判"不可上线"——主理人采信最严判断，因 QA 与安全共同指出的 Cookie 明文存储 + 插件无签名验证为硬性阻塞
- **阻塞项数量**：5 项 P0
- **下一步**：按行动清单完成 P0 → 再走一轮回归 → 进入内测（3-5 台机器 7 天）→ 灰度

---

## 🎯 核心结论卡片

| 项目 | 内容 |
|------|------|
| Go / No-Go | 🔴 **No-Go**（修复 P0 后可重新评估） |
| 严重度分布 | 🔴 5 / 🟠 10 / 🟡 10 / 🟢 9 |
| 综合风险评分 | 7/10（偏高） |
| 关键行动项 | 5 项 P0 + 10 项 P1 + 9 项 P2 |
| 建议负责人 | 工程负责人 + 安全负责人联合跟进 |
| 三方互相印证点 | ① 插件反射加载无安全校验 ② `_isRefreshing` 非线程安全 ③ Cookie 明文存储 ④ MiniMax DOM 每次启动 Edge ⑤ Playwright TLS 校验关闭 |

---

## 🚫 阻塞项清单（P0，必须修复方可上线）

| # | 问题 | 位置 | 来源 |
|---|------|------|------|
| 1 | **Cookie 明文存储** — `%AppData%/UsageMonitor/cookies/MiniMax.json` 写入完整 Cookie（含 `_token` JWT）的明文 JSON，且 MiniMaxProvider 主动回读作为自愈逻辑 | `src/UsageMonitor.Core/Services/BrowserLoginService.cs:510-523` | 安全🔴 + QA P0 |
| 2 | **插件反射加载无安全校验** — `Assembly.LoadFrom` 扫描 plugins/ 目录所有 DLL 并 `Activator.CreateInstance`，无签名/哈希/白名单，恶意 DLL 可获得主进程完整权限窃取所有 API Key | `src/UsageMonitor.Core/Plugins/PluginManager.cs:73` | 产品🔴 + 安全🟡 + QA P0 |
| 3 | **`_isRefreshing` 非线程安全** — 普通 `bool` 无 `volatile`/锁，Timer 回调与手动刷新可能同时进入导致重复 HTTP 请求 | `src/UsageMonitor.Core/Services/RefreshService.cs:82` | 产品🟠 + QA P0 |
| 4 | **RefreshAllAsync 无 CancellationToken** — `Task.WhenAll` 并行刷新无超时/取消，MiniMax Playwright 挂起会永久阻塞后续刷新 | `src/UsageMonitor.Core/Services/RefreshService.cs` | QA P0 |
| 5 | **零测试 + 零 CI** — 解决方案 7 个项目无任何 `*.Tests` 项目、无 GitHub Actions / Azure Pipelines、无覆盖率工具，回归只能靠人肉 | 解决方案级 | QA P0 |

---

## 1. 各成员核心结论

### 🔍 产品官（代码审查）
- **核心判断**：条件 Go，代码质量评分 7/10
- **关键发现**：1 Blocker（插件反射加载）+ 5 Critical（并发/资源管理：`_isRefreshing`、ProviderConfig 字典、fire-and-forget、Settings 暴露引用、CloneSettings 锁内序列化、MiniMax DOM 重启 Edge）
- **亮点**：HttpClient 正确复用、配置原子写入 + `.bak` 备份、per-provider 锁、FileLogger 后台队列、注释质量极高、nullable 启用、CommunityToolkit.Mvvm 规范、错误兜底意识强
- **关键建议**：优先修插件信任边界 + 三处并发问题；MiniMax 浏览器实例需复用；`FileLogger` 日志目录应改用 `%AppData%/UsageMonitor/logs/`

### 🛡️ 安全卫士（OWASP + STRIDE 审计）
- **核心判断**：🟡 有条件通过，风险评分 6/10
- **关键发现**：1 Critical（Cookie 明文存储）+ 2 High（Playwright TLS 校验关闭、LoginHelper 打印 Cookie 前缀）+ 3 Medium（插件无签名、Cookie 名称入日志、debug 文件无清理）
- **亮点**：DPAPI 加密（CurrentUser scope）、AES-256-GCM 降级方案设计正确（CSPRNG nonce + tag + 内存清零）、Windows Credential Manager 集成、参数化 SQL、无硬编码密钥、Git 历史干净、解密失败明确"绝不记明文"
- **关键建议**：Cookie 加密 / Playwright 恢复 TLS 校验 / 移除控制台 Cookie 输出 / 插件加签名

### ✅ 质量门神（QA 测试与就绪度）
- **核心判断**：🔴 不可上线（零测试 / 零 CI / 零自动化门禁）
- **关键发现**：5 P0（零测试、插件无验证、刷新无 CancellationToken、Cookie 明文、`_isRefreshing` 非线程安全）+ 6 P1
- **亮点**：代码本身防御性编程到位（原子写入、DPAPI、双后端凭据存储、per-provider 锁），但缺乏质量基础设施是最大阻碍
- **关键建议**：先建 `UsageMonitor.Core.Tests` xUnit 项目覆盖 ConfigService/PluginManager/RefreshService；配 GitHub Actions；Windows 10/11 双环境手测任务栏嵌入；24h 长稳测试

> 未上场成员：设计师、排障手（本次场景不涉及）

---

## 2. 综合审查发现（去重合并后按严重度排序）

### 🔴 Blocker / Critical（5 项，阻塞上线）

| # | 类别 | 位置 | 问题描述 | 建议 | 来源 |
|---|------|------|---------|------|------|
| 1 | 安全 | `BrowserLoginService.cs:510-523` | Cookie 明文存储于 `cookies/MiniMax.json`，含 `_token` JWT，主动回读作为自愈逻辑 | 对 cookies 文件用 DPAPI 加密，或废弃并行存储统一走 ConfigService 加密路径 | 安全🔴 + QA P0 |
| 2 | 安全/可维护 | `PluginManager.cs:73` | `Assembly.LoadFrom` 加载 plugins/ 目录所有 DLL，无签名/哈希/白名单 | 内置插件已通过 `RegisterBuiltinPlugins` 注册，考虑移除 `LoadPlugins()` 调用；若需外部插件，加 Authenticode 或 SHA-256 白名单 | 产品🔴 + 安全🟡 + QA P0 |
| 3 | 并发 | `RefreshService.cs:82` | `_isRefreshing` 普通 `bool`，Timer 回调与手动刷新可能同时进入 | 改为 `Interlocked.CompareExchange` 或 `SemaphoreSlim(1,1)` | 产品🟠 + QA P0 |
| 4 | 可靠性 | `RefreshService.cs` | `RefreshAllAsync` 通过 `Task.WhenAll` 并行无超时/取消，MiniMax Playwright 挂起会永久阻塞 | 为每个 Provider 的 `GetUsageAsync` 加 `CancellationToken` + 超时包装（如 60s 整体超时） | QA P0 |
| 5 | 质量 | 解决方案级 | 零测试项目、零 CI、零覆盖率工具 | 创建 `UsageMonitor.Core.Tests` xUnit 项目 + GitHub Actions CI，目标 Core 覆盖率 ≥ 60% | QA P0 |

### 🟠 High / Critical（10 项，上线前修复）

| # | 类别 | 位置 | 问题描述 | 建议 | 来源 |
|---|------|------|---------|------|------|
| 6 | 安全 | `BrowserLoginService.cs:131` + `MiniMaxDomExtractor.cs:72` | Playwright `IgnoreHTTPSErrors=true` 关闭 TLS 证书校验，MITM 可窃取登录凭据 | 改为 `false`；若有自签名证书需求，精确 pin CA | 安全🟠 + QA P1 |
| 7 | 安全 | `LoginHelper/Program.cs:54` | `Console.WriteLine` 打印 Cookie 前 60 字符明文 | 删除该行，或仅输出长度和条数 | 安全🟠 |
| 8 | 并发 | `ProviderConfig.cs:17` | `Values` 为 `Dictionary<string,string>`，被 ThreadPool 读取 + UI 线程写入，MiniMaxProvider 从 ThreadPool 直接 `SetValue` 修改 | 改为 `ConcurrentDictionary` 或在 ConfigService 层加锁 | 产品🟠 |
| 9 | 可靠性 | `RefreshService.cs:185` | `_ = RefreshAllAsync("auto")` fire-and-forget，回调中异常成 unobserved task exception | 加 `ContinueWith(t => FileLogger.Error(...), OnlyOnFaulted)` | 产品🟠 |
| 10 | 并发 | `ConfigService.cs:265` | `Settings` 属性返回内部引用，多处外部修改绕过 `_ioLock`，与 `Save()` 的 `CloneSettings` 产生竞态 | 提供 `UpdateSettings(Action<AppSettings>)` 线程安全 API | 产品🟠 |
| 11 | 性能 | `ConfigService.cs:671-681` | `CloneSettings` 通过 JSON round-trip 深拷贝，在 `_ioLock` 内执行，阻塞并发配置读写 | 改用 `record` + `with` 或手写深拷贝；或将序列化移到锁外 | 产品🟠 |
| 12 | 性能 | `MiniMaxDomExtractor.cs:48-88` | 每次定时刷新（默认 5 分钟）启动 + 销毁完整 headless Edge 进程 | 引入浏览器实例池/长连接复用；DOM 抓取结果缓存（3 分钟内不重复）；`SemaphoreSlim` 限制并发浏览器数 | 产品🟠 + 安全🟡 |
| 13 | 并发 | LoginHelper 与主程序 | 两个独立进程各自创建 ConfigService 实例写 `config.json`，可能互相覆盖 | 用命名 Mutex 跨进程互斥，或 LoginHelper 通过 IPC 通知主程序 | QA P1 |
| 14 | 安全/正确性 | `ConfigService.IsSensitiveKey` | 关键词列表含 "key"，会匹配 "hotkey"/"keyboard"/"monkey" 等非敏感字段 | 移除单独的 "key"，保留 "apikey"/"secret"/"password"/"token"/"cookie" | QA P1 |
| 15 | 兼容性 | `TaskbarHelper.EmbedWindow` | 使用 `SetWindowLongPtr(GWL_HWNDPARENT)` 视觉嵌入 hack 而非标准 COM DeskBand，Windows 更新后可能失效；多显示器/DPI 支持不完整 | README 标注限制；准备纯托盘 tooltip 回退方案；多屏用 `Screen.AllScreens` | QA P1 |

### 🟡 Medium / Major（10 项，短期修复）

| # | 类别 | 位置 | 问题描述 | 建议 | 来源 |
|---|------|------|---------|------|------|
| 16 | 安全 | 日志多处 | Cookie 名称列表（含 `_token`）写入日志，暴露 session 结构 | 仅记录 Cookie 数量 | 安全🟡 |
| 17 | 安全 | debug 目录多处 | 原始 API 响应 JSON/HTML 快照写入 `%AppData%/UsageMonitor/debug/` 无自动清理 | opt-in 默认关闭；或保留最近 7 天自动轮转 | 安全🟡 |
| 18 | 可维护 | `MiniMaxProvider.cs:211-213` | 插件方法修改传入的 config 对象（`SetValue("Cookie", ...)`），绕过加密保存流程 | 自愈逻辑改用事件或返回值通知 ConfigService | 产品🟡 |
| 19 | 性能 | `BrowserLoginService.cs:546` | `CheckCookieValidAsync` 每次新建 HttpClient（socket 耗尽风险） | 改用 `static readonly HttpClient` 或 `IHttpClientFactory` | 产品🟡 |
| 20 | 可维护 | `FileLogger.cs:199-213` | `ResolveProjectRoot` 在生产环境回退到 `GetCurrentDirectory()`，日志散落 | 改用 `%AppData%/UsageMonitor/logs/` | 产品🟡 |
| 21 | 正确性 | `DeepseekProvider.cs:123-134` | `decimal.TryParse` 用当前区域设置，en-US 数字格式在 zh-CN 系统解析失败 | 加 `CultureInfo.InvariantCulture` | 产品🟡 |
| 22 | 正确性 | `OpenAIProvider.cs:89` | `/v1/organization/usage` 可能是 2024 年前的旧版 API，当前实现可能完全无法工作 | 验证最新 OpenAI Billing API 文档；或标注"实验性" | 产品🟡 |
| 23 | 性能 | 多处 | `new JsonSerializerOptions` 未复用，每次构造有非平凡开销 | 提取为 `static readonly` | 产品🟡 |
| 24 | 并发 | `PluginManager.cs:11` | `_plugins` 为 `List<LoadedPlugin>`，`ReloadPlugins` 与枚举可能竞态 | 改 `ConcurrentDictionary` 或加锁 | 产品🟡 |
| 25 | 安全 | `Deepseek/MiMo/OpenAI Provider` | BaseUrl 用户可配且无 URL 校验，可指向内网/元数据 IP（SSRF） | 校验 HTTPS scheme + 拒绝私有 IP 段 + 拒绝 169.254.169.254 | 安全🟢 |

### 🟢 Low / Minor（9 项，择机修复）

| # | 类别 | 位置 | 问题描述 | 建议 | 来源 |
|---|------|------|---------|------|------|
| 26 | 可维护 | `IPluginMetadata.cs` | 接口定义但全代码库无引用（死代码） | 移除或标 `[Obsolete]` | 产品🟢 |
| 27 | 一致性 | ProviderId | 大小写不一致（"MiniMax" vs "deepseek" vs "mimo" vs "openai"） | 统一小写或所有字典用 `OrdinalIgnoreCase` | 产品🟢 |
| 28 | 可维护 | `MainViewModel.cs` | 950+ 行，`ProviderUsageViewModel` 含 MiniMax 专用渲染逻辑，违反 SRP | 拆分到独立文件 | 产品🟢 |
| 29 | 可观测 | 多处 `catch { /* ignore */ }` | 静默吞异常无日志 | 至少 `FileLogger.Debug` 一行 | 产品🟢 |
| 30 | 健壮性 | `RefreshService.cs:64` | `RefreshIntervalSeconds * 1000` 可能 int 溢出（无上限校验） | `NormalizeAfterLoad` 钳制到 30-86400 | 产品🟢 |
| 31 | 架构 | `Security/` 目录 | 完整 `ISecretStore` 体系存在但 ConfigService 仍直接用 DPAPI，两套体系并存 | 迁移或标注"未启用" | 产品🟢 |
| 32 | 安全 | `.gitignore` | 未覆盖 `config.json`/`cookies/`/`secrets/`/`*.key`/`*.bin`/`appsettings.json` | 追加模式 | 安全🟢 |
| 33 | 可靠性 | `RefreshService` | 无熔断机制，MiniMax 连续失败仍持续尝试启动 Edge | 加 circuit breaker：连续 N 次失败后临时禁用 | 安全🟢 |
| 34 | 文档 | README | 路线图多项未勾选但代码已实现，文档过时 | 更新 README | QA P2 |

---

## 🔄 回滚预案（上线后若出问题）

### COM 任务栏嵌入异常 / Explorer 崩溃
- 当前采用"视觉嵌入"（`SetWindowLongPtr GWL_HWNDPARENT`）而非真正 COM DeskBand，无需 regsvr32，回退简单
- **一键回退**：设置中关闭"任务栏显示" → 自动回退纯托盘模式
- **配置级回退**：编辑 `%AppData%/UsageMonitor/config.json`，将 `ShowInTaskbar` 设为 `false`
- **应急脚本**：
  ```powershell
  Stop-Process -Name UsageMonitor -Force
  $cfg = "$env:APPDATA\UsageMonitor\config.json"
  (Get-Content $cfg) -replace '"ShowInTaskbar": true', '"ShowInTaskbar": false' | Set-Content $cfg
  ```

### 配置文件损坏
- ConfigService 已实现三级恢复：`config.json` → `config.json.bak` → `config.json.corrupted-{ts}` → 默认配置
- **手动恢复**：复制 `config.json.bak` 覆盖 `config.json`
- **完全重置**：删除 `config.json`（API Key/Cookie 需重新配置）；`history.db` 可独立备份恢复

### 插件加载异常
- `PluginManager.LoadPluginFromAssembly` 已有 try-catch，单插件失败不影响其他
- **手动回退**：从 `plugins/` 目录删除问题 DLL，重启
- 内置 4 插件通过 `RegisterBuiltinPlugins` 注册，不依赖外部 DLL

### Playwright/Edge 崩溃 / 进程残留
- `BrowserLoginService` finally 块会 `context.CloseAsync()` + `playwright.Dispose()` + `Directory.Delete(tempProfile)`
- **残留处理**：任务管理器结束 `msedge.exe`（注意会影响用户正常 Edge）
- 临时 profile 残留在 `%TEMP%/UsageMonitor_Edge_*`，可手动删除
- Cookie 降级：用户从浏览器开发者工具手动复制 Cookie 粘贴到设置

### API Key 泄露
- DPAPI 加密绑定 CurrentUser，其他用户无法解密
- **应急**：立即在服务商平台吊销/轮换 API Key
- 删除 `%AppData%/UsageMonitor/config.json` 清除本地存储

### Cookie 明文泄露（本次审计发现）
- **应急**：立即在 MiniMax 平台注销所有会话 → 重新通过 LoginHelper 登录（修复后版本）
- 删除 `%AppData%/UsageMonitor/cookies/` 目录
- 检查 `%TEMP%/UsageMonitor_Edge_*` 是否有残留

---

## 📦 Canary / 灰度发布策略

由于是本地桌面工具（非 SaaS），传统 Canary 不直接适用，建议分阶段：

| 阶段 | 范围 | 时长 | 监控指标 | 通过标准 |
|------|------|------|---------|---------|
| Phase 1 内测 | 开发者 + 2-3 名内部用户 | 7 天 | 启动崩溃率、配置恢复触发率、Playwright 登录成功率、24h 内存趋势 | 0 崩溃 + 内存增长 < 20MB |
| Phase 2 灰度 | 10-20 名种子用户 | 14 天 | 同上 + 用户反馈 | 0 P0 + ≤ 3 个 P1 |
| Phase 3 公测 | 公开下载（标注 Beta） | 持续 | FileLogger 日志收集渠道 | 0 P0 + ≤ 5 个 P1 |

**监控指标采集**：
- `logs/` 中 Unhandled exception 频率
- `config.json.bak` / `.corrupted-*` 文件出现频率（配置恢复触发）
- `BrowserLoginService` 日志中 "Login success confirmed" vs "LoginTimeout" 比例
- 24h/48h/72h 内存占用对比

**版本管理**：保留上一版本完整目录供一键切换；`config.json` 格式向后兼容（`NormalizeAfterLoad` 已保证）

---

## ✅ 行动清单（按优先级）

| # | 行动 | 负责方 | 紧急度 | 期望完成 | 预估工时 |
|---|------|--------|--------|---------|---------|
| 1 | 加密 `cookies/*.json`（DPAPI 或 ISecretStore），或废弃并行存储统一走 ConfigService | 工程+安全 | P0 | 上线前 | 0.5 天 |
| 2 | 移除/禁用 `PluginManager.LoadPlugins()` 外部 DLL 扫描（若插件已内置注册）；或加签名验证 | 工程 | P0 | 上线前 | 0.5 天 |
| 3 | `RefreshService._isRefreshing` 改 `Interlocked.CompareExchange` 或 `SemaphoreSlim` | 工程 | P0 | 上线前 | 0.5 小时 |
| 4 | 为 `RefreshAllAsync` + 每个 `GetUsageAsync` 加 `CancellationToken` + 60s 整体超时 | 工程 | P0 | 上线前 | 0.5 天 |
| 5 | 创建 `UsageMonitor.Core.Tests` xUnit 项目，覆盖 ConfigService/PluginManager/RefreshService 基本路径 | 工程 | P0 | 上线前 | 2-3 天 |
| 6 | Playwright `IgnoreHTTPSErrors` 改为 `false`（生产环境） | 工程 | P1 | 上线前 | 0.5 小时 |
| 7 | 移除 `LoginHelper/Program.cs:54` Cookie 前缀控制台输出 | 工程 | P1 | 上线前 | 5 分钟 |
| 8 | `ProviderConfig.Values` 改 `ConcurrentDictionary` 或加锁 | 工程 | P1 | 上线前 | 0.5 天 |
| 9 | `ConfigService.Settings` 提供 `UpdateSettings(Action<AppSettings>)` 线程安全 API | 工程 | P1 | 上线前 | 0.5 天 |
| 10 | MiniMax DOM 抓取引入浏览器实例复用 + 结果缓存（3 分钟） | 工程 | P1 | 上线前 | 1 天 |
| 11 | 配置 GitHub Actions CI（`dotnet build` + `dotnet test`） | 工程 | P1 | 上线前 | 0.5 天 |
| 12 | 修复 `IsSensitiveKey` 中 "key" 关键词过宽 | 工程 | P1 | 上线前 | 0.5 小时 |
| 13 | `BrowserLoginService.CheckCookieValidAsync` 改用 static HttpClient | 工程 | P1 | 上线前 | 5 分钟 |
| 14 | `FileLogger` 日志目录改 `%AppData%/UsageMonitor/logs/` | 工程 | P1 | 上线前 | 0.5 小时 |
| 15 | LoginHelper 与主程序跨进程 Mutex 互斥 | 工程 | P1 | 上线前 | 0.5 天 |
| 16 | Windows 10 + Windows 11 双环境手动验证任务栏嵌入/注销/Explorer 重启/多显示器/DPI | QA | P1 | 上线前 | 1 天 |
| 17 | 24 小时长时间运行稳定性测试（内存/CPU/Playwright 残留） | QA | P1 | 上线前 | 1 天（挂机） |
| 18 | 日志中 Cookie 名称输出降级为仅记录数量 | 工程 | P2 | 短期 | 0.5 小时 |
| 19 | debug 目录自动清理（保留 7 天）或 opt-in | 工程 | P2 | 短期 | 0.5 天 |
| 20 | 插件 DLL 加 Authenticode 签名验证或 SHA-256 白名单 | 工程+安全 | P2 | 短期 | 1 天 |
| 21 | MiniMaxProvider 自愈逻辑改用事件通知，不修改传入 config | 工程 | P2 | 短期 | 0.5 天 |
| 22 | 所有 `decimal.TryParse`/`double.TryParse` 加 `CultureInfo.InvariantCulture` | 工程 | P2 | 短期 | 0.5 小时 |
| 23 | 验证 OpenAI Provider API endpoint 是否仍有效 | 工程 | P2 | 短期 | 0.5 天 |
| 24 | `JsonSerializerOptions` 提取为 static readonly 复用 | 工程 | P2 | 短期 | 0.5 小时 |
| 25 | 插件 BaseUrl 增加 HTTPS 校验 + 内网 IP 过滤（SSRF） | 工程+安全 | P2 | 短期 | 0.5 天 |
| 26 | 创建 VERSION 文件 + CHANGELOG.md | 工程 | P2 | 短期 | 0.5 天 |
| 27 | 添加 coverlet 覆盖率（Core 目标 ≥ 60%） | 工程 | P2 | 短期 | 1 天 |
| 28 | 扩展 `.gitignore` 覆盖 `config.json`/`cookies/`/`secrets/`/`*.key` | 工程 | P2 | 短期 | 5 分钟 |
| 29 | 统一 ProviderId 为小写 + 所有字典用 `OrdinalIgnoreCase` | 工程 | P2 | 短期 | 0.5 小时 |
| 30 | 拆分 `MainViewModel.cs` 中的 `ProviderUsageViewModel` | 工程 | P2 | 短期 | 0.5 天 |
| 31 | RefreshService 加 circuit breaker（连续 N 次失败临时禁用） | 工程 | P2 | 短期 | 0.5 天 |
| 32 | 更新 README 路线图与实际代码一致 | 工程 | P2 | 短期 | 0.5 小时 |

---

## ⚠️ 待完善 / 已知局限

- **审查范围未覆盖**：XAML 视图文件、UsageHistoryRepository 完整实现、Converters/ThemeManager 等 Helper、HistoryViewModel
- **未实际执行测试**：QA 报告基于静态分析 + 测试设计，实际 Windows 10/11 双环境手测、24h 长稳测试需在真机执行
- **OpenAI API endpoint 有效性**：产品官指出可能失效，但未实际调用验证
- **加密强度未做密码学深度审计**：安全卫士确认算法选择正确（AES-256-GCM、DPAPI），但未做侧信道/时序攻击分析
- **Git 历史仅抽查**：未做全历史敏感关键字扫描
- **三方发现的互相印证率高**（5 个重叠点），说明问题真实存在而非个别人误判

---

## 📚 成员产出索引

- **gstack-product-reviewer（产品官）原始产出**：见对话历史，1 Blocker + 5 Critical + 7 Major + 6 Minor，含 16 条行动清单 + 8 条优点 + 审查范围说明
- **gstack-security-officer（安全卫士）原始产出**：见对话历史，1 Critical + 2 High + 3 Medium + 3 Low，含 OWASP Top 10 检查表 + STRIDE 威胁建模 + 9 条安全亮点
- **gstack-qa-lead（质量门神）原始产出**：见对话历史，5 P0 + 6 P1 + 1 P2，含 70+ 条测试矩阵 + 20 条上线 Checklist + 5 类回滚预案 + 3 阶段 Canary 策略 + 10 条 Windows 手测清单

---

> 本报告由软件工坊 AI 协作生成（产品官 + 安全卫士 + 质量门神 三路并行），关键决策请由工程负责人 + 安全负责人联合复核。
> 综合判断为 🔴 No-Go，建议完成 5 项 P0 阻塞项后重新走一轮回归验证。

---

## 📋 开发需求录入记录（2026-07-19 追加）

本次检查发现的全部 34 项整改问题已按主题合并录入项目开发需求清单，共 **9 个主需求 + 39 个子需求**。

- **清单文件**：`D:\应用开发\UsageMonitor\.dev_require\.dev_require_list.md`
- **详情目录**：`D:\应用开发\UsageMonitor\.dev_require\req-053-*` 至 `req-061-*`（共 9 个详情文件，含位置/问题/影响/建议/严重度）
- **录入方式**：dev-master skill（reserve-req --gap 占位 → add-req --placeholder-of 升级 → add-req --parent 挂子需求）

### 主需求映射表

| req-id | 类型 | 主题 | 优先级 | 子需求数 | 覆盖发现项 |
|--------|------|------|--------|---------|-----------|
| **req-053** | main | 安全加固-凭据存储与传输 | P0 | 6 | #1, #6, #7, #14, #16, #17 |
| **req-054** | main | 插件安全边界加固 | P0 | 2 | #2, #20 |
| **req-055** | main | 任务栏多显示器与DPI兼容性 | P1 | 2 | #15 |
| **req-056** | main | 插件BaseUrl SSRF防护 | P2 | 1 | #25 |
| **req-057** | internal | 并发与线程安全修复 | P0 | 6 | #3, #8, #9, #10, #13, #24 |
| **req-058** | internal | 刷新服务可靠性增强 | P0 | 4 | #4, #9, #12, #33 |
| **req-059** | internal | 测试基础设施搭建 | P0 | 3 | #5, #11, #27 |
| **req-060** | internal | 代码质量与性能优化 | P1 | 11 | #18, #19, #20, #21, #22, #23, #26, #27, #28, #29, #30 |
| **req-061** | internal | 配置与版本管理规范化 | P2 | 4 | #31, #32, #34, +VERSION/CHANGELOG |

### 子需求明细

#### req-053 安全加固-凭据存储与传输（P0，6 子需求）
- Cookie明文存储加密(P0) → 对应发现 #1
- Playwright恢复TLS证书校验(P1) → 对应发现 #6
- 移除LoginHelper控制台Cookie输出(P1) → 对应发现 #7
- IsSensitiveKey关键词收窄(P1) → 对应发现 #14
- 日志Cookie名称脱敏(P2) → 对应发现 #16
- debug目录自动清理(P2) → 对应发现 #17

#### req-054 插件安全边界加固（P0，2 子需求）
- 移除或禁用LoadPlugins外部DLL扫描(P0) → 对应发现 #2
- 插件DLL加签名或SHA256白名单(P2) → 对应发现 #20

#### req-055 任务栏多显示器与DPI兼容性（P1，2 子需求）
- 多显示器与高DPI缩放适配(P1) → 对应发现 #15
- 任务栏嵌入回退方案准备(P1) → 对应发现 #15

#### req-056 插件BaseUrl SSRF防护（P2，1 子需求）
- BaseUrl HTTPS校验与内网IP过滤(P2) → 对应发现 #25

#### req-057 并发与线程安全修复（P0，6 子需求）
- _isRefreshing改Interlocked.CompareExchange(P0) → 对应发现 #3
- ProviderConfig.Values改ConcurrentDictionary(P1) → 对应发现 #8
- ConfigService.Settings提供线程安全API(P1) → 对应发现 #10
- CloneSettings移出锁内或改手写深拷贝(P1) → 对应发现 #11
- PluginManager._plugins加锁保护(P2) → 对应发现 #24
- LoginHelper与主程序跨进程Mutex互斥(P1) → 对应发现 #13

#### req-058 刷新服务可靠性增强（P0，4 子需求）
- RefreshAllAsync加CancellationToken与超时(P0) → 对应发现 #4
- fire-and-forget Task加faulted回调(P1) → 对应发现 #9
- MiniMax DOM浏览器实例复用与缓存(P1) → 对应发现 #12
- RefreshService加circuit breaker熔断(P2) → 对应发现 #33

#### req-059 测试基础设施搭建（P0，3 子需求）
- 创建UsageMonitor.Core.Tests xUnit项目(P0) → 对应发现 #5
- 配置GitHub Actions CI流水线(P1) → 对应发现 #11
- 接入coverlet覆盖率与ReportGenerator(P2) → 对应发现 #27

#### req-060 代码质量与性能优化（P1，11 子需求）
- BrowserLoginService改用static HttpClient(P1) → 对应发现 #19
- FileLogger日志目录改AppData(P1) → 对应发现 #20
- decimal.TryParse加InvariantCulture(P2) → 对应发现 #21
- JsonSerializerOptions提取为static readonly复用(P2) → 对应发现 #23
- MiniMaxProvider不修改传入config参数(P2) → 对应发现 #18
- 验证OpenAI Provider API endpoint有效性(P2) → 对应发现 #22
- 拆分MainViewModel.cs中ProviderUsageViewModel(P2) → 对应发现 #28
- 统一ProviderId大小写与字典比较器(P2) → 对应发现 #27
- catch静默吞异常补FileLogger.Debug(P2) → 对应发现 #29
- RefreshIntervalSeconds加上限钳制(P2) → 对应发现 #30
- 清理未使用的IPluginMetadata接口(P2) → 对应发现 #26

#### req-061 配置与版本管理规范化（P2，4 子需求）
- 创建VERSION文件与CHANGELOG.md(P2) → 行动清单补充项
- 扩展.gitignore覆盖敏感文件模式(P2) → 对应发现 #32
- 更新README路线图与代码状态一致(P2) → 对应发现 #34
- Security基础设施接入ConfigService或标注未启用(P2) → 对应发现 #31

### P0 阻塞项与 req-id 对应关系

| P0 阻塞项 | 对应 req-id | 子需求 |
|----------|------------|--------|
| Cookie 明文存储 | req-053 | Cookie明文存储加密(P0) |
| 插件反射加载无安全校验 | req-054 | 移除或禁用LoadPlugins外部DLL扫描(P0) |
| `_isRefreshing` 非线程安全 | req-057 | _isRefreshing改Interlocked.CompareExchange(P0) |
| RefreshAllAsync 无 CancellationToken | req-058 | RefreshAllAsync加CancellationToken与超时(P0) |
| 零测试 + 零 CI | req-059 | 创建UsageMonitor.Core.Tests xUnit项目(P0) + 配置GitHub Actions CI流水线(P1) |

### 已知问题（非本次操作引起）

1. **历史遗留 req-013 重复**：清单"已完成"区有两个 req-013（`req-013-history-refresh-aggregates-slicer` 和 `req-013-shared-config-template`），validate.py 报 FAIL。建议后续人工重命名其中一个为 req-014 或合并。
2. **dev-master skill 子需求"详见"路径 bug**：子需求按 skill 规范不创建详情目录，但 add-req.py 仍生成了路径引用，实际文件不存在。子需求细节已写入父需求详情文件，不影响使用。

### 后续工作建议

1. **优先实施 5 个 P0 主需求**：req-053 / req-054 / req-057 / req-058 / req-059
2. 历史遗留 req-013 重复问题单独处理
3. 详情文件（`req-053-*.md` 至 `req-061-*.md`）的"技术细节"段落可直接作为开发者实施规格
