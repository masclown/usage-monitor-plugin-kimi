using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services.Display;

/// <summary>
/// 显示资源包加载器（req-115）：扫描包根目录下的 &lt;pack&gt;/&lt;清单文件&gt;，反序列化并校验。
/// <para>坏包（JSON 语法错误 / 缺 Id / 字段白名单不通过）跳过并记日志，不影响其他包加载。</para>
/// </summary>
public static class DisplayPackLoader
{
    private const string LogSource = "DisplayPackLoader";

    /// <summary>JSON 反序列化选项（属性名大小写不敏感、允许注释与尾逗号，与插件清单一致）。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>加载主题包目录（themes/&lt;pack&gt;/theme.json）。</summary>
    /// <param name="rootDirectory">主题包根目录。</param>
    public static List<ThemePack> LoadThemePacks(string rootDirectory)
        => LoadPacks<ThemePack>(rootDirectory, "theme.json", ValidateThemePack);

    /// <summary>加载图表样式包目录（charts/&lt;pack&gt;/chartstyle.json）。</summary>
    /// <param name="rootDirectory">图表样式包根目录。</param>
    public static List<ChartStylePack> LoadChartStylePacks(string rootDirectory)
        => LoadPacks<ChartStylePack>(rootDirectory, "chartstyle.json", p => ValidateChartStyles(p.ChartStyles, p.Id));

    /// <summary>加载 mini 图表样式包目录（minicharts/&lt;pack&gt;/ministyle.json）。</summary>
    /// <param name="rootDirectory">mini 图表样式包根目录。</param>
    public static List<MiniChartStylePack> LoadMiniChartStylePacks(string rootDirectory)
        => LoadPacks<MiniChartStylePack>(rootDirectory, "ministyle.json", p => ValidateChartStyles(p.ChartStyles, p.Id));

    /// <summary>加载悬浮窗模板包目录（traytooltips/&lt;pack&gt;/traytooltip.json）。</summary>
    /// <param name="rootDirectory">悬浮窗模板包根目录。</param>
    public static List<TrayTooltipPack> LoadTrayTooltipPacks(string rootDirectory)
        => LoadPacks<TrayTooltipPack>(rootDirectory, "traytooltip.json", ValidateTrayTooltipPack);

    /// <summary>
    /// 通用包扫描：遍历 &lt;root&gt;/&lt;包目录&gt;/&lt;清单文件&gt;，反序列化 + 基础校验 + 类型级校验。
    /// </summary>
    /// <typeparam name="T">包类型。</typeparam>
    /// <param name="rootDirectory">包根目录（不存在时返回空列表）。</param>
    /// <param name="manifestFileName">包清单文件名。</param>
    /// <param name="validate">类型级校验（返回 false 时整包跳过）。</param>
    private static List<T> LoadPacks<T>(string rootDirectory, string manifestFileName, Func<T, bool> validate)
        where T : DisplayPackBase
    {
        var packs = new List<T>();
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return packs;

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(rootDirectory))
        {
            var path = Path.Combine(dir, manifestFileName);
            if (!File.Exists(path)) continue;
            try
            {
                var pack = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
                if (pack == null || string.IsNullOrWhiteSpace(pack.Id))
                {
                    FileLogger.Warn(LogSource, $"包缺少 id，跳过: {path}");
                    continue;
                }
                if (!seenIds.Add(pack.Id))
                {
                    FileLogger.Warn(LogSource, $"包 id 重复（{pack.Id}），跳过: {path}");
                    continue;
                }
                if (!validate(pack)) continue;

                pack.PackDirectory = dir;
                packs.Add(pack);
                FileLogger.Info(LogSource, $"已加载显示资源包: {pack.Id} ({manifestFileName}) from {Path.GetFileName(dir)}");
            }
            catch (Exception ex)
            {
                FileLogger.Warn(LogSource, $"包解析失败，跳过: {path} - {ex.Message}");
            }
        }
        return packs;
    }

    /// <summary>主题包校验：tokens 非空。</summary>
    /// <param name="pack">主题包。</param>
    private static bool ValidateThemePack(ThemePack pack)
    {
        if (pack.Tokens.Count == 0)
        {
            FileLogger.Warn(LogSource, $"主题包 {pack.Id} 无任何 token，跳过");
            return false;
        }
        return true;
    }

    /// <summary>图表样式校验：thresholds 与 colors 等长（不等长的条目丢弃色阶保留参数）。</summary>
    /// <param name="styles">图表样式字典。</param>
    /// <param name="packId">包 Id（日志用）。</param>
    private static bool ValidateChartStyles(Dictionary<string, ChartStyleEntry> styles, string? packId)
    {
        foreach (var (kind, entry) in styles)
        {
            if (entry.Thresholds.Count != entry.Colors.Count && entry.Colors.Count > 0)
            {
                FileLogger.Warn(LogSource,
                    $"样式包 {packId} 图表 {kind} 色阶阈值数({entry.Thresholds.Count})与颜色数({entry.Colors.Count})不一致，该项色阶忽略");
                entry.Thresholds.Clear();
                entry.Colors.Clear();
            }
        }
        return true;
    }

    /// <summary>
    /// 悬浮窗模板包校验：行必须声明 fieldName 或 textTemplate 之一；
    /// fieldName 走 SDK 字段白名单（虚拟字段 __xxx__ 放行），非法行剔除。
    /// </summary>
    /// <param name="pack">悬浮窗模板包。</param>
    private static bool ValidateTrayTooltipPack(TrayTooltipPack pack)
    {
        var validRows = new List<TrayTooltipRow>();
        foreach (var row in pack.Rows)
        {
            if (!string.IsNullOrWhiteSpace(row.FieldName))
            {
                var f = row.FieldName!;
                var isVirtual = f.StartsWith("__", StringComparison.OrdinalIgnoreCase)
                                && f.EndsWith("__", StringComparison.OrdinalIgnoreCase);
                if (!isVirtual && !UsageFieldMetadataRegistry.IsRegistered(f))
                {
                    FileLogger.Warn(LogSource, $"悬浮窗模板包 {pack.Id} 行字段 {f} 非 SDK 合法字段，该行剔除");
                    continue;
                }
                validRows.Add(row);
            }
            else if (!string.IsNullOrEmpty(row.TextTemplate))
            {
                validRows.Add(row);
            }
            // fieldName 与 textTemplate 均缺失的空行直接丢弃
        }

        if (validRows.Count == 0)
        {
            FileLogger.Warn(LogSource, $"悬浮窗模板包 {pack.Id} 无任何有效行，跳过");
            return false;
        }
        pack.Rows = validRows;
        return true;
    }
}
