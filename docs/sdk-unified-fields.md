# UsageMonitor 统一 SDK 字段清单（req-107 B1 地基）

> 创建日期：2026-07-23
> 依据：6 站实测手册（MiniMax/Kimi/Qoder/WorkBuddy/TRAE/DeepSeek，见 `docs/*-usage-data-reference.md`）汇总。
> 目的：定义插件上报的**统一 SDK 字段名**（= 数据库入库列名），作为声明式插件框架的字段地基。
> 落地：统一后**先从 MiniMax 开始**逐个插件开发与匹配（见 §6）。

---

## 0. 设计原则（6 站汇总后确认）

1. **去 Provider 前缀**：字段名不带 `mm_`/`ds_`（决策①）。同语义字段所有 Provider 共用一个名字（如 `five_hour_used_percent` 而非 `mm_5h_used_percent`）。
2. **存原始数字**：百分比存数字 0-100（不存 "53%" 字符串）；比例小数 ×100 归一；金额分→元（决策②）。
3. **派生字段可入库**：如 Kimi `kimi_used_percent = total - code`、Qoder `request_duration_sec = finish - begin`——入库省重复计算（决策）。
4. **敏感字段永不入库**：api_key/token/手机号/uin/sensitive_id/用户对话 input（Visibility=Sensitive）。
5. **字段 = 入库列名**：SDK 字段常量反向生成 SQLite schema；额外系统列 ProviderId/AccountId/Timestamp/PlanType。
6. **通用优先，专有兜底**：能泛化的进通用 UsageFields；无法泛化的进 Provider 专属子表（不污染通用清单）。

## 字段元数据（每个字段 5 属性）
- **Category**：Quota(配额) / Usage(用量) / Cost(费用) / Subscription(订阅) / Reset(重置时间) / Detail(明细) / Meta(元信息)
- **Visibility**：Public(可显示) / Internal(仅内部) / Sensitive(禁止入库)
- **Unit**：Percent / Token / Currency / Credit / Count / DateTime / Text / Bool
- **DataType**：number / string / bool / datetime
- **多窗口归属**：Total / FiveHour / Weekly / SevenDay / Daily（用量类字段的时间窗口维度）

---

## 1. 通用快照字段（进 UsageFields 常量 + 快照表列）

### 1.1 系统列（每行必带，非 UsageFields）
| 字段 | 语义 | 类型 |
|---|---|---|
| `provider_id` | 插件 ID | string |
| `account_id` | 账号哈希 = hash(provider_id + 平台稳定ID) | string |
| `plan_type` | 用量窗口/配额类型（见 §1.2 枚举）| string |
| `timestamp` | 采集时间（UTC）| datetime |

### 1.2 用量百分比（多窗口，Category=Usage/Percent）
> 各 Provider 用量窗口不同，用 `plan_type` 区分同名字段的窗口归属。

| SDK 字段 | 语义 | 来源站点 |
|---|---|---|
| `used_percent` | 通用已用百分比（0-100）| 所有站（按窗口）|
| `total_used_percent` | 总用量百分比 | Kimi(amountUsedRatio) |
| `code_used_percent` | Code 子维度用量 | Kimi(kimiCodeUsedRatio) |
| `kimi_used_percent` | 派生：total - code | Kimi（派生）|
| `five_hour_used_percent` | 5 小时窗口用量 | MiniMax / Kimi |
| `weekly_used_percent` | 周窗口用量 | MiniMax |
| `seven_day_used_percent` | 7 天窗口用量 | Kimi（★区别于周）|

### 1.3 配额额度（Category=Quota）
| SDK 字段 | 语义 | 来源 |
|---|---|---|
| `used_amount` / `total_amount` / `remaining_amount` | 通用已用/总/剩余额度 | 所有 |
| `used_tokens` / `total_tokens` | Token 数（-1=不限）| MiniMax/DeepSeek |
| `used_credits` / `total_credits` / `remaining_credits` | 积分额度 | MiniMax/Qoder/WorkBuddy |
| `total_used_credits` | 总消耗积分 | WorkBuddy(TotalDosage) |
| `available_token_estimation` | 可用 token 估算 | DeepSeek |
| `unit` | 额度单位（credits/token/CNY）| 所有 |

### 1.4 费用/钱包（Category=Cost/Currency）
| SDK 字段 | 语义 | 来源 |
|---|---|---|
| `balance_amount` | 充值余额 | DeepSeek(normal_wallets) |
| `bonus_balance_amount` | 赠送余额 | DeepSeek(bonus_wallets) |
| `monthly_cost` | 月消费 | DeepSeek |
| `total_cost` | 累计消费 | DeepSeek |
| `currency` | 货币（CNY/USD）| DeepSeek/Qoder |

### 1.5 重置/有效期时间（Category=Reset/DateTime，UTC）
| SDK 字段 | 语义 | 来源 |
|---|---|---|
| `five_hour_reset_at` | 5h 窗口重置 | MiniMax/Kimi |
| `weekly_reset_at` | 周窗口重置 | MiniMax |
| `seven_day_reset_at` | 7 天窗口重置 | Kimi |
| `total_quota_reset_at` | 总额度重置 | Kimi |
| `quota_next_reset_at` | 配额刷新日期 | Qoder |
| `quota_period_start_at` / `quota_period_end_at` | 当月配额有效期 | Qoder |
| `expire_date` | 通用过期时间 | 所有 |
| `last_updated` | 最后更新 | 所有 |

### 1.6 订阅信息（Category=Subscription）
| SDK 字段 | 语义 | 来源 |
|---|---|---|
| `subscription_tier` | 订阅档位（Allegretto/Pro+/体验版）| Kimi/Qoder/WorkBuddy |
| `subscription_level` | 会员等级 | Kimi(membershipLevel) |
| `subscription_price` | 订阅价格（分→元）| Kimi/Qoder |
| `subscription_cycle_type` | 计费周期（月/年）| Kimi/Qoder |
| `subscription_active` | 订阅是否激活 | MiniMax |
| `subscription_next_billing_at` | 下次续费 | Kimi |
| `subscription_end_at` | 订阅到期 | Kimi |

### 1.7 加油包/运营额度（Category=Quota，可选）
| SDK 字段 | 语义 | 来源 |
|---|---|---|
| `booster_enabled` | 加油包开启 | Kimi |
| `booster_balance` | 加油包余额（分→元）| Kimi |
| `booster_monthly_used` | 加油包本月消费 | Kimi |
| `gift_credits` / `gift_expires_at` | 赠送额度/有效期 | WorkBuddy |
| `compensation_credits` / `compensation_active` | 补偿额度/生效 | WorkBuddy |

### 1.8 视频/次数类（Category=Usage/Count）
| SDK 字段 | 语义 | 来源 |
|---|---|---|
| `video_used_count` / `video_total_count` | 视频赠送次数 | MiniMax |

### 1.9 缓存命中（Category=Usage，与用量趋势相关）
| SDK 字段 | 语义 | 来源 |
|---|---|---|
| `cache_hit_percent` | 缓存命中率 | MiniMax(热力图tooltip) |

### 1.10 状态（Category=Meta）
| SDK 字段 | 语义 |
|---|---|
| `is_success` | 查询是否成功 |
| `error_message` | 错误信息 |
| `active_days` | 活跃天数 | MiniMax |
| `usage_ranking_percent` | 用量排名 | MiniMax |

---

## 2. 明细表字段（时序数据，独立子表，非快照）

> 6 站有 3 种明细粒度，统一为「**模型/请求维度明细表**」，按唯一键去重。

### 表 usage_request_detail（逐请求流水）
`provider_id, account_id, request_id(去重键), request_time, model_name, request_client, request_operation, request_status(httpStatus), request_credits, request_original_credits, request_cost, request_original_cost, request_discount_factor, request_charge_kind, request_source`

- 来源：Qoder(histories) / Kimi(ListUnifiedRequests) / WorkBuddy(get-user-request-usage) / DeepSeek(明细)
- 🔴 排除：input/inputTrunc（对话内容）、api_key.key/id

### 表 usage_model_daily（模型×日聚合）
`provider_id, account_id, date, model_name, request_count, response_token, cache_hit_token, cache_miss_token, cost, total_token, input_token, output_token`

- 来源：MiniMax(daily per-model) / DeepSeek(amount by model) / Kimi(流水按天聚合)
- ★ DeepSeek 按模型分开渲染的图 → 合并进此表，渲染时按 model 分组

### 表 usage_daily_trend（每日趋势，画折线/热力）
`provider_id, account_id, date, token_total, cache_hit_percent`

- 来源：MiniMax(daily_token_usage 168天) / WorkBuddy(heatmap 365天 score) / TRAE(活跃天数)
- 数值含义可配（token/活跃分/次数），支持任意天数

---

## 3. Provider 专属子表（无法泛化的字段）

> 各站特色字段不进通用清单，进 `provider_extra_<provider>` 子表（或 extras JSON）。

| Provider | 专属字段 |
|---|---|
| Qoder | discount_factor 折扣三元组（部分已进明细表）|
| WorkBuddy | 成长中心：energy_balance/streak_days/level/badges；N个资源包各自周期 |
| TRAE | 行为分析：code_accept_count/dialog_count/best_partner/model_preference/coding_hours(24点) |
| Kimi | Code/Kimi 双维度分类原始比例 |

---

## 4. 敏感字段黑名单（Visibility=Sensitive，永不入库）

| 字段 | 站点 |
|---|---|
| api_key / api_key.key / api_key.id / sensitive_id | MiniMax/Kimi/DeepSeek |
| access_token / userToken / msToken | Kimi/DeepSeek/TRAE |
| phoneNumber / uin（明文）| WorkBuddy |
| input / inputTrunc（用户对话）| WorkBuddy |
| tracking_id | DeepSeek |

> 平台稳定 ID（group_id/user_id/uid）仅用于**哈希**成 account_id，不明文存。

---

## 5. 数据库表结构总览

```
usage_snapshot        -- 通用快照（§1 所有字段 + 系统列），当日行覆盖
usage_request_detail  -- 逐请求流水（§2），requestId 去重
usage_model_daily     -- 模型×日聚合（§2），(date,model) 去重
usage_daily_trend     -- 每日趋势（§2），date 去重
provider_extra_*      -- Provider 专属（§3）
```
- 去重策略：快照当日行覆盖；明细/趋势按唯一键，刷新时遇已存键即停（增量同步）
- 账号主键：`account_id = hash(provider_id + 平台稳定ID)`，重装重登可匹配旧数据

---

## 6. MiniMax 首匹配对照（统一后第一个落地插件）

> 用户择机先从 MiniMax 开始逐插件开发。以下是 MiniMax 现有字段 → 统一 SDK 的迁移对照。

| 现有 mm_ 字段 | → 统一 SDK 字段 | 数据来源 |
|---|---|---|
| `mm_5h_used_percent` | `five_hour_used_percent` | remains_percent.current_interval_used_percent |
| `mm_weekly_used_percent` | `weekly_used_percent` | remains_percent.current_weekly_used_percent |
| `mm_remaining_credits` | `remaining_credits` | token_plan_credit.remaining_credits |
| `mm_subscription_title` | `subscription_tier` | DOM「Token Plan · ...」|
| `mm_subscription_active` | `subscription_active` | 派生 |
| （新增）| `five_hour_reset_at` / `weekly_reset_at` | remains_percent.remains_time |
| （新增）| `video_used_count` / `video_total_count` | remains_percent(model=video) |
| （新增）| `used_tokens` | usage_summary.total_token_consumed |
| （新增）| `active_days` / `usage_ranking_percent` | usage_summary |
| （明细）| usage_model_daily | usage_summary.date_model_usage(per-model) |
| （趋势）| usage_daily_trend | usage_summary.daily_token_usage(168天) |
| 🔴 | 不入库 | token_plan_credit.api_key |

**MiniMax 迁移要点**：
1. 去 `mm_` 前缀，用通用字段名
2. 百分比 "53%" 字符串 → 数字 53
3. 补 5h/周 重置时间（remains_time 换算）
4. per-model 明细进 usage_model_daily（决策⑥）
5. 折线/热力数据进 usage_daily_trend（注意 daily_token_usage 与 date_model_usage 错位1天的陷阱，见 minimax 手册 §4）

---

## 7. 六站字段覆盖矩阵（哪站有哪类字段）

| 字段类 | MiniMax | Kimi | Qoder | WorkBuddy | TRAE | DeepSeek |
|---|---|---|---|---|---|---|
| 用量百分比 | ✅5h/周 | ✅总/Code/5h/7天 | ✅配额% | ✅包% | — | — |
| 配额额度 | ✅积分 | — | ✅三档Credits | ✅N包Credits | — | ✅token |
| 费用钱包 | — | — | — | — | — | ✅余额/消费 |
| 订阅信息 | ✅ | ✅ | ✅ | ✅ | — | — |
| 重置时间 | ✅5h/周 | ✅总/5h/7天 | ✅刷新 | ✅包周期 | — | — |
| 加油包/运营 | — | ✅加油包 | — | ✅赠送/补偿 | — | ✅赠送钱包 |
| 请求明细 | — | ✅流水 | ✅流水 | ✅流水 | ✅明细 | ✅ |
| 模型×日 | ✅ | (聚合) | — | ✅model | ✅偏好 | ✅ |
| 每日趋势 | ✅168天 | (聚合) | — | ✅365天 | ✅活跃 | ✅ |
| 缓存命中 | ✅ | — | — | — | — | ✅命中/未命中 |
| 行为分析 | — | — | — | ✅成长 | ✅丰富 | — |

> 覆盖最全的窗口/配额来自 Kimi(4窗口)、WorkBuddy(N包)、DeepSeek(钱包)——SDK 字段以并集设计，单站只填自己有的。
