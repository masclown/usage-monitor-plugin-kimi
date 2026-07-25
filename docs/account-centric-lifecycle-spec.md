# 账号为中心的生命周期模型 Spec —— 插件安装 → 账号创建 → 凭据配置 → 卡片显示

> 状态：**P1+P2 已实施完成，待用户验收**（2026-07-25；Core 195/195 + App 19/19 全绿；实施记录见 req-110 实现备注）。
> 背景：主窗口 MiniMax 卡片消失排查（2026-07-25）暴露生命周期断点——账号存在但名下零卡片记录，
> `BuildCardTuples` 返回空导致一张卡片 VM 都不创建。用户拍板采用"账号为中心"模型，本 Spec 为执行依据。

## 0. 用户模型（决策记录）

| 阶段 | 行为 | 主窗口表现 |
|------|------|-----------|
| ① 安装插件 | 声明包放入 `plugins/`，程序扫描加载 | **不显示**用量卡片（仅插件管理页可见） |
| ② 创建账号 | 用户在插件下显式「+ 添加账号」 | 卡片**待命**（账号已有默认卡片实例，等待数据） |
| ③ 配置账号 | 对账号获取登录态 / 输入 API Key | 卡片显示引导文案（未取到数据前） |
| ④ 程序取数 | 用账号凭据刷新，插件声明字段映射与默认卡片样式 | 主窗口显示用量卡片 + 任务栏迷你图 |
| ⑤ 删除账号 | 任意账号可删（**包括最后一个**）；弹窗询问历史数据删/保 | 该账号卡片消失；全删后回到 ① 空态 |

核心不变量：**卡片严格跟随账号生命周期**——有账号必有至少一张卡片实例；无账号即无卡片。
账号身份**先于数据存在**（用户创建），刷新返回的网页身份哈希只作为账号的**绑定元数据**，不再反向自动注册账号。

## 1. 现状差距（2026-07-25 调查结论）

| # | 断点 | 现状代码 | 违反模型阶段 |
|---|------|---------|-------------|
| G1 | 账号创建不实例化卡片 | `ConfigService.AddAccount` 只建 Account；`AddCard` 全库无调用方 | ② |
| G2 | 零账号时回退隐形默认卡 | `DisplayModule.BuildCardTuples` 回退 `("default","default-card")` | ① |
| G3 | 刷新自动注册哈希账号 | `RefreshService` L294 `EnsureAccount(usage.AccountId哈希)`，绕过用户账号体系 | ②④ |
| G4 | 最后一个账号不可删除 | `ConfigService.RemoveAccount` 抛异常 | ⑤ |
| G5 | 凭据为 Provider 级 | Cookie/ApiKey 存 `ProviderConfigs[providerId]` 与 `cookies/{ProviderId}.json` | ③（多账号时） |
| G6 | 空态文案未引导账号流程 | MainWindow 空态="请先在设置中启用至少一个插件" | ①③ |

## 2. 阶段划分

### Phase 1 —— 生命周期主干修复（本次执行）

**P1-1 卡片严格跟随账号**（G1/G2）
- `DisplayModule.BuildCardTuples`：删除零账号回退分支；无账号 / 无启用账号 → 返回空列表（主窗口空态）。
- `ConfigService.AddAccount`：创建账号后在同一锁内同步创建首张卡片（`default-card`），单次 Save。
- `ConfigService.EnsureAccount` 保留方法但**不再被刷新链路调用**（见 P1-3），同样补默认卡（防御）。

**P1-2 存量数据迁移**（`ConfigService.NormalizeAfterLoad`，一次性幂等）
- M1：为所有**零卡片账号**补建 `default-card`（修复当前 MiniMax_1 黑屏）。
- M2：旧三段定制 `Provider:default:*` 存在、且该 Provider 无 `default` 账号但有默认账号（IsDefault）时，
  将定制 rekey 到默认账号名下（保留用户已保存的图表 / 迷你图勾选）。
- M3：**老用户兜底**——已启用 Provider 有凭据（ProviderConfigs 存在 Cookie/ApiKey）但零账号时，
  自动创建一个账号 + 默认卡（昵称 `{ProviderId}_1`），避免升级后主窗口无故变空。

**P1-3 账号身份绑定替代自动注册**（G3）
- `Account` 新增 `BoundStableId`（string?，存网页身份哈希）。
- `RefreshService.RefreshPluginAsync`：
  - 删除 `EnsureAccount` 调用；
  - 刷新门控（Q1 拍板）：**刷新单元 = 已启用插件 × 已启用且配置完成的账号**——插件未启用不刷新；插件无账号不刷新；账号未启用不刷新；账号未配置凭据不刷新；多插件时仅刷新满足条件的账号（Info 日志记录每个跳过原因）；
  - 刷新成功后：`usage.AccountId`（插件哈希）写入该账号 `BoundStableId`（首次绑定；后续不一致仅 Warn 日志——网页侧换号提示）；
  - **`usage.AccountId` 重写为配置账号 ID** 后再落库 / 路由，保证 RenderCard 三段路由与 SQLite `account_id` 列一致。
- 历史数据关联迁移：启动时对 `usage_field_versions` / `usage_points` 等表执行一次性
  `UPDATE ... SET account_id = <账号ID> WHERE account_id = <BoundStableId哈希>`（beta 数据量小，可接受）。

**P1-4 账号删除完全放开**（G4，Q2 拍板）
- `ConfigService.RemoveAccount`：移除"仅剩一个账号不可删"限制；补充清理账号级（二段）与卡片级（三段）`AccountCustomizations`。
- 删除确认弹窗提供"删除/保留历史数据"选项：选保留 → `BoundStableId` 机制保证重建同一网页账号可重新关联；
  选删除 → 调用新增的 `UsageHistoryRepository.DeleteAccountDataAsync(providerId, accountId)` 账号级清库。

**P1-5 空态引导**（G6）
- MainWindow 空态文案：分层引导——"已安装插件但未创建账号 → 请在设置 → 插件管理中为插件添加账号并配置登录态 / API Key"。

**P1-6 配套测试**（Core.Tests + App.Tests）
- AddAccount 自动建默认卡；RemoveAccount 最后账号可删且级联清理；
- BuildCardTuples：零账号无卡、账号有卡则出元组、禁用账号被过滤；
- NormalizeAfterLoad 三条迁移（M1/M2/M3）幂等；
- RefreshService：无启用账号跳过、usage.AccountId 重写、BoundStableId 首绑与不一致告警。

**门槛**：`dotnet build` 0/0 + `dotnet test` 全绿 + 用户本地验收（MiniMax 卡片恢复显示、删除账号后空态、重建账号历史可关联）。

### Phase 2 —— 凭据账号级隔离（Q3 拍板：与 Phase 1 连做）

- **P2-1 凭据下沉**：Cookie/ApiKey 从 `ProviderConfigs[providerId]` 下沉到账号级——`ProviderConfig` 键值语义扩展为
  `AccountConfigs[accountId]`（或等价的 `Provider:Account` 键），敏感字段加密规则不变（DPAPI）。
- **P2-2 登录态账号化**：`cookies/{ProviderId}.json` → `cookies/{ProviderId}.{AccountId}.json`；
  `BrowserLoginService` / `LoginHelper`（命令行加 `--account` 参数）/ `AuthManager` 全链路贯通 accountId。
- **P2-3 按账号循环刷新**：`RefreshService` 按 Q1 门控矩阵逐账号取数——每账号独立凭据、独立熔断计数、独立 LoginExpired 事件；
  `DeclarativeProvider.GetUsageAsync` 的 Cookie 自愈路径同步改为账号级文件。
- **P2-4 状态灯账号级**：插件管理页账号行 API/Sub 状态灯改读账号级凭据（当前 `HasApiKey` 为 Provider 级共享）。
- **P2-5 凭据迁移**：现有 Provider 级凭据（config.json + cookies/*.json）一次性归属到该 Provider 的默认账号（IsDefault）。

## 3. 涉及文件（Phase 1）

| 文件 | 改动 |
|------|------|
| `src/UsageMonitor.Core/Models/Account.cs` | 新增 `BoundStableId` |
| `src/UsageMonitor.Core/Services/ConfigService.cs` | AddAccount/EnsureAccount 建默认卡；RemoveAccount 放开 + 级联清理；NormalizeAfterLoad 迁移 M1/M2/M3 |
| `src/UsageMonitor.Core/Services/RefreshService.cs` | 删 EnsureAccount 调用；账号解析 + 跳过无账号；AccountId 重写 + BoundStableId 绑定 |
| `src/UsageMonitor.Core/Services/UsageHistoryRepository.cs` | 一次性 account_id 关联迁移 + `DeleteAccountDataAsync` 账号级清库 |
| `src/UsageMonitor.App/Services/Display/DisplayModule.cs` | BuildCardTuples 删零账号回退 |
| `src/UsageMonitor.App/MainWindow.xaml`(+VM) | 空态文案分层引导 |
| `src/UsageMonitor.App/Views/SettingsWindow.xaml.cs` | 删除账号确认弹窗（历史数据删/保选项） |
| `tests/UsageMonitor.Core.Tests` / `tests/UsageMonitor.App.Tests` | P1-6 全部用例 |

**Phase 2 追加**：

| 文件 | 改动 |
|------|------|
| `src/UsageMonitor.Core/Models/ProviderConfig.cs`（或新增账号级配置模型） | 凭据账号级键值 |
| `src/UsageMonitor.Core/Services/ConfigService.cs` | 账号级凭据读写 + P2-5 迁移 |
| `src/UsageMonitor.Core/Services/BrowserLoginService.cs` | cookies 文件账号级命名 |
| `src/UsageMonitor.Core/Services/Auth/AuthManager.cs`（含 3 个 AuthProvider） | accountId 贯通到凭据读取 |
| `src/UsageMonitor.Core/Services/RefreshService.cs` | 按账号循环 + 账号级熔断 |
| `src/UsageMonitor.Core/Plugins/DeclarativeProvider.cs` | Cookie 自愈改账号级文件 |
| `src/LoginHelper/Program.cs` | `--account` 参数 + 账号级写回 |
| `src/UsageMonitor.App/ViewModels/PluginAccountItemViewModel.cs` | 状态灯账号级 |

## 4. 风险

- **R1（中）** 移除零账号回退影响老用户升级 → M3 兜底自动建账号，无感迁移。
- **R2（中）** `usage.AccountId` 从哈希改为账号 ID 切断既有历史关联 → P1-3 一次性 UPDATE 迁移 + `UsageFieldAliases` 式兜底查询不受影响；迁移前自动备份 history.db。
- **R3（低）** M2 rekey 与用户手动改过的定制冲突 → rekey 仅在目标 key 不存在时执行（不覆盖）。
- **R4（中）** 两阶段连做改动面大（鉴权/刷新/登录全链路）→ 按 P1→P2 顺序串行提交，每完成一个子项即 build+test 验证；
  Phase 1 完成点作为中间检查点（可独立运行验证）。

## 5. 验收标准

| # | 验收点 |
|---|--------|
| 1 | 只装插件不建账号：主窗口空态 + 引导文案，无卡片、无刷新空跑 |
| 2 | 创建账号：立即出现待命卡片（显示"未配置"引导） |
| 3 | 配置登录态 / API Key 后刷新：卡片显示用量数据，任务栏迷你图正常 |
| 4 | 当前 MiniMax_1 黑屏修复：升级后无需手动操作，卡片恢复 |
| 5 | 删除任意账号（含最后一个）：弹窗询问历史数据删/保；卡片消失；全删后回到空态 |
| 6 | 删除时选"保留"后重建同一网页账号：历史数据经 BoundStableId 重新关联 |
| 7 | 刷新门控：未启用插件 / 无账号 / 账号未启用 / 账号未配置凭据均不触发刷新（日志可证） |
| 8 | Phase 2：同 Provider 两个账号各自独立 Cookie/ApiKey，刷新互不干扰，单账号失效不影响另一账号 |
| 9 | Phase 2：升级后原 Provider 级凭据自动归属默认账号，无需重新登录 |
| 10 | `dotnet build` 0 警告 0 错误；`dotnet test` 全绿 |

## 6. 决策记录（2026-07-25 用户拍板）

| # | 问题 | 决策 |
|---|------|------|
| Q1 | 刷新门控 | **刷新单元 = 已启用插件 × 已启用且配置完成的账号**：插件未启用不刷新；插件无账号不刷新；账号未启用不刷新；账号未配置凭据不刷新；多插件时仅刷新满足全部条件的账号 |
| Q2 | 删除账号时历史数据 | **弹窗询问删/保**：选删除走账号级清库（DeleteAccountDataAsync）；选保留可经 BoundStableId 重新关联 |
| Q3 | Phase 2 时机 | **两阶段连做**（凭据账号级隔离本次一并交付） |
| Q4 | 创建账号后未取数前 | **显示待命卡**（含"未配置"引导与设置入口），维持"有账号必有卡片"不变量 |
