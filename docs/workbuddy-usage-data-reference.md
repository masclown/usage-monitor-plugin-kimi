# WorkBuddy 用量数据接口排查手册

> 创建日期：2026-07-22
> 用途：记录 WorkBuddy（workbuddy.cn）用量网页的真实接口、查询方法、完整数据结构，供 SDK 字段设计与插件开发参考。
> 数据来源：2026-07-22 通过浏览器（chrome-devtools 实例）实测抓取。
> 账号状态：Mr.M（个人版/体验版），套餐 500/500 积分（0 剩余），28 个资源包，总消耗 5898 credits。
> 🔴 安全约束：`/console/accounts` 含手机号/uin —— **不采集、不入库、不记录**。

---

## 0. 关键发现：WorkBuddy = 腾讯云 CodeBuddy 计费后端

接口数据显示 WorkBuddy 用量页背后是**腾讯云代码助手 CodeBuddy** 的计费系统：
- `ProductName` = "腾讯云代码助手"
- `SubProductName` = "腾讯云代码助手 (IDE)"
- `PackageName` = "CodeBuddy个人体验版" / "CodeBuddy个人版国内运营裂变包"
- 接口路径 `/billing/meter/*`（腾讯云计量计费风格）

## 0.1 与其他三站对比

| 维度 | MiniMax | Kimi | Qoder | WorkBuddy |
|---|---|---|---|---|
| 接口风格 | REST GET | gRPC-web POST | REST GET | **REST POST（/billing/meter/*）** |
| 鉴权 | Cookie+x-group-id | Bearer token | 纯 Cookie | **纯 Cookie** |
| 用量单位 | 百分比/次/Token | ratio/credit | Credits | **credits（积分）** |
| 配额模型 | 5h/周+视频 | 总+5h+7天+加油包 | 三档 | **N 个资源包（28 个）+ 赠送/补偿包** |
| 进度条配色 | 绿/黄/红色阶 | 白+蓝分段 | 绿色主题 | **绿色系（满条绿）** |
| 请求明细表 | 每日聚合 | 虚拟滚动流水 | REST 分页流水 | **未见明细表（需参数）** |

---

## 1. 基础信息

- **用量页 URL**：`https://www.workbuddy.cn/profile/plans-usage`
- **API 域名**：`https://www.workbuddy.cn`
- **鉴权**：纯 Cookie（`credentials:'include'`）
- **登录跳转链**（实测）：
  ```
  /profile/plans-usage（未登录）
    → /login/?platform=usercenter&state=0&redirect_uri=.../profile/plans-usage
    → 微信扫码 / 手机号登录（腾讯账号体系，个人/企业 tab）
    → 回跳 /profile/plans-usage
  ```
- **产品**：WorkBuddy - AI Agent 办公新范式（腾讯公司，Copyright 1998-2026）

---

## 2. 接口详情（实测完整结构）

### 2.1 `get-user-resource` — 资源包列表（核心）

- **URL**：`POST https://www.workbuddy.cn/billing/meter/get-user-resource`
- **响应**：包裹在 `data.Response.Data`（腾讯云 API 风格，大驼峰字段）
- **汇总**：`TotalCount=28`（资源包数量）、`TotalDosage=5898`（总消耗）
- **单个资源包结构**（50+ 字段，核心）：

```jsonc
{
  "AccountId": 6703423,
  "CapacityUnit": "credits",                      // 单位
  "PackageName": "CodeBuddy个人体验版",            // 套餐名（见 §3 枚举）
  "PackageCode": "TCACA_code_008_...",
  "ProductName": "腾讯云代码助手",
  "SubProductName": "腾讯云代码助手 (IDE)",        // 子产品（见 §3 枚举）
  "CapacitySize": 500,                            // ★ 总额度
  "CapacityUsed": 0,                              // ★ 已用
  "CapacityRemain": 500,                          // ★ 剩余
  "CapacitySizePrecise": ...,                     // ★ 精确值（小数版，与整数并存）
  "CapacityUsedPrecise": ...,
  "CapacityRemainPrecise": ...,
  "CycleStartTime": "2026-07-01 00:00:00",        // ★ 周期起（字符串，非时间戳）
  "CycleEndTime": "2026-07-31 23:59:59",          // ★ 周期止
  "CycleCapacitySize": 500,                       // 本周期额度
  "CycleCapacityUsed": 500,                       // 本周期已用
  "CycleCapacityRemain": 0,                       // 本周期剩余
  "RemainCycles": 0, "TotalCycles": 1,            // 剩余/总周期数
  "PkgSourceType": 0,                             // 包来源类型
  "ResourceType": "2",
  "ResourceId": "codebuddy-...",
  "Status": 0,                                    // 状态（0=正常）
  "ExpiredTime": "",                              // 过期时间
  "SupportAutoRenew": 0, "AutoRenewFlag": 0,      // 自动续费
  "DeductionStartTime": 1774838499000,            // 扣费起（毫秒时间戳）
  "DeductionEndTime": 2035248098000,
  "Region": "ap-others", "Zone": "ap-others-4"    // 腾讯云地域
}
```

> ⚠️ 时间字段两种格式并存：`CycleStartTime` 是字符串（"2026-07-01 00:00:00"），`DeductionStartTime` 是毫秒时间戳。SDK 入库需统一。

### 2.2 `check-gift-claimed` — 赠送包

```jsonc
{
  "claimed": true,                       // 已领取
  "claimed_at": "2026-03-30 10:41:41",
  "active": true,                        // 生效中
  "credit_num": 1500,                    // 赠送积分数
  "validity_period": 1,                  // 有效周期数
  "start_time": "2026-04-01 00:00:00",
  "end_time": "2026-07-31 23:59:59"
}
```

### 2.3 `compensation-status` — 补偿包

```jsonc
{
  "claimed": true,
  "active": false,                       // 已过期
  "credit_num": 1000,                    // 补偿积分
  "validity_period": 12,
  "end_time": "2026-04-30 23:59:59"
}
```

### 2.4 `get-user-request-usage` — 用量明细表（★核心时序数据，已破解参数）

- **URL**：`POST https://www.workbuddy.cn/billing/meter/get-user-request-usage`
- **★ 必需参数**（之前空 body 报 400，已破解）：`{ pageNum, pageSize, startTime, endTime }`
  - `startTime`/`endTime` 为**字符串日期**（`"2026-07-15 00:00:00"`），非时间戳
  - 注：时间跨度过大（如 >30天）会返回 total=0，需按 UI 的今天/7天/30天窗口查询
- **响应**：`data.total`（实测 71，★仅近7天窗口，非全量）+ `data.data[]`
  - ⚠️ **total=71 是近7天的不完整数据**（用户澄清）：按 startTime/endTime 窗口返回，全量历史需逐窗（每次≤30天）翻页累加。
- **单行结构**（实测）：
  ```jsonc
  {
    "requestId": "fc80767fd3344fabb42fac88223afaa5",  // ★ 请求唯一 ID（去重键）
    "requestTime": "2026-07-22 00:13:00",             // ★ 请求时间（分钟级）
    "credit": 0,                                       // ★ 本次积分消耗（可为小数，如 4.25）
    "model": "hy3",                                    // ★ 模型（见 §3 枚举）
    "client": "WorkBuddy",                             // 客户端
    "agentPurpose": "conversation",                    // ★ Agent 用途（见 §3 枚举）
    "inputTrunc": "hi",                                // 🔴 用户输入（截断）
    "input": "hi"                                      // 🔴 用户输入（完整，隐私敏感）
  }
  ```
- **UI 表头**（6 列）：时间 / 积分消耗 / 模型 / 客户端 / 消息 / Request（ID）；顶部有**今天/7天/30天**筛选 + 日期范围 + **导出**按钮
- 🔴 **隐私敏感**：`input`/`inputTrunc` 是**用户对话输入内容**（如 "随便说一个1~100的随机数字"、"hi"）——**建议不入库或严格脱敏**（UI 上“消息”列展示了输入，属于会话隐私）。

> ★ **永久保存 + 刷新去重**（用户要求，同 Kimi/Qoder）：这张表需永久保存，按 `requestId` 去重；刷新时按时间窗口拉取，遇到已保存的 requestId 即可停止（增量同步）。

### 2.5 `/console/accounts` — 账户（🔴 含敏感字段，跳过）
- 返回 `nickname`、`type`、`pluginEnabled` 等；**同时含 `phoneNumber`（手机号）+ `uin`** → 敏感，不入库/日志。
- ✅ 可用作**跨会话稳定账号 ID**：`uid`（UUID，如 8c154118-...）或 `uin`（330101...）——重登不变，适合作账号识别主键（建议存哈希值）。

### 2.6 成长中心页（`/profile/growth-center`，游戏化运营）

> ★ 用户指定采集的第二个 WorkBuddy 页面。主要是**游戏化激励**（做任务攒能量、开盲盒解锁 Buddy、连登抽奖、活跃热力图），与核心用量弱相关，但几个字段对用量趋势有参考价值。

接口群（`/activity/growth/*`、`/v2/activity/growth/*`，纯 Cookie）：

| 接口 | 用途 | 实测字段 |
|---|---|---|
| `/activity/growth/energy` | 能量值 | `balance:3`、`total_consumed:70`、`total_earned:73` |
| `/activity/growth/streak` | 连登 | `days:1`、`month_total_days:8`、`month_consumed_days:0`、`next_tier:"7d"`、`next_tier_remaining:6`、`makeup_cards{balance,max:4}`、`timezone:"Asia/Shanghai"`、`launch_date:"2026-06-17"` |
| `/v2/activity/growth/profile` | 等级 | `level:11`、`completed:11`、`total:12`、`max_level:false`、`level_icon`(badge png) |
| `/activity/growth/heatmap` | ★ 活跃热力图 | 见下方专段 |
| `/v2/activity/growth/badges` | 徒章 | `badges[]`：total=11，earned=8（按 task_type：beginner 5/2、auto 1/1、single 5/5）|
| `/v2/activity/growth/tasks` | 任务列表 | 如“夜猫子”夜间折扣活动（进度 2/3）|
| `/activity/growth/buddy/*` | 宠物/盲盒 | 攒能量开盲盒解锁 Buddy（每次 10 能量，3/10）|
| `/activity/growth/lottery/summary`、`redeem/summary` | 抽奖/兑换 | 运营活动 |

#### ★ 活跃热力图（`/activity/growth/heatmap`，用户指定参考）
用户指出：月活跃地图 + 全年活跃记录都是热力图。实测确认：
- **同一接口返回全年 365 天**（`cells[]`，2025-07-23 ~ 2026-07-22 滚动一年）：
  - UI“本月活跃地图”= 前端截取近 31 天；“全年活跃记录”按钮 = 展开全部 365 格
- **单格结构**：`{ date, score, has_new_buddy }`
  | 字段 | 语义 | 实测 |
  |---|---|---|
  | `date` | 日期 | `2026-05-12` |
  | `score` | 当日活跃分（连续值，非分档）| 0~155，实测值域 `[0,2,4,6,8,9,10,11,12,14,20,21,25,27,32,40,42,45,56,61,74,95,155]` |
  | `has_new_buddy` | 当天是否解锁新 Buddy | true/false（实测 7 天）|
- **实测**：365 格中 30 天 score>0（有活动）；“完成一次对话才会被记录”
- → 这与 MiniMax 热力图（168天）直接呼应，但 WorkBuddy 是 **365 天 + 活跃分**（非 token 消耗），SDK 热力图字段需支持“任意天数 + 数值含义可配（token/活跃分/次数）”。

#### ★ 热力图色阶 + 交互（用户指定，为后续开发不同样式热力图提供现实参考）

WorkBuddy 有**两套不同样式的热力图**（实测 computed style），非常有参考价值：

**样式 A：月历视图（“7月活跃地图”，默认显示）** —— 日历格子式（非 GitHub 方块矩阵）
- 主色：**藄荷绿 `rgb(111,229,198)` = #6FE5C6**，通过**透明度分档**表达活跃度：
  | 活跃档 | 背景色 | 实测样本 |
  |---|---|---|
  | 未打卡/未来 | `#FAFAFA`（浅灰，文字 #B1B1B1）| 28 |
  | 低活跃 | `rgba(111,229,198,0.18)` | 6 |
  | 中活跃 | `rgba(111,229,198,0.58)` | 2 |
  | 高活跃 | `rgba(111,229,198,0.82)` | 19 |
  | 今日（满色实心）| `rgb(111,229,198)` | 今日 |
- 格子：圆角 **8px**，尺寸较大（日历块、含日期数字）；cursor=**default**（格子不可点击，纯色阶展示）
- 图例：`■■■ 已打卡`（三档深浅绿）+ `未打卡（可补登）`
- 顶部有**连登里程碑进度条**（1天→7天→14天→28天，里程碑圆点 `#6FE5C6` 实心=已达、`#CBD0DA` 灰=未达）

**样式 B：年视图（“全年活跃记录”点击弹窗）** —— **GitHub 式方块矩阵**
- 12 个月横向排列，375 个小方块（10px，圆角 **3px**）
- **GitHub 式 5 档绿色阶**（实心，非透明度）：
  | 档 | 背景色 | 格数 |
  |---|---|---|
  | L0 未活跃 | `#EEF0F2` rgb(238,240,242) | 335 |
  | L1 低 | `#C6F0C2` rgb(198,240,194) | 13 |
  | L2 中 | `#7BD389` rgb(123,211,137) | 8 |
  | L3 高 | `#2EA451` rgb(46,164,81) | 5 |
  | L4 最高 | `#166633` rgb(22,102,51) | 4 |
- 弹窗顶部统计：**30 天活跃 · 8% 活跃率 · 3 周连登**；图例从左到右：未活跃(浅灰) → 浅绿 → 中绿 → 深绿 → 最深绿(活跃)；底部“数据更新于每日 02:00”
- 弹窗背景：页面模糊遮罩 + 居中白卡片

**对后续开发的参考价值**：
- 同一数据源可渲染**两种截然不同的热力图**（月历透明度档 vs 年 GitHub 实心档）→ 证明 SDK 热力图应抽象为“数据（date+value）+ 渲染样式（布局/色阶/分档方式）分离”
- 色阶两种分档策略都要支持：**单色+透明度**（月历）、**多色阶实心**（年/GitHub），与 MiniMax 的绿/黄/红阈值色阶并列为三种色阶模式
- 交互：月历格子不可点（纯展示）；年视图为弹窗 + 图例分档 + 顶部聚合统计（活跃天/活跃率/连登周）

#### 成长中心完整数字（用户核对）
| UI 文案 | 字段 | 实测值 |
|---|---|---|
| 本月已连登 【N】天 | streak.days | 1 |
| 任务徒章 【N】 | badges（auto+single 任务类）| 6（=auto 1 + single 5；beginner 5 个为成长等级类不计，全部 earned=8）|
| 本月已兑 【N】次 | redemption_status 各 tier 之和 | 0 |
| 剩余可兑 【N】天 | redemption_status.remaining_days | 1 |
| 补登卡×【N】 | makeup_cards.balance | 0（max 4）|
| 能量余额 | energy.balance | 3（consumed 70 / earned 73）|
| 等级 | profile.level | 11 |

**对 SDK 的价值**：成长中心主体是**运营游戏化**（能量/连登/徒章/盲盒/抽奖，不入用量核心表，入 Provider 专属子表）；但 **`heatmap`（365天活跃分）极有参考价值**——与 MiniMax 热力图同形，SDK 热力图应支持“任意天数 + 数值含义可配”；`streak` 连登可选入库作活跃度辅助指标。

---

## 3. 字段枚举值（实测 28 个包）

| 字段 | 出现值 | 备注 |
|---|---|---|
| `PackageName` | `CodeBuddy个人体验版`、`CodeBuddy个人版国内运营裂变包` | 套餐/活动包名 |
| `SubProductName` | `腾讯云代码助手 (IDE)`、`腾讯云代码助手 (IDE) - 赠送包` | 正式包 vs 赠送包 |
| `ResourceType` | `"2"` | 本账号只见 2 |
| `CapacityUnit` | `credits` | 统一积分单位 |
| `PkgSourceType` | `0` | 包来源 |
| `Status` | `0` | 0=正常 |
| `model`（明细表）| `hy3`、`glm-5.1`、`glm-5v-turbo`、`glm-5.2`、`glm-5.2-x`、`kimi-k2.7`、`kimi-k3-1`、`deepseek-v4-pro`、`deepseek-v4-flash`、`minimax-m3` | ★ 10 种模型（混合游步多家：混元/GLM/Kimi/DeepSeek/MiniMax），动态增长 |
| `agentPurpose`（明细表）| `conversation`、`""`、`subagent:Explore`、`custom_agent:gstack-security-officer`、`custom_agent:gstack-qa-lead`、`custom_agent:gstack-product-reviewer`、`custom_agent:software-architect`、`custom_agent:software-engineer` | ★ Agent 用途：对话/子 Agent/自定义 Agent，动态 |
| `client`（明细表）| `WorkBuddy` | 本账号仅一种 |

> 28 个包多为不同时间领取的"赠送包/裂变包"，各有独立周期与有效期。

---

## 4. 数据陷阱

### 4.1 N 个资源包（不是固定档位）
WorkBuddy 是**任意数量资源包**（本账号 28 个），每个包独立的 `CapacitySize/Used/Remain` + `CycleStartTime/EndTime` + 有效期。不同于 Qoder 固定三档。SDK 配额字段需支持"**包数组**，每包独立周期/额度/来源"。

### 4.2 整数 vs 精确值双字段
`CapacityUsed`（整数）+ `CapacityUsedPrecise`（小数）并存。SDK 入库用精确值（Precise）。

### 4.3 时间格式不统一
`CycleStartTime` 是字符串日期，`DeductionStartTime` 是毫秒时间戳。UI 显示如 "2026-08-19 17:09:44"。SDK 入库需归一化为统一格式（建议 UTC 时间戳）。

### 4.4 赠送包/补偿包独立于资源包
`check-gift-claimed`（赠送）和 `compensation-status`（补偿）是**独立接口**，与 `get-user-resource` 的资源包分开。是运营活动额度维度。

---

## 5. WorkBuddy 字段 → 统一 SDK 映射

| WorkBuddy 字段 | 语义 | 建议统一 SDK 字段名 |
|---|---|---|
| Data.TotalCount | 资源包数量 | package_count |
| Data.TotalDosage | 总消耗 | total_used_credits |
| Account.PackageName | 套餐名 | package_name |
| Account.SubProductName | 子产品 | sub_product_name |
| Account.CapacitySizePrecise | 包总额度 | package_credits_limit |
| Account.CapacityUsedPrecise | 包已用 | package_credits_used |
| Account.CapacityRemainPrecise | 包剩余 | package_credits_remaining |
| Account.CycleStartTime / EndTime | 周期起止 | package_cycle_start_at / _end_at |
| Account.PkgSourceType | 包来源 | package_source_type |
| Account.Status | 状态 | package_status |
| gift.credit_num / end_time | 赠送额度/有效期 | gift_credits / gift_expires_at |
| compensation.credit_num / active | 补偿额度/是否生效 | compensation_credits / compensation_active |
| accounts.uid / uin | 跨会话稳定账号 ID | account_stable_id（存哈希）|
| **—— 以下为明细表（request-usage）——** | | |
| history.requestId | 请求唯一 ID | request_id（去重键）|
| history.requestTime | 请求时间 | request_time |
| history.credit | 积分消耗 | request_credits |
| history.model | 模型 | request_model |
| history.client | 客户端 | request_client |
| history.agentPurpose | Agent 用途 | request_agent_purpose |
| ~~history.input / inputTrunc~~ | 🔴 用户输入 | **不入库/脱敏** |

---

## 6. 对统一 SDK 的启示

1. **N 个资源包模型**——四站里配额最灵活的（Qoder 三档、WorkBuddy 任意 N 包）。SDK 配额字段必须支持"包数组 + 每包独立周期/额度/来源/有效期"，不能用固定字段。
2. **整数/精确双值**——与 Qoder 原价/实扣双值呼应，SDK 数值字段普遍需要"展示值 + 精确值"考量。
3. **运营额度维度**（赠送包/补偿包）——WorkBuddy 特有，SDK 可预留 gift/compensation 字段组。
4. **腾讯云计费风格**（大驼峰 + Response.Data 包裹）——提取器需支持 jsonpath 深层路径 `data.Response.Data.Accounts[*]`。
5. **纯 Cookie 鉴权**——同 Qoder，登录态简单。
6. 🔴 **敏感字段**：手机号/uin 在 accounts 接口，标 Sensitive 不入库；明细表 input/inputTrunc（用户对话输入）属会话隐私，不入库或脱敏。
7. **★ 跨会话账号识别（用户问1）**：插件删除保留 DB 后重装重登，靠**平台稳定账号 ID** 识别同一账号（WorkBuddy=uid/uin、Qoder=user_id、Kimi=user_id、MiniMax=group_id）。数据库账号主键建议 `hash(ProviderId + 平台账号ID)`，既能重登匹配旧数据、又不明文存个人 ID。
8. **★ Provider 专属子表（用户问3）**：各站用量差异大，不能全泛化进 SDK 通用字段。设计采“**通用快照表（SDK 字段）+ Provider 专属子表（非泛化字段）**”：
   - 通用快照表：能泛化为 SDK 的字段（已用/总额/剩余/百分比/重置时间等）
   - Provider 专属子表：本插件特有、无法泛化的字段（如 WorkBuddy 的 agentPurpose/能量/连登、Qoder 的 discount_factor）
   - **不同频次明细分子表**：逐请求流水表（request-usage）vs 每日聚合表（MiniMax daily）vs 5h/周窗口快照——不同粒度各一张表，避免混存。

---

## 7. 复现查询脚本（浏览器 Console）

```js
// 资源包列表（28 个包）
await (await fetch('https://www.workbuddy.cn/billing/meter/get-user-resource',{method:'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:'{}'})).json();
// 赠送包
await (await fetch('https://www.workbuddy.cn/billing/meter/check-gift-claimed',{method:'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:'{}'})).json();
// 补偿包
await (await fetch('https://www.workbuddy.cn/billing/meter/compensation-status',{method:'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:'{}'})).json();
// ★ 用量明细表（参数=pageNum/pageSize/startTime/endTime，字符串日期，7天窗口）
await (await fetch('https://www.workbuddy.cn/billing/meter/get-user-request-usage',{method:'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify({pageNum:1,pageSize:100,startTime:'2026-07-15 00:00:00',endTime:'2026-07-22 23:59:59'})})).json();
// 成长中心：能量/连登/等级
await (await fetch('https://www.workbuddy.cn/activity/growth/energy',{credentials:'include'})).json();
```

---

## 附：待后续
- ~~get-user-request-usage 需参数~~ → 已破解：pageNum/pageSize/startTime/endTime（字符串日期，7天窗），total=71。
- 进度条/图表精确配色（绿色系 hex）未抓 computed style，如需还原视觉可补（同 MiniMax/Qoder 视觉补采）。
- 28 个资源包的完整列表未全部展开（仅取样本 + 枚举）；如需全量入库测试可再抓。
- 成长中心 heatmap/lottery/redeem 等运营接口未逐个展开（与用量弱相关，需时再抓）。
- 🔴 accounts 接口的手机号/uin + 明细表 input 已确认为敏感，永不入库（uid/uin 可哈希后作账号主键）。
