# Qoder 用量数据接口排查手册

> 创建日期：2026-07-22
> 用途：记录 Qoder（qoder.com）用量网页的真实接口、查询方法、完整数据结构、枚举值，供 SDK 字段设计与插件迁移（req-087/108）参考。
> 数据来源：2026-07-22 通过浏览器（chrome-devtools 实例，标准 Chrome UA + 屏蔽 webdriver 绕过风控后登录）实测抓取。
> 账号状态：Masclown（Pro+，月付 60 USD），订阅配额 0/6000，资源包 12/81，共用 461 条 Credits 记录。

---

## 0. 与 MiniMax / Kimi 的关键差异

| 维度 | MiniMax | Kimi | Qoder |
|---|---|---|---|
| 接口风格 | REST GET（Cookie+x-group-id）| gRPC-web POST（Bearer token）| **REST GET（纯 Cookie）** |
| 鉴权 | Cookie | localStorage Bearer | **纯 Cookie**（最简单）|
| 用量单位 | 百分比/次/Token | ratio 小数/credit | **Credits**（信用点）|
| 额度模型 | 5h/周 + 视频 | 总+5h+7天 + 加油包 | **三档：订阅配额 + 资源包 + 总计** |
| 历史记录 | 每日聚合数组 | 逐条流水（虚拟滚动）| **逐条流水（标准 REST 分页）** |
| 折扣维度 | 无 | 无 | **有（原价/实扣 + discount_factor）** |
| ⚠️ 登录风控 | 已登录态直接读 | 已登录态直接读 | **新登录强风控**（需屏蔽 webdriver + 标准 UA）|

---

## 1. 基础信息

- **用量页 URL**：`https://qoder.com/account/usage`
- **API 域名**：`https://qoder.com`
- **鉴权**：纯 Cookie（`credentials:'include'` 即可）
- **登录跳转链**（实测）：
  ```
  /account/usage（未登录）
    → 302 /users/sign-in?oauth_callback=.../account/usage&signInStep=passwordStep
    → 邮箱+密码 / Google / GitHub 登录
    → 回跳 /account/usage
  ```
- ⚠️ **登录风控**：Qoder 对新登录有强风控。CDP 自动化浏览器需：① UA 不含 "Qoder" 字样（用标准 Chrome UA，不要用内置 Qoder 浏览器的 `Qoder/1.1` UA）；② 屏蔽 `navigator.webdriver`（`Object.defineProperty(navigator,'webdriver',{get:()=>undefined})` via initScript）。已登录态读取则无此问题。

---

## 2. 接口详情（实测完整结构）

### 2.1 `/api/v2/me/usages/big_model_credits` — 三档配额汇总

- **URL**：`GET https://qoder.com/api/v2/me/usages/big_model_credits`
- **实测响应**：

```jsonc
{
  "user_id": "019e3126-...",
  "quota_key": "big_model_credits",
  "status": "active",
  "plan_quota": {                          // ① 订阅版配额
    "quota_summary": { "used_value": 0, "limit_value": 6000, "remaining_value": 6000, "usage_percentage": 0, "unit": "credits" },
    "quota_detail": [{ "source": "PLAN", "limit_value": 6000, "used_value": 0, "expires_at": 0 /* 0=不过期 */, "status": "ACTIVE" }]
  },
  "resource_package_quota": {              // ② 个人资源包
    "quota_summary": { "used_value": 12, "limit_value": 81, "remaining_value": 69, "usage_percentage": 15, "unit": "credits" },
    "quota_detail": [{ "source": "RESOURCE_PACKAGE_SOURCE_CARRY_OVER", "limit_value": 81, "used_value": 12, "expires_at": 1785340800000 /* 2026-07-29 有效期 */, "status": "ACTIVE" }]
  },
  "total_quota": {                         // ③ 总计（① + ②）
    "quota_summary": { "used_value": 12, "limit_value": 6081, "remaining_value": 6069, "usage_percentage": 1, "unit": "credits" },
    "quota_detail": [ /* PLAN + RESOURCE_PACKAGE 两条 */ ]
  },
  "lastResetAt": 1778941056370,            // 上次重置 2026-05-16
  "nextResetAt": 1787328000000             // ★ 下次重置/刷新 2026-08-21（UTC；UI 显示 08月21日）
}
```

### 2.2 `/api/v1/me/userplan` — 订阅计划

```jsonc
{
  "plan_tier": "PLAN_TIER_PRO_PLUS",       // 档位（UI "Pro+"）
  "is_highest_tier": false,                // 上面还有 Ultra
  "status": "USER_PLAN_STATUS_ACTIVE",
  "billing_cycle": "BILLING_CYCLE_MONTHLY",// 月付
  "amount_paid": 60, "currency": "USD",    // 60 USD/月
  "auto_renew": true,
  "start_date": 1784649553997,             // ★ 当月配额起始（UI "2026年07月21日"）
  "end_date": 1787328000000,               // 当月配额结束（UI "2026年08月21日"）
  "next_refresh_date": 1787328000000,      // ★ 刷新配额日期（= end_date）
  "capabilities": ["personal.analytics", "personal.qmind"]
}
```

> ★ **配额有效期**（UI "当月配额有效期：2026年07月21日 - 2026年08月21日；将于 08月21日 刷新配额"）：
> - 起始 = `userplan.start_date`（07-21）
> - 结束/刷新 = `userplan.end_date` = `usages.nextResetAt`（08-21）
> - 资源包单独有效期 = `resource_package_quota.quota_detail[].expires_at`（07-29，UI "剩余 69 credits，有效期至 2026年7月29日"）

### 2.3 `/api/v1/me/usages/big_model_credits/histories` — Credits 记录表（分页）

- **URL**：`GET /api/v1/me/usages/big_model_credits/histories?page=1&page_size=100&start_time=<ms>&end_time=<ms>&order_by=begin_at&order=-1`
- **分页**：标准 REST，`page_result` 给全量元信息：
  ```jsonc
  "page_result": { "prev_page": 0, "current_page": 1, "next_page": 2, "last_page": 5, "page_size": 100, "total_size": 461, "next_token": "..." }
  ```
- **单条结构**（实测，无敏感字段，可全入库）：
  ```jsonc
  {
    "time": 1784733028657,
    "begin_at": 1784733028657,             // 开始时间戳（毫秒，去重键之一）
    "finish_at": 1784733142825,            // 结束时间戳
    "source": "IDE",                       // 来源（见 §3 枚举）
    "operation": "Quest Mode",             // 操作（见 §3 枚举）
    "model_category": "Ultimate",          // 模型分级（见 §3 枚举）
    "kind": "Not Charged",                 // 是否扣费（见 §3 枚举）
    "credits": 0,                          // ★ 实扣 Credits
    "original_credits": 157.42,            // ★ 原价 Credits（划线价）
    "cost": 0,                             // ★ 实扣费用（USD）
    "original_cost": 1.57,                 // ★ 原价费用（USD）
    "discount_factor": 1,                  // 折扣系数（见 §3 枚举）
    "discount_visible": true               // 折扣是否可见
    // ⚠️ 无独立 duration 字段！“时长”为前端派生 = finish_at - begin_at
  }
  ```

- **行明细（展开/hover）——开始/结束/时长**（★用户补充）：
  表格每行展开后显示三项时间明细，均由 begin_at/finish_at 派生（接口无独立字段）：
  | 展示项 | 来源 | 实测样本（本地 UTC+8）|
  |---|---|---|
  | 开始时间 | `begin_at` 格式化 | `Jul 22, 11:10:28 PM` |
  | 结束时间 | `finish_at` 格式化 | `Jul 22, 11:12:22 PM` |
  | 时长 | `finish_at - begin_at` 前端算 | `1min 54s`（= 114 秒，实测已验证）|
  > 时间展示格式：`MMM D, h:mm:ss A`（本地时区，精确到秒）；表格主行只显示到分钟（如 "7月22日 23:10"），展开才有秒级。

### 2.4 其他接口（备查/跳过）
- `/api/v1/me` — 用户信息
- `/api/v1/products/pricing/all` — 定价（跳过）
- `/api/v2/me/joinedOrganization` — 加入的组织

---

## 3. 字段枚举值（★全量 461 条扫描）

用户要求：这些字段不止一个值，需抓全枚举。以下是扫描全部 461 条记录得到的**完整枚举**：

| 字段 | 全部出现过的值 | 备注 |
|---|---|---|
| `source` | `IDE` | 本账号仅 IDE；文档注明也支持 CLI（V0.1.8+）、JetBrains（V0.5.3+），SDK 应预留 |
| `operation` | `Quest Mode`、`Voice Input`、`Repo Wiki`、`Experts`、`Optimize Input` | 5 种操作类型 |
| `model_category` | `Ultimate`（极致）、`Auto`、`Lite`、`Vision`、`MiniMax-M3`、`Qwen3.8-Max-Preview`、`Qwen3.7-Plus`、`Qwen3.7-Max` | 分级 + 具体模型名混合 |
| `kind` | `Not Charged`（未扣费）、`Charged`（已扣费） | 2 种 |
| `discount_factor` | `1`、`0.5`、`0.4`、`0.2`、`0.1`、`0.02` | 6 种折扣档（1=原价，0.02=2 折扣到极低）|

> ⚠️ 枚举是**动态的**（模型名会随 Qoder 上新增加），SDK 不应硬编码为固定枚举，应作为**开放字符串 + 已知值参考**。

---

## 4. 数据陷阱

### 4.1 三档配额需分别存储
`plan_quota`（订阅）、`resource_package_quota`（资源包）、`total_quota`（总计）三档，各有 used/limit/remaining/percentage。**total 是前两者之和**，但资源包有独立有效期（07-29）先于订阅（08-21）过期。SDK 需支持"多来源配额 + 各自有效期"。

### 4.2 原价 vs 实扣（双值，★用户要求分开）
每条记录有**两组双值**，必须分开存：
- Credits：`original_credits`（原价划线）/ `credits`（实扣）
- 费用：`original_cost`（原价 USD）/ `cost`（实扣 USD）
- `discount_factor` 是折扣系数（原价 × factor ≈ 实扣，但优惠期 kind=Not Charged 时实扣=0）
- UI 显示如 "276.70/0.00" 和 "$2.76/0.00"（划线原价 / 实付）

### 4.3 时间戳全是毫秒 UTC
`begin_at`/`finish_at`/`nextResetAt`/`expires_at`/`start_date` 均为毫秒时间戳（UTC）。UI 显示转本地（UTC+8）。与 MiniMax/Kimi 一致，SDK 存 UTC。

### 4.4 histories 是标准 REST 分页（易抓）
不同于 Kimi 的虚拟滚动，Qoder histories 是干净的 `page/page_size/time_range` 分页 + `page_result.total_size`/`last_page`/`next_token`。声明式提取用 jsonpath + 分页循环即可，**最好抓的一个**。

---

## 5. Qoder 字段 → 统一 SDK 映射

| Qoder 字段 | 语义 | 建议统一 SDK 字段名 |
|---|---|---|
| plan_quota.used/limit/remaining | 订阅配额 | plan_credits_used / _limit / _remaining |
| resource_package_quota.* | 资源包配额 | package_credits_used / _limit / _remaining |
| resource_package.expires_at | 资源包有效期 | package_expires_at |
| total_quota.* | 总配额 | total_credits_used / _limit / _remaining |
| usage_percentage | 使用百分比 | *_used_percent |
| nextResetAt / userplan.next_refresh_date | 配额刷新日期 | quota_next_reset_at |
| userplan.start_date / end_date | 当月配额有效期 | quota_period_start_at / _end_at |
| plan_tier | 订阅档位 | subscription_tier |
| amount_paid + currency | 订阅价格 | subscription_price |
| billing_cycle | 计费周期 | subscription_cycle_type |
| history.begin_at / finish_at | 调用起止 | request_begin_at / _finish_at |
| finish_at - begin_at（派生）| 调用时长 | request_duration_sec（派生，无独立接口字段）|
| history.source | 调用来源 | request_source（IDE/CLI/JetBrains）|
| history.operation | 操作类型 | request_operation |
| history.model_category | 模型分级 | request_model |
| history.kind | 是否扣费 | request_charge_kind |
| history.original_credits / credits | 原价/实扣 Credits | request_original_credits / request_credits |
| history.original_cost / cost | 原价/实扣费用 | request_original_cost / request_cost |
| history.discount_factor | 折扣系数 | request_discount_factor |

---

## 6. 对统一 SDK 的启示

1. **三档配额模型**（订阅/资源包/总计 + 各自有效期）——比 MiniMax/Kimi 都复杂，SDK 配额字段需支持"多来源 + 独立有效期"。
2. **原价/实扣双值 + 折扣**——Qoder 独有维度，SDK 需为每个计费字段预留"标价/实付/折扣系数"三元组。
3. **history 标准 REST 分页**——最易声明式抓取（jsonpath + page 循环 + total_size 判停）；与 Kimi 虚拟滚动、MiniMax 每日聚合并列为三种明细形态。
   - ★**永久保存 + 刷新去重**（用户要求，与 Kimi 使用明细一致）：这张表需永久保存，后续刷新时按 `begin_at`（或 begin_at+operation 组合）去重；分页拉取时**遇到已保存的重复 begin_at 即可停止**（增量同步，不用重复拉到 total_size=461 全量）。
   - 时长（request_duration）为派生字段，可入库也可查询时算；建议入库以省重复计算。
4. **动态枚举**（model_category 会随上新增加）——SDK 字段应为开放字符串，不硬编码枚举。
5. **纯 Cookie 鉴权**——登录态获取最简单（对比 Kimi 需 localStorage Bearer）。
6. **request_source 维度**（IDE/CLI/JetBrains）+ **operation 维度**（Quest Mode/Repo Wiki 等）——多维分析价值高，与 Kimi 的 userAgent 维度呼应。

---

## 6.5 视觉样式 + 交互（实测 computed style，为 SDK 开发参考）

Qoder 用量页是**深色主题 + 绿色品牌色**，无图表控件（纯配额进度条 + Credits 表格）。

### 主题色
| 项 | 色值 |
|---|---|
| 页面背景 | **深黑 `#080807`**（rgb 8,8,7）|
| 品牌绿（Pro+ 徒章/升级按钮/进度条高亮）| **亮绿 `#2ADB5C`**（rgb 42,219,92）|
| 卡片/控件背景 | `#1D1D1A`（rgb 29,29,26）|

### 进度条（三档配额）
- 槽：深色 `#1D1D1A`；填充：**浅色近白 `rgba(238,238,235,0.9)`**（非绿！主用量条用浅色）
- 高 4px，圆角 2px
- 订阅配额 0% 时空条；资源包 15% 时浅色小段
> 与 MiniMax（按量分绿/黄/红）、Kimi（白+蓝双色）不同：Qoder 进度条是**单一浅色填充**（深色主题下的中性色），不用阈值色阶。

### 表格（Credits 记录）
- 深色行；“已使用 / 已获得” tab 切换（选中态文字 `rgba(238,238,235,0.9)` 亮白，未选灰 `#95958F`）
- Credits/费用列：**划线原价（灰）+ 实付（白）**（对应 original_credits/credits 双值）
- 日期范围选择器 + 分页（100 条/页）

### 对 SDK 的启示
- Qoder = **深色主题 + 单一绿品牌色**；进度条不用阈值色（中性浅色）——与 MiniMax 阈值色、Kimi 双色并列为**第三种进度条色彩策略**
- 无图表（无折线/热力图），仅进度条 + 表格→ 证明 SDK 图表应可选（有的 Provider 不需图表）
- SDK 主题适配：四站深/浅主题均有（MiniMax 浅/Qoder 深/Kimi 深/WorkBuddy 浅）→ SDK 色彩声明需适配两种主题

---

## 7. 复现查询脚本（浏览器 Console）

```js
// 三档配额汇总
await (await fetch('https://qoder.com/api/v2/me/usages/big_model_credits',{credentials:'include'})).json();
// 订阅计划
await (await fetch('https://qoder.com/api/v1/me/userplan',{credentials:'include'})).json();
// Credits 记录（分页，扫全量取枚举）
await (await fetch('https://qoder.com/api/v1/me/usages/big_model_credits/histories?page=1&page_size=100&start_time=1782000000000&end_time=1784735999999&order_by=begin_at&order=-1',{credentials:'include'})).json();
```

---

## 附：待后续
- "已获得"tab（Credits 获得记录，与"已使用"对应）未采集。
- `/api/v1/me` 用户信息字段未详列（含头像/邮箱等，注意脱敏）。
- CLI/JetBrains 来源的 history 记录未见（本账号仅 IDE），SDK 需预留 source 枚举。
- 🔴 安全：本页"注"明确"使用自带 API Key 的消耗请在供应商处查看"——Qoder 本页不含用户 API Key，但 `/api/v1/me` 可能含敏感信息，入库需脱敏。
