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
    protected override string LoginUrl => "https://www.qoder.com/";

    /// <inheritdoc />
    protected override string UsageUrl => "https://www.qoder.com/console/usage";

    /// <inheritdoc />
    protected override string[] CookieDomainFilters => new[] { ".qoder.com", "www.qoder.com" };

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
                        if (/\\d+\\.?\\d*/.test(text) && text.length < 100) {
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
                
                // 策略2：div 模拟表格结构
                const divRows = document.querySelectorAll('[class*=""table""] [class*='row''], [class*=""list""] [class*='item'']');
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
