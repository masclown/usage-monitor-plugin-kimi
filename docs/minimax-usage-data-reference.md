# MiniMax 用量数据接口排查手册

> 创建日期：2026-07-22
> 用途：记录 MiniMax 用量网页的**真实接口、查询方法、完整数据结构、数据陷阱**，供未来排查、SDK 字段设计、插件迁移参考。
> 数据来源：2026-07-22 通过浏览器（已登录 platform.minimaxi.com）实测抓取，非读代码推断。
> 账号状态：TokenPlanMax-年度会员，5h 已用 1%，周已用 53%，累计 5.54B token，活跃 37 天。

---

## 0. 快速索引

| 数据类别 | 来源接口 | 关键字段 |
|---|---|---|
| 5h/周 用量百分比 + 重置倒计时 | `remains_percent` | current_interval/weekly_used_percent, remains_time |
| 视频赠送次数（5h + 周）| `remains_percent`（model_name=video）| current_interval/weekly_total/remains_count |
| 每日趋势（折线图）| `usage_summary` → daily_token_usage | 168 长数组 |
| 每日明细（热力图 + 顶部数字）| `usage_summary` → date_model_usage | 168 长对象数组，含 per-model |
| 累计/排名/活跃天 | `usage_summary` | total_token_consumed, usage_ranking_percent, active_days |
| 积分 | `token_plan_credit` | total/used/remaining_credits（⚠️ 含 api_key 敏感）|
| 订阅档位名称 | **DOM 渲染**（接口源待深挖）| `Token Plan · TokenPlanMax-年度会员` |

---

## 1. 基础信息

### 1.1 域名与登录
- **控制台域名**：`https://platform.minimaxi.com`（国内版）/ `https://platform.minimax.io`（Global）
- **用量页 URL**：`https://platform.minimaxi.com/console/usage`
- **API 域名**：`https://www.minimaxi.com`（注意是 www 子域，SPA 实际调用的）
- **鉴权方式**：Cookie Session（`_token` JWT + `minimax_group_id_v2` 作 x-group-id）；`/backend/account/*` 接口需要 `x-group-id` header
- **登录判定**：登录后 URL 含 `/console/` `/user-center/` `/plan` `/usage`；落地页 path="/" 视为未登录

### 1.2 查询方法（浏览器实测）
在已登录页面的 DevTools Console 或通过 `fetch(url, {credentials:'include'})` 直接调用（Cookie 自动带上）。所有接口均为 **GET**。

```js
// 通用查询模板
const r = await fetch('https://www.minimaxi.com/backend/account/token_plan/usage_summary', {credentials:'include'});
const j = await r.json();
```

---

## 2. 接口详情（实测完整结构）

### 2.1 `remains_percent` — 5h/周/视频核心用量

- **URL**：`GET https://www.minimaxi.com/backend/account/token_plan/remains_percent`
- **鉴权**：Cookie + x-group-id
- **实测响应**（2026-07-22）：

```jsonc
{
  "model_remains": [
    {
      "model_name": "general",                    // 文本用量（卡片主指标来源）
      "start_time": 1784721600000,                // 5h 窗口开始（毫秒）
      "end_time": 1784736000000,                  // 5h 窗口结束（毫秒，= 重置时刻）
      "remains_time": 10264939,                   // 5h 重置倒计时（毫秒，≈2h51m）
      "current_interval_total_count": -1,         // 5h 总次数（-1 = 无限）
      "current_interval_used_count": -1,          // ⚠️ 语义坑见 §4.1
      "current_interval_remains_count": -1,       // 5h 剩余次数（-1 = 无限）★真实存在
      "current_interval_used_percent": "1%",      // ★ 5h 已用百分比（卡片主指标）
      "current_interval_total_percent": "100%",
      "current_interval_status": 1,
      "weekly_start_time": 1784476800000,
      "weekly_end_time": 1785081600000,           // 周窗口结束（= 周重置时刻）
      "weekly_remains_time": 355864939,           // 周重置倒计时（毫秒，≈4天2h）
      "current_weekly_total_count": -1,
      "current_weekly_used_count": -1,
      "current_weekly_remains_count": -1,
      "current_weekly_used_percent": "53%",       // ★ 周已用百分比
      "current_weekly_total_percent": "100%",
      "current_weekly_status": 1
    },
    {
      "model_name": "video",                      // 视频赠送
      "current_interval_total_count": 3,          // 5h 视频总额度 3 次
      "current_interval_used_count": 0,           // ⚠️ 见 §4.1
      "current_interval_remains_count": 3,        // 5h 视频剩余 3 → 已用 0（"0/3 已用"）
      "current_interval_used_percent": "0%",
      "current_weekly_total_count": 21,           // ★ 周视频总额度 21 次（当前插件未展示）
      "current_weekly_used_count": 0,
      "current_weekly_remains_count": 21,         // 周视频剩余 21
      "current_weekly_used_percent": "0%",
      "start_time": 1784649600000,
      "end_time": 1784736000000,
      "weekly_start_time": 1784476800000,
      "weekly_end_time": 1785081600000
    }
  ],
  "base_resp": { "status_code": 0, "status_msg": "success" }
}
```

### 2.2 `usage_summary` — 趋势/热力/汇总（数据量最大）

- **URL**：`GET https://www.minimaxi.com/backend/account/token_plan/usage_summary`
- **响应长度**：约 41KB（含 168 天明细）
- **实测顶层结构**：

```jsonc
{
  "total_days": 42,                               // 统计总天数
  "total_token_consumed": "5.54B",                // ⚠️ 格式化字符串！累计调用量
  "usage_ranking_percent": 1.3605161324876924,    // 用量排名前 %（越小越靠前）
  "active_days": 37,                              // 活跃天数（有数据的天）
  "current_consecutive_days": 0,                  // 连续活跃天数（当前插件未提取）
  "most_active_day": {
    "date": "2026-07-01",
    "token_count": "552.49M",                     // ⚠️ 格式化字符串！单日峰值
    "image_count": "0", "video_count": "0",
    "music_count": "0", "voice_character_count": "0"  // 峰值日各媒体计数（未提取）
  },
  "daily_token_usage": [ /* 168 个数字，见 §3 折线图 */ ],
  "date_model_usage": [ /* 168 个对象，见 §2.3 每日明细 */ ]
}
```

### 2.3 `date_model_usage` 每日明细结构（热力图 + 顶部数字来源）

- **长度**：168 天（`2026-02-05` → `2026-07-22`），其中**只有 37 天 models 非空**（对应 active_days=37）
- **单日完整结构**（2026-07-22 实测）：

```jsonc
{
  "date": "2026-07-22",
  "total_input_token": 25482887,        // 当日总输入 token
  "total_output_token": 146327,         // 当日总输出 token
  "total_cache_read_token": 21161322,   // 当日缓存读取 token
  "total_cache_create_token": 0,        // 当日缓存创建 token
  "total_token": 25629214,              // ★ 当日总计 = input + output（= 顶部"25.63M"、热力图值）
  "cache_hit_percent": "83.04%",        // 当日缓存命中率（字符串）
  "models": [                           // ★ per-model 明细（决策⑥要进 SDK；空账号为 []）
    {
      "model": "MiniMax-M3-512k",       // 模型名（= 顶部"MiniMax-M3-512K"）
      "input_token": 25482887,          // = 顶部"25.48M"
      "output_token": 146327,           // = 顶部"146.33K"
      "cache_read_token": 21161322,
      "cache_create_token": 0,
      "total_token": 46790536,          // ⚠️ model 级 total 口径不同，见 §4.3
      "cache_hit_percent": "83.04%"     // = 顶部"83.0%"
    }
  ]
}
```

### 2.4 `token_plan_credit` — 积分

- **URL**：`GET https://www.minimaxi.com/backend/account/token_plan_credit`
- **实测响应**：

```jsonc
{
  "total_credits": 0,
  "used_credits": 0,                    // 已用积分（当前插件未提取）
  "remaining_credits": 0,               // 剩余积分（"暂无积分"）
  "api_key": "sk-cp-q2YG...",           // 🔴 敏感！API 密钥明文，绝不能入库/日志
  "balance_breakdown": { "total_balance": 0, "buckets": [] },
  "base_resp": { "status_code": 0, "status_msg": "success" }
}
```

### 2.5 订阅档位名称 — 来源为 DOM（接口源待深挖）

- **UI 显示**："Token Plan · TokenPlanMax-年度会员"
- **实测**：`cycle_audio_resource_package` 接口的 `current_subscribe` 各字段**均为空字符串**；订阅名实际渲染在 DOM：
  ```html
  <span ...>Token Plan · TokenPlanMax-年度会员</span>
  ```
- **DOM 提取选择器线索**：含文本 `Token Plan ·` 的 `<span>`，位于页面顶部"当前订阅"区块
- **⚠️ 待深挖**：真实数据接口未定位（可能在 SSR __NEXT_DATA__ 或另一个带正确参数的 combo 接口）；`cycle_audio_resource_package?cycle_type=3` 返回的是**可订阅套餐列表**（Plus/Max 等），非当前订阅
- **可订阅套餐接口**（备查）：`GET /v1/api/openplatform/charge/combo/cycle_audio_resource_package?biz_line=2&cycle_type=3&resource_package_type=7` → `cycle_resource_packages[]`（title/price/combo_id/benefit）

---

## 3. 折线图 vs 热力图：两份不同数据（★重点）

**结论：折线图和热力图不是同一份数据，且两数组错位一天。**

| | 折线图 | 热力图 + 顶部调用量数字 |
|---|---|---|
| 数据源 | `daily_token_usage[]`（纯数字数组）| `date_model_usage[].total_token` |
| 长度 | 168 | 168 |
| 日期标注 | ❌ 无日期（需自己推断）| ✅ 每项带 `date` |
| 7-22 位置的值 | 379,039,854 | 25,629,214 |

### 3.1 ⚠️ 陷阱：两数组错位整整一天
实测对齐（同索引 k）：
```
daily_token_usage[7-22 位置] = 379,039,854  ≈  date_model_usage[7-21].total_token = 379,039,852
daily_token_usage[7-21 位置] = 254,674,208  ≈  date_model_usage[7-20].total_token = 254,674,208
```
即 **`daily_token_usage[k]` ≈ `date_model_usage[k-1].total_token`**。
- **原因推测**：daily_token_usage 可能包含"今天未结算的实时值"或时区/口径差，导致比 date_model_usage 多一天/错一位。
- **教训**：**不要**直接拿 `daily_token_usage` 最后一项当"今天"，日期必须以 `date_model_usage[].date` 为准。折线图渲染应优先用带日期的 `date_model_usage`。
- **✅ 2026-07-23 浏览器复测定论**：去掉零值天后，37 个活跃天**同索引对齐命中 = 0**、错位命中为主 → **错位一天确认成立**。更关键的是，网页汇总数字实测全部来自 `date_model_usage`：「近7天 = 1.28B」= `date_model` 求和(07-17…07-22) = 1,283,555,106 ✅（用 daily 算 = 1.37B ❌）、「近30天 = 5.22B」= `date_model`(06-24…07-22) = 5,224,372,858 ✅。**即网页折线/热力/汇总全部使用 `date_model_usage`，`daily_token_usage` 未被任何图表或汇总使用** → SDK **整字段弃用 `daily_token_usage`**，趋势统一用 `date_model_usage`（按 .date）。这也解释了"折线图与热力图看起来一样"（两图同源 date_model + 131/168 天为零）。

### 3.2 切片器"近7天/近30天"语义
- 折线图右上角"近7天/近30天"**只改显示窗口**，不重新请求接口。
- 折线图 Y 轴顶部数字（如 379.04M / 552.49M）是**当前窗口的最大值**，随切片变化：
  - 近 7 天 → 窗口 = 数组最后 7 项，峰值 7-21 = 379.04M
  - 近 30 天 → 窗口 = 数组最后 30 项，峰值 7-1 = 552.49M
  - 同一屏幕位置的数字会因切片不同而变化
- **底部汇总数字**（截图）实测来自 `date_model_usage` 求和：
  - 当日"25.63M" = `date_model_usage[今天].total_token`
  - "近7天调用量 1.39B" = date_model_usage 最后 7 天 total_token 求和（实测 1,392,376,147）
  - "近30天调用量 5.26B" = 最后 30 天求和（实测 5,264,717,879）

---

## 4. 数据陷阱汇总

### 4.1 `current_*_used_count` 语义反转
- MiniMax API 的 `current_interval_used_count` / `current_weekly_used_count` **实际是"剩余次数"不是"已用次数"**（历史 issue #81156 记录）。
- 正确算法：`已用 = total_count - remains_count`（用 `current_*_remains_count`）。
- 视频 5h：total=3, remains=3 → 已用 = 0（"0/3 已用"）。

### 4.2 格式化字符串 vs 原始数字
- `total_token_consumed`="5.54B"、`most_active_day.token_count`="552.49M" 是**格式化字符串**，不能直接计算。
- 决策：SDK 入库应存**原始 long 数字**，显示时才格式化。原始值可从 `date_model_usage` 求和还原。

### 4.3 model 级 total_token ≠ day 级 total_token
- day 级 `total_token`(25,629,214) = `total_input_token` + `total_output_token`（净计，不含 cache_read）。
- model 级 `total_token`(46,790,536) = input + output + cache_read（含缓存读取）。
- **口径不同！** SDK 设计时须明确：卡片顶部"调用量"用 **day 级 total_token = input+output**；per-model 分析另存。

### 4.4 当日值是累积增长（可去重覆盖）
- 同一天多次刷新，当日 total_token 会增长（实测两次抓取：25,629,214 → 25,770,116）。
- 印证 req-092 去重设计：**当日数据是"可覆盖"的**，历史完结日数据才稳定。字段级差异去重时，当日行应始终覆盖更新，历史行只在变化时更新。

---

## 5. 对 SDK 字段设计的启示（待汇总）

1. **趋势统一用 `date_model_usage`**（2026-07-23 实测定论）：`daily_token_usage` 相对 date_model 错位一天且网页从未使用，**整字段弃用**；折线/热力/近7近30 汇总全部基于 `date_model_usage.total_token`(= input + output) 按 `.date` 计算。
2. **每日明细单独建数据表**（用户要求）：`date` + 5 种 token（input/output/cache_read/cache_create/total）+ cache_hit_percent + per-model 展开。带 ProviderId/AccountId 系统列，支持去重覆盖。
3. **敏感字段隔离**：`api_key` 必须标 Sensitive，禁止入库/日志。
4. **格式化字符串还原**：入库存原始数字。
5. **视频周维度补全**：当前插件只展示 5h 视频(0/3)，接口有周维度(0/21)，SDK 应保留。
6. **未提取但有价值的字段**（决策保留进 SDK）：连续活跃天数、峰值日媒体计数、已用积分、续订信息、每日 input/output/cache 细分、per-model 明细。

---

## 6. 数据表设计建议（用户要求：明细单独分表）

初步建议两张表（最终以 SDK 字段汇总为准）：

### 表 A：`usage_snapshot`（当前状态，随刷新去重覆盖）
`provider_id, account_id, snapshot_at, five_hour_used_percent, weekly_used_percent, five_hour_reset_at, weekly_reset_at, video_5h_used, video_5h_total, video_weekly_used, video_weekly_total, remaining_credits, used_credits, total_credits, subscription_tier, subscription_active, total_token_consumed, usage_ranking_percent, active_days, consecutive_days, most_active_date, most_active_token`

### 表 B：`usage_daily_detail`（每日明细，按日期去重覆盖，单独表）
`provider_id, account_id, date, line_token（daily_token_usage）, total_token（day 级=in+out）, input_token, output_token, cache_read_token, cache_create_token, cache_hit_percent`

### 表 C：`usage_daily_model`（per-model 明细，二阶分析预留，决策⑥）
`provider_id, account_id, date, model_name, input_token, output_token, cache_read_token, cache_create_token, total_token, cache_hit_percent`

> ⚠️ 是否累积：当日行会随刷新增长（累积到当日结束），历史完结日稳定。去重策略：当日行覆盖，历史行按字段差异更新。

---

## §6.5 视觉样式 + 交互（实测 computed style，为 SDK 图表开发参考）

MiniMax 用量页（platform.minimaxi.com/console/usage）是**浅色主题 + 红色主题色**，图表最丰富的一站。

### 进度条（按用量类型分色）
槽：`#F3F4F6`（rgb 243,244,246），高 8px，圆角 9999px（全圆）；填充颜色★**随用量百分比动态变化**（用户确认）：
| 实测点 | 已用 % | 填充色 |
|---|---|---|
| 5h 限额 | 3% | **绿 `#00B42A`**（rgb 0,180,42）|
| 周限额 | 54% | **黄 `#FACD14`**（rgb 250,205,20）|
> ★ 颜色**跟随用量变化**（3%绿 → 54%黄，高位应为红）。⚠️ 实测仅抓到 3%绿/54%黄 两个点，**精确阈值未确认**（JS 压缩无明文）；推断规则类似低绿/中黄/高红，具体分界点待后续造高用量时补测。颜色可能与热力图红 `#FF5A3D` 不同系（进度条高位红待确认）。

### 折线图（“调用趋势图”）
- 折线：**红 `#FF5A3D`**（rgb 255,90,61），线宽 2px
- 面积填充：`url(#tokenTrendFill)` 红色渐变（与折线同色系，上浓下透）
- 坐标轴/网格：灰 `rgb(134,144,156)`
- **交互**：右上“近 7 天 / 近 30 天” segment 切换——选中态白底+深字+font-weight 500，未选透明底+灰字 400；hover 数据点显 tooltip（同热力图格式：日期+调用量）

### 热力图（“调用热力图”，GitHub 式）
- 方块：15px，圆角 2px；布局：周一~周日（行）× 月份（列）；cursor=**pointer**（可交互）
- **红色系 + 透明度 5 档**（主色同折线 `#FF5A3D`）：
  | 档 | 背景色 |
  |---|---|
  | L0 无 | `#F3F4F6` |
  | L1 | `rgba(255,90,61,0.15)` |
  | L2 | `rgba(255,90,61,0.35)` |
  | L3 | `rgba(255,90,61,0.55)` |
  | L4 | `rgba(255,90,61,0.8)` |
  | L5 满 | `rgb(255,90,61)` |
- **★ hover tooltip（实测）**：`6月22日 调用 56.12M 缓存命中 91.3%`——悬停显示当日**调用量 + 缓存命中率**
- 图例：“少 → 多”（红色深浅渐变）；右侧聚合统计：累计调用量 5.54B / 单日峰值 552.49M / 活跃天数 37

### 对 SDK 的启示
- MiniMax = **红色主题**（折线+热力图同 #FF5A3D）；进度条则按用量类型绿/黄分色
- 热力图色阶：**单色（红）+ 透明度分档**——与 WorkBuddy 月历（绿透明度）同机制，但 MiniMax 是 GitHub 式布局
- 三站热力图色阶对比：MiniMax（红透明度）/ WorkBuddy 月历（绿透明度）/ WorkBuddy 年（绿多色阶实心）→ SDK 热力图需支持“主色可配 + 透明度档/多色阶两种分档”

---

## 7. 复现查询脚本（浏览器 Console 直接跑）

```js
// 1. 核心用量
await (await fetch('https://www.minimaxi.com/backend/account/token_plan/remains_percent',{credentials:'include'})).json();

// 2. 趋势/热力/汇总（41KB）
await (await fetch('https://www.minimaxi.com/backend/account/token_plan/usage_summary',{credentials:'include'})).json();

// 3. 积分
await (await fetch('https://www.minimaxi.com/backend/account/token_plan_credit',{credentials:'include'})).json();

// 4. 验证折线vs热力错位
const j = await (await fetch('https://www.minimaxi.com/backend/account/token_plan/usage_summary',{credentials:'include'})).json();
const dtu=j.daily_token_usage, dmu=j.date_model_usage, n=dtu.length;
console.table(dmu.slice(n-5).map((d,i)=>({date:d.date, dmu_total:d.total_token, dtu_same_idx:dtu[n-5+i]})));
```

---

## 附：本次未解决/待后续

- 订阅档位名称的**真实数据接口**未定位（当前从 DOM 取），需深挖 SSR `__NEXT_DATA__` 或其他 combo 接口。
- `token_plan/usage` 接口需要正确参数（当前返回 2013 invalid params），可能是另一个数据源。
- 多模型场景未实测（当前账号只有 MiniMax-M3-512k 单模型）；多模型时 day 级 total = 所有模型 input+output 之和。
