using System;
using System.Collections.Generic;
using System.Linq;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// 插件校验结果（req-107 B9）。
/// </summary>
public sealed class PluginValidationResult
{
    /// <summary>错误（阻断加载）。</summary>
    public List<string> Errors { get; } = new();

    /// <summary>警告（不阻断，提示性）。</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>是否通过（无错误）。</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// 生成可读校验报告（--validate-plugin 命令行与设置界面共用）。
    /// </summary>
    public string ToReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(IsValid ? "✅ 校验通过" : "❌ 校验未通过");
        foreach (var error in Errors) sb.AppendLine("  [ERROR] " + error);
        foreach (var warning in Warnings) sb.AppendLine("  [WARN]  " + warning);
        return sb.ToString();
    }
}

/// <summary>
/// 插件声明校验引擎（req-107 B9）：主程序当校验器。
/// <para>运行期加载与 <c>UsageMonitor.exe --validate-plugin</c> / 设置界面"校验插件"按钮共用同一份代码。
/// 校验项：JSON 语法 / 字段名白名单（<see cref="UsageFieldMetadataRegistry"/>）/ ChartKind 合法 /
/// ChartKindSpec 约束（<see cref="ChartKindSpecRegistry.Validate"/>）/ slicer 模式支持 / minSdkVersion 兼容。</para>
/// </summary>
public static class PluginValidator
{
    /// <summary>
    /// 校验 defaults.json 文本：先解析 JSON 语法，再校验声明语义。
    /// </summary>
    /// <param name="defaultsJson">defaults.json 内容。</param>
    /// <param name="currentSdkVersion">当前 SDK 版本（用于 minSdkVersion 兼容校验）。</param>
    public static PluginValidationResult Validate(string defaultsJson, Version? currentSdkVersion = null)
    {
        var result = new PluginValidationResult();

        PluginManifest? manifest;
        try
        {
            manifest = PluginManifest.Load(defaultsJson);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"JSON 语法错误：{ex.Message}");
            return result;
        }

        if (manifest == null)
        {
            result.Errors.Add("defaults.json 为空或无法解析为 PluginManifest");
            return result;
        }

        ValidateManifest(manifest, currentSdkVersion, result);
        return result;
    }

    /// <summary>
    /// 校验已解析的插件清单（运行期加载复用）。
    /// </summary>
    public static void ValidateManifest(PluginManifest manifest, Version? currentSdkVersion, PluginValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(manifest.ProviderId))
            result.Errors.Add("缺少 providerId");

        // minSdkVersion 兼容校验
        if (!string.IsNullOrWhiteSpace(manifest.MinSdkVersion) && currentSdkVersion != null)
        {
            if (Version.TryParse(manifest.MinSdkVersion, out var required))
            {
                if (required > currentSdkVersion)
                    result.Warnings.Add($"插件要求 SDK {manifest.MinSdkVersion}，当前 {currentSdkVersion}，可能不兼容");
            }
            else
            {
                result.Warnings.Add($"minSdkVersion 格式无效：{manifest.MinSdkVersion}");
            }
        }

        if (manifest.Card != null) ValidateCard(manifest.Card, result);
        if (manifest.Taskbar != null) ValidateTaskbar(manifest.Taskbar, result);
        ValidateAccessSections(manifest, result);
    }

    /// <summary>
    /// Stage A：校验声明包新增节（configFields / errorGuidance / trayTooltip / fetch http 端点 / refresh）。
    /// </summary>
    private static void ValidateAccessSections(PluginManifest manifest, PluginValidationResult result)
    {
        // configFields：键非空且唯一（大小写不敏感）
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in manifest.ConfigFields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                result.Errors.Add("configFields 存在空 Key 的字段");
                continue;
            }
            if (!seenKeys.Add(field.Key))
                result.Errors.Add($"configFields 字段 Key 重复：{field.Key}");
        }

        // errorGuidance：文案必填；兑底规则（空关键字）至多一条
        var fallbackCount = 0;
        foreach (var rule in manifest.ErrorGuidance)
        {
            if (string.IsNullOrWhiteSpace(rule.Message))
                result.Errors.Add("errorGuidance 存在空 Message 的规则");
            if (rule.MatchKeywords.Count == 0) fallbackCount++;
        }
        if (fallbackCount > 1)
            result.Warnings.Add($"errorGuidance 有 {fallbackCount} 条兑底规则（空关键字），仅首条生效");

        // trayTooltip：字段白名单校验
        if (manifest.TrayTooltip != null)
        {
            foreach (var fieldName in manifest.TrayTooltip.Fields)
                EnsureField(fieldName, "trayTooltip", result);
        }

        // fetch http 端点：UrlTemplate 必填且必须 https；Method 限 GET/POST；真实 URL 展开后运行期另经 req-056 SSRF 校验
        if (manifest.Fetch != null)
        {
            foreach (var ep in manifest.Fetch.Endpoints)
            {
                if (string.Equals(ep.Mode, "http", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(ep.UrlTemplate))
                        result.Errors.Add("fetch 存在 http 端点缺少 urlTemplate");
                    else if (!ep.UrlTemplate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        result.Errors.Add($"http 端点 urlTemplate 必须为 https：{ep.UrlTemplate}");
                    if (!string.Equals(ep.Method, "GET", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(ep.Method, "POST", StringComparison.OrdinalIgnoreCase))
                        result.Errors.Add($"http 端点 method 仅支持 GET/POST：{ep.Method}");
                }
                else if (string.IsNullOrWhiteSpace(ep.UrlMatch))
                {
                    result.Warnings.Add("fetch 存在 capture 端点缺少 urlMatch（将无法命中任何响应）");
                }
            }
        }

        // refresh：间隔关系合理性
        if (manifest.Refresh != null
            && (manifest.Refresh.MinIntervalSeconds > manifest.Refresh.DefaultIntervalSeconds
                || manifest.Refresh.DefaultIntervalSeconds > manifest.Refresh.MaxIntervalSeconds))
        {
            result.Warnings.Add("refresh 间隔应满足 Min ≤ Default ≤ Max，当前声明不满足（运行期将被钳制）");
        }
    }

    /// <summary>校验卡片声明：基础信息字段白名单 + 各图表 ChartKindSpec 约束 + Tooltip 字段白名单。</summary>
    private static void ValidateCard(CardDeclaration card, PluginValidationResult result)
    {
        if (card.BaseInfo != null)
        {
            foreach (var field in card.BaseInfo.Fields)
                EnsureField(field.FieldName, "card.baseInfo", result);
        }

        foreach (var chart in card.Charts)
        {
            if (string.IsNullOrWhiteSpace(chart.ChartId))
                result.Errors.Add("card 存在缺少 chartId 的图表");

            // ChartKindSpec 约束（含 slicer 模式支持 / 字段角色 / 数据类型 / 色阶支持）
            foreach (var error in ChartKindSpecRegistry.Validate(chart))
                result.Errors.Add(error);

            // 数据组字段白名单
            foreach (var group in chart.DataGroups)
            {
                foreach (var field in group.Fields)
                    EnsureField(field.FieldName, $"chart {chart.ChartId} group {group.Id}", result);
            }

            // Tooltip 字段白名单
            if (chart.Tooltip != null)
            {
                foreach (var fieldName in chart.Tooltip.Fields)
                    EnsureField(fieldName, $"chart {chart.ChartId} tooltip", result);
            }

            // 色阶内联一致性
            if (chart.ColorTiers != null
                && chart.ColorTiers.Ref == null
                && chart.ColorTiers.Thresholds.Count > 0
                && chart.ColorTiers.Colors.Count > 0
                && chart.ColorTiers.Colors.Count != chart.ColorTiers.Thresholds.Count)
            {
                result.Warnings.Add($"chart {chart.ChartId}：色阶阈值数({chart.ColorTiers.Thresholds.Count})与颜色数({chart.ColorTiers.Colors.Count})不一致");
            }
        }
    }

    /// <summary>校验任务栏声明：迷你图表字段白名单 + 基础信息字段白名单。</summary>
    private static void ValidateTaskbar(TaskbarDeclaration taskbar, PluginValidationResult result)
    {
        if (taskbar.BaseInfo != null)
        {
            foreach (var field in taskbar.BaseInfo.Fields)
                EnsureField(field.FieldName, "taskbar.baseInfo", result);
        }

        foreach (var mini in taskbar.MiniCharts)
        {
            if (string.IsNullOrWhiteSpace(mini.ChartId))
                result.Errors.Add("taskbar 存在缺少 chartId 的迷你图表");

            foreach (var group in mini.DataGroups)
            {
                foreach (var field in group.Fields)
                    EnsureField(field.FieldName, $"miniChart {mini.ChartId} group {group.Id}", result);
            }
        }
    }

    /// <summary>字段白名单校验：字段名必须是已注册的 SDK 合法字段。</summary>
    private static void EnsureField(string? fieldName, string context, PluginValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            result.Errors.Add($"{context}：存在空字段名");
            return;
        }
        if (!UsageFieldMetadataRegistry.IsRegistered(fieldName))
            result.Errors.Add($"{context}：字段 {fieldName} 非 SDK 合法字段（白名单校验失败）");
    }
}
