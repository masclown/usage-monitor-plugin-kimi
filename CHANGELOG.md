# Changelog

## usage-monitor-plugin-kimi 1.2.0 (2026-09-05)

- 新增迷你堆叠柱状图 `km.mini.stackedBar`：按 feature(channel) x day 透视 `amountRatio`，产物 `mini_series_group:km.daily.credit_*`。
- `StackedProgress` 改造：Upper 字段由常量基数改为字段派生；`X-1r/X-2r/X-3r(total_amount)` 下线。
- 流水透视剔除无状态码记录（`ListBalanceActions` 的 `httpStatusCode`/`request_status` 为 NULL），只统计真实状态码。
- 下线 X-8 subscription_active 相关 computed 段。

## usage-monitor-plugin-kimi 1.0.0 (2026-09-02)

Initial release as an independent plugin repository.

迁移自主项目 `src/Plugins/UsageMonitor.Provider.`（如适用）。
