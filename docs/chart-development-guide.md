# 自定义图表注册指南

> req-086-3.6：如何在插件中注册自定义图表，让主窗口自动识别并渲染。

---

## 图表体系概览

UsageMonitor 的图表体系分为两层：

| 层 | 接口 | 说明 |
|---|------|------|
| v1 | `IUsageChartFactory` | 基础工厂，`Create()` 无参数 |
| v2 | `IUsageChartFactory2` | 扩展工厂，`Create(ChartContext)` 接收运行时上下文 |

插件通过 `IUsageProvider` 的两个属性注册图表：

```csharp
// v1 图表工厂列表
IReadOnlyList<IUsageChartFactory> ChartFactories { get; }

// v2 图表工厂列表（可选，优先级高于 v1）
IReadOnlyList<IUsageChartFactory2>? CustomChartFactories { get; }
```

---

## 内置图表类型

主程序内置 5 种图表（`ChartKind` 枚举）：

| ChartKind | 说明 | 适用场景 |
|-----------|------|----------|
| `Line` | 折线图 | 用量趋势 |
| `Bar` | 柱状图 | 用量对比 |
| `Ring` | 环形图 | 百分比展示 |
| `StackedBar` | 堆叠柱状图 | 多维度用量 |
| `Area` | 面积图 | 累计用量 |

---

## 实现自定义图表

### 第一步：实现 IUsageChart

```csharp
public class MyCustomChart : IUsageChart
{
    public ChartKind Kind => ChartKind.Line; // 或自定义种类
    public Type ControlType => typeof(MyCustomChartControl); // WPF 控件类型
    public string DisplayName => "我的自定义图表";

    public void Bind(IChartData data, IChartTheme? theme)
    {
        // 根据 data 更新控件渲染
        if (data is LineChartData lineData)
        {
            // 更新折线图数据点
        }
    }
}
```

### 第二步：实现图表工厂

**v1 工厂**（简单场景）：

```csharp
public class MyCustomChartFactory : IUsageChartFactory
{
    public ChartKind Kind => ChartKind.Line;
    public IUsageChart Create() => new MyCustomChart();
}
```

**v2 工厂**（需要上下文信息）：

```csharp
public class MyCustomChartFactory2 : IUsageChartFactory2
{
    public ChartKind Kind => ChartKind.Line;
    public IUsageChart Create() => new MyCustomChart();
    public IUsageChart Create(ChartContext context)
    {
        var chart = new MyCustomChart();
        // 根据 context.Location / context.Theme 调整图表
        return chart;
    }
}
```

### 第三步：在插件中注册

```csharp
public class YourPlugin : PluginBase, IUsageProvider
{
    // v1 图表注册
    public IReadOnlyList<IUsageChartFactory> ChartFactories => new IUsageChartFactory[]
    {
        new MyCustomChartFactory(),
    };

    // v2 图表注册（可选，优先级更高）
    public IReadOnlyList<IUsageChartFactory2>? CustomChartFactories => new IUsageChartFactory2[]
    {
        new MyCustomChartFactory2(),
    };
}
```

---

## ChartContext 说明

v2 工厂接收的 `ChartContext` 包含：

| 属性 | 类型 | 说明 |
|------|------|------|
| `Location` | `ChartLocation` | 展示位置（Card / Popup / Detail） |
| `Theme` | `IChartTheme?` | 当前主题（颜色刷） |
| `Width` | `double` | 可用宽度 |
| `Height` | `double` | 可用高度 |

```csharp
public IUsageChart Create(ChartContext context)
{
    var chart = new MyCustomChart();
    if (context.Location == ChartLocation.Card)
    {
        // 卡片模式：简化显示
    }
    else if (context.Location == ChartLocation.Detail)
    {
        // 详情模式：完整显示
    }
    return chart;
}
```

---

## IChartTheme 主题适配

`IChartTheme` 提供 5 个颜色刷（`object` 类型，WPF 宿主装箱为 `Brush`）：

| 属性 | 说明 |
|------|------|
| `LowBrush` | 低用量色（<60%） |
| `MidBrush` | 中用量色（60~85%） |
| `HighBrush` | 高用量色（>85%） |
| `TrackBrush` | 背景轨道色 |
| `TextBrush` | 轴/标签文字色 |

```csharp
public void Bind(IChartData data, IChartTheme? theme)
{
    if (theme?.HighBrush is System.Windows.Media.Brush highBrush)
    {
        // 使用高用量色
    }
}
```

---

## IChartData 数据接口

图表数据实现 `IChartData` 接口：

```csharp
public interface IChartData
{
    ChartKind Kind { get; }
}
```

内置数据类型：

| 类型 | 说明 |
|------|------|
| `LineChartData` | 折线图数据（Points 列表） |
| `BarChartData` | 柱状图数据（Items 列表） |
| `RingChartData` | 环形图数据（Percent） |
| `MetricBarData` | 指标条数据（v2） |
| `MetricGridData` | 指标网格数据（v2） |

---

## 卡片图表配置

插件可声明支持的卡片图表类型：

```csharp
// 支持的卡片图表种类
public IReadOnlyList<CardChartKind> SupportedCardCharts => new[]
{
    CardChartKind.Line, CardChartKind.Bar, CardChartKind.Ring
};

// 默认渲染种类：在插件目录下 defaults.json 的 card.renderKinds 数组中声明
// {
//   "card": {
//     "renderKinds": ["card", "ring"]
//   }
// }

// 环形图支持的指标
public IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent" };
```

> **SDK 破坏性变更提示**：`DefaultRenderKinds` 接口成员已于 2026-07-24 删除，默认渲染种类请改在插件 `defaults.json` 的 `card.renderKinds` 数组中声明。

---

## 完整示例

```csharp
// 1. 自定义图表控件（WPF UserControl）
public partial class MyChartControl : UserControl { ... }

// 2. 图表实现
public class MyChart : IUsageChart
{
    public ChartKind Kind => ChartKind.Bar;
    public Type ControlType => typeof(MyChartControl);
    public string DisplayName => "自定义柱状图";

    public void Bind(IChartData data, IChartTheme? theme)
    {
        if (data is BarChartData barData)
        {
            // 更新控件
        }
    }
}

// 3. 工厂
public class MyChartFactory : IUsageChartFactory2
{
    public ChartKind Kind => ChartKind.Bar;
    public IUsageChart Create() => new MyChart();
    public IUsageChart Create(ChartContext context) => new MyChart();
}

// 4. 插件注册
public class YourPlugin : PluginBase, IUsageProvider
{
    public IReadOnlyList<IUsageChartFactory> ChartFactories => new IUsageChartFactory[]
    {
        new MyChartFactory(),
    };

    public IReadOnlyList<IUsageChartFactory2>? CustomChartFactories => new IUsageChartFactory2[]
    {
        new MyChartFactory(),
    };
}
```

---

## 注意事项

1. **v2 优先**：宿主检测到 `IUsageChartFactory2` 实现时优先调用 `Create(ChartContext)`，否则回退到 `Create()`
2. **主题适配**：`IChartTheme` 的颜色刷是 `object` 类型，WPF 端需判断为 `Brush` 后使用
3. **控件生命周期**：图表控件由宿主创建和管理，插件只需提供 `ControlType`，不直接实例化控件
4. **Kind 唯一性**：同一 `ChartKind` 只注册一个工厂，重复注册后者覆盖前者
