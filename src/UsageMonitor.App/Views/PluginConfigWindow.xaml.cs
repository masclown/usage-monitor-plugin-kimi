using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;
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
    private readonly IReadOnlyList<ConfigField> _configFields;
    private readonly ProviderConfig _config;
    private readonly BrowserLoginConfig? _loginConfig;
    private readonly Dictionary<string, FrameworkElement> _inputControls = new();

    /// <summary>
    /// 正在登录中的 ProviderId 集合（进程级共享，避免同一插件重复触发登录）。
    /// <para>
    /// 计划文件字面建议字段名为 <c>_isLoginInProgress</c>（单一 bool），但实际实现采用
    /// <see cref="HashSet{T}"/> 以支持多 ProviderId 的独立并发控制：
    /// 例如用户在 DeepSeek 登录中点击 MiniMax 按钮不应被错误阻塞。
    /// </para>
    /// </summary>
    private static readonly HashSet<string> _isLoginInProgress = new();

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
    public PluginConfigWindow(
        string pluginName,
        IReadOnlyList<ConfigField> configFields,
        ProviderConfig config,
        BrowserLoginConfig? loginConfig = null)
    {
        InitializeComponent();
        _configFields = configFields;
        _config = config;
        _loginConfig = loginConfig;

        TitleText.Text = $"{pluginName} 配置";
        BuildForm();

        // 当插件声明了登录需求时，显示通用的"获取登录态"按钮
        if (_loginConfig != null)
        {
            GetCookieButton.Content = _loginConfig.UiButtonText ?? "🌐 获取登录态";
            GetCookieButton.Visibility = Visibility.Visible;
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
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };
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
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var labelRun = new System.Windows.Documents.Run(field.DisplayName);
        label.Inlines.Add(labelRun);

        if (field.IsRequired)
        {
            var requiredRun = new System.Windows.Documents.Run(" *")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
            };
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
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 2, 0, 0)
            };
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
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 63)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(61, 61, 82)),
            BorderThickness = new Thickness(1),
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
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 63)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(61, 61, 82)),
            BorderThickness = new Thickness(1),
            Tag = field.Key
        };
        Grid.SetColumn(passwordBox, 0);
        grid.Children.Add(passwordBox);

        var toggleBtn = new WpfButton
        {
            Content = "显示",
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 63)),
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var textBox = new WpfTextBox
        {
            Text = currentValue,
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 14,
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 63)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(61, 61, 82)),
            BorderThickness = new Thickness(1),
            Tag = field.Key
        };

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
                Grid.SetColumn(textBox, 0);
                grid.Children.Add(textBox);
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

        // 设置 Template 使按钮圆角
        toggleBtn.Template = CreateRoundedButtonTemplate();

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
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 63)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
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
        return comboBox;
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
            var data = await BrowserLoginService.LoginAndExtractCookieAsync(_loginConfig);

            if (data == null || string.IsNullOrEmpty(data.Cookie))
            {
                // 显示真实错误信息（来自 BrowserLoginService.LastError），
                // 而不是写死的"未检测到 account.minimaxi.com 域的会话 Cookie"误导用户。
                var lastError = BrowserLoginService.LastError;
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
