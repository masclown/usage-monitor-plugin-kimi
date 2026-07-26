# 显示资源包开发指南（themes / charts / minicharts / traytooltips）

> req-115 交付。四类声明式 JSON 资源包目录与 `plugins/` 平级（程序目录下），宿主渲染、零代码执行、支持热重载（放入/修改/删除文件后自动生效，无需重启）。

## 目录结构

```
UsageMonitor.exe
├─ plugins/                    # 插件声明包
├─ themes/<pack>/theme.json          # 主题包
├─ charts/<pack>/chartstyle.json     # 图表样式包
├─ minicharts/<pack>/ministyle.json  # mini 图表样式包
└─ traytooltips/<pack>/traytooltip.json  # 悬浮窗模板包
```

所有包共用元信息头：`schemaVersion`（当前 1）、`id`（必填、目录内唯一）、`displayName`（设置界面展示，缺省回退 id）。坏包（JSON 语法错误 / 缺 id）跳过并记日志，不影响其他包。

## 主题包 themes/<pack>/theme.json

```json
{
  "schemaVersion": 1,
  "id": "solarized-dark",
  "displayName": "Solarized 深色",
  "isDark": true,
  "tokens": {
    "AppBackgroundBrush": "#002B36",
    "SurfaceBrush": "#073642",
    "TextPrimaryBrush": "#FDF6E3",
    "AccentBrush": "#268BD2",
    "AccentColor": "#268BD2"
  }
}
```

- token 键对齐宿主 `Themes/Dark.xaml` / `Light.xaml` 的资源键；值为 `#RRGGBB` / `#AARRGGBB`。
- 键名以 `Color` 结尾写入 Color 资源，否则写入 SolidColorBrush（冻结）。
- 缺失 token 由宿主按 `isDark` 对应的内置主题打底；`id` 不可用 `dark` / `light`（内置保留）。
- 生效入口：设置 → 常规 → 外观主题下拉。

## 图表样式包 charts/<pack>/chartstyle.json

```json
{
  "schemaVersion": 1,
  "id": "ocean-tiers",
  "displayName": "海洋色阶",
  "chartStyles": {
    "usage": { "thresholds": [0, 50, 75, 90], "colors": ["#38BDF8", "#0EA5E9", "#0369A1", "#EF4444"] }
  }
}
```

- 特殊键 `usage`：选中该包后覆盖全局用量色阶（进度条 / 环形图取色）。
- `HeatMap` 键：供卡片管理页 per-chart 色阶来源（见下）与插件 `pack:` 引用消费，阈值语义为 token 下界。
- 其余键（`Bar` / `Line` / `Ring` / `Number`）与 `parameters`、`assets` 为预留扩展位（未来"图表主题 / 视觉特效 / 图片素材替换" SDK），当前版本声明合法但不渲染。
- `thresholds` 与 `colors` 必须等长，否则该项色阶被忽略。
- 生效入口：
  - 全局：设置 → 常规 → 图表样式包下拉（usage 色阶覆盖全局取色）；
  - 按图表（req-115 `pack:<packId>` 源）：设置 → 卡片管理 → 展开图表 →「色阶来源」下拉（仅支持色阶的图表类型显示），
    选择后写入 `AccountCustomization.ChartColorTierSources[chartId] = "pack:<packId>"`；热力图按包内 `HeatMap`（回退 `usage`）条目取色，其余类型当前回退全局色阶；
  - 插件声明：mini 图表的 `"colorTiers": { "ref": "pack:<packId>" }"` 可直接引用样式包（优先 minicharts/ 包，回退 charts/ 包）。

## mini 图表样式包 minicharts/<pack>/ministyle.json

```json
{
  "schemaVersion": 1,
  "id": "mono-ring",
  "displayName": "单色圆环",
  "chartStyles": {
    "MiniRingChart": { "thresholds": [0, 60, 85], "colors": ["#9CA3AF", "#F59E0B", "#EF4444"] }
  }
}
```

- 键为 MiniChart 类型名（`MiniRingChart` / `MiniText` 等）；选中后按类型覆盖任务栏迷你图私有色阶（最多 6 档）。
- 生效入口：设置 → 任务栏迷你图表 → mini 图表样式包下拉。

## 悬浮窗模板包 traytooltips/<pack>/traytooltip.json

```json
{
  "schemaVersion": 1,
  "id": "compact-rows",
  "displayName": "紧凑字段行",
  "rows": [
    { "fieldName": "five_hour_used_percent" },
    { "fieldName": "weekly_used_percent" },
    { "textTemplate": "———" },
    { "fieldName": "remaining_credits" }
  ]
}
```

- 每行 `fieldName`（SDK 标准字段名，经白名单校验，非法行剔除）或 `textTemplate`（静态文本）二选一。
- 选中后每个 Provider 摘要卡的明细区按行序渲染（替换内置"已使用/总额度/剩余额度"区），头部与错误区布局仍由宿主统一。
- 生效入口：设置 → 悬浮窗设置 → 悬浮窗模板下拉。

## 安全与热重载

- 全部 JSON 纯数据，宿主渲染，零代码执行（与插件零 DLL 安全路线一致）。
- 四个目录各挂 800ms 防抖监视器；变更后自动重扫并即时应用（当前选中包被删除时自动回退内置默认）。
