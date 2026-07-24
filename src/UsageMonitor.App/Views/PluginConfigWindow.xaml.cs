using System.Windows;
using System.Windows.Controls;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

// WinForms/WPF命名空间冲突解决：使用别名
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
/// <para>
/// S6 瘦身：账号增删改已统一迁移到设置窗口【插件管理】页，本窗口不再承载账号管理区；
/// 旧"卡片图表多选 + 示例预览"区（含空集合防御）一并移除，改为按插件 defaults.json
/// 声明的 chartId 列出「卡片图表 / 任务栏迷你图表」两组简单启用开关，
/// 持久化分别落 <c>AccountCustomization.VisibleCharts</c> / <c>VisibleMiniCharts</c>
/// （与设置窗口【卡片管理】/【任务栏迷你图表】页同一数据落点，避免双写冲突）。
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

    /// <summary>S6：启用开关生效的账号 ID（缺省 "default"；由调用方传入账号上下文时按账号生效）。</summary>
    private readonly string _accountId;

    /// <summary>S6：卡片图表启用开关映射（chartId → CheckBox，按插件 Card.Charts 声明顺序）。</summary>
    private readonly List<KeyValuePair<string, WpfCheckBox>> _cardChartSwitches = new();

    /// <summary>S6：任务栏迷你图表启用开关映射（miniChartId → CheckBox，按插件 Taskbar.MiniCharts 声明顺序）。</summary>
    private readonly List<KeyValuePair<string, WpfCheckBox>> _miniChartSwitches = new();

    /// <summary>Phase 2 修复：卡片图表开关初始是否为 legacy/null 全选语义（用户未改动时跳过写入）。</summary>
    private bool _cardChartIsLegacyAll;

    /// <summary>Phase 2 修复：迷你图表开关初始是否为 legacy/null 全选语义（用户未改动时跳过写入）。</summary>
    private bool _miniChartIsLegacyAll;

    /// <summary>Phase 2 修复：卡片图表开关初始勾选快照（用于比对用户是否实际改动）。</summary>
    private List<bool> _cardChartInitialChecked = new();

    /// <summary>Phase 2 修复：迷你图表开关初始勾选快照（用于比对用户是否实际改动）。</summary>
    private List<bool> _miniChartInitialChecked = new();

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
    /// req-065 B4：可选的 ConfigService，用于 BrowserLoginService 实例化（登录成功后自动重载内存配置）；
    /// S6：同时用于图表/迷你图表启用开关的读取与持久化。
    /// </param>
    /// <param name="provider">
    /// req-fix-Kimi-ConfigFields 动态模式：可选的插件实例引用。
    /// 传入后 PluginConfigWindow 会在 Mode ComboBox 切换时自动调用 <c>provider.ConfigFields</c>
    /// 重新拉取字段列表（如双模式插件根据 mode 字段返回不同字段）。
    /// 传 null 时按构造时传入的 _configFields 列表使用（向后兼容）。
    /// <para>S6：图表/迷你图表启用开关依赖 <c>provider.Card</c> / <c>provider.Taskbar</c> 声明，传 null 时两区隐藏。</para>
    /// </param>
    /// <param name="accountId">
    /// S6：可选的账号上下文。启用开关按该账号的 <c>AccountCustomization</c> 生效；
    /// 传 null / 空字符串时规范化为 "default"（Provider 级入口的缺省行为）。
    /// </param>
    public PluginConfigWindow(
        string pluginName,
        IReadOnlyList<ConfigField> configFields,
        ProviderConfig config,
        BrowserLoginConfig? loginConfig = null,
        ConfigService? configService = null,
        UsageMonitor.Core.Plugins.IUsageProvider? provider = null,
        string? accountId = null)
    {
        InitializeComponent();
        _configFields = configFields;
        _config = config;
        _loginConfig = loginConfig;
        _configService = configService;
        _provider = provider;
        _accountId = string.IsNullOrWhiteSpace(accountId) ? "default" : accountId.Trim();

        TitleText.Text = $"{pluginName} 配置";
        BuildForm();
        BuildChartSwitchSections();

        // 当插件声明了登录需求时，显示通用的"获取登录态"按钮
        if (_loginConfig != null)
        {
            GetCookieButton.Content = _loginConfig.UiButtonText ?? "🌐 获取登录态";
            GetCookieButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// S6：构建「卡片图表 / 任务栏迷你图表」两组启用开关。
    /// <para>按插件 defaults.json 声明的 chartId 逐一列出 CheckBox；
    /// 插件未声明（或 <see cref="_provider"/> / <see cref="_configService"/> 缺失）时整组隐藏。</para>
    /// </summary>
    private void BuildChartSwitchSections()
    {
        BuildCardChartSwitches();
        BuildMiniChartSwitches();
    }

    /// <summary>
    /// S6：按插件 <c>Card.Charts</c> 声明构建卡片图表启用开关。
    /// <para>初始勾选态读取 <c>GetEffectiveAccountCustomization</c> 的 <c>VisibleCharts</c>
    /// （null = 沿用 defaults.json 全部可见，含旧 ProviderCardChartKinds 读取兼容回退）。</para>
    /// </summary>
    private void BuildCardChartSwitches()
    {
        var charts = _provider?.Card?.Charts;
        if (_provider == null || _configService == null || charts == null || charts.Count == 0)
        {
            CardChartSwitchSection.Visibility = Visibility.Collapsed;
            return;
        }

        var eff = _configService.GetEffectiveAccountCustomization(_provider.ProviderId, _accountId);
        // Phase 2 修复：检测旧配置回显错误——旧 ProviderCardChartKinds 类型名（如 "Ring","Line"）
        // 被兼容层填进 VisibleCharts，与声明 chartId 永不匹配 → 开关全部回显为不勾选。
        // 判定 isLegacyFill：VisibleCharts 非 null 且含任一不在声明 chartId 集合中的值。
        var declaredIds = new HashSet<string>(charts.Select(c => c.ChartId), StringComparer.Ordinal);
        bool isLegacyFill = eff.VisibleCharts != null
            && eff.VisibleCharts.Any(v => !declaredIds.Contains(v));
        _cardChartIsLegacyAll = eff.VisibleCharts == null || isLegacyFill;

        CardChartSwitchPanel.Children.Clear();
        _cardChartSwitches.Clear();
        _cardChartInitialChecked.Clear();
        foreach (var chart in charts)
        {
            // Phase 2 修复：legacy/null 时全选回显，避免旧配置被误显示为未勾选。
            var isChecked = _cardChartIsLegacyAll || eff.VisibleCharts!.Contains(chart.ChartId);
            var cb = new WpfCheckBox
            {
                Content = ExtractChartShortName(chart.ChartId),
                IsChecked = isChecked,
                ToolTip = chart.ChartId,
                Margin = new Thickness(0, 4, 0, 4)
            };
            cb.SetResourceReference(WpfCheckBox.ForegroundProperty, "TextPrimaryBrush");
            CardChartSwitchPanel.Children.Add(cb);
            _cardChartSwitches.Add(new KeyValuePair<string, WpfCheckBox>(chart.ChartId, cb));
            _cardChartInitialChecked.Add(isChecked);
        }
    }

    /// <summary>
    /// S6：按插件 <c>Taskbar.MiniCharts</c> 声明构建任务栏迷你图表启用开关。
    /// <para>初始勾选态读取 <c>GetEffectiveAccountCustomization</c> 的 <c>VisibleMiniCharts</c>（null = 全部可见）。</para>
    /// </summary>
    private void BuildMiniChartSwitches()
    {
        var miniCharts = _provider?.Taskbar?.MiniCharts;
        if (_provider == null || _configService == null || miniCharts == null || miniCharts.Count == 0)
        {
            MiniChartSwitchSection.Visibility = Visibility.Collapsed;
            return;
        }

        var eff = _configService.GetEffectiveAccountCustomization(_provider.ProviderId, _accountId);
        // Phase 2 修复：同卡片图表侧逻辑——检测旧配置回显错误。
        var declaredIds = new HashSet<string>(miniCharts.Select(m => m.ChartId), StringComparer.Ordinal);
        bool isLegacyFill = eff.VisibleMiniCharts != null
            && eff.VisibleMiniCharts.Any(v => !declaredIds.Contains(v));
        _miniChartIsLegacyAll = eff.VisibleMiniCharts == null || isLegacyFill;

        MiniChartSwitchPanel.Children.Clear();
        _miniChartSwitches.Clear();
        _miniChartInitialChecked.Clear();
        foreach (var mini in miniCharts)
        {
            var isChecked = _miniChartIsLegacyAll || eff.VisibleMiniCharts!.Contains(mini.ChartId);
            var cb = new WpfCheckBox
            {
                Content = ExtractChartShortName(mini.ChartId),
                IsChecked = isChecked,
                ToolTip = mini.ChartId,
                Margin = new Thickness(0, 4, 0, 4)
            };
            cb.SetResourceReference(WpfCheckBox.ForegroundProperty, "TextPrimaryBrush");
            MiniChartSwitchPanel.Children.Add(cb);
            _miniChartSwitches.Add(new KeyValuePair<string, WpfCheckBox>(mini.ChartId, cb));
            _miniChartInitialChecked.Add(isChecked);
        }
    }

    /// <summary>S6：从 chartId 提取简短显示名（去掉 Provider 前缀，与卡片管理页 ChartNode 规则一致：
    /// "mm.chart.usage_bar" → "usage_bar"；不足三段时原样返回）。</summary>
    private static string ExtractChartShortName(string chartId)
    {
        var parts = chartId.Split('.');
        return parts.Length > 2 ? string.Join(".", parts.Skip(2)) : chartId;
    }

    /// <summary>
    /// S6：保存时持久化两组启用开关。
    /// <para>卡片图表落 <c>AccountCustomization.VisibleCharts</c>（ConfigService.SetVisibleCharts），
    /// 迷你图表落 <c>VisibleMiniCharts</c>（ConfigService.SetVisibleMiniCharts）——
    /// 与设置窗口【卡片管理】/【任务栏迷你图表】页同一数据落点；
    /// 两个窄写入方法只更新单一字段，不触碰排序 / 数据组等兄弟配置，避免双写冲突。
    /// 声明缺失（开关区未构建）时跳过对应写入，不产生空配置条目。</para>
    /// </summary>
    private void PersistChartSwitches()
    {
        if (_provider == null || _configService == null) return;

        // Phase 2 修复：若初始为 legacy/null 全选语义且用户未改动任何开关，则跳过写入（保持 null 兼容路径）。
        if (_cardChartSwitches.Count > 0)
        {
            bool cardChanged = false;
            for (int i = 0; i < _cardChartSwitches.Count; i++)
            {
                if ((_cardChartSwitches[i].Value.IsChecked == true) != _cardChartInitialChecked[i])
                { cardChanged = true; break; }
            }
            if (!(_cardChartIsLegacyAll && !cardChanged))
            {
                var visible = _cardChartSwitches
                    .Where(kv => kv.Value.IsChecked == true)
                    .Select(kv => kv.Key)
                    .ToList();
                _configService.SetVisibleCharts(_provider.ProviderId, visible, _accountId);
            }
        }

        if (_miniChartSwitches.Count > 0)
        {
            bool miniChanged = false;
            for (int i = 0; i < _miniChartSwitches.Count; i++)
            {
                if ((_miniChartSwitches[i].Value.IsChecked == true) != _miniChartInitialChecked[i])
                { miniChanged = true; break; }
            }
            if (!(_miniChartIsLegacyAll && !miniChanged))
            {
                var visible = _miniChartSwitches
                    .Where(kv => kv.Value.IsChecked == true)
                    .Select(kv => kv.Key)
                    .ToList();
                _configService.SetVisibleMiniCharts(_provider.ProviderId, visible, _accountId);
            }
        }
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
    /// 创建布尔开关控件（CheckBox）。
    /// <para>S6：前景色改用主题画刷动态引用（原硬编码 RGB 已移除），随主题切换自适应。</para>
    /// </summary>
    private WpfCheckBox CreateBooleanInput(ConfigField field)
    {
        var currentValue = _config.GetValue(field.Key) ?? field.DefaultValue ?? "false";
        var checkBox = new WpfCheckBox
        {
            IsChecked = bool.TryParse(currentValue, out var b) && b,
            FontSize = 14,
            Tag = field.Key
        };
        checkBox.SetResourceReference(WpfCheckBox.ForegroundProperty, "TextPrimaryBrush");
        return checkBox;
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
    /// 3. 重新构建整个表单（图表启用开关不依赖 mode，无须重建）
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

            // 4. 把之前抓取的值填回新控件
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
    /// 保存按钮点击 - 收集所有输入值写入 ProviderConfig，并持久化图表/迷你图表启用开关（S6）。
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

        // S6：持久化卡片图表 / 任务栏迷你图表启用开关（落点同卡片管理页 / 迷你图表页）。
        // 旧"卡片图表多选"的空集合防御已随该区删除——启用开关写的是 chartId 列表，
        // 空集合是合法用户选择（= 不显示任何图表），语义与 AccountCustomization 契约一致。
        PersistChartSwitches();

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
