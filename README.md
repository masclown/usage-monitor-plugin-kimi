# UsageMonitor - AI用量监控工具

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)]()
[![License](https://img.shields.io/badge/license-MIT-green)]()

一款轻量级 Windows 任务栏工具，用于统一监控各类 AI 服务的 API 用量和余额信息。参考 [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) 的任务栏嵌入方式，采用插件式架构，支持灵活扩展更多 AI 服务商。

## 功能特性

- **任务栏嵌入显示** — 将 AI 用量信息直接嵌入 Windows 任务栏，随时可查看
- **系统托盘** — 托盘图标右键菜单，快速访问设置和刷新
- **插件式架构** — 基于 `IUsageProvider` 接口，支持添加任意 AI 服务商插件
- **定时自动刷新** — 可配置刷新间隔，自动获取最新用量数据
- **多服务商支持** — 内置 Deepseek、MiMo 插件，可扩展 OpenAI、Claude 等
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
| 插件加载 | 基于接口的反射加载 |

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
│    IUsageProvider 插件接口                       │
├─────────┼───────────────────────────────────────┤
│  ┌──────┴──────┐  ┌──────────┐  ┌──────────┐   │
│  │  Deepseek   │  │   MiMo   │  │  OpenAI  │   │
│  │   Plugin    │  │  Plugin  │  │  Plugin  │   │
│  └─────────────┘  └──────────┘  └──────────┘   │
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
│   └── Plugins/                        # 插件项目
│       ├── UsageMonitor.Plugin.Deepseek/
│       ├── UsageMonitor.Plugin.MiMo/
│       └── UsageMonitor.Plugin.OpenAI/
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
2. 右键托盘图标 → "设置"，配置 AI 服务商的 API Key
3. 启用"任务栏显示"，用量信息将嵌入任务栏
4. 数据会按照设定的间隔自动刷新

## 插件开发指南

### 1. 创建插件项目

```bash
dotnet new classlib -n UsageMonitor.Plugin.YourProvider
```

### 2. 引用核心库

```xml
<ItemGroup>
  <ProjectReference Include="..\..\UsageMonitor.Core\UsageMonitor.Core.csproj" />
</ItemGroup>
```

### 3. 实现 IUsageProvider 接口

```csharp
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Models;

public class YourProvider : IUsageProvider
{
    public string ProviderId => "your-provider";
    public string DisplayName => "Your Provider";
    public string IconPath => "pack://application:,,,/Assets/your-icon.png";
    
    public IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        new ConfigField("ApiKey", "API Key", ConfigFieldType.Password, true)
    };
    
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config)
    {
        // 调用服务商API查询用量
        var apiKey = config.GetValue("ApiKey");
        // ... 实现查询逻辑
    }
    
    public async Task<bool> ValidateConfigAsync(ProviderConfig config)
    {
        // 验证配置是否有效
    }
}
```

### 4. 部署插件

将编译后的插件 DLL 放入程序目录下的 `plugins/` 文件夹，重启程序即可自动加载。

### 插件接口说明

| 接口/类 | 说明 |
|---------|------|
| `IUsageProvider` | 核心接口，定义用量查询方法 |
| `IPluginMetadata` | 插件元数据（名称、版本、作者等） |
| `UsageInfo` | 用量信息模型（已用/总额度、Token数等） |
| `ProviderConfig` | 服务商配置（API Key 等键值对） |
| `ConfigField` | 配置字段定义（类型、是否必填等） |

## 内置插件

### Deepseek

- **API 端点**: `https://api.deepseek.com/`
- **功能**: 查询账户余额、已用额度、剩余额度
- **配置项**: API Key

### MiMo

- **功能**: 查询 MiMo Token Plan 用量
- **配置项**: API Key / Token

## 已知限制

- 仅支持 Windows 平台
- 任务栏嵌入依赖 Windows Shell COM 接口，Windows 11 可能存在兼容性问题
- 部分 AI 服务商可能未提供用量查询 API，需要逆向或模拟请求

## 路线图

- [x] 项目初始化与架构设计
- [ ] 核心插件接口实现
- [ ] 插件管理器与配置服务
- [ ] Deepseek 插件
- [ ] MiMo 插件
- [ ] WPF 主程序界面
- [ ] 任务栏嵌入窗口
- [ ] 更多插件支持（OpenAI、Claude、Gemini 等）
- [ ] 用量历史统计与图表

## 许可证

MIT License
