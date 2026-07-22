# DeepSeek 用量数据接口排查手册

> 创建日期：2026-07-23
> 用途：记录 DeepSeek 开放平台（platform.deepseek.com）用量页的接口、数据结构、3 个图表（2 种堆积柱状图 + 面积图）的样式/颜色/交互/数据，供 SDK 字段设计与插件开发参考。
> 数据来源：2026-07-23 通过浏览器（chrome-devtools 实例）实测抓取。
> 产品：DeepSeek 开放平台（API 计费型/按量付费），深色主题 #151517。
> 账号状态：Mr.M，余额 35.34 CNY，累计消费 164.65 CNY。

---

## 0. 关键特点：API 计费型（按量付费）

不同于订阅制（MiniMax/Kimi/WorkBuddy）和额度制（Qoder），DeepSeek 是**纯 API 按量付费**：
- 充值余额（钱包）+ 累计消费 + token 消耗
- 图表按**模型分组**展示（每个模型组一对"请求面积图 + token 柱状图"）
- 峰谷定价（高峰时段 2 倍价，北京时间 9-12/14-18）

### 与其他站对比

| 维度 | 订阅制(MiniMax/Kimi/WB) | 额度制(Qoder) | **计费制(DeepSeek)** |
|---|---|---|---|
| 计价 | 套餐/额度 | Credits | **钱包余额 CNY + token** |
| 主图表 | 进度条/热力图 | 进度条+表格 | **堆积柱状图×2 + 面积图** |
| 分组 | 用量类型 | 配额档 | **按模型分组** |
| 鉴权 | Cookie/Bearer | Cookie | **Bearer(userToken)** |

---

## 1. 基础信息

- **用量页 URL**：`https://platform.deepseek.com/usage`
- **API 域名**：`https://platform.deepseek.com/api/v0`
- **鉴权**：`localStorage.userToken`（appKit JSON，取 `.value`）→ `Authorization: Bearer <token>`
  - ⚠️ 纯 Cookie 返回 `40002 Missing Token`（同 Kimi 需 Bearer）
- **登录跳转链**：`/usage → /sign_in（手机号+验证码/微信扫码/密码）→ 回跳 /usage`
- **时区**：所有日期按 UTC+0 显示，数据 5 分钟延迟

---

## 2. 接口详情（实测）

### 2.1 `users/get_user_summary` — 账户汇总
```jsonc
{
  "current_token": 10000000,
  "monthly_usage": "265080031",          // 月 token 用量
  "total_usage": 0,
  "normal_wallets": [{ "currency":"CNY", "balance":"35.35", "token_estimation":"11782393" }],  // 充值余额
  "bonus_wallets": [{ "currency":"CNY", "balance":"0", "token_estimation":"0" }],               // 赠送余额
  "total_available_token_estimation": "11782393",
  "monthly_costs": [{ "currency":"CNY", "amount":"25.25" }],      // 月消费
  "monthly_token_usage": "265080031",
  "total_costs": [{ "currency":"CNY", "amount":"164.66" }]        // 累计消费
}
```

### 2.2 `usage/by_api_key/cost` — 消费金额时序（图A 数据源）
- **URL**：`GET .../api/v0/usage/by_api_key/cost?start=<秒>&end=<秒>&tz=0`
- **结构**：
  ```jsonc
  {
    "start":1782172800, "end":1784764800,
    "bucket": 86400,                        // 按天（86400秒）
    "models": ["deepseek-chat & deepseek-reasoner", "deepseek-v4-pro", "deepseek-v4-flash"],
    "data": [{ "currency":"CNY", "series":[
      { "api_key": { "name":"Qoder", "valid":true /* 🔴 sensitive_id/tracking_id 跳过 */ },
        "model":"deepseek-chat & deepseek-reasoner",
        "buckets":[{ "time":1782172800, "cost":"0" }, ...] }
    ]}]
  }
  ```

### 2.3 `usage/by_api_key/amount` — token/请求时序（图B/C 数据源）
- **URL**：`GET .../api/v0/usage/by_api_key/amount?start=<秒>&end=<秒>&tz=0`
- **结构**：`series` 在顶层，每条 = api_key×model
  ```jsonc
  {
    "bucket": 86400,
    "models": ["deepseek-v4-flash", "deepseek-chat & deepseek-reasoner", "deepseek-v4-pro"],
    "series": [{
      "api_key": {...}, "model": "deepseek-chat & deepseek-reasoner",
      "buckets": [{ "time":1782172800, "usage": {
        "RESPONSE_TOKEN": 0,           // ★ 输出 token
        "REQUEST": 0,                  // ★ 请求次数
        "PROMPT_CACHE_HIT_TOKEN": 0,   // ★ 缓存命中 token
        "PROMPT_CACHE_MISS_TOKEN": 0   // ★ 缓存未命中 token
      }}]
    }]
  }
  ```

### 2.4 🔴 `users/get_api_keys` — 跳过
含 API Key（sk-***），敏感，不采集。

---

## 3. 模型枚举（实测，接口固定 3 组）

| 模型组 | 说明 | 上月请求数（实测）|
|---|---|---|
| `deepseek-chat & deepseek-reasoner` | ★ **chat + reasoner 合并为一组**（接口层已合并）| 0 |
| `deepseek-v4-pro` | V4 Pro | 2 |
| `deepseek-v4-flash` | V4 Flash（主力）| 3109 |

> ★ 用户观察：切"上个月"时间维度能看到更多模型分区——实际是接口固定返回这 3 组，UI 只渲染**有数据的模型组**（每组一对图）。`deepseek-chat & deepseek-reasoner` 在接口层就是合并的（用 ` & ` 连接）。

---

## 4. 三个图表详解（★用户重点：2 种堆积柱状图 + 面积图）

> 图表库：ECharts（SVG 渲染，非 canvas）；深色主题背景 `#151517`。

### 图A：消费金额 —— 堆积柱状图①（橙色系）
- **类型**：堆积柱状图（多模型堆叠）
- **颜色**（3 模型堆叠，橙→黄橙）：
  | 层 | 颜色 |
  |---|---|
  | 模型1 | `#FF810C`（橙）|
  | 模型2 | `#FFA10A`（橙黄）|
  | 模型3 | `#FFC104`（黄）|
- **交互**：
  - ★ **视角切换 tab**：`模型` / `API Key`（同一图两种堆叠维度）
  - ★ **hover tooltip**：`2026-07-03  ¥0.43 / deepseek-v4-flash ¥0.43`（日期 + 各模型分项金额）
  - 切片器：时间维度（近30天/上月等）+ API Key 筛选 + 导出
- **数据源**：`cost` 接口，bucket=86400（按天）

### 图B：API 请求次数 —— 面积图（蓝色）
- **类型**：面积图（单序列，带渐变填充）
- **颜色**：蓝色渐变填充 `url(#zr0-g0)`（`#70B2FE` @ 0.7 半透明 → 透明）
- **布局**：**per-model 独立**（每个模型组一个面积图，如 flash 3674 / pro 21）
- **数据源**：`amount` 接口的 `REQUEST` 字段

### 图C：Tokens —— 堆积柱状图②（浅蓝系）
- **类型**：堆积柱状图（token 类型分层堆叠）
- **颜色**（3 层，深→浅蓝）：
  | 层 | 颜色 |
  |---|---|
  | 深 | `#0C70F3` |
  | 中 | `#60B3FE` |
  | 浅 | `#A0DCFD` |
- **布局**：**per-model 独立**（每个模型组一个 token 柱状图，如 flash 314,592,356 / pro 1,274,784）
- **数据源**：`amount` 接口的 `RESPONSE_TOKEN` / `PROMPT_CACHE_HIT_TOKEN` / `PROMPT_CACHE_MISS_TOKEN` 堆叠（缓存命中/未命中/输出分层）

### 图表交互总结
| 图表 | 类型 | 交互 |
|---|---|---|
| 消费金额 | 堆积柱状图（橙）| 模型/API Key 切换 + hover tooltip + 时间/Key 切片器 |
| API请求 | 面积图（蓝）| per-model + hover tooltip |
| Tokens | 堆积柱状图（浅蓝）| per-model + hover tooltip（token 分层）|

---

## 5. ★ 按模型分开渲染 → SDK 合并字段考虑（用户提出）

**现状**：图B（请求面积图）和图C（token 柱状图）都是**每个模型组各渲染一对**（flash 一对、pro 一对、chat&reasoner 一对）。

**对 SDK 的启示**：
- 数据本质是"**模型 × 日期 × 指标**"的三维结构，UI 按模型拆成多个图只是展示选择
- SDK 入库应**合并为一张明细表**（不按模型拆表）：
  ```
  usage_model_daily(provider_id, account_id, date, model, request_count,
                    response_token, cache_hit_token, cache_miss_token, cost)
  ```
- 渲染时按 model 分组即可还原"每模型一对图"，或聚合成"全模型堆叠图"（图A 就是聚合视角）
- 与其他站呼应：MiniMax 的 per-model 明细表（决策⑥）、WorkBuddy 明细的 model 维度、TRAE 模型偏好 —— **统一为"模型维度用量明细表"**

---

## 6. DeepSeek 字段 → 统一 SDK 映射

| DeepSeek 字段 | 语义 | 建议 SDK 字段名 |
|---|---|---|
| normal_wallets.balance | 充值余额 | balance_amount |
| bonus_wallets.balance | 赠送余额 | bonus_balance_amount |
| total_available_token_estimation | 可用 token 估算 | available_token_estimation |
| monthly_costs.amount | 月消费 | monthly_cost |
| total_costs.amount | 累计消费 | total_cost |
| monthly_token_usage | 月 token | monthly_token_usage |
| amount.usage.REQUEST | 请求次数 | request_count |
| amount.usage.RESPONSE_TOKEN | 输出 token | response_token |
| amount.usage.PROMPT_CACHE_HIT_TOKEN | 缓存命中 token | cache_hit_token |
| amount.usage.PROMPT_CACHE_MISS_TOKEN | 缓存未命中 token | cache_miss_token |
| cost.buckets.cost | 每日消费 | daily_cost |
| model | 模型（组）| model_name |
| ~~api_key.sensitive_id / tracking_id~~ | 🔴 密钥/追踪 | **不入库** |

---

## 7. 对统一 SDK 的启示

1. **计费制新样本**：钱包余额（充值+赠送）+ 累计消费 + token，SDK 需支持"货币余额"类字段（对比订阅制的百分比、Qoder 的 Credits）。
2. **缓存命中维度**：DeepSeek 明确区分 `CACHE_HIT/CACHE_MISS token`——与 MiniMax 的 cache_hit_percent 呼应，SDK 应有缓存命中通用字段。
3. **模型维度合并表**（用户提出）：图按模型拆分只是展示，入库应合并为一张 model×date 明细表。
4. **三种图表类型**：堆积柱状图（多模型/多token层）+ 面积图 —— SDK 图表库需支持"堆叠系列"。
5. **视角切换**（模型/API Key）：同一数据两种分组维度，SDK 图表可支持"分组维度切换"。
6. 🔴 **敏感**：userToken、api_key sensitive_id/tracking_id，标 Sensitive 不入库。

---

## 8. 复现查询脚本（浏览器 Console）

```js
// 取 token
let raw = localStorage.getItem('userToken');
let token = raw; try { token = JSON.parse(raw).value; } catch(e) {}
const h = { 'Authorization': 'Bearer ' + token };
// 账户汇总
await (await fetch('https://platform.deepseek.com/api/v0/users/get_user_summary', {credentials:'include', headers:h})).json();
// 消费金额时序（图A）
await (await fetch('https://platform.deepseek.com/api/v0/usage/by_api_key/cost?start=1782172800&end=1784764800&tz=0', {credentials:'include', headers:h})).json();
// token/请求时序（图B/C）
await (await fetch('https://platform.deepseek.com/api/v0/usage/by_api_key/amount?start=1782172800&end=1784764800&tz=0', {credentials:'include', headers:h})).json();
```

---

## 附：待后续
- 图B/C 的 hover tooltip 精确文案未逐字抓（ECharts SVG hover 需坐标触发）；图A tooltip 已确认 `日期+模型+金额`。
- 账单页（/账单）、充值页未采集。
- 峰谷定价对 cost 的影响（高峰 2 倍）未在数据结构中体现，可能已含在 cost 值里。
- 🔴 userToken / api_key 敏感字段，永不入库。
