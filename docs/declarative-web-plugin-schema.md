# 声明式网页插件 Schema 文档

> req-106：配置驱动 + 免编译热重载的网页插件描述格式（`*.plugin.json`）。
>
> 本文是 [`网页插件开发指南`](./plugin-development-guide-web.md) 的**声明式对照版**——
> 前者用 C# 继承 `WebPluginBase` 写编译型插件，本文用一份 JSON 描述"零代码"定义一个网页插件。
>
> **适用范围**：只需从页面 DOM 提取若干数字/文本的简单插件（约 80% 场景）。
> 复杂插件（XHR 捕获、派生计算、并发缓存，如 MiniMax）仍需编译型，见文末《边界》。

---

## 快速开始

在插件描述目录放一个 `*.plugin.json`，主程序启动时自动加载，运行时改动即时热重载（无需编译、无需重启）：

```
%AppData%\UsageMonitor\plugins-declarative\yourservice.plugin.json   ← 用户自定义（优先）
<主程序目录>\plugins-declarative\yourservice.plugin.json              ← 随包内置
```

最小示例（抓两个 DOM 数字）：

```jsonc
{
  "schemaVersion": 1,
  "providerId": "yourservice",
  "displayName": "Your Service",
  "loginUrl": "https://yourservice.com/login",
  "usageUrl": "https://yourservice.com/console/usage",
  "cookieDomainFilters": [".yourservice.com"],
  "quantityUnit": "USD",
  "extractRules": [
    { "targetField": "used",  "mode": "css", "pattern": ".usage-used",  "valueType": "number" },
    { "targetField": "total", "mode": "css", "pattern": ".usage-total", "valueType": "number" }
  ]
}
```

改 `pattern`/`usageUrl` 保存 → 卡片数据即时刷新；新增/删除文件 → 卡片列表即时增删。

---

## 完整字段清单

```jsonc
{
  "schemaVersion": 1,                       // 必填。schema 版本号，向后兼容用
  "providerId": "example-web",              // 必填。全局唯一标识（小写），与内置插件冲突时以内置优先
  "displayName": "Example Web",             // 必填。卡片显示名
  "version": "1.0.0",                       // 选填。插件版本
  "author": "王晨",                          // 选填
  "iconPath": null,                         // 选填。图标路径（相对主程序或绝对路径）

  // —— 登录 / 导航（映射 WebPluginBase 抽象属性）——
  "loginUrl": "https://example.com/login",         // 必填。登录入口 URL（须过 SSRF 白名单）
  "usageUrl": "https://example.com/console/usage", // 必填。用量页面 URL（须过 SSRF 白名单）
  "cookieDomainFilters": [".example.com"],         // 必填。判定登录态的 Cookie 域名过滤
  "headless": true,                                // 选填。默认 true（后台运行，不弹浏览器窗口）

  // —— 配置字段（映射 ConfigFields，按名走 StandardWebConfigFields）——
  "configFields": ["Cookie", "Region", "AutoRefresh", "Proxy", "Headless"],

  // —— 第一层核心：DOM 提取规则表 ——
  "extractRules": [
    { "targetField": "used",   "mode": "css",   "pattern": ".usage-used",       "valueType": "number"  },
    { "targetField": "total",  "mode": "css",   "pattern": ".usage-total",      "valueType": "number"  },
    { "targetField": "percent","mode": "regex", "pattern": "(\\d+)%",           "valueType": "percent" },
    { "targetField": "plan",   "mode": "xpath", "pattern": "//span[@id='plan']","valueType": "text", "extraKey": "PlanName" }
  ],

  // —— 显示声明（映射现有声明式属性）——
  "quantityUnit": "USD",                    // 选填。Quantity 单位：USD/CNY/EUR/Percent/Credit/Token
  "supportedCardCharts": ["Line", "Ring"],  // 选填。卡片支持的图表类型
  "defaultRenderKinds": ["card", "ring"],   // 选填。首次渲染默认显示的部件
  "toolTipFields": ["ProviderName", "CurrentValue", "RefreshCountdown"], // 选填。托盘提示字段

  // —— 第二层预留（本版本仅反序列化保留，执行器未实现）——
  "xhrCaptures": [],                        // 预留。{ "urlGlob": "...", "jsonPath": "...", "extraKey": "..." }
  "derivedExpressions": []                  // 预留。{ "targetField": "...", "expr": "used/total*100" }
}
```

---

## 字段详解

### 基本信息

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | :---: | --- |
| `schemaVersion` | int | ✅ | 当前为 `1`；主程序按此判断兼容性，未知版本会拒绝加载并记日志 |
| `providerId` | string | ✅ | 全局唯一标识，小写。与内置编译型插件（minimax/deepseek 等）**冲突时以内置优先**，声明式被跳过并记 Warn |
| `displayName` | string | ✅ | 主窗口卡片、设置页显示的名称 |
| `version` | string | ➖ | 语义化版本号，默认 `1.0.0` |
| `author` | string | ➖ | 作者署名 |
| `iconPath` | string\|null | ➖ | 图标路径，`null` 时使用默认占位图标 |

### 登录 / 导航

| 字段 | 类型 | 必填 | 映射到 | 说明 |
| --- | --- | :---: | --- | --- |
| `loginUrl` | string | ✅ | `WebPluginBase.LoginUrl` | 登录入口。**必须通过 SSRF 白名单校验（req-056）**，非法 URL 拒绝加载 |
| `usageUrl` | string | ✅ | `WebPluginBase.UsageUrl` | 用量页面，登录后导航目标。同样过 SSRF 校验 |
| `cookieDomainFilters` | string[] | ✅ | `WebPluginBase.CookieDomainFilters` | 判定登录态的 Cookie 域名列表（req-089 ACL） |
| `headless` | bool | ➖ | `WebPluginBase.Headless` | 默认 `true`。调试时设 `false` 可见浏览器窗口 |

### 配置字段（`configFields`）

字符串数组，每一项按名映射到 `StandardWebConfigFields` 的工厂方法（与编译型插件一致）：

| 名称 | 生成的字段类型 | 说明 |
| --- | --- | --- |
| `Cookie` | Password | 浏览器登录态 Cookie |
| `Region` | Select | 服务区域（默认 `CN`，可选 `CN`/`Global`） |
| `AutoRefresh` | Boolean | 自动刷新开关（默认开） |
| `Proxy` | Text | HTTP 代理地址 |
| `Headless` | Boolean | 浏览器无头模式 |

> 省略 `configFields` 时，默认装配 `["Cookie", "Region", "AutoRefresh", "Proxy", "Headless"]`。

### 提取规则表（`extractRules`）★ 核心

数组，每条规则描述"从页面提取一个值 → 写入 UsageInfo 的哪个字段"。由 `DeclarativeUsageMapper` 复用 [`WebPageParser`](../src/UsageMonitor.Core/Services/WebPageParser.cs) 执行。

| 规则字段 | 类型 | 必填 | 说明 |
| --- | --- | :---: | --- |
| `targetField` | string | ✅ | 目标字段名，见下方《targetField 取值》 |
| `mode` | string | ✅ | 提取模式：`css` / `xpath` / `regex` |
| `pattern` | string | ✅ | 选择器 / XPath 表达式 / 正则（JSON 中反斜杠需转义为 `\\`） |
| `valueType` | string | ✅ | 值类型：`number` / `percent` / `text` / `currency` |
| `extraKey` | string | ➖ | 当 `targetField` 为 `extra` 时，指定写入 `UsageInfo.Extra` 的键名 |

#### mode（提取模式）

| mode | 对应 `WebPageParser.ExtractMode` | 示例 pattern |
| --- | --- | --- |
| `css` | `CssSelector` | `.usage-used` |
| `xpath` | `XPath` | `//div[@class='usage']/span` |
| `regex` | `Regex`（从页面 HTML 提取，取第 1 分组） | `"usedAmount":\\s*(\\d+\\.?\\d*)` |

#### valueType（值类型转换）

| valueType | 转换逻辑 | 落地字段 |
| --- | --- | --- |
| `number` | `ExtractNumberAsync`：自动去千分位逗号/百分号/单位后缀 → decimal | 标准字段（如 `used`→`UsedAmount`） |
| `percent` | `ExtractPercentAsync`：`"66%"` → `66.0` | 写入 `Extra` 或用于百分比 |
| `text` | 原样字符串 | 通常写 `Extra[extraKey]` |
| `currency` | decimal + `quantityUnit` 构造 `Quantity(CurrencyUnit)` | `UsageInfo.Quantity` |

#### targetField（目标字段）

标准字段直接映射到 [`UsageInfo`](../src/UsageMonitor.Core/Models/UsageInfo.cs)；其余走 `Extra` 字典：

| targetField | 映射到 | 建议 valueType |
| --- | --- | --- |
| `used` | `UsageInfo.UsedAmount` + 参与 `Quantity` | `number` / `currency` |
| `total` | `UsageInfo.TotalAmount` | `number` |
| `usedTokens` | `UsageInfo.UsedTokens` | `number` |
| `totalTokens` | `UsageInfo.TotalTokens` | `number` |
| `percent` | 百分比展示（写 `Extra`） | `percent` |
| `extra` | `UsageInfo.Extra[extraKey]`（需配 `extraKey`） | `text` / `number` / `percent` |

> **容错**：单条规则提取失败**不会**导致整体失败——记 `FileLogger.Warn` 并跳过该字段；只有**全部规则失败**才返回错误卡片。

### 显示声明

映射到 `IUsageProvider` 的同名声明式属性，控制卡片"显示什么、怎么显示"：

| 字段 | 类型 | 映射到 | 取值示例 |
| --- | --- | --- | --- |
| `quantityUnit` | string | `Quantity` 的单位 | `USD` / `CNY` / `Percent` / `Credit` / `Token` |
| `supportedCardCharts` | string[] | `SupportedCardCharts` | `Line` / `Bar` / `Ring` |
| `card.renderKinds` | string[] | `CardDeclaration.RenderKinds` | `card` / `ring` / `line` |
| `toolTipFields` | string[] | `ToolTipFields` | `ProviderName` / `CurrentValue` / `RefreshCountdown` |

### 第二层预留字段（尚未实现执行器）

以下字段**允许写入并会被反序列化保留**，但本版本**不执行**，仅为格式向前兼容（写了也不会报错，但不生效）：

| 字段 | 规划用途 |
| --- | --- |
| `xhrCaptures` | 拦截 XHR 响应 JSON（`urlGlob` 匹配 + `jsonPath` 取值），覆盖"数据不在 DOM"的场景 |
| `derivedExpressions` | 轻量派生表达式（如 `used/total*100` 算百分比、5h 倒计时） |

---

## 完整示例：抓多个指标 + 写入 Extra

```jsonc
{
  "schemaVersion": 1,
  "providerId": "example-web",
  "displayName": "Example Web",
  "version": "1.0.0",
  "author": "王晨",

  "loginUrl": "https://example.com/login",
  "usageUrl": "https://example.com/console/usage",
  "cookieDomainFilters": [".example.com"],
  "headless": true,

  "configFields": ["Cookie", "Region", "AutoRefresh", "Proxy", "Headless"],

  "extractRules": [
    { "targetField": "used",   "mode": "css",   "pattern": ".quota-used",         "valueType": "currency" },
    { "targetField": "total",  "mode": "css",   "pattern": ".quota-total",        "valueType": "number"   },
    { "targetField": "extra",  "mode": "css",   "pattern": ".plan-name",          "valueType": "text",    "extraKey": "PlanName" },
    { "targetField": "extra",  "mode": "regex", "pattern": "reset in (\\d+) days","valueType": "number",  "extraKey": "ResetDays" }
  ],

  "quantityUnit": "USD",
  "supportedCardCharts": ["Line", "Ring"],
  "defaultRenderKinds": ["card", "ring"],
  "toolTipFields": ["ProviderName", "CurrentValue", "RefreshCountdown"]
}
```

---

## 加载与热重载机制

```
主程序启动
  └─ DeclarativePluginLoader.LoadAndWatch(pluginManager)
       ├─ 扫描 plugins-declarative/*.plugin.json
       ├─ 反序列化为 WebPluginManifest（schemaVersion / SSRF 校验）
       ├─ new DeclarativeWebPlugin(manifest) → PluginManager.RegisterPlugin
       └─ FileSystemWatcher 监听目录（去抖 500ms）
            ├─ Changed → UnregisterPlugin(id) → 重新加载 → 通知 VM 刷新
            ├─ Created → 加载新插件 → 卡片列表新增
            └─ Deleted → UnregisterPlugin(id) → 卡片列表移除
```

**为什么能热重载**：`DeclarativeWebPlugin` 对象生存在**已编译的 Core 程序集**里，`.plugin.json` 只是数据。重载只是替换数据对象，**不涉及 .NET 程序集卸载**（`Assembly.LoadFrom` 无法卸载的难题被绕开）——这是"配置型插件"相对"代码型 DLL 插件"的根本优势。

**req-111 现行实现**：主程序对 `plugins/` 目录挂 `DebouncedDirectoryWatcher`（800ms 防抖），任何文件变更自动触发统一重载管线（重扫声明包 → 重建卡片/mini 图表/任务栏）；设置 → 插件管理页也提供「刷新」按钮手动触发、「安装插件…」按钮（文件夹 / zip，含 zip-slip 防护与安装前预校验）与「校验插件」按钮（直接聚合校验全部声明包）。

---

## i18n 插件语言包（req-116）

声明包里的显示文案（`displayName` / `placeholder` / `display` / `errorGuidance.message` 等）支持两种写法：

| 写法 | 示例 | 行为 |
| --- | --- | --- |
| 字面量（旧插件兼容） | `"display": "5h 限额"` | 原样显示，不随语言切换 |
| i18n 键 | `"display": "i18n:plugin.MiniMax.group.bar5h"` | 按当前语言从语言包解析，缺键回退默认语言再回退键名 |

**语言包文件**：`plugins/<包>/i18n/<lang>.json`（扁平 key→text，如 `i18n/zh-CN.json`、`i18n/en-US.json`）。

**强制约束**：
- 键必须以 `plugin.` 前缀开头（惯例 `plugin.<providerId>.xxx`），非法键被忽略（防宿主词条劫持）；
- 插件重载前宿主自动清除 `plugin.` 命名空间旧词条；
- 校验器会检查 i18n 键在默认语言（zh-CN）词条中是否存在（缺失报警告）。

**语言切换**：设置 → 常规 → 界面语言；切换后宿主自动重载插件，manifest 里的 i18n 键按新语言重新解析。

## errorGuidance 错误码匹配（req-116）

新增 `matchCodes` 字段，匹配宿主生成的稳定错误码（不依赖错误文案措辞/语言）：

```json
"errorGuidance": [
  { "matchCodes": ["credential_missing"], "message": "i18n:plugin.Xxx.guidance.credential" },
  { "matchKeywords": ["1004", "login fail"], "message": "i18n:plugin.Xxx.guidance.authInvalid" },
  { "matchKeywords": [], "message": "i18n:plugin.Xxx.guidance.fallback" }
]
```

**匹配顺序**：① `matchCodes` 精确匹配 → ② `matchKeywords` 包含匹配（保留给服务商 API 原文）→ ③ 两者均空的兑底规则。

**稳定错误码清单**（`UsageErrorCodes`）：`credential_missing`、`auth_invalid`、`network_error`、`timeout`、`cancelled`、`data_empty`、`config_missing`。

---

## 安全约束

| 约束 | 说明 |
| --- | --- |
| SSRF 白名单（req-056） | `loginUrl` / `usageUrl` 必须通过白名单校验，非法 URL 拒绝加载并记日志 |
| Cookie ACL（req-089） | `cookieDomainFilters` 限定注入 Cookie 的域名范围 |
| 凭据域名同源约束 | 携带 `{cookieHeader}` / `{cookie:名}` / `{config:敏感键}` 占位符的 http 端点，目标域必须命中声明包官方域集合（`loginConfig` / `fetch.capture` / `usageUrls` / 顶层 `credentialDomains` 列表），否则宿主拒绝发送；纯 API 型声明包建议显式声明 `"credentialDomains": ["api.xxx.com"]` 以启用强制校验 |
| 敏感配置字段声明 | `configFields` 条目支持 `"sensitive": true`（Password 类型隐含敏感），声明后该字段落盘自动 DPAPI 加密；非常规命名的凭据字段（如 SessionId）务必声明，不要依赖关键词兜底 |
| 无可执行代码 | 声明文件是纯数据，不含代码，天然比外部 DLL 插件安全 |
| 内置优先 | `providerId` 与内置插件冲突时以内置为准，声明式无法覆盖核心插件 |

---

## 边界：什么时候仍需编译型插件？

| 场景 | 声明式（本文） | 编译型（[网页插件开发指南](./plugin-development-guide-web.md)） |
| --- | :---: | :---: |
| 页面上几个 DOM 数字 | ✅ | ✅ |
| 多元素聚合、简单文本提取 | ✅ | ✅ |
| XHR 响应 JSON 捕获（趋势/热力图） | ⏳ 第二层规划 | ✅ |
| 派生计算（倒计时/百分比换算/多字段合成） | ⏳ 第二层规划 | ✅ |
| 并发限制、结果缓存、多区域分支 | ❌ | ✅ |
| 强逻辑（如 MiniMax 的 801 行抽取器） | ❌ | ✅ |

> 原则：**不把声明格式做成图灵完备 DSL**。复杂逻辑请写编译型 `WebPluginBase` 子类，避免"重新发明一门语言"。

---

## 相关文档

- [网页插件开发指南](./plugin-development-guide-web.md)：编译型网页插件（`WebPluginBase` 子类）
- [API 插件开发指南](./plugin-development-guide-api.md)：API 型插件
- [图表开发指南](./chart-development-guide.md)：卡片图表 SDK
- 需求详情：`.dev_require/req-106-declarative-web-plugin-hotreload/req-106-declarative-web-plugin-hotreload.md`
