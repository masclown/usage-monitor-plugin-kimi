using System;
using System.Collections.Generic;
using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Modules;

/// <summary>
/// req-099 B1：显示模块契约。定义"把插件用量数据渲染到卡片"这一显示关注点的 WPF-无关操作，
/// 使主程序（<c>MainViewModel</c>）的卡片装配 / 渲染路由逻辑与具体 ViewModel 实现解耦。
/// <para>
/// 设计说明：卡片 ViewModel（<c>ProviderUsageViewModel</c>）依赖 WPF 类型（ObservableCollection /
/// Brush / ImageSource 等），而 Core 项目不引用 WPF，故本接口只暴露 WPF-无关的操作，
/// 具体实现 <c>UsageMonitor.App.Services.Display.DisplayModule</c> 放在 App 层（与
/// <c>App/Charts/ChartFactories.cs</c> 同策略）。宿主通过本接口驱动显示，实现"固定接口通信"。
/// </para>
/// </summary>
public interface IDisplayModule
{
    /// <summary>
    /// 已启用卡片集合发生变化时触发（新增 / 移除卡片、重排后），
    /// 供宿主刷新空状态（IsEmpty）等派生属性。
    /// </summary>
    event EventHandler? EnabledCardsChanged;

    /// <summary>
    /// 按插件管理器中已加载的插件一次性装配全部卡片 ViewModel 与插件列表项，
    /// 并构建首屏"已启用"过滤集合。仅应在初始化时调用一次。
    /// </summary>
    void Build();

    /// <summary>
    /// 把一次刷新返回的用量数据渲染到对应卡片（按 <see cref="UsageInfo.ProviderId"/> 路由）。
    /// 找不到对应卡片时静默忽略。
    /// </summary>
    /// <param name="data">刷新得到的用量信息。</param>
    void RenderCard(UsageInfo data);

    /// <summary>
    /// 批量渲染多个 Provider 的用量数据（逐个调用 <see cref="RenderCard"/>）。
    /// </summary>
    /// <param name="usages">本轮刷新得到的全部用量信息。</param>
    void RenderCards(IReadOnlyList<UsageInfo> usages);

    /// <summary>
    /// 根据各卡片当前启用状态与用户配置的卡片顺序，重建"已启用"过滤集合。
    /// 取消勾选某插件后调用即可让对应卡片立即从主窗口消失。
    /// </summary>
    void RebuildEnabledCards();

    /// <summary>
    /// 更新指定插件的启用状态：同步配置、插件管理器、卡片 VM，并刷新已启用集合与持久化。
    /// </summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    /// <param name="isEnabled">是否启用。</param>
    void SetPluginEnabled(string providerId, bool isEnabled);

    /// <summary>
    /// 修改指定 Provider 的任务栏显示模式（同步到卡片 VM 与配置并持久化）。
    /// </summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    /// <param name="mode">目标任务栏显示模式。</param>
    void ChangeTaskbarMode(string providerId, TaskbarDisplayMode mode);

    /// <summary>
    /// 获取指定 Provider 的卡片图表显示顺序：用户自定义顺序优先（过滤掉插件不再支持的类型、
    /// 追加插件新支持的类型），未配置时回退到插件声明的支持顺序。
    /// </summary>
    /// <param name="providerId">Provider 唯一标识。</param>
    /// <param name="supportedCharts">插件声明支持的卡片图表类型集合。</param>
    /// <returns>最终生效的图表顺序。</returns>
    IReadOnlyList<CardChartKind> GetChartOrder(string providerId, IReadOnlyList<CardChartKind> supportedCharts);
}
