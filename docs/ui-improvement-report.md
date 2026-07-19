# UsageMonitor UI 全方位改进报告

> 审查对象：`D:\应用开发\UsageMonitor`（.NET 8 / WPF）
> 审查范围：7 个窗口、8 个自定义控件、双主题系统、设计 Token
> 审查时间：2026-07-19
> 目标读者：开发团队（按 P0 → P2 优先级实施）

---

## 零、整改问题与开发需求清单的映射表

本次 UI 审查识别的 **42 项可改进点**（U-01 ~ U-42）已录入项目开发需求清单 `.dev_require/.dev_require_list.md`，按模块聚合为 **11 条主需求**（req-071 ~ req-081），每条主需求含 2~6 个子需求。其中 2 项（U-08 / U-39）完全重复已有需求，跳过新建。

| UI 编号 | 主需求 req-id | 子需求说明 | 优先级 | 关联现有 req |
|---------|--------------|-----------|--------|--------------|
| U-01 | req-071 | 浅色主题 TextTertiary 对比度修正 | P0 | — |
| U-02 | req-071 | 深色主题 TextTertiary 对比度修正 | P0 | — |
| U-03 | req-071 | 卡片阴影 BlurRadius 减小 | P0 | — |
| U-04 | req-072 | 标题栏图标与字号重构 | P0 | — |
| U-05 | req-072 | 卡片拆分子模板 + Expander 折叠 | P0 | req-064-4 |
| U-06 | req-073 | 设置窗口左侧导航 + 三组分组 | P0 | — |
| U-07 | req-074 | 自定义控件依赖属性默认值主题化 | P0 | — |
| U-08 | **跳过**（已被覆盖） | TriggerAreaOverlayWindow 颜色 Token 化 | P0 | **req-070-1** |
| U-09 | req-075 | Tokens.xaml 追加间距/状态/动画/Elevation | P1 | — |
| U-10 | req-075 | 全项目硬编码 FontSize 改用 Token | P1 | — |
| U-11 | req-075 | 中文字体回退链扩展 | P1 | — |
| U-12 | req-076 | PrimaryButton 悬停反馈强化 | P1 | — |
| U-13 | req-076 | TextBox focus ring | P1 | — |
| U-14 | req-076 | PasswordBoxEx 显隐切换控件 | P1 | — |
| U-15 | req-076 | ComboBox disabled 样式 | P1 | — |
| U-16 | req-072 | 顶部按钮风格统一 | P1 | — |
| U-17 | req-072 | 进度条尺寸统一 | P1 | — |
| U-18 | req-072 | 底部状态栏扩充 | P1 | — |
| U-19 | req-072 | 空状态引导 | P1 | req-064-5 |
| U-20 | req-077 | 输入框单位标签与范围校验 | P1 | — |
| U-21 | req-077 | 提示文字拆为要点列表 | P1 | — |
| U-22 | req-077 | 嵌套 ListView 改造 | P1 | — |
| U-23 | req-077 | 色阶操作按钮分级 | P1 | — |
| U-24 | req-077 | 只读 TextBox 样式区分 | P1 | — |
| U-25 | req-073 | 全局保存栏统一 | P1 | — |
| U-26 | req-078 | DataGrid 列宽比例 | P1 | — |
| U-27 | req-078 | 右侧摘要卡比例宽度 | P1 | — |
| U-28 | req-078 | 图表切换淡入动画 | P1 | — |
| U-29 | req-078 | 空状态引导升级 | P1 | — |
| U-30 | req-079 | emoji 按钮替换 | P1 | — |
| U-31 | req-079 | Taskbar 圆环图尺寸增大 | P1 | — |
| U-32 | req-079 | Taskbar 字号统一 | P1 | — |
| U-33 | req-079 | Taskbar 加载骨架屏 | P1 | — |
| U-34 | req-079 | TrayTooltip 拖拽条加宽 | P1 | — |
| U-35 | req-079 | TrayTooltip 三列指标字号增大 | P1 | — |
| U-36 | req-080 | TriggerArea Thumb Style 抽取 | P1 | req-070-1 |
| U-37 | req-080 | 边 Thumb 命中区扩大 | P1 | — |
| U-38 | req-080 | 蒙版顶部提示条 + Esc | P1 | — |
| U-39 | **跳过**（已被覆盖） | ErrorColorConverter 走主题 | P2 | **req-064-7** |
| U-40 | req-081 | Brush Freeze 性能优化 | P2 | — |
| U-41 | req-081 | 进度条平滑过渡 | P2 | — |
| U-42 | req-081 | 主题切换渐变过渡 | P2 | — |

**说明**：
- **跳过原因**：U-08 与现有 req-070-1（TriggerAreaOverlayWindow 硬编码颜色改走主题 Token）完全重复；U-39 与现有 req-064-7（错误颜色 Converter 改 Style+DataTrigger）完全重复。
- **关联现有 req**：U-05 部分内容包含 req-064-4（拆 ProviderCard UserControl）；U-19 实施时一并覆盖 req-064-5（a11y AutomationProperties 标注）；U-36 抽 Thumb Style 时与 req-070-1 合并实施。
- **详情文件位置**：每条主需求详情位于 `.dev_require/req-XXX-<slug>/req-XXX-<slug>.md`，含背景、子需求清单、改进前后代码片段、设计依据。

---

## 一、执行摘要

整体 UI 已具备较为完整的设计系统骨架（Tokens / Styles / 双主题），珊瑚橙强调色辨识度高，深色主题观感成熟。但存在 **6 项 P0 级 UI 问题**：文本对比度未达 WCAG AA、窗口标题栏视觉失衡、卡片信息密度过高、设置窗口 Tab 信息架构混乱、自定义控件默认颜色硬编码、TriggerAreaOverlayWindow 全硬编码颜色未走 Token。此外有 12 项 P1 问题涵盖交互反馈、空状态、键盘可达性、动画过渡等。建议优先修复对比度与窗口标题栏，再处理卡片信息架构与设置 Tab 重组。

报告共识别 **42 项** 可改进点，分布于 8 大模块，每项均含：问题描述 / 代码位置 / 改进前代码 / 改进后代码 / 设计依据。

---

## 二、问题总览表

| 编号 | 等级 | 模块 | 问题 | 文件位置 |
|------|------|------|------|----------|
| U-01 | P0 | 主题 | 浅色主题 TextTertiary 对比度 3.5:1 未达 WCAG AA | Themes/Light.xaml:24 |
| U-02 | P0 | 主题 | 深色主题 TextTertiary 对比度 3.3:1 未达 WCAG AA | Themes/Dark.xaml:26 |
| U-03 | P0 | 主题 | 卡片阴影 BlurRadius 18/22 过大，卡片"漂"感过重 | Themes/Dark.xaml:58, Light.xaml:55 |
| U-04 | P0 | MainWindow | 标题栏 40x40 图标与 21pt 文字比例失衡 | MainWindow.xaml:25-35 |
| U-05 | P0 | MainWindow | 卡片内 6 段信息堆叠，密度过高 | MainWindow.xaml:84-431 |
| U-06 | P0 | 设置 | 6 Tab 无层级分组，导航混乱 | SettingsWindow.xaml:12-598 |
| U-07 | P0 | 自定义控件 | 控件依赖属性默认值硬编码颜色，与主题脱钩 | Controls/RingChartControl.cs:66-84, BarChartControl.cs:66 |
| U-08 | P0 | TriggerArea | 全部颜色硬编码未走 Token | TriggerAreaOverlayWindow.xaml:28-101 |
| U-09 | P1 | 设计系统 | 缺少间距/交互状态/动画时长 Token | Themes/Tokens.xaml |
| U-10 | P1 | 设计系统 | 字号 Token 引用率低，XAML 中大量硬编码 FontSize | 多文件 |
| U-11 | P1 | 设计系统 | 中文字体回退链不完整 | Themes/Tokens.xaml:24 |
| U-12 | P1 | 按钮 | PrimaryButton 悬停反馈过弱（仅 opacity 0.9） | Themes/Styles.xaml:64-94 |
| U-13 | P1 | 输入 | TextBox 缺少 focus ring | Themes/Styles.xaml:192-219 |
| U-14 | P1 | 输入 | PasswordBox 缺少显示/隐藏切换 | Themes/Styles.xaml:221-245 |
| U-15 | P1 | 输入 | ComboBox 缺少 disabled 样式 | Themes/Styles.xaml:281-310 |
| U-16 | P1 | MainWindow | 顶部按钮风格不统一（PrimaryButton + GhostButton 混用） | MainWindow.xaml:38-77 |
| U-17 | P1 | MainWindow | 进度条尺寸不统一（9px vs 7px） | MainWindow.xaml:190,268,290 |
| U-18 | P1 | MainWindow | 底部状态栏信息不足 | MainWindow.xaml:436-445 |
| U-19 | P1 | MainWindow | 缺少空状态（无插件时白屏） | MainWindow.xaml:82-433 |
| U-20 | P1 | 设置 | "刷新间隔"等输入框无单位标签与范围校验 | SettingsWindow.xaml:37-41 |
| U-21 | P1 | 设置 | "环形图中心数字"提示文字过长无层次 | SettingsWindow.xaml:82-85 |
| U-22 | P1 | 设置 | 嵌套 ListView 易出现滚动嵌套 | SettingsWindow.xaml:246-284 |
| U-23 | P1 | 设置 | 色阶操作按钮无视觉层级 | SettingsWindow.xaml:462-472 |
| U-24 | P1 | 设置 | 日志路径 TextBox 样式未区分只读 | SettingsWindow.xaml:573 |
| U-25 | P1 | 设置 | 全局保存行为不一致（每 Tab 独立保存按钮） | SettingsWindow.xaml 全文 |
| U-26 | P1 | 历史 | DataGrid 8 列列宽硬编码，窄屏溢出 | HistoryWindow.xaml:240-249 |
| U-27 | P1 | 历史 | 右侧摘要卡固定 188px 宽度，4K 屏比例失衡 | HistoryWindow.xaml:134 |
| U-28 | P1 | 历史 | 图表切换无过渡动画 | HistoryWindow.xaml:96-124 |
| U-29 | P1 | 历史 | 空状态"暂无历史数据"无引导插画/操作 | HistoryWindow.xaml:126-129 |
| U-30 | P1 | 插件配置 | "🌐 获取登录态" emoji 与项目风格不一致 | PluginConfigWindow.xaml:50 |
| U-31 | P1 | 任务栏 | 圆环图模板 38x38 过小，中心数字不可读 | TaskbarWindow.xaml:78-86 |
| U-32 | P1 | 任务栏 | 字号 11/12pt 在任务栏偏小 | TaskbarWindow.xaml:31,58,63 |
| U-33 | P1 | 任务栏 | 缺少加载骨架屏 | TaskbarWindow.xaml |
| U-34 | P1 | 托盘悬浮 | 拖拽条 6px 过窄且无 hover 反馈 | TrayTooltipWindow.xaml:20-21 |
| U-35 | P1 | 托盘悬浮 | 三列指标 10pt 偏小 | TrayTooltipWindow.xaml:65,72,79 |
| U-36 | P1 | TriggerArea | 8 个 Thumb 代码重复，未抽 Style | TriggerAreaOverlayWindow.xaml:55-103 |
| U-37 | P1 | TriggerArea | 边 Thumb 6x20 过小难点击 | TriggerAreaOverlayWindow.xaml:55-78 |
| U-38 | P1 | TriggerArea | 缺少 Esc 提示与取消按钮 | TriggerAreaOverlayWindow.xaml |
| U-39 | P2 | 转换器 | ErrorColorConverter 颜色硬编码未走主题 | Helpers/Converters.cs:46-50 |
| U-40 | P2 | 转换器 | Brush 未 Freeze，高频刷新性能损耗 | Helpers/Converters.cs:48,49,234 |
| U-41 | P2 | 动画 | 进度条数值变化无平滑过渡 | MainWindow.xaml 多处 |
| U-42 | P2 | 动画 | 主题切换无渐变过渡 | Helpers/ThemeManager.cs |

---

## 三、P0 级问题详细改进

### U-01 [P0] 浅色主题 TextTertiary 对比度未达 WCAG AA

- **文件**：`Themes/Light.xaml:24`
- **问题**：`TextTertiaryBrush` 颜色 `#FF8A93A2` 对背景 `#FFF8F9FB` 的对比度仅约 **3.5:1**，低于 WCAG 2.1 AA 标准要求的 4.5:1（普通文字）。该色用于"作者"、"详情"等次要文字，对视力较弱用户不可读。
- **改进前**：
```xml
<SolidColorBrush x:Key="TextTertiaryBrush" Color="#FF8A93A2" />
```
- **改进后**：
```xml
<!-- 提升至 #6B7280，对比度约 5.3:1，达到 AA 标准 -->
<SolidColorBrush x:Key="TextTertiaryBrush" Color="#FF6B7280" />
```
- **设计依据**：WCAG 2.1 SC 1.4.3 Contrast (Minimum)。普通文字（< 18pt 或 14pt bold）对比度需 ≥ 4.5:1。
- **影响范围**：`MainWindow.xaml:204,243`、`SettingsWindow.xaml:193,413-420` 等所有使用 `TextTertiaryBrush` 的位置会自动生效（DynamicResource）。

### U-02 [P0] 深色主题 TextTertiary 对比度未达 WCAG AA

- **文件**：`Themes/Dark.xaml:26`
- **问题**：`TextTertiaryBrush` 颜色 `#FF64748B` 对深色背景 `#FF141824` 的对比度约 **3.3:1**，未达 AA 标准。
- **改进前**：
```xml
<SolidColorBrush x:Key="TextTertiaryBrush" Color="#FF64748B" />
```
- **改进后**：
```xml
<!-- 提升至 #94A3B8（与 TextSecondary 接近但仍可区分），对比度约 6.2:1 -->
<SolidColorBrush x:Key="TextTertiaryBrush" Color="#FF94A3B8" />
```
- **备注**：原 `TextSecondaryBrush` 为 `#FF9AA6B8`（对比度约 5.5:1），改后 Tertiary 与 Secondary 接近但仍可区分。建议同步调整 Secondary 至 `#FFB8C2D1`（约 7.5:1）以拉开层次。

### U-03 [P0] 卡片阴影 BlurRadius 过大

- **文件**：`Themes/Dark.xaml:58`、`Themes/Light.xaml:55`
- **问题**：`CardShadowEffect` 的 `BlurRadius="18"`（深色）/ `22`（浅色）过大，导致每张卡片视觉上"漂浮"过远，多卡片纵向堆叠时阴影互相重叠形成深色带，破坏信息层级。参考 Linear / Vercel 风格的卡片阴影通常 BlurRadius 8-12。
- **改进前**：
```xml
<!-- Dark.xaml -->
<DropShadowEffect x:Key="CardShadowEffect" Color="#FF000000" BlurRadius="18"
                  ShadowDepth="3" Direction="270" Opacity="0.45" />
<!-- Light.xaml -->
<DropShadowEffect x:Key="CardShadowEffect" Color="#FF334155" BlurRadius="22"
                  ShadowDepth="2" Direction="270" Opacity="0.14" />
```
- **改进后**：
```xml
<!-- Dark.xaml：BlurRadius 18→10，ShadowDepth 3→2，Opacity 0.45→0.35 -->
<DropShadowEffect x:Key="CardShadowEffect" Color="#FF000000" BlurRadius="10"
                  ShadowDepth="2" Direction="270" Opacity="0.35" />
<!-- Light.xaml：BlurRadius 22→12，ShadowDepth 2→1 -->
<DropShadowEffect x:Key="CardShadowEffect" Color="#FF334155" BlurRadius="12"
                  ShadowDepth="1" Direction="270" Opacity="0.12" />
```
- **设计依据**：Material Design Elevation 表 — Level 1（卡片）建议 BlurRadius 6-10、ShadowDepth 1-2。过大阴影会导致视觉噪点与性能损耗（DropShadowEffect 是 WPF 中较重的 Effect）。

### U-04 [P0] MainWindow 标题栏视觉失衡

- **文件**：`MainWindow.xaml:25-35`
- **问题**：Logo `40x40` 与标题 `FontSize=21` 比例失衡，40px 图标视觉重量压过文字标题。参考 Linear / Vercel / Notion 桌面应用，标题栏图标通常 24-28px。同时副标题 12pt 与主标题 21pt 形成的字号阶梯跳跃过大（21→12 缺少 15-18 中间档）。
- **改进前**：
```xml
<Border DockPanel.Dock="Left" Width="40" Height="40" VerticalAlignment="Center">
    <Image Source="{Binding CurrentLogoSource}" Width="40" Height="40" .../>
</Border>
<StackPanel DockPanel.Dock="Left" Margin="12,0,0,0" VerticalAlignment="Center">
    <TextBlock Text="AI 用量监控" FontSize="21" FontWeight="Bold" .../>
    <TextBlock Text="统一监控各家 AI 服务的用量与余额" FontSize="12" .../>
</StackPanel>
```
- **改进后**：
```xml
<!-- 图标 40→28，与标题视觉重量平衡；标题字号走 token；副标题字号走 token -->
<Border DockPanel.Dock="Left" Width="28" Height="28" VerticalAlignment="Center">
    <Image Source="{Binding CurrentLogoSource}" Width="28" Height="28"
           RenderOptions.BitmapScalingMode="HighQuality" />
</Border>
<StackPanel DockPanel.Dock="Left" Margin="10,0,0,0" VerticalAlignment="Center">
    <TextBlock Text="AI 用量监控"
               FontSize="{DynamicResource FontSizeTitle}"
               FontWeight="SemiBold"
               Foreground="{DynamicResource TextPrimaryBrush}" />
    <TextBlock Text="统一监控各家 AI 服务的用量与余额"
               FontSize="{DynamicResource FontSizeCaption}"
               Foreground="{DynamicResource TextSecondaryBrush}"
               Margin="0,2,0,0" />
</StackPanel>
```
- **设计依据**：标题字号 21pt 偏大且未走 `FontSizeTitle` token（18）。改后图标 28px、标题 18pt（SemiBold 而非 Bold），副标题 11pt，符合 macOS / Windows 应用标题栏惯例。

### U-05 [P0] MainWindow 卡片信息密度过高

- **文件**：`MainWindow.xaml:84-431`
- **问题**：单个用量卡片 `DataTemplate` 高达 **347 行**，承载 6 个段落：服务商头 / 5h 限额 / 周限额 / 视频赠送 / 余额快照 / 6 种图表。多 Provider 时整页卡片纵向堆叠过长，且单卡内信息无折叠/展开机制。开发者难以维护，用户认知负荷高。
- **改进方向**：拆分子模板 + 默认折叠次要信息。
- **改进后（核心思路）**：

  **第 1 步**：将卡片模板拆分为 5 个独立 `DataTemplate`，放入 `Window.Resources`：
```xml
<Window.Resources>
    <DataTemplate x:Key="CardHeaderTemplate">
        <!-- 服务商名 + 订阅胶囊 + 刷新/设置按钮 -->
    </DataTemplate>
    <DataTemplate x:Key="CardLimitBarsTemplate">
        <!-- 5h 限额 + 周限额 + 视频赠送进度条，可选展开 -->
    </DataTemplate>
    <DataTemplate x:Key="CardBalanceTemplate">
        <!-- 余额快照 4 列 -->
    </DataTemplate>
    <DataTemplate x:Key="CardChartTemplate">
        <!-- 6 种图表按 CardChartKinds 切换 -->
    </DataTemplate>
    <DataTemplate x:Key="CardErrorTemplate">
        <!-- 错误信息 -->
    </DataTemplate>
</Window.Resources>
```

  **第 2 步**：主卡片模板仅组合这些子模板，并加入折叠开关：
```xml
<DataTemplate DataType="{x:Type vm:ProviderUsageViewModel}">
    <Border Style="{StaticResource CardBorderStyle}" Margin="0,0,0,14">
        <StackPanel>
            <!-- 头部始终显示 -->
            <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource CardHeaderTemplate}" />

            <!-- 错误信息始终显示 -->
            <ContentPresenter Content="{Binding}"
                              ContentTemplate="{StaticResource CardErrorTemplate}"
                              Visibility="{Binding IsError, Converter={StaticResource BoolToVisibility}}" />

            <!-- 详情区折叠/展开：默认折叠，点击头部右侧"详情"切换 -->
            <Expander IsExpanded="{Binding IsDetailExpanded, Mode=TwoWay}"
                      Header=""
                      Visibility="{Binding HasDetail, Converter={StaticResource BoolToVisibility}}">
                <StackPanel>
                    <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource CardLimitBarsTemplate}" />
                    <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource CardBalanceTemplate}" />
                    <ContentPresenter Content="{Binding}" ContentTemplate="{StaticResource CardChartTemplate}" />
                </StackPanel>
            </Expander>
        </StackPanel>
    </Border>
</DataTemplate>
```

  **第 3 步**：在 `ProviderUsageViewModel` 增加 `IsDetailExpanded` 属性（默认 false），仅展开当前关注的卡片。
- **设计依据**：渐进式信息展示（Progressive Disclosure）。多 Provider 时用户只关心头部 + 当前进度条，详情按需展开。

### U-06 [P0] 设置窗口 Tab 信息架构混乱

- **文件**：`SettingsWindow.xaml:12-598`
- **问题**：6 个 Tab 平铺（常规设置 / 插件管理 / 任务栏显示 / 悬浮窗 / 色阶 / 诊断日志），无层级分组。Tab 标题与 Tab 内一级标题重复（"常规设置" Tab 内又有 "常规设置" 标题）。色阶 Tab 内容长（用量色阶 + 热力图色阶）但无视觉分隔提示。
- **改进方向**：左侧导航分组 + 取消 Tab 内标题重复 + 色阶 Tab 内部分段卡片化。
- **改进后（核心结构）**：
```xml
<!-- 替换 TabControl 为 Grid + 左侧导航 -->
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="180" />  <!-- 左侧导航 -->
        <ColumnDefinition Width="*" />   <!-- 右侧内容 -->
    </Grid.ColumnDefinitions>

    <!-- 左侧导航分组 -->
    <Border Grid.Column="0" Background="{DynamicResource SurfaceAltBrush}" Padding="8,12">
        <StackPanel>
            <!-- 分组：通用 -->
            <TextBlock Text="通用" Style="{StaticResource NavGroupHeaderStyle}" />
            <RadioButton Content="外观与刷新"
                         GroupName="SettingsNav"
                         Style="{StaticResource NavItemStyle}"
                         IsChecked="{Binding CurrentSection, Converter={StaticResource EnumToBool}, ConverterParameter=General}" />
            <RadioButton Content="插件管理"
                         GroupName="SettingsNav"
                         Style="{StaticResource NavItemStyle}"
                         IsChecked="{Binding CurrentSection, Converter={StaticResource EnumToBool}, ConverterParameter=Plugins}" />

            <!-- 分组：显示 -->
            <TextBlock Text="显示" Style="{StaticResource NavGroupHeaderStyle}" Margin="0,12,0,0" />
            <RadioButton Content="任务栏" .../>
            <RadioButton Content="悬浮窗" .../>

            <!-- 分组：高级 -->
            <TextBlock Text="高级" Style="{StaticResource NavGroupHeaderStyle}" Margin="0,12,0,0" />
            <RadioButton Content="色阶" .../>
            <RadioButton Content="诊断与日志" .../>
        </StackPanel>
    </Border>

    <!-- 右侧内容：用 ContentControl + DataTemplate Selector 切换 -->
    <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto">
        <ContentControl Content="{Binding}">
            <ContentControl.ContentTemplateSelector>
                <local:SettingsSectionSelector />
            </ContentControl.ContentTemplateSelector>
        </ContentControl>
    </ScrollViewer>

    <!-- 全局底部保存栏（解决 U-25：每 Tab 独立保存不一致问题） -->
    <Border Grid.ColumnSpan="2" VerticalAlignment="Bottom"
            Background="{DynamicResource SurfaceBrush}" Padding="16,10">
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="取消" Style="{StaticResource GhostButtonStyle}" Margin="0,0,8,0" />
            <Button Content="保存" Style="{StaticResource PrimaryButtonStyle}" />
        </StackPanel>
    </Border>
</Grid>
```
- **设计依据**：VS Code / Figma / Notion 设置页采用左侧导航 + 分组。当设置项 ≥ 5 个时，左侧导航比顶部 Tab 更易扫描。

### U-07 [P0] 自定义控件依赖属性默认值硬编码颜色

- **文件**：`Controls/RingChartControl.cs:66-84`、`Controls/BarChartControl.cs:66,76,81`
- **问题**：自定义控件的依赖属性默认值使用硬编码颜色（如 `RingChartControl.TrackBrush` 默认 `#2D2D3F`），与主题 `TrackBrush`（`#2A3040`）不一致。若调用方未显式传值，控件在浅色主题下会显示深色硬编码色。
- **改进前**：
```csharp
// RingChartControl.cs:64-67
public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
    nameof(TrackBrush), typeof(Brush), typeof(RingChartControl),
    new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3F)),
        FrameworkPropertyMetadataOptions.AffectsRender));
```
- **改进后**：依赖属性默认值改为 `null`，在控件构造函数中通过 `TryFindResource` 读取主题资源；如未找到再回退硬编码值：
```csharp
public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
    nameof(TrackBrush), typeof(Brush), typeof(RingChartControl),
    new FrameworkPropertyMetadata(null,
        FrameworkPropertyMetadataOptions.AffectsRender,
        OnTrackBrushChanged));

public RingChartControl()
{
    // 控件构造时从主题资源解析默认 TrackBrush
    if (TrackBrush == null)
        SetValue(TrackBrushProperty, TryFindResource("TrackBrush") ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x40)));
}

private static void OnTrackBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    // 主题切换时若用户未显式覆盖，跟随主题更新
    if (e.NewValue == null && d is RingChartControl ctrl)
    {
        ctrl.SetValue(TrackBrushProperty, ctrl.TryFindResource("TrackBrush"));
    }
}
```
- **影响范围**：`RingChartControl`、`BarChartControl`、`MiniLineChartControl`、`HistoryLineChartControl`、`YearHeatMapControl`、`DayNightArcControl` 全部依赖属性均需检查。
- **设计依据**：WPF 主题资源解析机制。控件默认值硬编码会导致主题切换后无法跟随更新。

### U-08 [P0] TriggerAreaOverlayWindow 颜色全硬编码

- **文件**：`Views/TriggerAreaOverlayWindow.xaml:28-101`
- **问题**：蒙版层 `#80000000`、触发矩形边框 `#FF1E90FF`、填充 `#201E90FF`、Thumb 背景 `White` 等 6 处颜色全部硬编码，未走主题 Token。该窗口在浅色主题下视觉割裂。
- **改进前**：
```xml
<Rectangle x:Name="MaskLayer" Fill="#80000000" ... />
<Border x:Name="TriggerBorder"
        Background="#201E90FF"
        BorderBrush="#FF1E90FF"
        BorderThickness="2" .../>
<Thumb ... Background="White" BorderBrush="#FF1E90FF" ... />
```
- **改进后**：
```xml
<!-- 在 Themes/Tokens.xaml 增加覆盖层专用 Token -->
<sys:Color x:Key="OverlayMaskColor">#80000000</sys:Color>
<sys:Color x:Key="TriggerBorderColor">#FF1E90FF</sys:Color>
<sys:Color x:Key="TriggerFillTintColor">#201E90FF</sys:Color>
<SolidColorBrush x:Key="TriggerBorderBrush" Color="{StaticResource TriggerBorderColor}" />
<SolidColorBrush x:Key="TriggerFillTintBrush" Color="{StaticResource TriggerFillTintColor}" />
<SolidColorBrush x:Key="OverlayMaskBrush" Color="{StaticResource OverlayMaskColor}" />
<SolidColorBrush x:Key="ThumbBackgroundBrush" Color="White" />
<SolidColorBrush x:Key="ThumbBorderBrush" Color="{StaticResource TriggerBorderColor}" />

<!-- XAML 中改用 DynamicResource -->
<Rectangle x:Name="MaskLayer" Fill="{DynamicResource OverlayMaskBrush}" ... />
<Border x:Name="TriggerBorder"
        Background="{DynamicResource TriggerFillTintBrush}"
        BorderBrush="{DynamicResource TriggerBorderBrush}"
        BorderThickness="2" .../>
<Thumb ... Background="{DynamicResource ThumbBackgroundBrush}"
         BorderBrush="{DynamicResource ThumbBorderBrush}" ... />
```
- **设计依据**：项目自身在 `Themes/Tokens.xaml` 注释中已声明"随明暗变化的颜色一律放 Dark/Light.xaml"——但该窗口违反了约定。

---

## 四、P1 级问题详细改进

### U-09 [P1] 设计 Token 不完整：缺少间距/状态/动画

- **文件**：`Themes/Tokens.xaml`
- **问题**：当前 Token 仅含圆角、内边距、字号、字体、语义用量色。**缺少**：间距阶梯（如 4/8/12/16/24/32）、交互状态色（hover/pressed/disabled）、动画时长（fast/normal/slow）、elevation token。
- **改进后（在 Tokens.xaml 追加）**：
```xml
<!-- 间距阶梯（4px 基准） -->
<Thickness x:Key="SpaceXxs">4</Thickness>
<Thickness x:Key="SpaceXs">8</Thickness>
<Thickness x:Key="SpaceSm">12</Thickness>
<Thickness x:Key="SpaceMd">16</Thickness>
<Thickness x:Key="SpaceLg">24</Thickness>
<Thickness x:Key="SpaceXl">32</Thickness>

<!-- 交互状态透明度 -->
<sys:Double x:Key="OpacityHover">0.85</sys:Double>
<sys:Double x:Key="OpacityPressed">0.7</sys:Double>
<sys:Double x:Key="OpacityDisabled">0.4</sys:Double>

<!-- 动画时长 -->
<sys:Double x:Key="DurationFast">120</sys:Double>   <!-- ms -->
<sys:Double x:Key="DurationNormal">200</sys:Double>
<sys:Double x:Key="DurationSlow">320</sys:Double>

<!-- Elevation 阶梯 -->
<DropShadowEffect x:Key="Elevation1" Color="#FF000000" BlurRadius="6" ShadowDepth="1" Direction="270" Opacity="0.20" />
<DropShadowEffect x:Key="Elevation2" Color="#FF000000" BlurRadius="10" ShadowDepth="2" Direction="270" Opacity="0.35" />
<DropShadowEffect x:Key="Elevation3" Color="#FF000000" BlurRadius="14" ShadowDepth="4" Direction="270" Opacity="0.45" />
```
- **应用方式**：所有 XAML 中的 `Margin="0,0,0,14"` 应改为 `Margin="{DynamicResource SpaceMd}"`。

### U-10 [P1] 字号 Token 引用率低

- **文件**：`MainWindow.xaml`、`SettingsWindow.xaml`、`HistoryWindow.xaml` 等全文
- **问题**：`Tokens.xaml` 定义了 `FontSizeDisplay/Title/Subtitle/Body/Caption`（25/18/15/13/11），但 XAML 中大量直接写 `FontSize="21"`、`FontSize="20"`、`FontSize="22"`、`FontSize="12"`、`FontSize="13"`、`FontSize="11"`，未引用 Token。
- **改进前**：
```xml
<TextBlock Text="AI 用量监控" FontSize="21" FontWeight="Bold" />
<TextBlock Text="历史用量" FontSize="22" FontWeight="Bold" />
<TextBlock Text="常规设置" FontSize="20" FontWeight="Bold" />
```
- **改进后**：统一引用 Token：
```xml
<TextBlock Text="AI 用量监控" FontSize="{DynamicResource FontSizeTitle}" FontWeight="SemiBold" />
<TextBlock Text="历史用量" FontSize="{DynamicResource FontSizeTitle}" FontWeight="SemiBold" />
<TextBlock Text="常规设置" FontSize="{DynamicResource FontSizeTitle}" FontWeight="SemiBold" />
```
- **备注**：建议增加 `FontSizeHeading`（22）用于窗口主标题，与 `FontSizeTitle`（18）区分窗口级 vs 卡片级标题。

### U-11 [P1] 中文字体回退链不完整

- **文件**：`Themes/Tokens.xaml:24`
- **问题**：当前字体链 `Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI` 仅覆盖 Windows。若未来扩展到 macOS（开发者使用 Mac 调试）或不同 Windows 版本（如 Server Core 无 YaHei），字体会回退到系统默认。
- **改进后**：
```xml
<!-- 完整回退链：Win 中文 → Win 英文 → Mac 中文 → Mac 英文 → Linux → 通用 -->
<FontFamily x:Key="AppFontFamily">
    Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI,
    PingFang SC, Hiragino Sans GB, Source Han Sans SC,
    -apple-system, BlinkMacSystemFont, sans-serif
</FontFamily>
```

### U-12 [P1] PrimaryButton 悬停反馈过弱

- **文件**：`Themes/Styles.xaml:64-94`
- **问题**：`PrimaryButtonStyle` 悬停状态仅将 `Opacity` 降到 `0.9`，视觉变化微弱（人眼几乎不可察）。缺少 hover 时的色变或阴影变化。
- **改进前**：
```xml
<Trigger Property="IsMouseOver" Value="True">
    <Setter TargetName="bd" Property="Opacity" Value="0.9" />
</Trigger>
```
- **改进后**：
```xml
<!-- 悬停：透明度轻微降低 + 阴影轻微抬升 -->
<Trigger Property="IsMouseOver" Value="True">
    <Setter TargetName="bd" Property="Opacity" Value="0.92" />
    <Setter TargetName="bd" Property="Effect">
        <Setter.Value>
            <DropShadowEffect Color="#33FF6B4A" BlurRadius="8" ShadowDepth="0" Opacity="0.6" />
        </Setter.Value>
    </Setter>
</Trigger>
<!-- 按下：明显下沉 -->
<Trigger Property="IsPressed" Value="True">
    <Setter TargetName="bd" Property="Opacity" Value="0.85" />
    <Setter TargetName="bd" Property="RenderTransform">
        <Setter.Value>
            <ScaleTransform ScaleX="0.97" ScaleY="0.97" CenterX="0.5" CenterY="0.5" />
        </Setter.Value>
    </Setter>
</Trigger>
```
- **设计依据**：Material Design / Fluent Design 按钮反馈三态：hover（轻微抬升 + 阴影）/ pressed（轻微下沉 + 缩放）/ disabled（透明度 0.4 + 灰色文字）。

### U-13 [P1] TextBox 缺少 focus ring

- **文件**：`Themes/Styles.xaml:192-219`
- **问题**：TextBox 获得焦点时仅边框颜色变为 AccentBrush，无 focus ring（如外发光或 2px outline），键盘用户难以辨识当前焦点位置。无障碍合规度低。
- **改进前**：
```xml
<Trigger Property="IsKeyboardFocused" Value="True">
    <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
</Trigger>
```
- **改进后**：
```xml
<Trigger Property="IsKeyboardFocused" Value="True">
    <Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
    <Setter TargetName="bd" Property="Effect">
        <Setter.Value>
            <!-- 外发光模拟 focus ring：强调色 25% 透明，blur 4 -->
            <DropShadowEffect Color="#59FF6B4A" BlurRadius="4" ShadowDepth="0" Opacity="0.8" />
        </Setter.Value>
    </Setter>
</Trigger>
```
- **设计依据**：WCAG 2.4.7 Focus Visible。键盘焦点必须有可见指示器。

### U-14 [P1] PasswordBox 缺少显示/隐藏切换

- **文件**：`Themes/Styles.xaml:221-245`
- **问题**：`PasswordBox` 样式中无显示密码按钮，用户输入 API Key 时无法确认输入是否正确。
- **改进后（核心思路）**：将 `PasswordBox` 包装为 UserControl，右侧加眼睛图标 Button。简化版样式：
```xml
<!-- 因 WPF PasswordBox 不支持在 ControlTemplate 内嵌入按钮（PasswordBox.Password 不能绑定），
     建议封装为 PasswordBoxEx : UserControl，内部含 PasswordBox + ToggleButton。
     此处给出封装控件的简化 XAML 结构： -->
<UserControl x:Class="UsageMonitor.App.Controls.PasswordBoxEx"
             xmlns="..." xmlns:x="..." Height="32">
    <Grid>
        <PasswordBox x:Name="PART_PasswordBox"
                     Background="{DynamicResource MutedBrush}"
                     BorderBrush="{DynamicResource DividerBrush}"
                     BorderThickness="1"
                     Padding="10,7"
                     FontFamily="{DynamicResource AppFontFamily}" />
        <ToggleButton x:Name="PART_Toggle"
                      Width="28" Height="28"
                      HorizontalAlignment="Right" VerticalAlignment="Center"
                      Cursor="Hand"
                      ToolTip="显示/隐藏密码">
            <TextBlock x:Name="EyeIcon" Text="👁"
                       FontSize="14"
                       FontFamily="Segoe MDL2 Assets" />
        </ToggleButton>
    </Grid>
</UserControl>
<!-- Code-behind 中监听 ToggleButton.IsChecked，
     切换时通过 PasswordBox.Replace 或新 PasswordBox 实例切换显示模式 -->
```

### U-15 [P1] ComboBox 缺少 disabled 样式

- **文件**：`Themes/Styles.xaml:281-310`
- **问题**：`ComboBox` 样式未处理 `IsEnabled=False` 状态，禁用时仍显示完整颜色，用户误以为可点击。
- **改进后（在 Style.Triggers 中追加）**：
```xml
<Style TargetType="ComboBox">
    <!-- ...原有 Setter... -->
    <Style.Triggers>
        <Trigger Property="IsEnabled" Value="False">
            <Setter Property="Opacity" Value="0.5" />
            <Setter Property="Cursor" Value="No" />
        </Trigger>
    </Style.Triggers>
</Style>
```

### U-16 [P1] MainWindow 顶部按钮风格不统一

- **文件**：`MainWindow.xaml:38-77`
- **问题**：顶部 3 个按钮"刷新"用 `PrimaryButtonStyle`，"历史"和"设置"用 `GhostButtonStyle`，视觉权重不一致。当前主操作（刷新）已通过命令的 `IsRunning` 状态有旋转反馈，无需再用 PrimaryButton 强调。
- **改进后**：3 个按钮统一用 `GhostButtonStyle`，仅"刷新"按钮在 `IsRunning=True` 时显示加载状态：
```xml
<StackPanel Orientation="Horizontal" DockPanel.Dock="Right"
            HorizontalAlignment="Right" VerticalAlignment="Center">
    <!-- 刷新：默认 Ghost，运行中变 AccentSoft -->
    <Button Command="{Binding RefreshCommand}" Margin="8,0,0,0"
            Style="{StaticResource GhostButtonStyle}">
        <Button.Style>
            <Style TargetType="Button" BasedOn="{StaticResource GhostButtonStyle}">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding RefreshCommand.IsRunning}" Value="True">
                        <Setter Property="Style" Value="{StaticResource AccentSoftButtonStyle}" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Button.Style>
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="↻" Margin="0,0,6,0" VerticalAlignment="Center" />
            <TextBlock Text="刷新" VerticalAlignment="Center" />
        </StackPanel>
    </Button>
    <Button Content="历史" Click="OnHistoryClick"
            Style="{StaticResource GhostButtonStyle}" Margin="8,0,0,0" />
    <Button Content="设置" Click="OnSettingsClick"
            Style="{StaticResource GhostButtonStyle}" Margin="8,0,0,0" />
</StackPanel>
```

### U-17 [P1] 进度条尺寸不统一

- **文件**：`MainWindow.xaml:190, 229, 268, 290`
- **问题**：5h 限额进度条 `Height="9"`，周限额 `Height="9"`，视频赠送 5h `Height="7"`，视频赠送周 `Height="7"`。同一卡片内 4 条进度条尺寸不统一。
- **改进后**：全部统一为 `Height="8"`，并通过 token 集中管理：
```xml
<!-- Tokens.xaml -->
<sys:Double x:Key="ProgressBarHeight">8</sys:Double>
<CornerRadius x:Key="ProgressBarRadius">4</CornerRadius>

<!-- MainWindow.xaml 中所有进度条 -->
<Grid Margin="0,5,0,0" Height="{DynamicResource ProgressBarHeight}">
    <Border CornerRadius="{DynamicResource ProgressBarRadius}" Background="{DynamicResource TrackBrush}" />
    <Border CornerRadius="{DynamicResource ProgressBarRadius}" .../>
</Grid>
```

### U-18 [P1] 底部状态栏信息不足

- **文件**：`MainWindow.xaml:436-445`
- **问题**：底部仅显示"刷新间隔 / 插件数量"。缺少：上次刷新时间、刷新进度（当前 Provider / 总 Provider）、网络状态、错误计数。
- **改进后**：
```xml
<Border Grid.Row="2" Margin="0,14,0,0"
        BorderBrush="{DynamicResource DividerBrush}" BorderThickness="0,1,0,0"
        Padding="0,10,0,0">
    <DockPanel>
        <!-- 左：上次刷新时间 + 刷新进度 -->
        <StackPanel DockPanel.Dock="Left" Orientation="Horizontal">
            <TextBlock FontSize="12" Foreground="{DynamicResource TextTertiaryBrush}">
                <Run Text="上次刷新: " />
                <Run Text="{Binding LastRefreshTime, StringFormat={}{0:HH:mm:ss}}" />
            </TextBlock>
            <TextBlock FontSize="12" Foreground="{DynamicResource TextTertiaryBrush}"
                       Margin="16,0,0,0" Visibility="{Binding IsRefreshing, Converter={StaticResource BoolToVisibility}}">
                <Run Text="刷新中: " />
                <Run Text="{Binding RefreshProgress, StringFormat={}{0}/{1}}" />
            </TextBlock>
        </StackPanel>

        <!-- 右：配置摘要 -->
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" HorizontalAlignment="Right">
            <TextBlock FontSize="12" Foreground="{DynamicResource TextTertiaryBrush}">
                <Run Text="间隔 " />
                <Run Text="{Binding RefreshInterval, StringFormat={}{0}s}" />
                <Run Text=" · 启用 " />
                <Run Text="{Binding EnabledUsages.Count}" />
                <Run Text="/" />
                <Run Text="{Binding TotalPluginCount}" />
            </TextBlock>
            <!-- 错误计数胶囊 -->
            <Border Background="{DynamicResource AccentSoftBrush}"
                    CornerRadius="{DynamicResource RadiusPill}"
                    Padding="8,2" Margin="12,0,0,0"
                    Visibility="{Binding HasErrors, Converter={StaticResource BoolToVisibility}}">
                <TextBlock FontSize="11" Foreground="{DynamicResource DangerBrush}">
                    <Run Text="⚠ " />
                    <Run Text="{Binding ErrorCount}" />
                    <Run Text=" 个错误" />
                </TextBlock>
            </Border>
        </StackPanel>
    </DockPanel>
</Border>
```

### U-19 [P1] 缺少空状态

- **文件**：`MainWindow.xaml:82-433`
- **问题**：当所有插件禁用或未配置时，`ItemsControl` 显示空白，无任何引导性提示。用户不知道下一步该做什么。
- **改进后**：
```xml
<ScrollViewer Grid.Row="1" ...>
    <Grid>
        <!-- 用量卡片列表 -->
        <ItemsControl ItemsSource="{Binding EnabledUsages}">
            <ItemsControl.ItemTemplate>...</ItemsControl.ItemTemplate>
        </ItemsControl>

        <!-- 空状态：无插件时显示 -->
        <Border VerticalAlignment="Center" HorizontalAlignment="Center"
                Visibility="{Binding IsEmpty, Converter={StaticResource BoolToVisibility}}"
                Padding="40">
            <StackPanel HorizontalAlignment="Center">
                <!-- 简单 SVG 插画（不依赖外部资源） -->
                <Viewbox Width="80" Height="80">
                    <Canvas>
                        <Ellipse Canvas.Left="10" Canvas.Top="10" Width="60" Height="60"
                                 Fill="{DynamicResource SurfaceAltBrush}" />
                        <TextBlock Canvas.Left="22" Canvas.Top="22" FontSize="36"
                                   Foreground="{DynamicResource TextTertiaryBrush}"
                                   Text="📊" />
                    </Canvas>
                </Viewbox>
                <TextBlock Text="还没有任何 AI 服务被启用"
                           FontSize="{DynamicResource FontSizeSubtitle}"
                           FontWeight="SemiBold"
                           Foreground="{DynamicResource TextPrimaryBrush}"
                           HorizontalAlignment="Center" Margin="0,16,0,8" />
                <TextBlock Text="点击下方按钮打开设置，配置服务商 API Key 后开始监控"
                           FontSize="{DynamicResource FontSizeCaption}"
                           Foreground="{DynamicResource TextSecondaryBrush}"
                           HorizontalAlignment="Center" Margin="0,0,0,20" />
                <Button Content="打开设置"
                        Click="OnSettingsClick"
                        Style="{StaticResource PrimaryButtonStyle}"
                        HorizontalAlignment="Center" />
            </StackPanel>
        </Border>
    </Grid>
</ScrollViewer>
```
- **ViewModel 增加**：`bool IsEmpty => !EnabledUsages.Any();`

### U-20 [P1] 输入框无单位标签与范围校验

- **文件**：`SettingsWindow.xaml:37-41, 64-75`
- **问题**："刷新间隔（秒）"、"警告阈值（%）"等输入框仅标签暗示单位，输入框本身无单位后缀、无范围提示、无校验反馈。
- **改进后**：
```xml
<!-- 包装为带单位的输入控件 -->
<StackPanel Margin="0,0,0,16">
    <TextBlock Text="刷新间隔"
               FontSize="{DynamicResource FontSizeBody}"
               Foreground="{DynamicResource TextSecondaryBrush}" Margin="0,0,0,4" />
    <Grid HorizontalAlignment="Left" Width="180">
        <TextBox Text="{Binding RefreshInterval, UpdateSourceTrigger=PropertyChanged}"
                 Margin="0,0,40,0" />
        <!-- 单位后缀 -->
        <TextBlock Text="秒"
                   HorizontalAlignment="Right" VerticalAlignment="Center"
                   Margin="0,0,12,0"
                   FontSize="{DynamicResource FontSizeCaption}"
                   Foreground="{DynamicResource TextTertiaryBrush}"
                   IsHitTestVisible="False" />
    </Grid>
    <!-- 范围提示 -->
    <TextBlock Text="建议 60-3600 秒（1 分钟至 1 小时）"
               FontSize="{DynamicResource FontSizeCaption}"
               Foreground="{DynamicResource TextTertiaryBrush}"
               Margin="0,4,0,0" />
    <!-- 校验错误（绑定到 ViewModel 的 ValidationErrors） -->
    <TextBlock Text="{Binding RefreshIntervalError}"
               FontSize="{DynamicResource FontSizeCaption}"
               Foreground="{DynamicResource DangerBrush}"
               Margin="0,4,0,0"
               Visibility="{Binding HasRefreshIntervalError, Converter={StaticResource BoolToVisibility}}" />
</StackPanel>
```

### U-21 [P1] 提示文字过长无层次

- **文件**：`SettingsWindow.xaml:82-85`
- **问题**：原文"单击可开启或关闭，关闭后字颜色变为灰色。修改后点击保存设置。离开鼠标后 sticky 秒数后回默认。" 4 句话堆在一行，用户难以快速扫读。
- **改进后**：拆分为要点列表：
```xml
<ItemsControl Margin="0,0,0,12">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><StackPanel /></ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <sys:String>• 单击：开启或关闭该 metric</sys:String>
    <sys:String>• 关闭后：中心数字变灰色</sys:String>
    <sys:String>• 滚轮：循环切换显示顺序</sys:String>
    <sys:String>• 鼠标离开 sticky 秒后：回默认 metric</sys:String>
    <sys:String>• 修改后请点击"保存设置"生效</sys:String>
</ItemsControl>
```

### U-22 [P1] 嵌套 ListView 滚动问题

- **文件**：`SettingsWindow.xaml:246-284`
- **问题**：外层 `ListView MaxHeight="240"` 强制滚动，内层每个 `ListViewItem` 又含 `ComboBox`。滚动嵌套时鼠标滚轮可能被内层捕获。
- **改进后**：外层改用 `ItemsControl + ScrollViewer`，禁用内层滚动：
```xml
<!-- 用 ScrollViewer 替代 ListView 的内置滚动 -->
<ScrollViewer MaxHeight="240" VerticalScrollBarVisibility="Auto">
    <ItemsControl ItemsSource="{Binding PluginItems}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <!-- 同原模板 -->
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</ScrollViewer>
```

### U-23 [P1] 色阶操作按钮无视觉层级

- **文件**：`SettingsWindow.xaml:462-472`
- **问题**："添加档位 / 恢复默认 / 应用预览" 3 个按钮均用 `AccentSoftButtonStyle` 或 `GhostButtonStyle` 平行排列，无视觉权重区分。用户不知道哪个是主要操作。
- **改进后**：按操作重要性分级：
```xml
<StackPanel Orientation="Horizontal" Margin="0,8,0,16">
    <!-- 主操作：添加 -->
    <Button Content="添加档位" Margin="0,0,8,0" Padding="14,6"
            Command="{Binding AddTierCommand}"
            Style="{StaticResource PrimaryButtonStyle}" />
    <!-- 次要：应用预览 -->
    <Button Content="应用预览" Margin="0,0,8,0" Padding="14,6"
            Command="{Binding ApplyPreviewCommand}"
            Style="{StaticResource AccentSoftButtonStyle}" />
    <!-- 危险：恢复默认（移到最右 + 间距） -->
    <Button Content="恢复默认" Padding="14,6"
            Command="{Binding ResetTierCommand}"
            Style="{StaticResource GhostButtonStyle}"
            Margin="24,0,0,0" />
</StackPanel>
<!-- 独立的保存按钮放在底部全局保存栏 -->
```

### U-24 [P1] 日志路径 TextBox 样式未区分只读

- **文件**：`SettingsWindow.xaml:573`
- **问题**：`IsReadOnly="True"` 的 TextBox 视觉上与可写输入框完全一致，用户可能尝试编辑。
- **改进后**：增加 `ReadOnlyTextBoxStyle`：
```xml
<!-- Styles.xaml 新增 -->
<Style x:Key="ReadOnlyTextBoxStyle" TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
    <Setter Property="Background" Value="{DynamicResource SurfaceAltBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}" />
    <Setter Property="CaretBrush" Value="Transparent" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="Cursor" Value="Arrow" />
</Style>

<!-- SettingsWindow.xaml -->
<TextBox x:Name="LogPathTextBox" IsReadOnly="True"
         Style="{StaticResource ReadOnlyTextBoxStyle}"
         Margin="0,0,0,16" />
```

### U-25 [P1] 全局保存行为不一致

- **文件**：`SettingsWindow.xaml` 全文
- **问题**：每个 Tab 内都有独立"保存设置"按钮，且保存按钮位置不一致（有的在 Tab 顶部下方，有的在底部）。用户难以判断修改是否已保存。
- **改进后**：参考 U-06 的左侧导航 + 全局底部保存栏。所有 Tab 共享底部保存/取消按钮，状态指示器显示"未保存修改"：
```xml
<!-- 底部保存栏 -->
<Border Grid.ColumnSpan="2" VerticalAlignment="Bottom"
        Background="{DynamicResource SurfaceBrush}"
        BorderBrush="{DynamicResource DividerBrush}" BorderThickness="0,1,0,0"
        Padding="16,10">
    <DockPanel>
        <!-- 左：未保存提示 -->
        <TextBlock DockPanel.Dock="Left"
                   Text="● 有未保存的修改"
                   FontSize="{DynamicResource FontSizeCaption}"
                   Foreground="{DynamicResource WarningBrush}"
                   VerticalAlignment="Center"
                   Visibility="{Binding HasUnsavedChanges, Converter={StaticResource BoolToVisibility}}" />
        <!-- 右：保存按钮 -->
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="取消" Style="{StaticResource GhostButtonStyle}" Margin="0,0,8,0"
                    Click="OnCancelClick" />
            <Button Content="保存" Style="{StaticResource PrimaryButtonStyle}"
                    Command="{Binding SaveAllCommand}" />
        </StackPanel>
    </DockPanel>
</Border>
```

### U-26 [P1] HistoryWindow DataGrid 列宽硬编码

- **文件**：`HistoryWindow.xaml:240-249`
- **问题**：8 列列宽 `"100,90,70,70,70,70,70"` 全部硬编码，窄窗口下出现水平滚动条。
- **改进后**：使用 `*` 比例宽度 + 最小宽度：
```xml
<DataGrid.Columns>
    <DataGridTextColumn Header="Provider" Binding="{Binding ProviderName}"
                        Width="*" MinWidth="100" SortMemberPath="ProviderName" />
    <DataGridTextColumn Header="日期" Binding="{Binding Day}"
                        Width="Auto" MinWidth="80" SortMemberPath="Day" />
    <DataGridTextColumn Header="刷新时间" Binding="{Binding RefreshedAtText}"
                        Width="Auto" MinWidth="80" SortMemberPath="RefreshedAtText" />
    <!-- 数值列固定宽度，使用右对齐 -->
    <DataGridTextColumn Header="最高%" Binding="{Binding MaxPercent, StringFormat={}{0:0.0}}"
                        Width="60" SortMemberPath="MaxPercent">
        <DataGridTextColumn.ElementStyle>
            <Style TargetType="TextBlock">
                <Setter Property="HorizontalAlignment" Value="Right" />
            </Style>
        </DataGridTextColumn.ElementStyle>
    </DataGridTextColumn>
    <!-- 其他数值列同模式 -->
</DataGrid.Columns>
```

### U-27 [P1] 右侧摘要卡固定宽度

- **文件**：`HistoryWindow.xaml:134`
- **问题**：`Width="188"` 硬编码，在 4K 屏（3840x2160）上仅占屏幕 5% 宽度，与主图区比例失衡。
- **改进后**：使用比例宽度：
```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="3*" />  <!-- 主图区 -->
    <ColumnDefinition Width="1*" MinWidth="200" MaxWidth="260" />  <!-- 摘要卡 -->
</Grid.ColumnDefinitions>
```

### U-28 [P1] 图表切换无过渡动画

- **文件**：`HistoryWindow.xaml:96-124`
- **问题**：4 个图表（折线/柱状/热力图/日夜弧）通过 Visibility 切换，无淡入淡出。
- **改进后**：用 `Storyboard` 加 200ms 淡入：
```xml
<Grid>
    <controls:HistoryLineChartControl x:Name="LineChart" ...>
        <controls:HistoryLineChartControl.Style>
            <Style TargetType="controls:HistoryLineChartControl">
                <Style.Triggers>
                    <Trigger Property="Visibility" Value="Visible">
                        <Trigger.EnterActions>
                            <BeginStoryboard>
                                <Storyboard>
                                    <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                                     From="0" To="1" Duration="0:0:0.2" />
                                </Storyboard>
                            </BeginStoryboard>
                        </Trigger.EnterActions>
                    </Trigger>
                </Style.Triggers>
            </Style>
        </controls:HistoryLineChartControl.Style>
    </controls:HistoryLineChartControl>
    <!-- 其他图表同模式 -->
</Grid>
```

### U-29 [P1] 历史空状态无引导

- **文件**：`HistoryWindow.xaml:126-129`
- **问题**：仅一行文字"暂无历史数据"，无引导插画或操作建议。
- **改进后**：参考 U-19 模式，加入插画 + 操作按钮（"去刷新数据"或"调整时间范围"）。

### U-30 [P1] Emoji 按钮风格不一致

- **文件**：`PluginConfigWindow.xaml:50`
- **问题**：`Content="🌐 获取登录态"` 含 emoji，但项目其他按钮全部为纯文字。
- **改进后**：
```xml
<Button x:Name="GetCookieButton" Content="获取登录态"
        Margin="0,0,12,0" Click="OnGetCookieClick"
        Style="{StaticResource GhostButtonStyle}"
        Visibility="Collapsed"
        ToolTip="自动启动独立 Edge 窗口并打开登录页，登录完成后自动获取 Cookie" />
<!-- 若需图标，改用 Segoe MDL2 Assets 字体：
     <TextBlock Text="&#xE774;" FontFamily="Segoe MDL2 Assets" /> -->
```

### U-31 [P1] 任务栏圆环图尺寸过小

- **文件**：`TaskbarWindow.xaml:78-86`
- **问题**：`Width="38" Height="38"` 在任务栏 44px 高度内，圆环图实际可用空间约 38px，中心数字几乎不可读（< 8px 字号）。
- **改进后**：增大至 44px，并允许任务栏高度自适应：
```xml
<!-- 任务栏窗口高度从 44 增至 48，圆环图 38→44 -->
<Window ... Height="48" Width="500" ...>
    <DataTemplate x:Key="TaskbarRingChartTemplate">
        <Grid Width="44" Height="44" Margin="0,0,8,0" ...>
            <controls:RingChartControl Width="44" Height="44"
                                       Size="44" StrokeThickness="4" ... />
        </Grid>
    </DataTemplate>
</Window>
```

### U-32 [P1] 任务栏字号偏小

- **文件**：`TaskbarWindow.xaml:31, 58, 63`
- **问题**：文字模式 `FontSize=12`、折线模式上下文字 `FontSize=11`，在 100% DPI 任务栏中偏小（Windows 任务栏时钟通常 12-13px）。
- **改进后**：统一至 12pt，并增加 DPI 自适应：
```xml
<TextBlock FontSize="12" FontWeight="SemiBold" ...>
    <Run Text="{Binding DisplayName, Mode=OneWay}" />
</TextBlock>
<!-- 折线模式百分比字号 11→12 -->
<TextBlock FontSize="12" Foreground="{DynamicResource TextSecondaryBrush}" ... />
```

### U-33 [P1] 任务栏缺少加载骨架屏

- **文件**：`TaskbarWindow.xaml`
- **问题**：刷新中无视觉反馈，用户以为程序卡死。
- **改进后**：增加骨架动画：
```xml
<DataTemplate x:Key="TaskbarLoadingTemplate">
    <Border Width="80" Height="36" Margin="0,0,8,0">
        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
            <!-- 骨架方块 -->
            <Border Width="14" Height="14" CornerRadius="3" Margin="0,0,6,0">
                <Border.Background>
                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                        <GradientStop Color="{DynamicResource TrackColor}" Offset="0" />
                        <GradientStop Color="{DynamicResource MutedColor}" Offset="0.5" />
                        <GradientStop Color="{DynamicResource TrackColor}" Offset="1" />
                        <LinearGradientBrush.RelativeTransform>
                            <TranslateTransform x:Name="ShimmerTransform" X="-1" />
                        </LinearGradientBrush.RelativeTransform>
                    </LinearGradientBrush>
                </Border.Background>
                <Border.Triggers>
                    <EventTrigger RoutedEvent="Loaded">
                        <BeginStoryboard>
                            <Storyboard>
                                <DoubleAnimation Storyboard.TargetName="ShimmerTransform"
                                                 Storyboard.TargetProperty="X"
                                                 From="-1" To="1" Duration="0:0:1.2"
                                                 RepeatBehavior="Forever" />
                            </Storyboard>
                        </BeginStoryboard>
                    </EventTrigger>
                </Border.Triggers>
            </Border>
            <Border Width="40" Height="8" CornerRadius="4" Background="{DynamicResource TrackBrush}" />
        </StackPanel>
    </Border>
</DataTemplate>
```

### U-34 [P1] 托盘悬浮窗拖拽条过窄

- **文件**：`TrayTooltipWindow.xaml:20-21`
- **问题**：拖拽条 `Width="6"` 过窄，用户难以瞄准；且无 hover 反馈。
- **改进后**：
```xml
<!-- 加宽至 12px，hover 时显示提示色 -->
<Rectangle x:Name="RightResizeGrip" Width="12" HorizontalAlignment="Right"
           Fill="Transparent" Cursor="SizeWE"
           ToolTip="拖动调整宽度">
    <Rectangle.Style>
        <Style TargetType="Rectangle">
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Fill" Value="#22FFFFFF" />
                </Trigger>
            </Style.Triggers>
        </Style>
    </Rectangle.Style>
</Rectangle>
```

### U-35 [P1] 托盘悬浮窗三列指标字号过小

- **文件**：`TrayTooltipWindow.xaml:65, 72, 79`
- **问题**："已使用 / 总额度 / 剩余额度" 标签 `FontSize=10` 过小，老花眼用户难以阅读。
- **改进后**：标签字号 10→11，数值字号 12→13：
```xml
<TextBlock Text="已使用" FontSize="11" Foreground="{DynamicResource TextTertiaryBrush}" />
<TextBlock Text="{Binding UsedText}" FontSize="13"
           Foreground="{DynamicResource TextPrimaryBrush}" FontWeight="Medium" />
```

### U-36 [P1] TriggerArea 8 个 Thumb 代码重复

- **文件**：`TriggerAreaOverlayWindow.xaml:55-103`
- **问题**：8 个 Thumb 元素除 `Tag / Cursor / 位置` 外完全一致（Width / Height / Background / BorderBrush / BorderThickness / DragDelta / DragCompleted）。
- **改进后**：抽取 `ThumbStyle`，Thumb 仅声明差异化属性：
```xml
<Window.Resources>
    <Style x:Key="ResizeThumbStyle" TargetType="Thumb">
        <Setter Property="Background" Value="{DynamicResource ThumbBackgroundBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource ThumbBorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="DragDelta" Value="OnThumbDragDelta" />
        <Setter Property="DragCompleted" Value="OnThumbDragCompleted" />
    </Style>
</Window.Resources>

<!-- 使用时只声明差异化属性 -->
<Thumb x:Name="ThumbLeft" Tag="Left" Style="{StaticResource ResizeThumbStyle}"
       Width="6" Height="20" Cursor="SizeWE"
       HorizontalAlignment="Left" VerticalAlignment="Center" />
```

### U-37 [P1] 边 Thumb 尺寸过小

- **文件**：`TriggerAreaOverlayWindow.xaml:55-78`
- **问题**：4 个边 Thumb `Width="6" Height="20"`，6px 宽度在高 DPI 屏上难以点击。Windows 推荐最小触控目标 32x32。
- **改进后**：增加可视尺寸但保持视觉小尺寸（用透明 Padding 扩大命中区）：
```xml
<Thumb x:Name="ThumbLeft" Tag="Left"
       Style="{StaticResource ResizeThumbStyle}"
       Width="6" Height="20"
       HorizontalAlignment="Left" VerticalAlignment="Center"
       Cursor="SizeWE">
    <Thumb.Template>
        <ControlTemplate TargetType="Thumb">
            <!-- 命中区：30x30 透明，视觉区 6x20 实色 -->
            <Grid Width="30" Height="30" Background="Transparent">
                <Rectangle Width="6" Height="20"
                           Fill="{DynamicResource ThumbBackgroundBrush}"
                           Stroke="{DynamicResource ThumbBorderBrush}"
                           StrokeThickness="1" />
            </Grid>
        </ControlTemplate>
    </Thumb.Template>
</Thumb>
```

### U-38 [P1] TriggerArea 缺少 Esc 提示与取消按钮

- **文件**：`TriggerAreaOverlayWindow.xaml`
- **问题**：用户进入触发区域调整模式后，无 Esc 退出提示，也无取消按钮，必须点击蒙版空白处退出，发现性差。
- **改进后**：在蒙版顶部增加提示条：
```xml
<!-- 在 MaskLayer 上方增加提示条 -->
<Border Canvas.Left="0" Canvas.Top="0"
        Width="{Binding ActualWidth, ElementName=RootCanvas}"
        Background="#CC000000" Padding="16,10">
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
        <TextBlock Foreground="White" FontSize="13" FontWeight="SemiBold"
                   Text="拖动调整触发区域" />
        <TextBlock Foreground="#99FFFFFF" FontSize="12" Margin="16,0,0,0"
                   Text="按 Esc 取消 · 点击空白处确认" />
        <Button Content="取消" Margin="24,0,0,0" Padding="12,4"
                Background="Transparent" Foreground="White"
                BorderBrush="#66FFFFFF" BorderThickness="1"
                Click="OnCancelClick" />
    </StackPanel>
</Border>
```

---

## 五、P2 级问题详细改进

### U-39 [P2] ErrorColorConverter 颜色硬编码

- **文件**：`Helpers/Converters.cs:46-50`
- **问题**：错误状态色 `FromRgb(220, 38, 38)` 与灰色 `FromRgb(148, 163, 184)` 硬编码，未引用 `DangerBrush` / `TextSecondaryBrush`。主题切换时不会跟随。
- **改进后**：
```csharp
public class ErrorColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isError = value is bool b && b;
        // 通过 Application.Current.Resources 取主题画笔
        var app = System.Windows.Application.Current;
        var fallback = isError ? Colors.Red : Colors.Gray;
        var key = isError ? "DangerBrush" : "TextSecondaryBrush";
        if (app?.TryFindResource(key) is Brush brush) return brush;
        return new SolidColorBrush(fallback);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

### U-40 [P2] Brush 未 Freeze 性能损耗

- **文件**：`Helpers/Converters.cs:48, 49, 234`、`Helpers/UsageTierScale.cs:117`
- **问题**：每次 `Convert` 都 `new SolidColorBrush(...)` 但未 `Freeze()`。WPF 中未 Freeze 的 Brush 会被纳入布局跟踪，热力图高频刷新（每秒数十次）时增加 GC 压力。
- **改进后**：
```csharp
public static Brush ResolveBrush(double percent)
{
    var brush = new SolidColorBrush(Resolve(percent).Color);
    brush.Freeze();  // 关键：冻结后 WPF 不再跟踪变化
    return brush;
}
```
- **备注**：`PercentToBrushConverter.Convert` 中同样需要 `Freeze`。

### U-41 [P2] 进度条数值变化无平滑过渡

- **文件**：`MainWindow.xaml:190-202, 229-241` 等所有进度条
- **问题**：进度条 `Border.Width` 直接绑定到 ViewModel 的 `PrimaryBarPercent`，数值变化时宽度瞬间跳变，无过渡动画。
- **改进后**：用 `Style.Triggers` 监听 Width 变化并加动画：
```xml
<Border CornerRadius="5"
        Background="{Binding PrimaryBarPercent, Converter={StaticResource PercentToBrush}}"
        HorizontalAlignment="Left">
    <Border.Width>
        <MultiBinding Converter="{StaticResource PercentageWidthConverter}">
            <Binding Path="PrimaryBarPercent" />
            <Binding RelativeSource="{RelativeSource FindAncestor, AncestorType={x:Type Grid}}" Path="ActualWidth" />
        </MultiBinding>
    </Border.Width>
    <Border.Style>
        <Style TargetType="Border">
            <Style.Triggers>
                <EventTrigger RoutedEvent="Loaded">
                    <BeginStoryboard>
                        <Storyboard>
                            <!-- Width 变化时自动平滑过渡 -->
                            <DoubleAnimation Storyboard.TargetProperty="Width"
                                             Duration="0:0:0.3"
                                             EasingFunction="{StaticResource EaseOut}" />
                        </Storyboard>
                    </BeginStoryboard>
                </EventTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
</Border>
```
- **设计依据**：进度条平滑过渡是用户感知"数据正在更新"的关键反馈。200-300ms 是 Material Design 推荐时长。

### U-42 [P2] 主题切换无渐变过渡

- **文件**：`Helpers/ThemeManager.cs`
- **问题**：`ThemeManager.Apply` 直接替换 `ResourceDictionary`，主题切换瞬间发生，无渐变过渡，视觉冲击大。
- **改进后**（思路）：WPF 中实现主题过渡较复杂，建议采用"双层遮罩淡入"：
```csharp
public static void Apply(ThemeMode mode)
{
    // 在主窗口上叠加一层半透明遮罩
    var mainWindow = System.Windows.Application.Current.MainWindow;
    if (mainWindow != null)
    {
        var overlay = new Border
        {
            Background = new SolidColorBrush(mode == ThemeMode.Light ? Colors.White : Colors.Black),
            Opacity = 0,
            IsHitTestVisible = false
        };
        var adorner = new AdornerLayer();
        // ... 简化：用 Panel.ZIndex 叠加 Border 到主窗口顶层 Grid
        // 1. overlay.Opacity 0→0.3 (200ms)
        // 2. 替换主题字典
        // 3. overlay.Opacity 0.3→0 (200ms)
        // 4. 移除 overlay
    }

    // 原有逻辑
    ReplaceThemeDictionary(mode);
    Current = mode;
    ThemeChanged?.Invoke(...);
}
```
- **备注**：此改进较复杂，可降级为仅在主窗口加 fade-out/fade-in 即可。

---

## 六、改进优先级与路线图

### 第一阶段（P0，1-2 周）

| 编号 | 任务 | 估时 | 负责人 |
|------|------|------|--------|
| U-01, U-02 | 调整深浅主题 TextTertiary / TextSecondary 对比度 | 0.5 天 | 设计 |
| U-03 | 减小卡片阴影 BlurRadius | 0.5 天 | 前端 |
| U-04 | MainWindow 标题栏图标与字号重构 | 0.5 天 | 前端 |
| U-07 | 自定义控件依赖属性默认值改用主题资源 | 2 天 | 前端 |
| U-08 | TriggerAreaOverlayWindow 颜色提取为 Token | 0.5 天 | 前端 |
| U-05 | MainWindow 卡片拆分子模板 + 折叠 | 3 天 | 前端 + 设计 |
| U-06 | 设置窗口改左侧导航 + 分组 | 3 天 | 前端 + 设计 |

### 第二阶段（P1，2-3 周）

| 编号 | 任务 | 估时 |
|------|------|------|
| U-09, U-10, U-11 | 完善 Token 体系 + 全局引用率提升 | 2 天 |
| U-12, U-13, U-14, U-15 | 按钮/输入控件交互状态完善 | 3 天 |
| U-16, U-17, U-18, U-19 | MainWindow 顶部/进度条/状态栏/空状态 | 2 天 |
| U-20 ~ U-25 | 设置窗口输入校验 + 提示优化 + 全局保存 | 3 天 |
| U-26 ~ U-29 | HistoryWindow DataGrid + 空状态 + 动画 | 2 天 |
| U-30 ~ U-35 | 任务栏/托盘悬浮窗细节 | 2 天 |
| U-36, U-37, U-38 | TriggerArea Thumb 抽样式 + Esc | 1 天 |

### 第三阶段（P2，1 周）

| 编号 | 任务 | 估时 |
|------|------|------|
| U-39, U-40 | Converter 走主题 + Freeze | 0.5 天 |
| U-41, U-42 | 进度条平滑 + 主题过渡 | 2 天 |

---

## 七、附录：设计依据

### WCAG 2.1 AA 标准

- **SC 1.4.3 Contrast (Minimum)**：普通文字对比度 ≥ 4.5:1；大文字（≥ 18pt 或 14pt bold）≥ 3:1。
- **SC 2.4.7 Focus Visible**：键盘焦点必须有可见指示器。
- **SC 4.1.2 Name, Role, Value**：所有 UI 控件需有 `AutomationProperties.Name`。

### Material Design 参考指标

- 卡片阴影：Level 1 → BlurRadius 6-10, ShadowDepth 1-2
- 按钮三态：hover（轻微抬升 + 0.92 opacity）/ pressed（轻微下沉 + ScaleTransform 0.97）/ disabled（0.4 opacity + 灰色文字）
- 动画时长：fast 100-150ms / normal 200-300ms / slow 350-500ms
- Easing：标准 `CubicBezier(0.4, 0, 0.2, 1)`

### 字号阶梯推荐

| Token | 字号 | 用途 |
|-------|------|------|
| FontSizeDisplay | 24 | 大屏标题（暂未用） |
| FontSizeHeading | 22 | 窗口主标题 |
| FontSizeTitle | 18 | 卡片标题 / Tab 标题 |
| FontSizeSubtitle | 15 | 段落标题 |
| FontSizeBody | 13 | 正文 |
| FontSizeCaption | 11 | 辅助文字 |

### 触控目标尺寸

- Windows 推荐：≥ 32x32 px（高 DPI 屏 ≥ 40x40）
- Apple HIG：≥ 44x44 pt
- 当前 6x20 的 Thumb 命中区不达标，需通过透明 Padding 扩大命中区。

---

**报告结束。** 开发团队按优先级实施时，建议每个 PR 聚焦一个 P0 项，附带 before/after 截图对比。P1 项可按模块批量提交。
