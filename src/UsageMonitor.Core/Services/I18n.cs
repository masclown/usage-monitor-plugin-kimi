using System;
using System.Collections.Generic;
using System.Globalization;

namespace UsageMonitor.Core.Services;

/// <summary>
/// 轻量级本地化服务 - 提供插件与 App 共用的文案解析能力。
/// <para>
/// 设计目标：
/// （1）零依赖：使用纯内存字典 + 嵌入式中文文案，不引入 resx/JSON 资源文件，避免 .csproj 复杂度。
/// （2）可扩展：通过 <see cref="Register"/> 允许插件或未来"语言包"模块追加词条/新语言。
/// （3）兜底友好：键缺失时按"当前语言 -> 默认语言(zh-CN) -> key 本身"逐级回退，
///      并通过 <see cref="FileLogger.Warn"/> 暴露漏翻，便于开发期发现。
/// </para>
/// <para>
/// 键命名约定：
/// <c>plugin.&lt;providerId&gt;.field.&lt;fieldKey&gt;.name</c> / <c>.placeholder</c>
/// （如 <c>plugin.MiniMax.field.ApiKey.name</c>）。
/// </para>
/// <para>
/// 当前范围仅覆盖插件配置字段名称/提示。App UI 文案仍以中文硬编码，
/// 未来需要多语言切换时复用同一 <see cref="T"/> API 即可，无需改动调用方。
/// </para>
/// </summary>
public static class I18n
{
    /// <summary>默认语言。键缺失时优先回退到此语言。</summary>
    public const string DefaultLanguage = "zh-CN";

    /// <summary>当前生效语言。默认值为 <see cref="DefaultLanguage"/>。</summary>
    public static string CurrentLanguage { get; private set; } = DefaultLanguage;

    /// <summary>
    /// 语言切换事件 - 为未来"运行时语言切换"预留。
    /// 当前 UI 文案仍为硬编码，切换后仅影响通过 <see cref="T"/> 取值的文案（如重新打开配置窗口）。
    /// </summary>
    public static event EventHandler? LanguageChanged;

    // 语言注册表：language -> (key -> text)。
    // 使用普通 Dictionary + 锁保护，因为注册集中在启动期，运行期几乎只读。
    private static readonly Dictionary<string, Dictionary<string, string>> _registry = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    /// <summary>
    /// 静态构造：在内置 zh-CN 词条之上初始化注册表。
    /// 不在此处注入其它语言，保持"仅中文"现状；调用 <see cref="Register"/> 追加。
    /// </summary>
    static I18n()
    {
        // 使用 var 局部变量便于维护：键 -> 中文文案，扁平结构便于搜索/审阅。
        var zhCn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ---- MiniMax ----
            ["plugin.MiniMax.field.ApiKey.name"] = "Token Plan 订阅密钥",
            ["plugin.MiniMax.field.ApiKey.placeholder"] = "请输入 MiniMax Token Plan 订阅密钥（可在订阅页面获取）",
            ["plugin.MiniMax.field.Cookie.name"] = "Cookie 登录态（可选备用）",
            ["plugin.MiniMax.field.Cookie.placeholder"] = "可选。当密钥失效时，可尝试使用已登录浏览器的 Cookie",
            ["plugin.MiniMax.field.Region.name"] = "接口区域",
            ["plugin.MiniMax.field.Show5hBar.name"] = "显示 5h 限额进度条",
            ["plugin.MiniMax.field.ShowWeeklyBar.name"] = "显示 本周限额 进度条",
            ["plugin.MiniMax.field.ShowVideo5hBar.name"] = "显示 视频赠送 5h 进度条",
            ["plugin.MiniMax.field.ShowVideoWeeklyBar.name"] = "显示 视频赠送 本周 进度条",

            // ---- OpenAI ----
            ["plugin.OpenAI.field.ApiKey.name"] = "API 密钥",
            ["plugin.OpenAI.field.ApiKey.placeholder"] = "sk-xxxxxxxxxxxxxxxx",
            ["plugin.OpenAI.field.BaseUrl.name"] = "API 地址",
            ["plugin.OpenAI.field.BaseUrl.placeholder"] = "https://api.openai.com",
            ["plugin.OpenAI.field.Organization.name"] = "组织 ID（Organization）",
            ["plugin.OpenAI.field.Organization.placeholder"] = "org-xxxxxxxx（可选）",

            // ---- req-070 F-28：历史窗口下拉框文案 ----
            ["history.range.last7days"] = "最近 7 天",
            ["history.range.last30days"] = "最近 30 天",
            ["history.range.last90days"] = "最近 90 天",
            ["history.range.all"] = "全部",
            ["history.chart.line"] = "折线图",
            ["history.chart.bar"] = "柱状图",
            ["history.chart.heatmap"] = "热力图",
            ["history.chart.daynightarc"] = "编程时段",
        };
        lock (_lock)
        {
            _registry[DefaultLanguage] = zhCn;
        }
    }

    /// <summary>
    /// 解析文案为当前语言；命中且 <paramref name="args"/> 非空时按当前 CultureInfo 格式化。
    /// </summary>
    /// <param name="key">资源键。</param>
    /// <param name="args">可选格式化参数（与 <see cref="string.Format(string, object[])"/> 一致）。</param>
    /// <returns>
    /// 解析顺序：当前语言 -> 默认语言(zh-CN) -> <paramref name="key"/> 本身。
    /// 当键在当前语言与默认语言均缺失时，记录一次 Warn 日志以便发现漏翻。
    /// </returns>
    public static string T(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        string? text = Resolve(CurrentLanguage, key) ?? Resolve(DefaultLanguage, key);
        if (text == null)
        {
            FileLogger.Warn("I18n", $"Missing translation for key='{key}' (lang='{CurrentLanguage}', fallback='{DefaultLanguage}')");
            text = key;
        }
        else if (args != null && args.Length > 0)
        {
            try
            {
                text = string.Format(CultureInfo.CurrentCulture, text, args);
            }
            catch (FormatException)
            {
                // 占位符不匹配时回退原文，避免 UI 崩溃。
            }
        }
        return text;
    }

    /// <summary>
    /// 合并式注册文案：向指定语言追加/覆盖键值对。
    /// <para>
    /// 典型用途：未来加载语言包（JSON/resx），或插件自带文案覆盖内置默认值。
    /// 已存在的键会被新值覆盖，便于在不修改 Core 的情况下调整措辞。
    /// </para>
    /// </summary>
    /// <param name="lang">语言标识（如 <c>zh-CN</c>、<c>en-US</c>）。</param>
    /// <param name="entries">要注册/覆盖的键值对。</param>
    public static void Register(string lang, IReadOnlyDictionary<string, string> entries)
    {
        if (string.IsNullOrWhiteSpace(lang) || entries == null) return;
        lock (_lock)
        {
            if (!_registry.TryGetValue(lang, out var bucket))
            {
                bucket = new Dictionary<string, string>(StringComparer.Ordinal);
                _registry[lang] = bucket;
            }
            foreach (var kv in entries)
            {
                bucket[kv.Key] = kv.Value;
            }
        }
    }

    /// <summary>
    /// 切换当前语言并触发 <see cref="LanguageChanged"/>。
    /// <para>
    /// 未来可由设置窗口调用：先 <see cref="Register"/> 加载目标语言包，再 <see cref="SetLanguage"/>。
    /// 当前 UI 文案仍为硬编码，切换后只会让通过 <see cref="T"/> 取得的文案按新语言解析
    /// （如重新打开插件配置窗口即可看到效果）。
    /// </para>
    /// </summary>
    /// <param name="lang">目标语言标识。未知语言将自动注册为空字典，不抛异常。</param>
    public static void SetLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return;
        if (string.Equals(CurrentLanguage, lang, StringComparison.OrdinalIgnoreCase)) return;

        lock (_lock)
        {
            if (!_registry.ContainsKey(lang))
            {
                _registry[lang] = new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
        CurrentLanguage = lang;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 内部按语言解析单个键。未命中返回 <c>null</c>。
    /// </summary>
    private static string? Resolve(string lang, string key)
    {
        lock (_lock)
        {
            if (_registry.TryGetValue(lang, out var bucket) &&
                bucket.TryGetValue(key, out var text))
            {
                return text;
            }
        }
        return null;
    }
}
