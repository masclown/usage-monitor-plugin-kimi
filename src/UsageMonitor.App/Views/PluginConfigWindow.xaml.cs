using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;
using UsageMonitor.App.Controls;
using UsageMonitor.App.Helpers;
using UsageMonitor.Plugin.MiniMax;

// WinForms/WPF命名空间冲突解决：使用别名
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfButton = System.Windows.Controls.Button;
using WpfPasswordBox = System.Windows.Controls.PasswordBox;

namespace UsageMonitor.App.Views;

/// <summary>
/// 插件配置对话框 - 根据插件的 ConfigFields 动态生成配置表单
/// 支持 TextBox、PasswordBox、CheckBox、ComboBox 等控件类型
/// <para>
/// 当插件在 <see cref="Models.BrowserLoginConfig"/> 中声明了登录需求时，
/// 会自动显示"🌐 获取登录态"按钮，点击后调用 <see cref="BrowserLoginService"/>
/// 启动临时 Edge 窗口并提取 Cookie。设计复刻自销项数据助手项目的
/// <c>browser-cookie-manager</c> Skill。
/// </para>
/// </summary>
public partial class PluginConfigWindow : Window
{
    // req-fix-Kimi-ConfigFields 动态模式：去掉 readonly，Mode 切换时 RebuildFormForModeChange 重新赋值。
    private IReadOnlyList<ConfigField> _configFields;
    private readonly ProviderConfig _config;
    private readonly BrowserLoginConfig? _loginConfig;
    private readonly Dictionary<string, FrameworkElement> _inputControls = new();
    private readonly ConfigService? _configService;

    /// <summary>
    /// req-fix-Kimi-ConfigFields 动态模式：保存 <see cref="IUsageProvider"/> 引用，
    /// 让 Mode 字段 ComboBox 切换时能重新调用 <see cref="IUsageProvider.ConfigFields"/>
    /// 获取与新模式匹配的字段列表。
    /// <para>非双模式插件可传 null（仍按原方式使用构造时传入的 _configFields 列表）。</para>
    /// </summary>
    private readonly UsageMonitor.Core.Plugins.IUsageProvider? _provider;

    /// <summary>插件声明支持的图表类型（用于生成复选框，保持声明顺序）。</summary>
    private readonly IReadOnlyList<CardChartKind> _supportedCardCharts;

    /// <summary>当前勾选的卡片图表类型集合（保存时由调用方读取持久化）。</summary>
    private readonly HashSet<CardChartKind> _selectedCardCharts = new();

    /// <summary>用户在本窗口勾选的卡片图表类型集合（按声明顺序）。调用方在 ShowDialog 返回 true 后读取。</summary>
    public IReadOnlyList<CardChartKind> SelectedCardChartKinds
        => _supportedCardCharts.Where(_selectedCardCharts.Contains).ToList();

    /// <summary>
    /// 正在登录中的 ProviderId 集合（进程级共享，避免同一插件重复触发登录）。
    /// <para>
    /// 计划文件字面建议字段名为 <c>_isLoginInProgress</c>（单一 bool），但实际实现采用
    /// <see cref="HashSet{T}"/> 以支持多 ProviderId 的独立并发控制：
    /// 例如用户在 DeepSeek 登录中点击 MiniMax 按钮不应被错误阻塞。
    /// </para>
    /// </summary>
    /// <summary>
    /// req-064 B12：登录防重复 HashSet 改为大小写不敏感，避免 "MiniMax" 与 "minimax" 绕过防重复锁。
    /// </summary>
    private static readonly HashSet<string> _isLoginInProgress = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>保护 <see cref="_isLoginInProgress"/> 的锁对象</summary>
    private static readonly object _loginInProgressLock = new();

    /// <summary>
    /// 创建插件配置对话框
    /// </summary>
    /// <param name="pluginName">插件显示名称</param>
    /// <param name="configFields">插件定义的配置字段</param>
    /// <param name="config">当前配置（读取和写入）</param>
    /// <param name="loginConfig">
    /// 可选的浏览器登录配置。传入非 <c>null</c> 时，窗口底部显示"获取登录态"按钮，
    /// 点击后调用 <see cref="BrowserLoginService"/> 启动临时 Edge 窗口提取 Cookie。
    /// </param>
    /// <param name="configService">
    /// req-065 B4：可选的 ConfigService，用于 BrowserLoginService 实例化（登录成功后自动重载内存配置）。
    /// </param>
    /// <param name="provider">
    /// req-fix-Kimi-ConfigFields 动态模式：可选的插件实例引用。
    /// 传入后 PluginConfigWindow 会在 Mode ComboBox 切换时自动调用 <c>provider.ConfigFields</c>
    /// 重新拉取字段列表（如双模式插件根据 mode 字段返回不同字段）。
    /// 传 null 时按构造时传入的 _configFields 列表使用（向后兼容）。
    /// </param>
    public PluginConfigWindow(
        string pluginName,
        IReadOnlyList<ConfigField> configFields,
        ProviderConfig config,
        BrowserLoginConfig? loginConfig = null,
        IReadOnlyList<CardChartKind>? supportedCardCharts = null,
        IReadOnlyList<CardChartKind>? currentCardCharts = null,
        ConfigService? configService = null,
        UsageMonitor.Core.Plugins.IUsageProvider? provider = null)
    {
        InitializeComponent();
        _configFields = configFields;
        _config = config;
        _loginConfig = loginConfig;
        _configService = configService;
        _provider = provider;
        _supportedCardCharts = supportedCardCharts ?? System.Array.Empty<CardChartKind>();
        if (currentCardCharts != null)
            foreach (var k in currentCardCharts) _selectedCardCharts.Add(k);

        TitleText.Text = $"{pluginName} 配置";
        BuildForm();
        BuildCardChartSection();
        BuildAccountSection();

        // 当插件声明了登录需求时，显示通用的"获取登录态"按钮
        if (_loginConfig != null)
        {
            GetCookieButton.Content = _loginConfig.UiButtonText ?? "🌐 获取登录态";
            GetCookieButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// req-109：账号管理 UI。列出该 Provider 下现有账号，提供 + 添加账号 / 编辑昵称 / 删除。
    /// <para>仅当 <see cref="_configService"/> 与 <see cref="_provider"/> 都非空时显示。保存按钮由现有 <c>OnSaveClick</c> 一次性统一持久化。</para>
    /// </summary>
    private void BuildAccountSection()
    {
        if (_configService == null || _provider == null)
        {
            AccountSection.Visibility = Visibility.Collapsed;
            return;
        }

        var providerId = _provider.ProviderId;
        var accounts = _configService.GetAccounts(providerId);
        // 无账号时提供一个引导行：引导用户添加第一个账号
        if (accounts.Count == 0)
        {
            // 自动添加一个 default 账号（首次打开时让用户可见列表，不再空白）
            // 注：仍需用户点击"+ 添加账号"才能持久化；此处不自动 add。
        }

        AccountListPanel.Children.Clear();
        foreach (var account in accounts)
        {
            AccountListPanel.Children.Add(BuildAccountRow(providerId, account));
        }
    }

    /// <summary>构建单行账号 UI（昵称 TextBox + UseNickname CheckBox + 删除 Button）。</summary>
    private FrameworkElement BuildAccountRow(string providerId, UsageMonitor.Core.Models.Account account)
    {
        var border = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            CornerRadius = (System.Windows.CornerRadius)FindResource("RadiusButton"),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 账号 ID（只读标签）
        var idLabel = new TextBlock
        {
            Text = $"账号：{account.AccountId}{(account.IsDefault ? "（默认）" : "")}",
            FontSize = 12,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(idLabel, 0);
        grid.Children.Add(idLabel);

        // 昵称 TextBox
        var nickBox = new WpfTextBox
        {
            Text = account.Nickname ?? string.Empty,
            MinWidth = 140,
            Margin = new Thickness(0, 0, 8, 0)
        };
        nickBox.TextChanged += (_, _) =>
        {
            account.Nickname = string.IsNullOrWhiteSpace(nickBox.Text) ? null : nickBox.Text.Trim();
            TryUpdateAccount(account);
        };
        Grid.SetColumn(nickBox, 1);
        grid.Children.Add(nickBox);

        // 删除按钮
        var delBtn = new WpfButton
        {
            Content = "删除",
            Margin = new Thickness(0, 0, 0, 0),
            Style = (Style)FindResource("GhostButtonStyle")
        };
        delBtn.Click += (_, _) =>
        {
            try
            {
                _configService!.RemoveAccount(providerId, account.AccountId);
            }
            catch (System.InvalidOperationException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            BuildAccountSection();
        };
        Grid.SetColumn(delBtn, 2);
        grid.Children.Add(delBtn);

        border.Child = grid;
        return border;
    }

    /// <summary>安全更新账号（不抛异常）。</summary>
    private void TryUpdateAccount(UsageMonitor.Core.Models.Account account)
    {
        if (_configService == null) return;
        try { _configService.UpdateAccount(account); }
        catch { /* 静默失败：用户继续编辑后下次点击保存时一起写入 */ }
    }

    /// <summary>req-109：+ 添加账号 按钮点击处理（分配唯一 AccountId，弹出昵称输入对话框）。</summary>
    private void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        if (_configService == null || _provider == null) return;
        var providerId = _provider.ProviderId;
        var defaultNickname = $"账号 {_configService.GetAccounts(providerId).Count + 1}";
        var inputBox = new WpfTextBox { Text = defaultNickname, MinWidth = 200 };
        var dialog = new Window
        {
            Title = "添加账号",
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("AppBackgroundBrush")
        };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "为新账号设置昵称（Provider 内唯一）：", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(inputBox);
        var btnRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var okBtn = new WpfButton { Content = "确定", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("PrimaryButtonStyle") };
        var cancelBtn = new WpfButton { Content = "取消", IsCancel = true, Style = (Style)FindResource("GhostButtonStyle") };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        stack.Children.Add(btnRow);
        dialog.Content = stack;
        okBtn.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() == true)
        {
            var nickname = string.IsNullOrWhiteSpace(inputBox.Text) ? null : inputBox.Text.Trim();
            _configService.AddAccount(providerId, nickname);
            BuildAccountSection();
        }
    }

    /// <summary>
    /// 根据插件声明的 <see cref="_supportedCardCharts"/> 动态生成卡片图表复选框（多选）并渲染初始预览。
    /// 插件未声明任何图表时隐藏整个「卡片图表」分组。
    /// </summary>
    private void BuildCardChartSection()
    {
        if (_supportedCardCharts.Count == 0)
        {
            CardChartSection.Visibility = Visibility.Collapsed;
            return;
        }

        CardChartCheckPanel.Children.Clear();
        foreach (var kind in _supportedCardCharts)
        {
            var cb = new WpfCheckBox
            {
                Content = DescribeChartKind(kind),
                IsChecked = _selectedCardCharts.Contains(kind),
                Tag = kind,
                Margin = new Thickness(0, 4, 0, 4)
            };
            cb.Checked += OnCardChartCheckChanged;
            cb.Unchecked += OnCardChartCheckChanged;
            CardChartCheckPanel.Children.Add(cb);
        }
        RefreshCardChartPreview();
    }

    /// <summary>图表类型 → 复选框中文标签。</summary>
    private static string DescribeChartKind(CardChartKind kind) => kind switch
    {
        CardChartKind.Line => "折线图",
        CardChartKind.Bar => "柱状图",
        CardChartKind.Ring => "圆环图",
        CardChartKind.HeatMap => "热力图",
        CardChartKind.DayNightArc => "编程时段",
        _ => kind.ToString()
    };

    /// <summary>复选框勾选变化：更新选中集合并刷新预览。</summary>
    private void OnCardChartCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is WpfCheckBox cb && cb.Tag is CardChartKind kind)
        {
            if (cb.IsChecked == true) _selectedCardCharts.Add(kind);
            else _selectedCardCharts.Remove(kind);
            RefreshCardChartPreview();
        }
    }

    /// <summary>
    /// 按当前勾选，用示例数据垂直堆叠重建所有选中图表的预览（主题感知；真实数据接入后卡片会用真实序列）。
    /// </summary>
    private void RefreshCardChartPreview()
    {
        if (CardChartPreviewHost == null) return;
        CardChartPreviewHost.Children.Clear();

        foreach (var kind in _supportedCardCharts.Where(_selectedCardCharts.Contains))
            CardChartPreviewHost.Children.Add(BuildPreviewBlock(kind));

        if (CardChartPreviewHost.Children.Count == 0)
        {
            var tb = new TextBlock
            {
                Text = "未选择图表（卡片仅显示进度条）", FontSize = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 12)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            CardChartPreviewHost.Children.Add(tb);
        }
    }

    /// <summary>为单个图表类型构建「标题 + 示例图表」的预览块（带主题感知边框）。</summary>
    private FrameworkElement BuildPreviewBlock(CardChartKind kind)
    {
        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1)
        };
        container.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        container.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
        container.SetResourceReference(Border.CornerRadiusProperty, "RadiusSmall");

        var stack = new StackPanel();
        var title = new TextBlock
        {
            Text = DescribeChartKind(kind), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        stack.Children.Add(title);

        // 热力图控件本身 MinHeight≈150，给更高的容器避免被裁剪；其它图表用统一 132 高度。
        var host = new Border { Height = kind == CardChartKind.HeatMap ? 170 : 132 };
        host.Child = BuildSampleChart(kind);
        stack.Children.Add(host);

        container.Child = stack;
        return container;
    }

    /// <summary>按图表类型创建示例控件（沿用原预览逻辑，热力图改用 YearHeatMapControl 示例日历）。</summary>
    private FrameworkElement BuildSampleChart(CardChartKind kind)
    {
        switch (kind)
        {
            case CardChartKind.Line:
            {
                var c = new MiniLineChartControl { Values = SampleChartData.UsageTrend, StrokeThickness = 2.4 };
                c.SetResourceReference(MiniLineChartControl.LowBrushProperty, "UsageLowBrush");
                c.SetResourceReference(MiniLineChartControl.MidBrushProperty, "UsageMidBrush");
                c.SetResourceReference(MiniLineChartControl.HighBrushProperty, "UsageHighBrush");
                return c;
            }
            case CardChartKind.Bar:
            {
                var c = new BarChartControl { Values = SampleChartData.DailyBars };
                c.SetResourceReference(BarChartControl.BarBrushProperty, "AccentGradientBrush");
                c.SetResourceReference(BarChartControl.GridLineBrushProperty, "ChartAxisBrush");
                c.SetResourceReference(BarChartControl.TextBrushProperty, "TextSecondaryBrush");
                return c;
            }
            case CardChartKind.Ring:
            {
                var c = new RingChartControl
                {
                    Percent = 68, Size = 120, StrokeThickness = 12,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                c.SetResourceReference(RingChartControl.TrackBrushProperty, "TrackBrush");
                c.SetResourceReference(RingChartControl.ProgressBrushProperty, "AccentBrush");
                c.SetResourceReference(RingChartControl.WarningBrushProperty, "WarningBrush");
                c.SetResourceReference(RingChartControl.DangerBrushProperty, "DangerBrush");
                return c;
            }
            case CardChartKind.HeatMap:
            {
                var c = new YearHeatMapControl { Cells = BuildSampleHeatMapCells() };
                c.SetResourceReference(YearHeatMapControl.EmptyCellBrushProperty, "TrackBrush");
                c.SetResourceReference(YearHeatMapControl.TextBrushProperty, "TextSecondaryBrush");
                return c;
            }
            case CardChartKind.DayNightArc:
            {
                var c = new DayNightArcControl { HourlyActivity = SampleChartData.HourlyActivity };
                c.SetResourceReference(DayNightArcControl.TrackBrushProperty, "TextTertiaryBrush");
                c.SetResourceReference(DayNightArcControl.AccentBrushProperty, "AccentBrush");
                c.SetResourceReference(DayNightArcControl.TextBrushProperty, "TextSecondaryBrush");
                return c;
            }
            default:
            {
                var tb = new TextBlock
                {
                    Text = "（无预览）", FontSize = 12,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                return tb;
            }
        }
    }

    /// <summary>为热力图预览生成一批示例日历单元（最近约 5 周、循环取示例强度）。</summary>
    private static IEnumerable<YearHeatMapCell> BuildSampleHeatMapCells()
    {
        var cells = new List<YearHeatMapCell>();
        var start = System.DateTime.Today.AddDays(-34);
        var sample = SampleChartData.UsageTrend;
        for (int i = 0; i < 35; i++)
        {
            var pct = sample[i % sample.Count];
            cells.Add(new YearHeatMapCell
            {
                Day = start.AddDays(i).ToString("yyyy-MM-dd"),
                Percent = pct,
                Background = PreviewHeatBrush(pct)
            });
        }
        return cells;
    }

    /// <summary>示例热力图三档画笔（低绿/中橙/高红），Freeze 后可安全绑定。</summary>
    private static System.Windows.Media.Brush PreviewHeatBrush(double percent)
    {
        byte r, g, b;
        if (percent >= 66) { r = 0xEF; g = 0x44; b = 0x44; }
        else if (percent >= 33) { r = 0xF5; g = 0x9E; b = 0x0B; }
        else { r = 0x22; g = 0xC5; b = 0x5E; }
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 根据 ConfigFields 动态构建表单控件
    /// </summary>
    private void BuildForm()
    {
        FormPanel.Children.Clear();
        _inputControls.Clear();

        foreach (var field in _configFields)
        {
            var row = CreateFormRow(field);
            FormPanel.Children.Add(row);
        }

        if (_configFields.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "此插件无需配置",
                FontSize = 14
            };
            emptyText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            FormPanel.Children.Add(emptyText);
        }
    }

    /// <summary>
    /// 为单个配置字段创建一行表单（标签 + 输入控件）
    /// </summary>
    private Border CreateFormRow(ConfigField field)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        // 标签（含必填标记）
        var label = new TextBlock
        {
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var labelRun = new System.Windows.Documents.Run(field.DisplayName);
        label.Inlines.Add(labelRun);

        if (field.IsRequired)
        {
            var requiredRun = new System.Windows.Documents.Run(" *");
            requiredRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, "DangerBrush");
            label.Inlines.Add(requiredRun);
        }

        panel.Children.Add(label);

        // 根据字段类型创建输入控件
        FrameworkElement inputControl = field.FieldType switch
        {
            ConfigFieldType.Password => CreatePasswordInput(field),
            ConfigFieldType.Boolean => CreateBooleanInput(field),
            ConfigFieldType.Select => CreateSelectInput(field),
            ConfigFieldType.Number => CreateTextInput(field),
            _ => CreateTextInput(field)
        };

        _inputControls[field.Key] = inputControl;
        panel.Children.Add(inputControl);

        // 占位提示
        if (!string.IsNullOrEmpty(field.Placeholder) && field.FieldType != ConfigFieldType.Boolean)
        {
            var hint = new TextBlock
            {
                Text = field.Placeholder,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            panel.Children.Add(hint);
        }

        return new Border
        {
            Child = panel,
            Padding = new Thickness(0),
            Background = Brushes.Transparent
        };
    }

    /// <summary>
    /// 创建文本输入控件（TextBox）
    /// </summary>
    private WpfTextBox CreateTextInput(ConfigField field)
    {
        var currentValue = _config.GetValue(field.Key) ?? field.DefaultValue ?? "";
        return new WpfTextBox
        {
            Text = currentValue,
            Tag = field.Key
        };
    }

    /// <summary>
    /// 创建密码输入控件（PasswordBox + 显示/隐藏切换）
    /// </summary>
    private FrameworkElement CreatePasswordInput(ConfigField field)
    {
        var currentValue = _config.GetValue(field.Key) ?? field.DefaultValue ?? "";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var passwordBox = new WpfPasswordBox
        {
            Password = currentValue,
            Tag = field.Key
        };
        Grid.SetColumn(passwordBox, 0);
        grid.Children.Add(passwordBox);

        var toggleBtn = new WpfButton
        {
            Content = "显示",
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("GhostButtonStyle")
        };

        var textBox = new WpfTextBox
        {
            Text = currentValue,
            Visibility = Visibility.Collapsed,
            Tag = field.Key
        };

        // req-064 B13：初始化时即 Add textBox，切换时只改 Visibility，避免重复 Add 抛 InvalidOperationException
        Grid.SetColumn(textBox, 0);
        grid.Children.Add(textBox);

        bool isVisible = false;
        toggleBtn.Click += (_, _) =>
        {
            isVisible = !isVisible;
            if (isVisible)
            {
                textBox.Text = passwordBox.Password;
                passwordBox.Visibility = Visibility.Collapsed;
                textBox.Visibility = Visibility.Visible;
                toggleBtn.Content = "隐藏";
            }
            else
            {
                passwordBox.Password = textBox.Text;
                textBox.Visibility = Visibility.Collapsed;
                passwordBox.Visibility = Visibility.Visible;
                toggleBtn.Content = "显示";
            }
        };

        Grid.SetColumn(toggleBtn, 1);
        grid.Children.Add(toggleBtn);

        // 将包装器保存到 Tag 供取值（按钮样式已改用全局 GhostButtonStyle）
        grid.Tag = new PasswordBoxWrapper(passwordBox, textBox);
        return grid;
    }

    /// <summary>
    /// 创建布尔开关控件（CheckBox）
    /// </summary>
    private WpfCheckBox CreateBooleanInput(ConfigField field)
    {
        var currentValue = _config.GetValue(field.Key) ?? field.DefaultValue ?? "false";
        return new WpfCheckBox
        {
            IsChecked = bool.TryParse(currentValue, out var b) && b,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 14,
            Tag = field.Key
        };
    }

    /// <summary>
    /// 创建下拉选择控件（ComboBox）
    /// </summary>
    private WpfComboBox CreateSelectInput(ConfigField field)
    {
        var currentValue = _config.GetValue(field.Key) ?? field.DefaultValue ?? "";
        var comboBox = new WpfComboBox
        {
            Tag = field.Key
        };

        if (field.Options != null)
        {
            foreach (var option in field.Options)
            {
                comboBox.Items.Add(option);
            }
        }

        comboBox.SelectedItem = currentValue;

        // req-fix-Kimi-ConfigFields 动态模式：Mode 字段 ComboBox 变化时
        // 重新调用 provider.ConfigFields 拉取与新模式匹配的字段列表。
        // 触发重建的字段 key 列表（双模式插件：KimiDualModeProvider/DeepseekDualModeProvider）
        // 将来新增双模式插件只需把 ModeKey 加入此集合。
        if (_provider != null && IsModeFieldKey(field.Key))
        {
            comboBox.SelectionChanged += (_, _) => RebuildFormForModeChange();
        }
        return comboBox;
    }

    /// <summary>
    /// req-fix-Kimi-ConfigFields 动态模式：判断字段 key 是否为模式选择字段（QueryMode）。
    /// 集中维护双模式插件的 Mode 字段 key，新增插件时只需扩展此集合。
    /// </summary>
    private static bool IsModeFieldKey(string fieldKey)
        => fieldKey == "QueryMode";

    /// <summary>
    /// req-fix-Kimi-ConfigFields 动态模式：Mode 字段切换时调用。
    /// 1. 重新调用 <c>provider.ConfigFields</c> 拉取与新模式匹配的字段列表
    /// 2. 保留用户已填的字段值（Cookie/ApiKey 等）
    /// 3. 重新构建整个表单 + 卡片图表区
    /// </summary>
    private void RebuildFormForModeChange()
    {
        if (_provider == null) return;

        // req-fix-Kimi-ModeRebuildStackOverflow：re-entrancy 保护
        // 场景：BuildForm 创建新 ComboBox 时 SelectedItem=currentValue 会触发 SelectionChanged，
        // 如果 currentValue ≠ 旧值（或某些边角情况），会再次调用 RebuildFormForModeChange → BuildForm → 新 ComboBox → ...
        // 形成无限递归，触发 StackOverflowException 导致程序闪退。
        // 用实例标志位防止重入（同一时刻只允许一次重建）。
        if (_isRebuildingForMode) return;
        _isRebuildingForMode = true;
        try
        {
            // 1. 抓取当前所有输入控件的当前值（保留用户输入）
            var currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, control) in _inputControls)
            {
                if (TryGetControlValue(control, out var v)) currentValues[key] = v;
            }

            // 2. 立即把 Mode 字段值持久化到 config（只持久化 Mode，不影响其他字段）
            // 理由：Mode 切换是用户明确的"试切"动作，下次开窗要看到新模式。
            // 但其他字段（Cookie/ApiKey 等）不持久化——用户可能没填完，避免误保存触发必填校验失败。
            if (_configService != null && currentValues.TryGetValue("QueryMode", out var newMode))
            {
                _config.SetValue("QueryMode", newMode);
                try
                {
                    _configService.UpdateProviderConfig(_provider.ProviderId, _config);
                }
                catch (Exception saveEx)
                {
                    FileLogger.Warn("PluginConfigWindow",
                        $"Mode 切换后持久化失败 ({_provider.ProviderId} -> {newMode}): {saveEx.Message}");
                }
            }

            // 3. 重新拉取字段列表（KimiDualModeProvider 按 mode 返回不同字段）
            _configFields = _provider.ConfigFields;

            // 3. 重新构建表单
            BuildForm();

            // 4. 卡片图表区不依赖 mode，无须重建

            // 5. 把之前抓取的值填回新控件
            foreach (var (key, value) in currentValues)
            {
                if (_inputControls.TryGetValue(key, out var control) && value != null)
                {
                    TrySetControlValue(control, value);
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("PluginConfigWindow",
                $"RebuildFormForModeChange failed: {ex.Message}", ex);
        }
        finally
        {
            _isRebuildingForMode = false;
        }
    }

    /// <summary>req-fix-Kimi-ModeRebuildStackOverflow：re-entrancy 保护标志。</summary>
    private bool _isRebuildingForMode;

    /// <summary>从已有控件读取当前值（支持 TextBox/PasswordBox/CheckBox/ComboBox）。</summary>
    private static bool TryGetControlValue(FrameworkElement control, out string value)
    {
        switch (control)
        {
            case WpfTextBox tb: value = tb.Text; return true;
            case WpfPasswordBox pb: value = pb.Password; return true;
            case WpfCheckBox cb: value = cb.IsChecked == true ? "true" : "false"; return true;
            case WpfComboBox combo:
                value = combo.SelectedItem?.ToString() ?? "";
                return !string.IsNullOrEmpty(value);
            default:
                value = "";
                return false;
        }
    }

    /// <summary>把值写回控件（用于 Mode 切换后保留字段值）。</summary>
    private static bool TrySetControlValue(FrameworkElement control, string value)
    {
        switch (control)
        {
            case WpfTextBox tb: tb.Text = value; return true;
            case WpfPasswordBox pb: pb.Password = value; return true;
            case WpfCheckBox cb: cb.IsChecked = bool.TryParse(value, out var b) && b; return true;
            case WpfComboBox combo:
                if (combo.Items.Contains(value)) { combo.SelectedItem = value; return true; }
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// "获取登录态" 按钮点击 - 启动独立 Edge 窗口让用户登录，自动提取 Cookie 后填入字段。
    /// <para>
    /// 复刻自销项数据助手项目的 <c>browser-cookie-manager</c> Skill：
    /// 通过 <see cref="BrowserLoginService"/> 启动临时 Edge + CDP 提取明文 Cookie，
    /// 并以 JSON 格式持久化（含 userAgent、时间戳、count、domain 等元数据）。
    /// </para>
    /// <para>
    /// 防止重复触发：使用静态锁 <see cref="_isLoginInProgress"/> 避免同一 ProviderId 的
    /// 多次并发登录（用户在弹窗期间按 Enter 等可能误触发）。
    /// </para>
    /// </summary>
    private async void OnGetCookieClick(object sender, RoutedEventArgs e)
    {
        if (_loginConfig == null) return;

        // 防重复调用：同一 ProviderId 已有进行中的登录任务，直接拒绝
        lock (_loginInProgressLock)
        {
            if (_isLoginInProgress.Contains(_loginConfig.ProviderId))
            {
                return;
            }
            _isLoginInProgress.Add(_loginConfig.ProviderId);
        }

        GetCookieButton.IsEnabled = false;
        var originalContent = GetCookieButton.Content;
        GetCookieButton.Content = "🔄 启动浏览器中...";

        try
        {
            // req-065 B4：BrowserLoginService 去静态化，每次登录创建独立实例避免并发时 LastError 互相覆盖
            var loginService = new BrowserLoginService(_configService);
            var data = await loginService.LoginAndExtractCookieAsync(_loginConfig);

            if (data == null || string.IsNullOrEmpty(data.Cookie))
            {
                // 显示真实错误信息（来自 BrowserLoginService.LastError），
                // 而不是写死的"未检测到 account.minimaxi.com 域的会话 Cookie"误导用户。
                var lastError = loginService.LastError;
                var message = "未获取到 Cookie。\n\n";
                if (!string.IsNullOrEmpty(lastError))
                {
                    message += $"【真实错误】{lastError}\n\n";
                }
                message +=
                    "可能原因：\n" +
                    "① 您取消了登录\n" +
                    "② Edge 启动失败或被阻止（首次运行需联网下载 Playwright 浏览器）\n" +
                    "③ 未检测到 " + (_loginConfig.RequiredCookieDomain ?? "目标域名") +
                    $" 域的会话 Cookie（请确认已 {_loginConfig.LoginUrl} 完成登录）\n" +
                    $"④ 登录超时（{_loginConfig.LoginTimeout.TotalMinutes:0}分钟）\n\n" +
                    $"请重试，或检查 Edge 是否能正常访问 {_loginConfig.LoginUrl}";
                System.Windows.MessageBox.Show(
                    message,
                    "获取 Cookie 失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 自动将 Cookie 填入 Cookie 字段（如果存在）
            if (_inputControls.TryGetValue("Cookie", out var control))
            {
                // Cookie 字段是 PasswordBox（用 Grid 包装），需要特殊处理
                if (control is Grid grid && grid.Tag is PasswordBoxWrapper wrapper)
                {
                    wrapper.PasswordBox.Password = data.Cookie;
                }
                // 也支持直接的 TextBox（如果用户已切换到"显示"模式）
                else if (control is WpfTextBox textBox)
                {
                    textBox.Text = data.Cookie;
                }
            }

            // 更新状态提示（显示关键元数据，与销项数据助手 cookie.json 字段对齐）
            GetCookieButton.Content = $"✅ 已获取 {data.Count} 条 Cookie（{data.Domain}）";

            System.Windows.MessageBox.Show(
                $"Cookie 获取成功！\n\n" +
                $"服务商: {data.ProviderId}\n" +
                $"域名: {data.Domain}\n" +
                $"条数: {data.Count}\n" +
                $"保存于: {data.SavedAt:yyyy-MM-dd HH:mm:ss}\n" +
                $"Cookie 长度: {data.Cookie.Length} 字符（完整内容已安全保存，不在弹窗中显示）\n\n" +
                $"持久化路径: %AppData%\\UsageMonitor\\cookies\\{data.ProviderId}.json\n\n" +
                "Edge 浏览器窗口已被自动关闭。\n" +
                "点击【保存】按钮保存配置后，回到主界面右键托盘 → 立即刷新即可看到用量数据。",
                "Cookie 已填入字段",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"启动登录窗口时出错：\n\n{ex.Message}\n\n请检查 Edge 浏览器是否已正确安装。",
                "启动失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // 释放防重复锁
            lock (_loginInProgressLock)
            {
                _isLoginInProgress.Remove(_loginConfig.ProviderId);
            }
            // 重要：用 try-catch 保护 finally 块中的 UI 操作。
            // await 期间用户可能关闭配置窗口，此时访问 GetCookieButton
            // 会抛 ObjectDisposedException，必须吞掉避免未处理异常导致 WPF 崩溃。
            try
            {
                if (GetCookieButton.IsLoaded)
                {
                    GetCookieButton.IsEnabled = true;
                    GetCookieButton.Content = originalContent;
                }
            }
            catch (System.ObjectDisposedException) { /* 窗口已关闭 */ }
            catch (System.InvalidOperationException) { /* 已释放 */ }
        }
    }

    /// <summary>
    /// 保存按钮点击 - 收集所有输入值写入 ProviderConfig
    /// </summary>
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        foreach (var field in _configFields)
        {
            if (!_inputControls.TryGetValue(field.Key, out var control))
                continue;

            string value = GetControlValue(control, field);

            // 验证必填项
            if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                System.Windows.MessageBox.Show(
                    $"\"{field.DisplayName}\" 为必填项",
                    "验证失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _config.SetValue(field.Key, value);
        }

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 取消按钮点击 - 关闭对话框不保存
    /// </summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 从输入控件中提取当前值
    /// </summary>
    private static string GetControlValue(FrameworkElement control, ConfigField field)
    {
        return field.FieldType switch
        {
            ConfigFieldType.Password => GetPasswordValue(control),
            ConfigFieldType.Boolean => (control is WpfCheckBox cb && cb.IsChecked == true).ToString(),
            ConfigFieldType.Select => (control as WpfComboBox)?.SelectedItem?.ToString() ?? "",
            _ => (control as WpfTextBox)?.Text ?? ""
        };
    }

    /// <summary>
    /// 从密码输入控件中获取值（支持PasswordBox和TextBox两种模式）
    /// </summary>
    private static string GetPasswordValue(FrameworkElement control)
    {
        if (control is Grid grid && grid.Tag is PasswordBoxWrapper wrapper)
        {
            // 如果当前显示的是TextBox，优先取TextBox的值
            if (wrapper.TextBox.Visibility == Visibility.Visible)
                return wrapper.TextBox.Text;
            return wrapper.PasswordBox.Password;
        }
        return "";
    }

    /// <summary>
    /// 创建圆角按钮模板
    /// </summary>
    private static ControlTemplate CreateRoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(WpfButton));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(WpfButton.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));

        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(contentPresenter);
        template.VisualTree = border;

        return template;
    }
}

/// <summary>
/// PasswordBox包装器 - 同时持有PasswordBox和TextBox引用，方便取值
/// </summary>
internal class PasswordBoxWrapper
{
    public WpfPasswordBox PasswordBox { get; }
    public WpfTextBox TextBox { get; }

    public PasswordBoxWrapper(WpfPasswordBox passwordBox, WpfTextBox textBox)
    {
        PasswordBox = passwordBox;
        TextBox = textBox;
    }
}
