# UsageMonitor - AI用量监控工具

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)]()
[![License: BSL 1.1](https://img.shields.io/badge/license-BSL--1.1-orange)](LICENSE)
[![SDK: Apache-2.0](https://img.shields.io/badge/SDK-Apache--2.0-green)](LICENSE-APACHE)

一款轻量级 Windows 任务栏工具，用于统一监控各类 AI 服务的 API 用量和余额信息。参考 [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) 的任务栏嵌入方式，采用**纯声明式插件架构**：宿主零内置 Provider，新增服务商只需编写一份 JSON 声明包（零 DLL、零 C#）。

## 功能特性

- **任务栏嵌入显示** — 将 AI 用量信息直接嵌入 Windows 任务栏，随时可查看
- **系统托盘** — 托盘图标右键菜单，快速访问设置和刷新
- **纯声明式插件** — 插件 = 一个 `plugins/<包名>/defaults.json` 声明包，由通用声明运行器加载，JSON 不可执行代码，天然安全
- **定时自动刷新** — 可配置刷新间隔，自动获取最新用量数据
- **多服务商支持** — 随包提供 MiniMax 声明包；基于声明式 SDK 可扩展任意服务商
- **API Key 安全存储** — 配置文件加密存储，保护敏感信息
- **低资源占用** — 轻量设计，不影响系统性能

## 技术架构

| 组件 | 技术选型 |
|------|---------|
| 语言 | C# 12 |
| 框架 | .NET 8 / WPF |
| 任务栏嵌入 | Windows Shell DeskBand (COM) |
| HTTP 请求 | System.Net.Http (HttpClient) |
| 配置存储 | JSON (%AppData%/UsageMonitor/) |
| 插件加载 | 声明包扫描（plugins/*/defaults.json → 通用 DeclarativeProvider 运行器） |

### 架构图

```
┌─────────────────────────────────────────────────┐
│                 UsageMonitor.App                │
│          (WPF 主程序 / 任务栏窗口)               │
├─────────────────────────────────────────────────┤
│               UsageMonitor.Core                 │
│    ┌──────────┐  ┌───────────┐  ┌───────────┐  │
│    │PluginMgr │  │ConfigSvc  │  │RefreshSvc │  │
│    └────┬─────┘  └───────────┘  └───────────┘  │
├─────────┼───────────────────────────────────────┤
│    声明清单 PluginManifest + DeclarativeProvider  │
├─────────┼───────────────────────────────────────┤
│  plugins/ 声明包（纯 JSON，零 DLL）              │
│  ┌──────┴───────────────┐  ┌──────────────┐    │
│  │ MiniMax/defaults.json│  │ <你的声明包>  │    │
│  └──────────────────────┘  └──────────────┘    │
└─────────────────────────────────────────────────┘
```

## 项目结构

```
UsageMonitor/
├── UsageMonitor.sln                    # 解决方案文件
├── src/
│   ├── UsageMonitor.Core/              # 核心库（接口、模型、服务）
│   │   ├── Models/                     # 数据模型
│   │   │   ├── UsageInfo.cs            # 用量信息模型
│   │   │   ├── ProviderConfig.cs       # 服务商配置模型
│   │   │   └── ConfigField.cs          # 配置字段定义
│   │   ├── Plugins/                    # 插件接口与管理
│   │   │   ├── IUsageProvider.cs       # 用量提供者接口
│   │   │   ├── IPluginMetadata.cs      # 插件元数据接口
│   │   │   └── PluginManager.cs        # 插件管理器
│   │   ├── Services/                   # 核心服务
│   │   │   ├── ConfigService.cs        # 配置管理服务
│   │   │   └── RefreshService.cs       # 定时刷新服务
│   │   └── UsageMonitor.Core.csproj
│   │
│   ├── UsageMonitor.App/               # WPF 主程序
│   │   ├── Views/                      # 界面视图
│   │   ├── ViewModels/                 # 视图模型
│   │   ├── Helpers/                    # 辅助类
│   │   └── UsageMonitor.App.csproj
│   │
│   └── Plugins/                       # 声明包源目录
│       ├── UsageMonitor.Plugin.MiniMax/   # 纯声明包（仅 defaults.json，零 DLL）
│       ├── UsageMonitor.Plugin.Template.Api/
│       └── UsageMonitor.Plugin.Template.Web/
│
├── .devdoc/                            # 开发文档
└── README.md
```

## 快速开始

### 环境要求

- Windows 10 / 11
- .NET 8 SDK
- Visual Studio 2022 或 JetBrains Rider（推荐）

### 编译运行

```bash
# 克隆项目
git clone <repo-url>

# 编译
dotnet build UsageMonitor.sln

# 运行
dotnet run --project src/UsageMonitor.App
```

### 使用说明

1. 启动程序后，系统托盘会出现 UsageMonitor 图标
2. 首次启动若未发现任何声明包，主窗口为空态：请将声明包目录放入程序目录的 `plugins/` 下（随包构建已自动部署 MiniMax 声明包）
3. 右键托盘图标 → "设置"，完成登录（🌐 获取登录态）或填写密钥
4. 启用"任务栏显示"，用量信息将嵌入任务栏；数据按设定间隔自动刷新

## 插件开发指南（纯声明包，零 C#）

一个插件 = 一个目录 `plugins/<包名>/`，核心是一份声明清单（单文件 `defaults.json`，或拆分为 `plugin.json`（接入）/ `fetch.json`（取数）/ `display.json`（显示）三文件，加载时自动合并）。

### 1. 最小声明包示例

```jsonc
{
  "providerId": "YourProvider",
  "displayName": "Your Provider",
  "meta": { "version": "1.0.0", "author": "you", "description": "..." },
  // 浏览器登录声明（Cookie 鉴权）
  "loginConfig": { "loginUrl": "https://example.com", "cookieDomainFilters": [ "example.com" ] },
  // 配置字段（设置页自动生成输入控件）
  "configFields": [ { "key": "Cookie", "displayName": "登录态", "fieldType": "Password" } ],
  // 取数声明：capture（浏览器响应捕获）或 http（直连端点，支持 {config:Key}/{cookie:名}/{cookieHeader} 占位符）
  "fetch": {
    "capture": { "navigateUrl": "https://example.com/usage", "cookieDomain": ".example.com" },
    "endpoints": [ { "urlMatch": "usage_api", "fields": [ { "path": "$.used_percent", "target": "used_percent", "transform": "parsePercent" } ] } ]
  },
  // 卡片/任务栏显示声明
  "card": { "primaryMetric": "used_percent", "charts": [ /* Bar/Line/HeatMap/Number 声明 */ ] },
  "taskbar": { "miniCharts": [ /* MiniRingChart/MiniText 声明 */ ] }
}
```

### 2. 部署

把声明包目录放入程序目录的 `plugins/` 文件夹，重启程序即自动扫描加载（经 PluginValidator 校验，字段名走 SDK 白名单，http 端点 URL 经 SSRF 防护）。可用 `UsageMonitor.exe --validate-plugin <路径>` 预检。

### 3. 参考

- 完整示例：`src/Plugins/UsageMonitor.Plugin.MiniMax/defaults.json`（登录/取数/计算列/图表/迷你图全覆盖）
- SDK 字段白名单与图表契约：`docs/` 下相关规范文档
- 声明表达不了的逻辑 → 提 issue 补框架原语（项目原则：不回退写插件 C#）

## 随包声明包

### MiniMax（plugins/UsageMonitor.Plugin.MiniMax/）

- **形态**: 纯声明包（仅 defaults.json，无 DLL）
- **功能**: 查询 Token Plan 用量、5h / 本周限额、积分余额、每日 Token 趋势与缓存命中热力图
- **登录方式**: 浏览器 Cookie 登录（Playwright 捕获）+ 声明式 HTTP 直连回退
- **配置项**: Token Plan 订阅密钥 / Cookie 登录态 / 接口区域（CN/Global）

## 已知限制

- 仅支持 Windows 平台
- 任务栏嵌入依赖 Windows Shell COM 接口，Windows 11 可能存在兼容性问题
- 部分 AI 服务商可能未提供用量查询 API，需要逆向或模拟请求

## 路线图

- [x] 项目初始化与架构设计
- [x] 核心插件接口实现
- [x] 插件管理器与配置服务
- [x] MiniMax 插件（网页版 + API 版）
- [x] WPF 主程序界面（现代化双主题 UI）
- [x] 任务栏嵌入窗口（圆环图 + 文字模式）
- [x] 托盘悬浮窗（触发区域可配置）
- [x] 用量历史统计与图表（折线图/柱状图/热力图/编程时段）
- [x] 安全加固（DPAPI 加密、Cookie 保护、SSRF 防护）
- [x] 图表 SDK v2 泛化架构
- [x] 完全声明式插件架构（宿主零内置 Provider，MiniMax 外置为纯声明包）
- [ ] 声明包生产测试期（SDK 缺口收集与原语补齐）
- [ ] 更多声明包支持（OpenAI、Claude、Gemini 等）
- [ ] MiniMax 声明包降级 samples/（sample 声明包 + sample 数据库）

## 许可证

> 🔒 安全设计与凭据保护说明见 [SECURITY.md](SECURITY.md)（DPAPI 加密、网络行为边界、插件安全模型与漏洞披露方式）。

本项目采用 **open-core（开放核心）** 双许可结构：

| 范围 | 许可证 | 说明 |
|------|--------|------|
| 主程序（App / Core 框架实现 / LoginHelper） | **Business Source License 1.1** | 源码公开，个人 / 教育 / 内部使用免费；**未经商业授权，不得作为付费 / 托管 / 订阅 / 竞争性服务对外提供**。自 **2030-07-24** 起自动转为 Apache-2.0。全文见 [LICENSE](LICENSE)。 |
| 插件 SDK / 接口契约 + 插件模板（Template.Api / Template.Web） | **Apache License 2.0** | 便于社区自由开发第三方插件，无商用限制。全文见 [LICENSE-APACHE](LICENSE-APACHE)。 |
| 云端后端 / 付费高级功能（规划中） | 专有闭源 | 多端同步、云备份、通知、多账号、二阶数据看板、高级插件等，不随本仓库开源。 |

> 商业授权（用于付费 / 托管场景）请联系版权方。
>
> 注：第三方 AI 服务商的品牌 Logo、名称、商标归各自所有者所有，本项目不随包分发其 Logo（改为运行时按服务商域名抓取 favicon 缓存），也不主张任何相关权利。
