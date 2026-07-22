# Kimi 用量数据接口排查手册

> 创建日期：2026-07-22
> 用途：记录 Kimi（www.kimi.com）用量网页的真实接口、查询方法、完整数据结构，供 SDK 字段设计与插件迁移参考。
> 数据来源：2026-07-22 通过浏览器（已登录 www.kimi.com）实测抓取。
> 账号状态：Allegretto 会员（年付 ¥1908），总使用量 21.08%，5h Code 0%，7天 Code 100%，下次续费 2027-07-19。

---

## 0. 与 MiniMax 的关键差异

| 维度 | MiniMax | Kimi |
|---|---|---|
| 接口风格 | REST GET（Cookie + x-group-id）| **gRPC-web POST**（`/apiv2/kimi.gateway.membership.v2.MembershipService/*`）|
| 鉴权 | Cookie Session | **Authorization: Bearer <access_token>**（localStorage.access_token）|
| ⚠️ 纯 Cookie 能否调接口 | 能 | **不能！** 必须带 Authorization header，否则 401 REASON_INVALID_AUTH_TOKEN |
| 用量表达 | 百分比字符串 "66%" | **ratio 小数** 0.2108（= 21.08%）|
| 额度模型 | 5h/周 双窗口 + 视频次数 | **总额度 + 5h + 7天** 三窗口，Code/Kimi 分类 |
| 历史趋势 | 有（168 天每日聚合数组）| 有（使用明细逐条消耗流水，需按天聚合）|
| 进度条色阶 | 绿/黄/红阈值色阶 | 总用量条双色分段（Kimi=白 / Code=蓝 #1A88FF）；5h/7天单色蓝 |

---

## 1. 基础信息

- **用量页 URL**：`https://www.kimi.com/membership/subscription?tab=quota`
- **API 域名**：`https://www.kimi.com`
- **鉴权**：`localStorage.getItem('access_token')` → `Authorization: Bearer <token>`
- **查询方法**：POST，body `{}`，需带 Authorization header

```js
const tok = localStorage.getItem('access_token');
const r = await fetch('https://www.kimi.com/apiv2/kimi.gateway.membership.v2.MembershipService/GetSubscription',
  {method:'POST', credentials:'include', headers:{'Content-Type':'application/json','Authorization':'Bearer '+tok}, body:'{}'});
const j = await r.json();
```

⚠️ **登录态获取要点**：Kimi 的 token 在 localStorage（不在 Cookie），插件迁移时需要能读取页面 localStorage 或 DOM 提取，纯 Cookie 注入方式无法调用其 gRPC 接口。

---

## 2. 接口详情（实测完整结构）

### 2.1 `GetSubscription` — 订阅信息 + 总额度

- **URL**：`POST https://www.kimi.com/apiv2/kimi.gateway.membership.v2.MembershipService/GetSubscription`
- **实测响应**（2026-07-22）：

```jsonc
{
  "subscription": {
    "subscriptionId": "19f793f0-...",
    "goods": {
      "title": "Allegretto",                    // ★ 订阅档位名称（= UI 顶部）
      "durationDays": 30,
      "membershipLevel": "LEVEL_INTERMEDIATE",  // 会员等级枚举
      "amounts": [{ "currency": "CNY", "priceInCents": "190800" }],  // ¥1908
      "billingCycle": { "duration": 1, "timeUnit": "TIME_UNIT_YEAR" }, // 年付
      "type": "GOODS_TYPE_SUBSCRIPTION"
    },
    "subscriptionTime": "2026-07-19T07:20:02Z",
    "currentStartTime": "2026-07-19T07:20:02Z",
    "currentEndTime": "2027-07-20T00:00:00Z",
    "nextBillingTime": "2027-07-19T07:20:02Z",  // ★ 下次续费（= UI "2027-07-19"）
    "status": "SUBSCRIPTION_STATUS_ACTIVE",
    "paymentChannel": "PAYMENT_CHANNEL_ALIPAY",
    "active": true
  },
  "balances": [
    {
      "id": "19f793f6-...",
      "feature": "FEATURE_OMNI",                // 额度类型
      "type": "SUBSCRIPTION",
      "unit": "UNIT_CREDIT",                    // 单位=积分
      "amountUsedRatio": 0.2108,                // ★ 总使用量比例（= UI "21.08%"）
      "expireTime": "2026-08-19T07:20:02Z"      // ★ 额度重置时间（= UI "2026-08-19 后重置"）
    }
  ],
  "subscribed": true,
  "purchaseSubscription": { /* 与 subscription 结构相同 */ }
}
```

### 2.2 `GetSubscriptionStats` — 5h/7天 限流 + 使用比例

- **URL**：`POST https://www.kimi.com/apiv2/kimi.gateway.membership.v2.MembershipService/GetSubscriptionStats`
- **实测响应**：

```jsonc
{
  "ratelimitCode5h": {
    "enabled": true,
    "resetTime": "2026-07-22T15:20:01Z"         // ★ 5h 重置时间（= UI "07-22 23:20 后重置"，UTC+8）
    // 注意：ratio 缺失 = 0%（UI "5小时用量 Code 0%"）
  },
  "ratelimitCode7d": {
    "ratio": 1,                                 // ★ 7天用量比例 = 100%（= UI "7天用量 Code 100%"）
    "enabled": true,
    "resetTime": "2026-07-26T07:20:01Z"         // ★ 7天重置（= UI "07-26 15:20 后重置"，UTC+8）
  },
  "subscriptionBalance": {
    "feature": "FEATURE_OMNI",
    "unit": "UNIT_CREDIT",
    "amountUsedRatio": 0.2108,                  // ★ 总使用量（Kimi + Code 合计）
    "kimiCodeUsedRatio": 0.2108,                // ★ Kimi Code 单独使用比例
    // ⚠ 无独立 kimiUsedRatio 字段！Kimi（非 Code）用量 = amountUsedRatio - kimiCodeUsedRatio = 0
    "expireTime": "2026-08-19T07:20:02.473155Z" // ★ 总额度重置时间（= UI "2026-08-19 后重置"）
  },
  "boosterWallets": [                           // ★ 额度加油包（之前截断漏掉）
    {
      "id": "19f8a41c-...",
      "moneyLeft":  { "currency": "CNY", "priceInCents": "0" },      // ★ 当前余额（= UI "¥0"）
      "moneyTotal": { "currency": "CNY", "priceInCents": "0" },      // 加油包总额
      "status": "STATUS_ACTIVE",                                     // ★ 已开启（= UI "已开启"）
      "allowTopup": true,
      "topupLimit":  { "currency": "CNY", "priceInCents": "300000" }, // 充值上限 ¥3000
      "monthlyChargeLimit": { "currency": "CNY", "priceInCents": "10000" }, // 月消费上限 ¥100（注：UI 显示"无限制"，需核实）
      "monthlyUsed": { "currency": "CNY", "priceInCents": "0" },     // ★ 本月消费（= UI "¥0"）
      "autoRefillCharge":    { "priceInCents": "0" },                // 自动充值金额
      "autoRefillThreshold": { "priceInCents": "0" }                 // 自动充值阈值
    }
  ]
}
```

> ⚠️ **之前截断漏掉 `boosterWallets`**（额度加油包），本次已补全。UI "额度加油包 / 已开启 / 当前余额 ¥0 / 本月消费 ¥0 / 无限制" 均来自此。

### 2.3 其他相关接口（未深挖，备查）
- `MembershipService/ListSubscriptions` — 订阅列表（历史订阅）
- `UserService/GetCurrentUser` — 当前用户信息
- `GET /api/user?t=<ts>` — 用户基础信息

---

## 3. 字段 → UI 映射（实测吻合）

| UI 显示 | 来源字段 | 值 |
|---|---|---|
| Allegretto | GetSubscription.subscription.goods.title | "Allegretto" |
| 下次自动续费 2027-07-19 | subscription.nextBillingTime | 2027-07-19 |
| 总使用量 21.08% | balances[0].amountUsedRatio | 0.2108 |
| 2026-08-19 后重置 | balances[0].expireTime | 2026-08-19 |
| 5小时用量 Code 0% | Stats.ratelimitCode5h（无 ratio = 0）| — |
| 07-22 23:20 后重置 | ratelimitCode5h.resetTime（UTC→UTC+8）| 15:20Z→23:20 |
| 7天用量 Code 100% | ratelimitCode7d.ratio | 1 |
| 07-26 15:20 后重置 | ratelimitCode7d.resetTime（UTC+8）| 07:20Z→15:20 |
| 额度加油包 已开启 | boosterWallets[0].status | STATUS_ACTIVE |
| 当前余额 ¥0 | boosterWallets[0].moneyLeft | 0 分 |
| 本月消费 ¥0 / 无限制 | boosterWallets[0].monthlyUsed / monthlyChargeLimit | 0 / 10000分（UI 显"无限制"）|

---

## 4. 数据陷阱

### 4.1 ratio 缺失 = 0
`ratelimitCode5h` 无 `ratio` 字段时表示 0%（不是 null 报错）。解析时缺失应按 0 处理。

### 4.2 时间是 UTC，UI 显示 UTC+8
所有 `resetTime`/`expireTime` 是 UTC ISO 8601，UI 显示时 +8 小时。SDK 入库建议存 UTC，显示时转本地（与 MiniMax req-067 一致）。

### 4.3 用量是 ratio 小数（0-1），非百分比
Kimi 用 `0.2108` 表示 21.08%；MiniMax 用 `"53%"` 字符串。SDK 统一字段应存 **0-100 的 double**（Kimi ×100，MiniMax 去 % 号）。

### 4.4 Kimi / Code 双维度（★用户澄清）
Kimi 额度分 **Kimi / Code** 两类（UI 总使用量进度条两色）。接口只给两个字段：
- `amountUsedRatio`（总 = Kimi + Code 合计）
- `kimiCodeUsedRatio`（Code 单独）
- 无独立 kimiUsedRatio 字段** → Kimi（非 Code）用量需**推算**：`kimi = amountUsedRatio - kimiCodeUsedRatio`。
- 实测（刷新后）：总 0.2196，Code 0.2108 → **Kimi 类 = 0.0088 ≈ 0.88%**（首次拓时恰好为 0 是巧合，Kimi 类确实有独立用量）。
- SDK 设计：存 `total_used_percent` + `code_used_percent`，Kimi 类用量由主程序相减派生 `kimi_used_percent = total - code`（无独立接口字段）。

### 4.5 趋势数据以“逐条消耗记录”形式存在（★修正前述结论）
**修正**：之前误判“Kimi 无趋势数据”。实际在“使用明细” tab 有 **Code 订阅额度消耗流水表**（逐条记录，非每日聚合）：
- 每条 = `消耗百分比` + `类型（Kimi Code）` + `时间戳`
- 实测样本：`0.01% / Kimi Code / 2026-07-22 09:05`、`0.09% / Kimi Code / 2026-07-22 08:58` …（同一分钟可多条）
- 与 MiniMax 的“每日聚合”不同，Kimi 是“**每次调用的额度消耗流水**”，需按日期聚合后才能画折线/热力图。
- 折线/热力图**可支持**（对流水按天 sum 后即得每日消耗），但数据源是“流水表”而非“现成数组”。详见 §8 使用明细。

---

## 5. Kimi 可提取字段清单（映射到统一 SDK）

| Kimi 字段 | 语义 | 建议统一 SDK 字段名 |
|---|---|---|
| goods.title | 订阅档位 | subscription_tier |
| nextBillingTime | 下次续费 | subscription_next_billing_at |
| currentEndTime | 订阅到期 | subscription_end_at |
| amounts[].priceInCents + currency | 价格 | subscription_price（分→元）|
| billingCycle | 计费周期 | subscription_cycle_type |
| balances[].amountUsedRatio | 总使用量 | total_used_percent（×100）|
| kimiCodeUsedRatio | Code 使用比例 | code_used_percent（Kimi 特有分类）|
| amountUsedRatio - kimiCodeUsedRatio | Kimi（非 Code）用量 | kimi_used_percent（派生，无独立接口字段）|
| balances[].expireTime | 总额度重置 | total_quota_reset_at（= 2026-08-19）|
| ratelimitCode5h（有无 ratio）| 5h 用量 | five_hour_used_percent |
| ratelimitCode5h.resetTime | 5h 重置 | five_hour_reset_at（= 07-22 23:20）|
| ratelimitCode7d.ratio | 7天用量 | seven_day_used_percent（★新窗口，MiniMax 是周/weekly）|
| ratelimitCode7d.resetTime | 7天重置 | seven_day_reset_at（= 07-26 15:20）|
| membershipLevel | 会员等级 | subscription_level |
| boosterWallets[].status | 加油包开启 | booster_enabled |
| boosterWallets[].moneyLeft | 加油包当前余额 | booster_balance（分→元）|
| boosterWallets[].monthlyUsed | 加油包本月消费 | booster_monthly_used |
| boosterWallets[].monthlyChargeLimit | 加油包月消费上限 | booster_monthly_limit（UI "无限制"待核）|

---

## 6. 对统一 SDK 的启示

1. **用量窗口不统一**：MiniMax 是 5h + **周(weekly)**；Kimi 是 5h + **7天(7d)** + 总额度。SDK 需要区分"周"和"7天滚动"两个概念，或统一为可配置的窗口标签。
2. **Code/Kimi 子分类**：Kimi 有"功能分类"维度（Code vs Kimi），MiniMax 有"模型分类"维度（general vs video）。SDK 的 planType/子维度设计需容纳这类"同 Provider 内多类别"。
3. **鉴权差异大**：MiniMax=Cookie、Kimi=localStorage Bearer token。登录态模块（req-096 AuthManager）需支持"从 localStorage 提取 token"这种方式。
4. **无趋势数据的 Provider**：Kimi 无每日趋势 → 其 defaults.json 不声明 Line/HeatMap 图表。印证"图表能力由插件按真实数据声明"的设计。
5. **ratio 小数 vs 百分比字符串**：SDK 入库统一为 0-100 double。

---

## 7. 复现查询脚本（浏览器 Console）

```js
const tok = localStorage.getItem('access_token');
const call = async (ep) => (await (await fetch(
  'https://www.kimi.com/apiv2/kimi.gateway.membership.v2.MembershipService/'+ep,
  {method:'POST',credentials:'include',headers:{'Content-Type':'application/json','Authorization':'Bearer '+tok},body:'{}'}
)).json());
console.log('subscription', await call('GetSubscription'));
console.log('stats', await call('GetSubscriptionStats'));
```

---

## 8. 进度条视觉样式（实测 computed style）

Kimi 用量页使用**两种进度条**，SDK 色阶/背景声明可参考：

### 8.1 结构：槽（背景）+ 填充 两层
| 部件 | class | 背景色 | 高度 | 圆角 |
|---|---|---|---|---|
| 进度条槽（背景/剩余）| `kimi-progress-remaining` | `rgba(255,255,255,0.1)`（深色主题半透明白）| 8px | 2px |
| 进度条填充（Code）| `blue` | `rgb(26,136,255)`（#1A88FF 亮蓝）| 8px | 2px |
| 徽章/标签底 | `next-sidebar-nav-item__badge` | `rgba(26,136,255,0.1)`（蓝 10% 透明）| — | 4px |

### 8.2 两种进度条
- **总使用量进度条（双色）**：★用户纠正：**Kimi 段 = 白色，Kimi Code 段 = 蓝色 #1A88FF**（不是都蓝）。
  - 截图实证（总 21.96%）：最左侧一小段**白色**（≈ 0.88% = Kimi）+ 紧接**蓝色**段（21.08% = Code）+ 深灰槽。
  - 图例：`■ Kimi`（白色方块） + `■ Code`（蓝色方块）。
  - ⚠️ 之前误记为“均为蓝色”——因刷新前 Kimi 用量=0，白色段未渲染导致漏看。
- **5h / 7天 进度条（单色）**：`#1A88FF` 填充 + `rgba(255,255,255,0.1)` 槽，高 8px，圆角 2px（仅 Code，无白色段）。
- **加油包余额进度条**：本月消费条，槽同为半透明白，余额 ¥0 时填充为空。

### 8.3 对 SDK 的启示
- Kimi 总用量条是**双色分段**（白 Kimi + 蓝 Code），不是单色；5h/7天是单色蓝。→ SDK 需支持“一根进度条多段堆叠 + 每段独立颜色”（req-104 多进度条的变体：同一条内多字段堆叠）。
- 进度条槽背景是**半透明白**（适配深色主题），SDK 若要还原需支持“槽背景色”声明。
- 高度 8px / 圆角 2px 与项目 ProgressBarHeight token（req-072 U-17 统一 8px）一致。

---

## 9. 使用明细表格（Code 订阅额度消耗流水）

### 9.1 数据形态
“使用明细” tab 下是**逐条消耗流水**（infinite-table 虚拟列表，非标准 `<table>`）。顶部有**两个分类 tab**：`订阅额度` / `加油包额度`（分开展示两类消耗）。

**表头两列：`消耗` | `百分比`**（实测 DOM）：

| 列 | class | 内容（★“消耗”列含两个信息）| 实测样本 |
|---|---|---|---|
| 消耗 | `infinite-table-cell left column` | **类型标签（Kimi Code）+ 消耗日期时间** | `Kimi Code 2026-07-22 09:08` |
| 百分比 | `infinite-table-cell right row` | 本次消耗占总额度比例（独立列）| `0.14%` |

> ★ 用户澄清：“消耗”列**不是单一标签**，而是 `Kimi Code`（类型）+ 右侧`日期时间` 两个信息合一列；“百分比”是独立的第二列。

### 9.2 无限滚动分页（★用户澄清）
- 表格容器 class = `infinite-table-scroll`，**鼠标滚动到底部自动加载历史数据**。
- 实测：连续滚动 DOM 行数 160 → 180 → 240 递增，最后加载到 `2026-07-21 18:27`。
- ⚠️ **滚动时无新网络请求**（apiv2 请求数 19 不变）→ 判断为**一次性拉全量数据 + 前端虚拟滚动分批渲染**（DOM 只渲染可见部分，滚动时增量渲染内存中已有数据）。

### 9.3 接口
- 表格数据**已通过 DOM 抓到**（类型 + 日期时间 + 百分比）；行 class `details-row`。
- 全量数据已在前端内存（虚拟滚动），未单独分页接口；原始数据可能在切 tab 时一次性请求的响应中（未定位到具体服务名，待深挖）。
- 提取策略：DOM 行提取（`.details-row` → 左 cell 拆类型+时间、右 cell 取百分比），需模拟滚动到底才能拿到全部行。

### 9.4 对 SDK 的价值
- 这是 Kimi 唯一的**时序数据源** → 支持折线图（按天聚合消耗%）、明细列表
- 需要一张 `usage_detail_record` 表（provider_id, account_id, timestamp, category, consume_percent），按时间戳去重
- ★**增量去重滚动策略**（用户提出）：首次全量滚动拉取历史；持久化后，后续刷新只需滚动到**遇到已持久化的重复时间戳即可停止**（不用重复滚到底）——类似增量同步。
- 与 MiniMax 的 daily_token 明细表结构不同（MiniMax 是每日聚合，Kimi 是每次调用流水）→ SDK 明细表设计需容纳两种粒度

---

# 10. Kimi Code Console 页（code/console）

> 页面：`https://www.kimi.com/code/console`（Kimi Code 控制台，与 membership 用量页互补）
> 🔴 **安全约束（用户明确要求）**：本页含 API ID / API Key（`APIKeyService/ListAPIKeys`、`ListUnifiedRequests` 的 `apiKey.key`/`apiKey.id`）——**一律跳过，不采集、不入库、不记录**。

### 10.1 额度使用（本周 + 频限）
页面 UI（来自 `BillingService/GetUsages`，需 `scope` 枚举参数；DOM 实测）：

| UI 显示 | 语义 | 实测值 |
|---|---|---|
| 本周用量 100% | Code 周额度已用比例 | 100% |
| 88 小时后重置 | 周额度重置倒计时 | 88h |
| 频限明细 0% | 5h 频限已用比例 | 0% |
| 18 分钟后重置 | 5h 频限重置倒计时 | 18min |
| 会员 Allegretto / 旗舰模型 K3 | 订阅档位 + 当前模型 | — |
| 加油包余额 ¥0.00 / 本月消费 ¥0.00 无限制 | 与 membership 页 boosterWallets 一致 | — |

> 注：本页“本周用量/频限”与 membership 页的 `ratelimitCode7d`/`ratelimitCode5h` 同源（都是 Code 的 7天/5h 限流），但本页额外给了**重置倒计时的人读文本**（88小时/18分钟）。

### 10.2 使用记录表（`code.v1.UsageService/ListUnifiedRequests`）
每条一次调用记录，实测字段（**已剔除 apiKey 敏感子字段**）：

| 字段 | 语义 | 实测样本 | 入库 |
|---|---|---|---|
| `requestId` | 请求唯一 ID | `bef2fd2b-85dc-4958-9152-02d98e7d7a5b` | ✅（去重键）|
| `requestTime` | 请求时间（UTC）| `2026-07-22T03:06:49Z` | ✅ |
| `callType` | 调用类型 | `Model Inference` | ✅ |
| `httpStatusCode` | HTTP 状态码 | `403` / `200` | ✅ |
| `modelDisplayName` | 模型名 | `kimi-for-coding` | ✅ |
| `userAgent` | 调用端 UA | `Qoder/1.0` | ✅（可看出哪个客户端）|
| `apiKey.scope` | 能力范围 | `["FEATURE_CODING"]` | ✅ |
| `apiKey.name` | Key 名称（如 Qoder/ClawX）| `Qoder` | ⚠️ 可选（区分客户端，非密钥本身）|
| ~~`apiKey.key`~~ | 密钥（部分）| `sk-ki...` | 🔴 **禁止入库** |
| ~~`apiKey.id`~~ | API ID | `19f7951a-...` | 🔴 **禁止入库** |

**特征**：
- 响应较大（实测 >46KB），包含多条请求流水
- `userAgent` 可区分调用客户端（Qoder / ClawX / Kimi CLI 等）——**这是很有价值的维度**（可分析“哪个工具消耗最多”）
- `httpStatusCode` 可统计成功/失败率（403 = 限流/鉴权失败）

### 10.3 对 SDK 的价值
- 使用记录表是 Kimi Code 的**逐请求流水**，比 membership 页的“消耗%”更细（到单次请求 + 模型 + 状态码 + 客户端）
- 新增维度：`userAgent`（客户端）、`callType`、`httpStatusCode`——SDK 字段可考虑 `request_client` / `request_status` / `request_model`
- 去重键：`requestId`（全局唯一）
- 🔴 敏感字段隔离：apiKey.key / apiKey.id 标 Sensitive，**与 MiniMax api_key 同等级，绝不入库/日志**

---

## 附：待后续
- ~~Kimi 类（非 Code）用量字段~~ → 已澄清：无独立字段，= amountUsedRatio - kimiCodeUsedRatio（本账号 = 0）
- ~~额度加油包~~ → 已补：boosterWallets（moneyLeft/monthlyUsed/monthlyChargeLimit 等）
- `使用明细` tab 未采集（可能有更细数据）。
- access_token 过期刷新机制（有 refresh_token）未研究。
- monthlyChargeLimit 实测为 10000 分（¥100），但 UI 显示"无限制"，需核实是否有另一个"无限制"标志字段。
