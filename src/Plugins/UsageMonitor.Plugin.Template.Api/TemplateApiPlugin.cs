using UsageMonitor.Core.Models;
using UsageMonitor.Core.Plugins;

namespace UsageMonitor.Plugin.Template.Api;

/// <summary>
/// req-086-3.5：API 插件模板——展示如何基于 <see cref="PluginBase"/> 快速开发一个 API 插件。
/// <para>
/// 复制此项目并重命名，修改 <see cref="ProviderId"/> / <see cref="DisplayName"/> /
/// <see cref="GetUsageAsync"/> 中的 API 调用逻辑即可完成一个新 API 插件。
/// </para>
/// </summary>
public class TemplateApiPlugin : PluginBase, IUsageProvider
{
    // ===================== 基本信息（必须修改） =====================

    /// <inheritdoc />
    public override string ProviderId => "template-api";

    /// <inheritdoc />
    public override string DisplayName => "Template API";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "Your Name";

    /// <inheritdoc />
    public string Description => "API 插件模板：展示如何基于 PluginBase 开发新插件";

    /// <inheritdoc />
    public string? IconPath => null; // 可选：设置图标路径

    // ===================== 配置字段（按需修改） =====================

    /// <inheritdoc />
    public IReadOnlyList<ConfigField> ConfigFields => new[]
    {
        StandardConfigFields.ApiKey(ProviderId),
        StandardConfigFields.BaseUrl(ProviderId, "https://api.example.com/v1"),
    };

    // ===================== 核心 API 调用逻辑（必须实现） =====================

    /// <summary>
    /// 查询当前用量信息（必须实现）。
    /// <para>
    /// 这是 API 插件的核心：调用服务商的 REST API，解析响应并填充到 <see cref="UsageInfo"/>。
    /// </para>
    /// </summary>
    /// <param name="config">服务商配置（包含 ApiKey 等信息）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用量信息</returns>
    public async Task<UsageInfo> GetUsageAsync(ProviderConfig config, CancellationToken ct = default)
    {
        try
        {
            var apiKey = config.GetValue("ApiKey")?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return UsageInfo.CreateError(ProviderId, DisplayName,
                    UsageError.Auth("未配置 ApiKey，请在设置中填写"));
            }

            var baseUrl = config.GetValue("BaseUrl")?.Trim() ?? "https://api.example.com/v1";

            // 示例：调用 API 获取用量数据
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var response = await http.GetAsync($"{baseUrl}/usage", ct);
            if (!response.IsSuccessStatusCode)
            {
                return UsageInfo.CreateError(ProviderId, DisplayName,
                    UsageError.Network($"API 请求失败: {response.StatusCode}", (int)response.StatusCode));
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            // 解析 JSON 响应（根据实际 API 格式调整）
            // var data = JsonSerializer.Deserialize<UsageResponse>(json);

            // 示例：使用 req-086-3.4 新字段 Quantity 表示用量
            return new UsageInfo
            {
                ProviderId = ProviderId,
                ProviderName = DisplayName,
                IsSuccess = true,
                Quantity = new Quantity(0, new CurrencyUnit("USD")),
                // 兼容旧字段（可选，但建议同时写入以支持旧版主窗口）
                UsedAmount = 0,
                TotalAmount = 100,
                Unit = "USD",
                LastUpdated = DateTime.Now,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return UsageInfo.CreateError(ProviderId, DisplayName, "用户取消");
        }
        catch (Exception ex)
        {
            LogError("GetUsageAsync 异常", ex);
            return UsageInfo.CreateError(ProviderId, DisplayName,
                UsageError.Unknown($"查询异常: {ex.Message}"));
        }
    }

    /// <summary>
    /// 验证配置是否有效（必须实现）。
    /// </summary>
    public async Task<bool> ValidateConfigAsync(ProviderConfig config, CancellationToken ct = default)
    {
        var result = await GetUsageAsync(config, ct);
        return result.IsSuccess;
    }

    // ===================== 可选：图表注册 =====================

    /// <inheritdoc />
    public IReadOnlyList<CardChartKind> SupportedCardCharts => new[]
    {
        CardChartKind.Line, CardChartKind.Bar, CardChartKind.Ring
    };

    // ===================== 可选：生命周期钩子 =====================

    /// <inheritdoc />
    public override async Task InitializeAsync(PluginContext context)
    {
        await base.InitializeAsync(context);
        LogInfo("TemplateApiPlugin 初始化完成");
    }

    /// <inheritdoc />
    public override async Task StartAsync()
    {
        await base.StartAsync();
        LogInfo("TemplateApiPlugin 启动完成");
    }

    /// <inheritdoc />
    public override async Task StopAsync()
    {
        await base.StopAsync();
        LogInfo("TemplateApiPlugin 停止完成");
    }
}
