namespace UsageMonitor.Core.Models;

/// <summary>
/// 热力图色阶档位的持久化模型（req-009）。
/// <para>
/// 与运行时 <c>HeatMapTier</c>（App 项目）一一对应，但持有可被 <c>System.Text.Json</c> 序列化的
/// 原始字段（<see cref="MinTokens"/> / <see cref="ColorHex"/> / <see cref="IsEnabled"/>），
/// 避免在 config.json 中存 WPF <c>Color</c> 结构。Core 项目不引用 App，由 App 端的
/// <c>HeatMapTierScale.ApplyConfig</c> 负责把 <see cref="HeatMapTierConfig"/> 列表转换为
/// 运行时 <c>HeatMapTier</c> 列表。
/// </para>
/// <para>
/// 阈值语义：单元格 token &gt;= <see cref="MinTokens"/> 时命中该档。多个档位的 MinTokens 应升序。
/// 0 档的 MinTokens 通常为 0，作为兜底颜色。
/// </para>
/// </summary>
public sealed class HeatMapTierConfig
{
    /// <summary>档位下界（包含），单位 tokens。0 档通常为 0 表示兜底色。</summary>
    public long MinTokens { get; set; }

    /// <summary>档位颜色（hex 字符串，如 "#f3f4f6" / "#ffa595"），WPF 端用 BrushConverter 解析。</summary>
    public string ColorHex { get; set; } = "#f3f4f6";

    /// <summary>档位是否启用；false 时该档在 ResolveBrush 中被跳过（不参与命中）。</summary>
    public bool IsEnabled { get; set; } = true;
}
