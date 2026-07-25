# MiniMax 完全解耦（纯声明化外置）Spec —— Provider 无关宿主

> 状态：**决策已全部拍板（2026-07-25），可按阶段执行**。
> 前置：MiMo / Deepseek / Kimi / Qoder 已移除，MiniMax 是当前唯一 Provider。

## 0. 决策记录

| 决策 | 结论 |
|------|------|
| A 宿主形态 | 纯插件宿主，无任何内置 Provider |
| B MiniMax 归宿 | 生产测试完成后降级 `samples/`（sample 声明包 + sample 数据库） |
| C 富功能 | **泛化**进声明式 SDK，功能不删；MiniMax 转完全解耦的外部插件做生产测试，未来所有 Provider 照此接入 |
| Q1 插件目标形态 | **纯声明包（仅 defaults.json，零 DLL）**——MiniMax 剩余 ~900 行 C# 全部声明化。DLL 白名单问题随之**消失**（JSON 不能执行代码）；`AllowExternalPlugins`/SHA256 白名单机制保留给未来可能的 DLL 插件通道，默认关闭不变 |
| Q2 sample 数据库 | 内容与形态留到 sample 化阶段（Phase 7）再定 |
| Q3 字段策略 | **全部迁移到 SDK 通用字段**（对照表见 §2），仅新增 `total_days`；删除 SDK 遗留 `mm_/ds_/kimi_` 常量；**不预建** customFields 机制（YAGNI，未来真有无法通用化的概念再建） |

## 1. 目标状态

1. 宿主（App / Core / LoginHelper）**零 Provider 硬编码**：代码逻辑不出现 `"MiniMax"` / `mm_` 等任何服务商专名；不 ProjectReference 任何插件；无 `RegisterBuiltinPlugins`。
2. MiniMax = **纯声明包**：`plugins/UsageMonitor.Plugin.MiniMax/defaults.json`（+可选图标），无 DLL；由宿主"声明包加载器 + 通用声明式 Provider 运行器"实例化，功能与内置时代**等价**。
3. 任何新 Provider 只需编写 defaults.json 即可接入，无需写 C#、无需改宿主。
4. 生产测试完成后：声明包与样例数据入 `samples/` 作项目内示例。

## 2. mm_* → 通用字段迁移对照表（Phase 2 执行依据）

| defaults.json 现 target | 迁移到（均已存在，除注明） |
|---|---|
| mm_5hUsedPercent / mm_weeklyUsedPercent | `five_hour_used_percent` / `weekly_used_percent` |
| mm_5hResetAt / mm_weeklyResetAt | `five_hour_reset_at` / `weekly_reset_at` |
| mm_videoIntervalTotal/Remaining | `video_total_count` / `video_used_count`（plan_type=FiveHour） |
| mm_videoWeeklyTotal/Remaining | 同上（plan_type=Weekly） |
| mm_totalDays | `total_days`（**新增**通用字段） |
| mm_activeDays / mm_rankingPercent | `active_days` / `usage_ranking_percent` |
| mm_totalTokens | `used_tokens` |
| mm_mostActiveDate / mm_mostActiveToken | `most_active_date` / `most_active_token` |
| mm_mostActiveTokenText | 删除（宿主按 most_active_token 格式化派生） |
| mm_dailyTokenDates / mm_dailyTokenValues | `daily_token_date` / `daily_token_value` |
| mm_cacheHitPercent | `cache_hit_percent` |
| （SDK 遗留常量）Mm5hUsedPercent 等 5 个 + Ds* 2 个 + KimiUsedPercent | 删除常量与元数据；`UsageFieldAliases` 保留旧名→新名解析以兼容历史库数据 |

## 3. 阶段划分

### Phase 1 — 宿主硬编码清除（MiniMax 仍内置，行为不变）
1. 错误引导声明化：删 `ProviderUsageViewModel.cs:1081` 硬编码分支；defaults.json 新增可选 `errorGuidance` 节（关键字/错误码 → 引导文案），MiniMax 3 条文案迁入。
2. token=0 清理退役：删 `CleanHistoricalZeroTokenDataAsync`（SQL 写死 'MiniMax'）+ App 调用 + `LastCleanedZeroTokensAt`。
3. 指纹泛化：`UsageHistoryRepository.cs:649-651` 去 `mm_*` 特判，改全部 extras 通用哈希（一次性指纹变化 → 首轮可能重复入库一点，可接受）。

**门槛**：build 0/0 + test 全绿 + 用户本地验收无回归。

### Phase 2 — 字段全通用化（req-108 收官）
4. 按 §2 对照表改 defaults.json 全部 target；新增 `UsageFields.TotalDays`。
5. 删 SDK 遗留 `Mm*/Ds*/KimiUsedPercent` 常量、`MapToStandardFieldName` 中 mm_/ds_ 分支、`MiniMaxProvider.MapToStandardFields` 映射；`UsageFieldAliases` 兜底历史数据。
6. 宿主凡读 `mm_*` extras 之处（VM/图表/迷你图/托盘）同步改读通用字段。

**门槛**：build/test 全绿 + 本地验收（卡片/图表/历史曲线含旧数据仍正常）。

### Phase 3 — 富功能泛化（声明驱动）
7. 5h 倒计时：`UpdateFromMiniMaxDom` 重构为通用"重置倒计时"（`five_hour_reset_at` 驱动）；`HasFiveHourCountdown`→`HasResetCountdown`。
8. 周期切换/每日 Token：确认 `Card.Line.Slicer(Period)` 完全接管后删 `_fullDaily*`/`_isDomExtractMode`。
9. 4 进度条开关（Show5hBar 等）并入通用"渲染块显隐"机制，删宿主专用 key 读取。
10. i18n 插件化：删 `I18n.cs` plugin.MiniMax.* 块；字段文案随 defaults.json 字段声明自带。

### Phase 4 — 全声明化（消灭 ~900 行 C#，插件零 DLL）
11. SDK 新能力①**声明式 HTTP 直连取数**：fetch 声明支持 http 模式（endpoint + 头模板可引用配置字段/从 Cookie 提取值 + jsonpath），替代 `QueryRemainsAsync`（含 x-group-id 头等细节需新增声明原语——遇到声明表达不了的逻辑，一律**补框架原语**，禁止回退写插件专有 C#）。
12. SDK 新能力②**loginConfig 声明节**：`BrowserLoginConfig` 全量属性搬进 defaults.json（登录 URL/域过滤/成功判定关键字等本就是纯数据）。
13. 余额/账单抓取（`MiniMaxBalanceFetcher`）并入 fetch 声明（`BrowserCaptureService` 已通用）。
14. SDK 新能力③**纯声明包加载**：`PluginManager` 支持扫描 `plugins/*/defaults.json`（无 DLL），以通用 `DeclarativeProvider` 运行器实例化（IconPath/LoginConfig/Card/Taskbar/Fetch 全部来自声明；ProviderIconService 的 favicon 域名解析随 loginConfig 声明继续工作）。
15. 删除 MiniMax 全部 .cs（`MiniMaxProvider`/`MiniMaxBalanceFetcher`/`DebugFileManager`），项目仅剩 defaults.json。

**门槛**：build/test 全绿（新框架能力配套单测）+ 本地验收等价性。

### Phase 5 — 外置化收官
16. 删 App/LoginHelper 的 MiniMax ProjectReference、`RegisterBuiltinPlugins`；sln 移除 MiniMax 项目（已无代码）。
17. MiniMax 声明包部署至 `plugins/UsageMonitor.Plugin.MiniMax/`（构建复制或文档指引）；根目录 defaults.json 覆盖冲突随目录隔离自然消除。
18. README 更新：纯插件宿主定位 + 声明包安装指引 + 空态引导。

**门槛**：外置声明包全功能生产验收（登录/5h/周/余额/图表/迷你图/多账号/历史持久化）。

### Phase 6 — 生产测试期（用户主导）
日常使用外置 MiniMax 声明包；每个"不得不改宿主"的点记录为 SDK 缺口并补齐框架原语。

### Phase 7 — Sample 化（测试完成后另行启动）
声明包样本 → `samples/`；产出 sample 数据库（内容/脱敏届时再定，Q2 已拍板延后）；README 指向 samples 作接入示例。

## 4. 安全模型（替代原白名单问题）
- 纯声明包=数据，不可执行宿主代码 → **SHA256 白名单不适用**；`AllowExternalPlugins`（DLL 通道）保持默认 false。
- 声明包安全边界：`PluginValidator` 校验必须通过；声明 URL 走 req-056 SSRF 防护；`jsFunction` 仅在浏览器页面沙箱执行（可加长度/模式约束），触不到宿主进程。

## 5. 风险
- **R1（高）** 字段迁移牵连持久化/校验/历史数据 → `UsageFieldAliases` 兜底 + 146 用例逐项绿 + 新增迁移用例。
- **R2（高）** 声明式 HTTP 直连与声明包运行器为全新框架能力；MiniMax API 细节（如从 Cookie 提取 group_id 作请求头）可能暴露声明表达力缺口 → 原则：补原语，不回退写 C#。
- **R3（中）** 指纹泛化一次性重复入库；必要时做迁移兼容。
- **R4（低）** 宿主空态引导。

## 6. 验收标准
- [ ] 宿主源码零 Provider 专名/专有字段硬编码（`grep -i "minimax|mm_"` 仅命中历史文档与 samples）。
- [ ] `plugins/UsageMonitor.Plugin.MiniMax/` 下**只有 defaults.json（无 DLL）**，加载后功能与内置时代等价。
- [ ] 新 Provider 仅凭 defaults.json 即可获得同级能力，无需写 C#、无需改宿主。
- [ ] `dotnet build` 0/0；`dotnet test` 全绿，含声明式 HTTP 取数 / 声明包加载 / errorGuidance / 字段迁移新用例。
