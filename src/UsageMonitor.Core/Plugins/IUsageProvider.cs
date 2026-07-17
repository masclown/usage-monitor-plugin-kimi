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
    /// 插件声明的默认渲染能力集合（在首次加载、未收到任何刷新数据前生效）。
    /// <para>
    /// 卡片可见性绑定 "插件声明 render_kind" 与 "用户开关"。如果插件未实现，
    /// 主窗口会在第一次渲染时就折叠对应区块，导致"数据未到则卡片残缺"。
    /// 由插件自己声明一组最常声明的能力，主窗口装配 VM 时立即写入 RenderKinds，
    /// 让首屏显示与数据到位后的显示保持一致；运行时真正收到的 mm_render_kinds
    /// 仍会通过 <c>UpdateFromMiniMaxDom</c> 等覆盖。
    /// </para>
    /// </summary>
    IReadOnlyList<string> DefaultRenderKinds => System.Array.Empty<string>();

    /// <summary>
    /// 插件声明「支持在主窗口卡片中展示的图表类型」集合，供插件配置窗口生成复选框。
    /// <para>
    /// 这是「不同插件支持不同图表」框架的核心契约：配置窗口只为本集合内的图表类型
    /// 生成勾选项，用户可多选；用户的多选结果随 <c>AppSettings.ProviderCardChartKinds</c>
    /// （key = ProviderId）持久化，主窗口卡片按选择叠加展示对应图表。
    /// </para>
    /// <para>
    /// 默认返回通用三件套 <see cref="Models.CardChartKind.Line"/> / <see cref="Models.CardChartKind.Bar"/> /
    /// <see cref="Models.CardChartKind.Ring"/>（数据源为应用统一记录的历史用量百分比与当前已用百分比，
    /// 任何插件都适用）。有专属数据的插件应覆盖此属性，例如 MiniMax 覆盖为折线图 + 热力图。
    /// 返回空集合表示该插件不提供任何卡片图表，配置窗口将隐藏「卡片图表」分组。
    /// </para>
    /// </summary>
    IReadOnlyList<Models.CardChartKind> SupportedCardCharts => new[]
    {
        Models.CardChartKind.Line,
        Models.CardChartKind.Bar,
        Models.CardChartKind.Ring
    };

    /// <summary>
    /// 插件可注册的自定义图表工厂集合（REQ-005 SDK）。
    /// <para>
    /// 默认返回空数组表示只使用内置 5 种图表（折线 / 柱状 / 圆环 / 热力 / 时段）。
    /// 插件可在自己的实现里覆盖此属性，把自研图表通过 <see cref="IUsageChartFactory.Create"/>
    /// 提供给宿主；宿主在装配 Provider 时把这些工厂合并到全局图表注册表中，
    /// 第三方控件即可在 <c>MainWindow.xaml</c> 通过 <see cref="Models.IChartData.Kind"/>
    /// 自动路由到对应模板，无需修改主窗口代码。
    /// </para>
    /// <para>
    /// 设计参考：这是把「数据契约」与「UI 控件契约」解耦的关键位——插件只需声明「我能产哪种 IChartData」，
    /// 宿主用 chart-data.Kind 路由到对应 IUsageChartFactory.Create 出的实例来展示。
    /// </para>
    /// </summary>
    IReadOnlyList<IUsageChartFactory> ChartFactories => System.Array.Empty<IUsageChartFactory>();

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
