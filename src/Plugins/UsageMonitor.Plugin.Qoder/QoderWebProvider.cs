using Microsoft.Playwright;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;
using UsageMonitor.Core.Services;

namespace UsageMonitor.Plugin.Qoder;

/// <summary>
/// req-087：Qoder 网页模式用量查询插件。
/// 通过 qoder.com 网页 DOM 抓取 Credits 用量数据。
/// 继承 <see cref="WebPluginBase"/> 复用浏览器生命周期管理。
/// </summary>
public class QoderWebProvider : WebPluginBase
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>req-067-002：Qoder DOM 主指标解析的 4 个正则提为 static readonly + Compiled，避免每次解析重新编译。</summary>
    private static readonly Regex _slashMetricRegex =
        new(@"([\d,]+(?:\.\d+)?)\s*[/\u5206]\s*([\d,]+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex _remainingMetricRegex =
        new(@"(?:剩余|可用|余量|remaining|left|available)\s*[:：]?\s*([\d,]+(?:\.\d+)?[KMk]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _totalMetricRegex =
        new(@"(?:总额|总计|总量|total)\s*[:：]?\s*([\d,]+(?:\.\d+)?[KMk]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _singleNumberRegex =
        new(@"([\d,]+(?:\.\d+)?[KMk]?)", RegexOptions.Compiled);

    /// <inheritdoc />
    protected override HttpClient Http => _httpClient;

    /// <inheritdoc />
    public override string ProviderId => "qoder_web";

    /// <inheritdoc />
    public override string DisplayName => "Qoder (网页模式)";

    /// <inheritdoc />
    public override string Version => "1.0.0";

    /// <inheritdoc />
    public override string Author => "UsageMonitor";

    /// <inheritdoc />
    public override string Description => "通过 Qoder 平台网页获取 Credits 用量数据（支持图表展示）";

    /// <inheritdoc />
    /// <remarks>req-fix-Qoder-B2-ValidateUrl（用户 2026-07-21 反馈）：登录后实际跳转到
    /// <c>https://qoder.com/account/usage</c>（裸域 + account/usage 路径），
    /// 不是写死的 <c>https://www.qoder.com/console/usage</c>。改用裸域 + 真实用量路径。</remarks>
    protected override string LoginUrl => "https://qoder.com/";

    /// <inheritdoc />
    /// <remarks>req-fix-Qoder-B2-ValidateUrl：真实用量页 <c>https://qoder.com/account/usage</c>。</remarks>
    protected override string UsageUrl => "https://qoder.com/account/usage";

    /// <inheritdoc />
    /// <remarks>req-fix-Qoder-B2-ValidateUrl：用户登录后实际跳 <c>qoder.com</c>（裸域），
    /// 而非 <c>www.qoder.com</c>。过滤列表改为 <c>[".qoder.com", "qoder.com"]</c>，确保 Cookie 提取覆盖两个 host。</remarks>
    protected override string[] CookieDomainFilters => new[] { ".qoder.com", "qoder.com" };

    /// <summary>
    /// req-fix-Qoder LoginConfig 强化：显式 override（替代之前的 <c>new</c> 隐藏），
    /// 恢复多态能力，同时补齐 BrowserLoginService 判定所需的关键字。
    /// </summary>
    public override BrowserLoginConfig? LoginConfig => new()
    {
        ProviderId = "qoder_web",
        LoginUrl = LoginUrl,
        CookieDomainFilters = CookieDomainFilters,
        ValidateUrl = UsageUrl,
        LoggedInHost = "qoder.com",
        LoginUrlKeywords = new[] { "login", "signin", "sign-in", "signup", "register",
                                    "auth", "passport", "oauth", "unified-login" }
    };

    /// <inheritdoc />
    public override IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        StandardWebConfigFields.Cookie(ProviderId),
        StandardWebConfigFields.Region(ProviderId, "CN", "CN", "Global"),
        StandardWebConfigFields.AutoRefresh(ProviderId, true),
        StandardWebConfigFields.Headless(ProviderId, false)
    };

    /// <inheritdoc />
    /// <remarks>
    /// req-099/bug5：移除 HeatMap。Qoder 网页数据仅有 Credits 余额/消耗快照（V2 数字网格），
    /// 没有「逐日 Token 日历」数据源，热力图选项会永远空白，属于图表能力误声明；
    /// 折线图/柱状/环形可由历史用量百分比或 Credits 比驱动，故保留。
    /// </remarks>
    public override IReadOnlyList<CardChartKind> SupportedCardCharts => new[]
    {
        CardChartKind.Line, CardChartKind.Bar, CardChartKind.Ring
    };

    /// <summary>
    /// req-098：Qoder 网页模式任务栏迷你图声明：半圆环 + 文字（基础两件套）。
    /// </summary>
    public override IReadOnlyList<UsageMonitor.Core.Plugins.MiniChart.MiniChartKind> SupportedMiniCharts => new[]
    {
        UsageMonitor.Core.Plugins.MiniChart.MiniChartKind.MiniRingChart,
        UsageMonitor.Core.Plugins.MiniChart.MiniChartKind.MiniText
    };

    /// <summary>
    /// req-098：Qoder 网页模式任务栏迷你图内容：主指标（已用百分比）+ Credits（剩余 Credits 余额）。
    /// </summary>
    public override IReadOnlyList<UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind> MiniChartDataTypes => new[]
    {
        UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind.PrimaryMetric,
        UsageMonitor.Core.Plugins.MiniChart.MiniChartContentKind.Credits
    };

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent", "Usage" };

    /// <summary>req-099/bug5：Qoder 卡片 V2 数字网格——剩余/总/已消耗 Credits + 消耗成本。</summary>
    protected override MetricGridData? BuildCardMetricGridData(UsageInfo usage)
    {
        var items = new System.Collections.Generic.List<MetricGridItem>();
        var remaining = ReadExtraDouble(usage, "credits_remaining", -1);
        var total = ReadExtraDouble(usage, "credits_total", -1);
        var consumed = ReadExtraDouble(usage, "total_credits_consumed", -1);
        var cost = ReadExtraDouble(usage, "total_cost_consumed", -1);
        if (remaining >= 0) items.Add(new MetricGridItem("剩余 Credits", remaining.ToString("N0")));
        if (total >= 0) items.Add(new MetricGridItem("总 Credits", total.ToString("N0")));
        if (consumed >= 0) items.Add(new MetricGridItem("已消耗", consumed.ToString("N0")));
        if (cost >= 0) items.Add(new MetricGridItem("消耗成本", "$" + cost.ToString("N2")));
        return items.Count > 0 ? new MetricGridData(items) : null;
    }

    /// <summary>req-099/bug5：Qoder 卡片 V2 进度条——Credits 已用百分比。</summary>
    protected override MetricBarData? BuildCardMetricBarData(UsageInfo usage)
    {
        var remaining = ReadExtraDouble(usage, "credits_remaining", -1);
        var total = ReadExtraDouble(usage, "credits_total", -1);
        if (total > 0 && remaining >= 0)
        {
            var usedPct = System.Math.Max(0, System.Math.Min(100, (total - remaining) / total * 100));
            return new MetricBarData(new[]
            {
                new MetricBarItem("Credits 已用", usedPct, FooterText: $"{(total - remaining):N0} / {total:N0}")
            });
        }
        return null;
    }

    /// <summary>
    /// 解析用量页面，提取 Credits 主指标和动态表格数据。
    /// </summary>
    protected override async Task<UsageInfo> ParseUsagePageAsync(IPage page)
    {
        var usageInfo = new UsageInfo
        {
            ProviderId = ProviderId,
            ProviderName = DisplayName,
            IsSuccess = true,
            LastUpdated = DateTime.UtcNow
        };

        try
        {
            // 1. 抓取主指标（顶部固定数字）
            var mainMetric = await ExtractMainMetricAsync(page);
            if (mainMetric != null)
            {
                usageInfo.TotalAmount = mainMetric.Total;
                usageInfo.UsedAmount = mainMetric.Used;
                usageInfo.Unit = "Credits";
                usageInfo.Extra["credits_remaining"] = mainMetric.Remaining;
                usageInfo.Extra["credits_total"] = mainMetric.Total;
            }

            // 2. 抓取动态表格（7 字段）
            var records = await ExtractTableDataAsync(page);
            if (records.Count > 0)
            {
                usageInfo.Extra["table_record_count"] = records.Count;

                var lineData = QoderChartAdapter.ToLineChartData(records);
                var barData = QoderChartAdapter.ToBarChartData(records);
                var heatData = QoderChartAdapter.ToHeatMapData(records);

                usageInfo.Extra["line_chart_data"] = JsonSerializer.Serialize(lineData);
                usageInfo.Extra["bar_chart_data"] = JsonSerializer.Serialize(barData);
                usageInfo.Extra["heat_map_data"] = JsonSerializer.Serialize(heatData);

                var totalCredits = QoderChartAdapter.CalculateTotalCredits(records);
                var totalCost = QoderChartAdapter.CalculateTotalCost(records);
                usageInfo.Extra["total_credits_consumed"] = totalCredits;
                usageInfo.Extra["total_cost_consumed"] = totalCost;

                usageInfo.Quantity = new Quantity(totalCredits, new TokenUnit("credits"));
            }

            usageInfo.Extra["parse_method"] = "dom";
        }
        catch (Exception ex)
        {
            LogError("ParseUsagePageAsync 异常", ex);
            usageInfo.IsSuccess = false;
            usageInfo.ErrorMessage = ex.Message;
        }

        return usageInfo;
    }

    /// <summary>
    /// req-087-002：提取主指标（顶部 Credits 剩余/总额度）。
    /// <para>选择器策略优先级（从高到低）：</para>
    /// <list type="number">
    ///   <item><description>data-testid / aria-label 含 credit / balance 的元素（最稳定，受 class 改名影响小）</description></item>
    ///   <item><description>class 含 credit / balance / quota / remaining / total 的元素</description></item>
    ///   <item><description>页面其它位置包含大数字的元素（兑底）</description></item>
    /// </list>
    /// <para>TODO: 待王晨提供 qoder.com/account/usage 登录后页面截图后进一步收窄选择器范围。</para>
    /// </summary>
    private async Task<QoderMainMetric?> ExtractMainMetricAsync(IPage page)
    {
        try
        {
            // 等待主指标区域加载（宽松 fallback —— 任何一个候选选择器出现即可）
            await page.WaitForSelectorAsync(
                "[class*='credit'], [class*='balance'], [class*='quota'], [class*='usage'], [data-testid*='credit'], [data-testid*='balance']",
                new PageWaitForSelectorOptions { Timeout = 10000 });

            // 尝试多种选择器策略（按优先级）
            var metricText = await page.EvaluateAsync<string?>(@"() => {
                // req-087-002：分层策略，优先带语义属性的元素。
                const strategies = [
                    '[data-testid*=""credit"" i]',
                    '[data-testid*=""balance"" i]',
                    '[data-testid*=""quota"" i]',
                    '[aria-label*=""credit"" i]',
                    '[aria-label*=""balance"" i]',
                    '[aria-label*=""剩余""]',
                    '[aria-label*=""可用""]',
                    '[class*=""credit"" i]',
                    '[class*=""balance"" i]',
                    '[class*=""quota"" i]',
                    '[class*=""remaining"" i]',
                    '[class*=""total"" i]',
                    '[class*=""usage"" i]'
                ];

                for (const sel of strategies) {
                    const els = document.querySelectorAll(sel);
                    for (const el of els) {
                        const text = (el.textContent || '').trim();
                        if (text.length === 0 || text.length > 200) continue;
                        if (/\d/.test(text)) return text;
                    }
                }
                return null;
            }");

            if (string.IsNullOrWhiteSpace(metricText))
            {
                LogWarn("未找到主指标元素（所有 fallback 选择器均未命中）");
                return null;
            }

            LogInfo($"主指标原始文本: {metricText}");
            return ParseMainMetricText(metricText);
        }
        catch (Exception ex)
        {
            LogWarn($"提取主指标失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// req-087-002：从主指标原始文本解析 (Remaining, Total, Used)。
    /// <para>支持顺序：</para>
    /// <list type="number">
    ///   <item><description>"X / Y" 格式 → Remaining=X, Total=Y</description></item>
    ///   <item><description>单个数字（含千分位、K/M 后缀）→ Remaining=Total=值</description></item>
    ///   <item><description>"剩余 X 总计 Y" 等自然语言 → 正则分别匹配剩余 / 总额</description></item>
    /// </list>
    /// </summary>
    private QoderMainMetric? ParseMainMetricText(string text)
    {
        var metric = new QoderMainMetric();

        // 策略 1："X / Y" 斜杠分割
        var slashMatch = _slashMetricRegex.Match(text);
        if (slashMatch.Success)
        {
            metric.Remaining = ParseNumericToken(slashMatch.Groups[1].Value);
            metric.Total = ParseNumericToken(slashMatch.Groups[2].Value);
            metric.Used = Math.Max(0, metric.Total - metric.Remaining);
            return metric;
        }

        // 策略 2："剩余 X 总额 Y" / "Remaining X Total Y" 自然语言
        var remainingMatch = _remainingMetricRegex.Match(text);
        var totalMatch = _totalMetricRegex.Match(text);
        if (remainingMatch.Success || totalMatch.Success)
        {
            if (remainingMatch.Success) metric.Remaining = ParseNumericToken(remainingMatch.Groups[1].Value);
            if (totalMatch.Success) metric.Total = ParseNumericToken(totalMatch.Groups[1].Value);
            if (metric.Total == 0) metric.Total = metric.Remaining;
            metric.Used = Math.Max(0, metric.Total - metric.Remaining);
            return metric;
        }

        // 策略 3：单个数字（含千分位、K/M 后缀）
        var singleMatch = _singleNumberRegex.Match(text);
        if (singleMatch.Success)
        {
            var value = ParseNumericToken(singleMatch.Groups[1].Value);
            metric.Remaining = value;
            metric.Total = value;
            metric.Used = 0;
            return metric;
        }

        return null;
    }

    /// <summary>
    /// req-087-002：解析带千分位 / K/M 后缀的数字字符串为 decimal。
    /// </summary>
    private static decimal ParseNumericToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var s = raw.Trim().ToUpperInvariant().Replace(",", "");
        decimal multiplier = 1;
        if (s.EndsWith("K")) { multiplier = 1_000m; s = s[..^1]; }
        else if (s.EndsWith("M")) { multiplier = 1_000_000m; s = s[..^1]; }
        if (decimal.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v * multiplier;
        return 0;
    }

    /// <summary>
    /// 提取动态表格数据（7 字段）。
    /// </summary>
    private async Task<List<QoderUsageRecord>> ExtractTableDataAsync(IPage page)
    {
        var records = new List<QoderUsageRecord>();

        try
        {
            await page.WaitForSelectorAsync("table tbody tr, [class*='table'] [class*='row'], [class*='list'] [class*='item']",
                new PageWaitForSelectorOptions { Timeout = 10000 });

            var rowsData = await page.EvaluateAsync<List<List<string>>>(@"() => {
                const results = [];

                const tableRows = document.querySelectorAll('table tbody tr');
                if (tableRows.length > 0) {
                    for (const row of tableRows) {
                        const cells = Array.from(row.querySelectorAll('td')).map(td => td.textContent?.trim() || '');
                        if (cells.length >= 7) {
                            results.push(cells);
                        }
                    }
                    return results;
                }

                const divRows = document.querySelectorAll('[class*=""table""] [class*=""row""], [class*=""list""] [class*=""item""]');
                for (const row of divRows) {
                    const cells = Array.from(row.querySelectorAll('[class*=""cell""], [class*=""col""], [class*=""field""]'))
                        .map(td => td.textContent?.trim() || '');
                    if (cells.length >= 7) {
                        results.push(cells);
                    }
                }

                return results;
            }");

            if (rowsData == null || rowsData.Count == 0)
            {
                LogWarn("未找到表格数据行");
                return records;
            }

            LogInfo($"找到 {rowsData.Count} 行表格数据");

            foreach (var cells in rowsData)
            {
                var record = QoderTableParser.ParseRow(cells.ToArray());
                if (record != null)
                {
                    records.Add(record);
                }
            }

            LogInfo($"成功解析 {records.Count} 行记录");
        }
        catch (Exception ex)
        {
            LogWarn($"提取表格数据失败: {ex.Message}");
        }

        return records;
    }
}

/// <summary>
/// Qoder 主指标数据。
/// </summary>
internal class QoderMainMetric
{
    /// <summary>剩余 Credits</summary>
    public decimal Remaining { get; set; }

    /// <summary>总额度 Credits</summary>
    public decimal Total { get; set; }

    /// <summary>已使用 Credits</summary>
    public decimal Used { get; set; }
}
