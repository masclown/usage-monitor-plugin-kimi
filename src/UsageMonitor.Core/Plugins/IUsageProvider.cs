using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Plugins;

/// <summary>
/// AI用量提供者插件接口 - 所有服务商插件必须实现此接口
/// 定义了插件的基本信息、配置项和用量查询能力
/// </summary>
public interface IUsageProvider
{
    /// <summary>服务商唯一标识（如 "deepseek"、"mimo"）</summary>
    string ProviderId { get; }

    /// <summary>服务商显示名称（如 "Deepseek"）</summary>
    string DisplayName { get; }

    /// <summary>服务商图标路径（支持 pack:// URI 或文件路径）</summary>
    string? IconPath { get; }

    /// <summary>插件版本</summary>
    string Version { get; }

    /// <summary>插件作者</summary>
    string Author { get; }

    /// <summary>插件描述</summary>
    string Description { get; }

    /// <summary>
    /// 配置项定义列表 - 定义插件需要的配置字段（如API Key等）
    /// 设置界面会根据此列表自动生成对应的输入控件
    /// </summary>
    IReadOnlyList<ConfigField> ConfigFields { get; }

    /// <summary>
    /// 可选的浏览器登录配置 - 声明此插件是否需要通过临时 Edge 窗口获取登录态 Cookie。
    /// <para>
    /// 返回 <c>null</c> 表示此插件无需浏览器登录（如纯 API Key 鉴权）。
    /// 设置界面会据此自动显示"🌐 获取登录态"按钮，调用
    /// <see cref="Services.BrowserLoginService"/> 启动临时 Edge 窗口并提取 Cookie。
    /// </para>
    /// <para>
    /// 设计参考：销项数据助手项目的 <c>browser-cookie-manager</c> Skill 采用的通用 Cookie
    /// 获取方案；本项目在此基础上用 Edge + CDP 替代 Playwright，降低外部依赖。
    /// </para>
    /// </summary>
    Models.BrowserLoginConfig? LoginConfig => null;

    /// <summary>
    /// 查询当前用量信息
    /// </summary>
    /// <param name="config">服务商配置（包含API Key等信息）</param>
    /// <returns>用量信息</returns>
    Task<UsageInfo> GetUsageAsync(ProviderConfig config);

    /// <summary>
    /// 验证配置是否有效（如API Key是否正确）
    /// </summary>
    /// <param name="config">待验证的配置</param>
    /// <returns>配置是否有效</returns>
    Task<bool> ValidateConfigAsync(ProviderConfig config);
}
