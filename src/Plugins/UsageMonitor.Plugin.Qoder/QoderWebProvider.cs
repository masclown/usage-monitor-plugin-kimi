using Microsoft.Playwright;
using System.Net.Http;
using System.Text.Json;
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
    /// <para>
    /// 关键修复点（req-fix-Qoder-B2-ValidateUrl，基于用户 2026-07-21 反馈的实际登录后 URL）：
    /// <list type="number">
    ///   <item><description><c>ValidateUrl = "https://qoder.com/account/usage"</c> —— 用户实际登录后
    ///   跳转到 <c>/account/usage</c> 路径（裸域），不再是写死的 <c>/console/usage</c>。</description></item>
    ///   <item><description><c>LoginUrl = "https://qoder.com/"</c>（裸域）—— 配合 <c>LoggedInHost = "qoder.com"</c>
    ///   解决 qoder.com ↔ www.qoder.com 跨子域跳转被误判为登录页的问题。</description></item>
    ///   <item><description><c>LoggedInHost = "qoder.com"</c> —— 显式声明已登录 host，覆盖从 LoginUrl
    ///   推断得到的 "www.qoder.com"，避免跨子域跳转被误判为登录页。</description></item>
    ///   <item><description><c>LoginUrlKeywords</c> 显式提供常见登录路径关键字，与 WebPluginBase 默认一致。</description></item>
    /// </list>
    /// </para>
    /// <para>req-087-B2 TODO 已完成：用户提供实际跳转 URL 后精化 ValidateUrl / LoggedInHost，
    /// 完成 BrowserLoginService 登录后强制导航 + IsLoginUrl 判定全链路。</para>
    /// </summary>
    public override BrowserLoginConfig? LoginConfig => new()
    {
        ProviderId = "qoder_web",
        // 裸域 qoder.com/（用户反馈登录后实际从这里开始）
        LoginUrl = LoginUrl,
        CookieDomainFilters = CookieDomainFilters,
        // 用户实际登录后跳转的用量页（裸域 + /account/usage）
        ValidateUrl = UsageUrl,
        // 显式声明已登录 host = qoder.com，覆盖从 LoginUrl 推断得到的 "www.qoder.com"
        LoggedInHost = "qoder.com",
        // 显式提供登录页路径关键字，覆盖 Qoder 实际登录页可能含的路径片段
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
    public override IReadOnlyList<CardChartKind> SupportedCardCharts => new[]
    {
        CardChartKind.Line, CardChartKind.Bar, CardChartKind.Ring, CardChartKind.HeatMap
    };

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedRingChartMetrics => new[] { "Percent", "Usage" };

    /// <summary>
    /// 解析用量页面，提取 Credits 主指标和动态表格数据。
    /// </summary>
    /// <param name="page">已导航到用量页面的 IPage 实例</param>
    /// <returns>解析后的 UsageInfo</returns>
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

                // 转换为图表数据
                var lineData = QoderChartAdapter.ToLineChartData(records);
                var barData = QoderChartAdapter.ToBarChartData(records);
                var heatData = QoderChartAdapter.ToHeatMapData(records);

                usageInfo.Extra["line_chart_data"] = JsonSerializer.Serialize(lineData);
                usageInfo.Extra["bar_chart_data"] = JsonSerializer.Serialize(barData);
                usageInfo.Extra["heat_map_data"] = JsonSerializer.Serialize(heatData);

                // 计算总消耗
                var totalCredits = QoderChartAdapter.CalculateTotalCredits(records);
                var totalCost = QoderChartAdapter.CalculateTotalCost(records);
                usageInfo.Extra["total_credits_consumed"] = totalCredits;
                usageInfo.Extra["total_cost_consumed"] = totalCost;

                // 设置 Quantity（req-086 新字段）
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
    /// 提取主指标（顶部 Credits 剩余/总额度）。
    /// </summary>
    /// <param name="page">IPage 实例</param>
    /// <returns>主指标数据，失败返回 null</returns>
    private async Task<QoderMainMetric?> ExtractMainMetricAsync(IPage page)
    {
        try
        {
            // 等待主指标区域加载
            // TODO: B2 - 待王晨提供页面截图后更新选择器
            await page.WaitForSelectorAsync("[class*='credit'], [class*='balance'], [class*='quota'], [class*='usage']",
                new PageWaitForSelectorOptions { Timeout = 10000 });

            // 尝试多种选择器策略
            var metricText = await page.EvaluateAsync<string?>(@"() => {
                // 策略1：查找包含 'Credits' 或 '剩余' 文本的元素
                const selectors = [
                    '[class*=""credit""]',
                    '[class*=""balance""]',
                    '[class*=""quota""]',
                    '[class*=""usage""]',
                    '[class*=""remaining""]',
                    '[class*=""total""]'
                ];
                
                for (const sel of selectors) {
                    const els = document.querySelectorAll(sel);
                    for (const el of els) {
                        const text = el.textContent || '';
                        // 匹配数字格式：123.45 或 123/456 或 剩余 123
                        // req-fix-Qoder正则转义：C# verbatim string 中 \d 字面传给 JS 正则，匹配数字；
                        // 原错误写法 \\d+\\.?\\d* 在 JS 正则中等价于匹配字面 \d 文本而非数字。
                        if (/\d+\.?\d*/.test(text) && text.length < 100) {
                            return text.trim();
                        }
                    }
                }
                return null;
            }");

            if (string.IsNullOrWhiteSpace(metricText))
            {
                LogWarn("未找到主指标元素");
                return null;
            }

            LogInfo($"主指标原始文本: {metricText}");

            // 解析数字
            var metric = new QoderMainMetric();

            // 尝试匹配 "剩余 X / 总额 Y" 或 "X / Y" 格式
            var slashMatch = System.Text.RegularExpressions.Regex.Match(
                metricText, @"(\d+\.?\d*)\s*/\s*(\d+\.?\d*)");
            if (slashMatch.Success)
            {
                metric.Remaining = decimal.Parse(slashMatch.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                metric.Total = decimal.Parse(slashMatch.Groups[2].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                metric.Used = metric.Total - metric.Remaining;
                return metric;
            }

            // 尝试匹配单个数字
            var singleMatch = System.Text.RegularExpressions.Regex.Match(
                metricText, @"(\d+\.?\d*)");
            if (singleMatch.Success)
            {
                var value = decimal.Parse(singleMatch.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                // 假设是剩余量，总额未知
                metric.Remaining = value;
                metric.Total = value; // 暂时设为相同值
                metric.Used = 0;
                return metric;
            }

            return null;
        }
        catch (Exception ex)
        {
            LogWarn($"提取主指标失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 提取动态表格数据（7 字段）。
    /// </summary>
    /// <param name="page">IPage 实例</param>
    /// <returns>解析后的记录列表</returns>
    private async Task<List<QoderUsageRecord>> ExtractTableDataAsync(IPage page)
    {
        var records = new List<QoderUsageRecord>();

        try
        {
            // 等待表格加载
            // TODO: B2 - 待王晨提供页面截图后更新选择器
            await page.WaitForSelectorAsync("table tbody tr, [class*='table'] [class*='row'], [class*='list'] [class*='item']",
                new PageWaitForSelectorOptions { Timeout = 10000 });

            // 提取表格行数据
            var rowsData = await page.EvaluateAsync<List<List<string>>>(@"() => {
                const results = [];
                
                // 策略1：标准 table 结构
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
                
                // 策略2：div 模拟表格结构（修复引号嵌套错误：原写法 [class*='row''] 多了一个 '，CSS 无效）
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

            // 解析每一行
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
