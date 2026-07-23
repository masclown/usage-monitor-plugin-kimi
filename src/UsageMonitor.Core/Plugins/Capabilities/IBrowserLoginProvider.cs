namespace UsageMonitor.Core.Plugins;

/// <summary>
/// req-069 F-08：可选能力接口——浏览器登录（Cookie 鉴权）。
/// <para>req-096：插件声明鉴权方式（Cookie vs API Key）。实现此接口的插件表示需要通过临时 Edge 窗口获取登录态 Cookie。
/// 宿主（<c>App/PluginConfigWindow</c>）用 <c>provider is IBrowserLoginProvider</c> 模式匹配检查能力，按需显示"获取登录态"按钮。</para>
/// <para>req-107 渐进拆分（F-08 ISP 原则第一步）：<see cref="IUsageProvider"/> 继承此接口，<see cref="LoginConfig"/> 成员由本接口承载。
/// 旧插件可继续仅实现 <see cref="IUsageProvider"/>（继承得到默认 <c>null</c> 实现）；新插件可显式实现本接口增强能力表达。</para>
/// </summary>
public interface IBrowserLoginProvider
{
    /// <summary>
    /// 浏览器登录配置（req-096）——声明此插件是否需要通过临时 Edge 窗口获取登录态 Cookie。
    /// <para>返回 <c>null</c> 表示无需浏览器登录（如纯 API Key 鉴权）。
    /// 设置界面会据此自动显示"🌐 获取登录态"按钮，调用
    /// <see cref="Services.BrowserLoginService"/> 启动临时 Edge 窗口并提取 Cookie。</para>
    /// <para>设计参考：销项数据助手项目的 <c>browser-cookie-manager</c> Skill 采用的通用 Cookie 获取方案；
    /// 本项目在此基础上用 Edge + CDP 替代 Playwright，降低外部依赖。</para>
    /// <para>req-096：此属性已被 <see cref="SupportedAuthKinds"/> 取代，保留仅为向后兼容。
    /// 新插件应实现 <see cref="SupportedAuthKinds"/> 而非此属性。</para>
    /// </summary>
    [System.Obsolete("请使用 SupportedAuthKinds 属性声明鉴权方式，此属性将在未来版本移除")]
    Models.BrowserLoginConfig? LoginConfig => null;
}
