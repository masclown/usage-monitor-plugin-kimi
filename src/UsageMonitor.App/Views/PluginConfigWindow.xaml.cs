using System.Windows;
using System.Windows.Controls;
using UsageMonitor.App.ViewModels;
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
/// 插件配置对话框 - 根据插件的 ConfigFields 动态生成配置表单。
/// <para>
/// req-069-009/010/011 MVVM 化：全部业务逻辑（表单保存校验、Cookie 获取、Mode 切换持久化、
/// 图表开关回显与写入决策）已迁移至 <see cref="PluginConfigViewModel"/>；
/// 本 code-behind 仅保留 WPF 动态控件构建与控件值读写（视图职责）。
/// </para>
/// <para>
/// S6 瘦身：账号增删改已统一迁移到设置窗口【插件管理】页，本窗口不再承载账号管理区；
/// 按插件 defaults.json 声明的 chartId 列出「卡片图表 / 任务栏迷你图表」两组简单启用开关，
/// 持久化分别落 <c>AccountCustomization.VisibleCharts</c> / <c>VisibleMiniCharts</c>。
/// </para>
/// </summary>
public partial class PluginConfigWindow : Window
{
    /// <summary>req-069：承载全部业务逻辑的视图模型。</summary>
    private readonly PluginConfigViewModel _viewModel;

    /// <summary>动态表单控件注册表（fieldKey → 输入控件）。</summary>
    private readonly Dictionary<string, FrameworkElement> _inputControls = new();

    /// <summary>S6：卡片图表启用开关 CheckBox 映射（chartId → CheckBox）。</summary>
    private readonly Dictionary<string, WpfCheckBox> _cardChartCheckBoxes = new();

    /// <summary>S6：任务栏迷你图表启用开关 CheckBox 映射（chartId → CheckBox）。</summary>
    private readonly Dictionary<string, WpfCheckBox> _miniChartCheckBoxes = new();

    /// <summary>req-fix-Kimi-ModeRebuildStackOverflow：re-entrancy 保护标志（防止 BuildForm 创建新 ComboBox 时
    /// 设置 SelectedItem 触发 SelectionChanged → 再次重建 → StackOverflowException）。</summary>
    private bool _isRebuildingForMode;

    /// <summary>
    /// 创建插件配置对话框。
    /// </summary>
    /// <param name="pluginName">插件显示名称</param>
    /// <param name="configFields">插件定义的配置字段</param>
    /// <param name="config">当前配置（读取和写入）</param>
    /// <param name="loginConfig">可选的浏览器登录配置（非 null 时显示"获取登录态"按钮）</param>
    /// <param name="configService">可选 ConfigService（登录成功后自动重载 + 图表开关持久化）</param>
    /// <param name="provider">可选插件实例（Mode 切换重拉字段 + 图表声明来源；null 时图表开关区隐藏）</param>
    /// <param name="accountId">账号上下文（null/空 → "default"）</param>
    public PluginConfigWindow(
        string pluginName,
        IReadOnlyList<ConfigField> configFields,
        ProviderConfig config,
        BrowserLoginConfig? loginConfig = null,
        ConfigService? configService = null,
        IUsageProvider? provider = null,
        string? accountId = null)
    {
        InitializeComponent();

        _viewModel = new PluginConfigViewModel(
            pluginName, configFields, config, loginConfig, configService, provider, accountId);
        DataContext = _viewModel;

        // 视图交互契约装配：VM 通过委托收集控件值，通过事件请求关闭/填入 Cookie
        _viewModel.CollectFormValues = CaptureFormValues;
        _viewModel.CollectCardChartStates = () => ReadCheckBoxStates(_cardChartCheckBoxes);
        _viewModel.CollectMiniChartStates = () => ReadCheckBoxStates(_miniChartCheckBoxes);
        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.CookieReceived += OnCookieReceived;

        BuildForm(_viewModel.ConfigFields);
        BuildChartSwitchCheckBoxes();
    }

    // =====================================================================
    // VM 事件回调
    // =====================================================================

    /// <summary>VM 请求关闭窗口（保存成功或取消）。</summary>
    private void OnCloseRequested(bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }

    /// <summary>VM 通知 Cookie 提取成功——将 Cookie 填入对应输入控件。</summary>
    private void OnCookieReceived(string cookie)
    {
        if (_inputControls.TryGetValue("Cookie", out var control))
        {
            // Cookie 字段是 PasswordBox（用 Grid 包装），需要特殊处理
            if (control is Grid grid && grid.Tag is PasswordBoxWrapper wrapper)
            {
                wrapper.PasswordBox.Password = cookie;
            }
            // 也支持直接的 TextBox（如果用户已切换到"显示"模式）
            else if (control is WpfTextBox textBox)
            {
                textBox.Text = cookie;
            }
        }
    }

    // =====================================================================
    // 动态表单构建（纯视图职责）
    // =====================================================================

    /// <summary>根据 ConfigFields 动态构建表单控件。</summary>
    private void BuildForm(IReadOnlyList<ConfigField> fields)
    {
        FormPanel.Children.Clear();
        _inputControls.Clear();

        foreach (var field in fields)
        {
            var row = CreateFormRow(field);
            FormPanel.Children.Add(row);
        }

        if (fields.Count == 0)
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

    /// <summary>为单个配置字段创建一行表单（标签 + 输入控件）。</summary>
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

    /// <summary>创建文本输入控件（TextBox）。</summary>
    private WpfTextBox CreateTextInput(ConfigField field)
    {
        var currentValue = _viewModel.ConfigFieldValue(field.Key) ?? field.DefaultValue ?? "";
        return new WpfTextBox
        {
            Text = currentValue,
            Tag = field.Key
        };
    }

    /// <summary>创建密码输入控件（PasswordBox + 显示/隐藏切换）。</summary>
    private FrameworkElement CreatePasswordInput(ConfigField field)
    {
        var currentValue = _viewModel.ConfigFieldValue(field.Key) ?? field.DefaultValue ?? "";

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
    /// <para>S6：前景色改用主题画刷动态引用，随主题切换自适应。</para>
    /// </summary>
    private WpfCheckBox CreateBooleanInput(ConfigField field)
    {
        var currentValue = _viewModel.ConfigFieldValue(field.Key) ?? field.DefaultValue ?? "false";
        var checkBox = new WpfCheckBox
        {
            IsChecked = bool.TryParse(currentValue, out var b) && b,
            FontSize = 14,
            Tag = field.Key
        };
        checkBox.SetResourceReference(WpfCheckBox.ForegroundProperty, "TextPrimaryBrush");
        return checkBox;
    }

    /// <summary>创建下拉选择控件（ComboBox）。Mode 字段挂载 SelectionChanged 触发 VM 业务处理。</summary>
    private WpfComboBox CreateSelectInput(ConfigField field)
    {
        var currentValue = _viewModel.ConfigFieldValue(field.Key) ?? field.DefaultValue ?? "";
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

        // req-fix-Kimi-ConfigFields 动态模式：Mode 字段 ComboBox 变化时触发 VM 业务处理 + 表单重建
        if (_viewModel.SupportsModeSwitch && PluginConfigViewModel.IsModeFieldKey(field.Key))
        {
            comboBox.SelectionChanged += (_, _) => RebuildFormForModeChange();
        }
        return comboBox;
    }

    /// <summary>
    /// req-fix-Kimi-ConfigFields 动态模式：Mode 字段切换时的视图重建流程。
    /// <para>业务处理（Mode 持久化 + 字段重拉）委托给 <see cref="PluginConfigViewModel.HandleModeChange"/>；
    /// 本方法仅负责抓取/恢复控件值与重建表单（视图职责）。</para>
    /// </summary>
    private void RebuildFormForModeChange()
    {
        // req-fix-Kimi-ModeRebuildStackOverflow：re-entrancy 保护
        if (_isRebuildingForMode) return;
        _isRebuildingForMode = true;
        try
        {
            // 1. 抓取当前所有输入控件的当前值（保留用户输入，跳过空值）
            var currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, control) in _inputControls)
            {
                if (TryGetControlValue(control, out var v)) currentValues[key] = v;
            }

            // 2. VM 业务处理：Mode 持久化 + 字段列表重拉
            var newFields = _viewModel.HandleModeChange(currentValues);
            if (newFields == null) return;

            // 3. 重新构建表单（图表启用开关不依赖 mode，无须重建）
            BuildForm(newFields);

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

    // =====================================================================
    // S6：图表启用开关 CheckBox 构建（纯视图；初始态由 VM 计算）
    // =====================================================================

    /// <summary>按 VM 提供的开关项列表构建两组 CheckBox；VM 列表为空时隐藏对应区块。</summary>
    private void BuildChartSwitchCheckBoxes()
    {
        BuildSwitchGroup(CardChartSwitchSection, CardChartSwitchPanel, _cardChartCheckBoxes, _viewModel.CardChartItems);
        BuildSwitchGroup(MiniChartSwitchSection, MiniChartSwitchPanel, _miniChartCheckBoxes, _viewModel.MiniChartItems);
    }

    /// <summary>构建单组图表启用开关 CheckBox 列表（声明缺失时整组隐藏）。</summary>
    private static void BuildSwitchGroup(
        StackPanel section, StackPanel panel,
        Dictionary<string, WpfCheckBox> registry,
        IReadOnlyList<ChartSwitchItem> items)
    {
        registry.Clear();
        panel.Children.Clear();

        if (items.Count == 0)
        {
            section.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var item in items)
        {
            var cb = new WpfCheckBox
            {
                Content = item.DisplayName,
                IsChecked = item.InitialChecked,
                ToolTip = item.ChartId,
                Margin = new Thickness(0, 4, 0, 4)
            };
            cb.SetResourceReference(WpfCheckBox.ForegroundProperty, "TextPrimaryBrush");
            panel.Children.Add(cb);
            registry[item.ChartId] = cb;
        }
    }

    // =====================================================================
    // 控件值读写（视图管道，供 VM 委托调用）
    // =====================================================================

    /// <summary>收集当前表单所有字段的值（保存语义：始终返回字符串，含空串）。</summary>
    private Dictionary<string, string> CaptureFormValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in _viewModel.ConfigFields)
        {
            if (_inputControls.TryGetValue(field.Key, out var control))
            {
                values[field.Key] = GetControlValue(control, field);
            }
        }
        return values;
    }

    /// <summary>读取一组 CheckBox 的当前勾选态（chartId → isChecked）。</summary>
    private static Dictionary<string, bool> ReadCheckBoxStates(Dictionary<string, WpfCheckBox> registry)
        => registry.ToDictionary(kv => kv.Key, kv => kv.Value.IsChecked == true);

    /// <summary>从已有控件读取当前值（支持 TextBox/PasswordBox/CheckBox/ComboBox；空值返回 false）。</summary>
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

    /// <summary>从输入控件中提取当前值（保存语义，按字段类型分派）。</summary>
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

    /// <summary>从密码输入控件中获取值（支持 PasswordBox 和 TextBox 两种显示模式）。</summary>
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
