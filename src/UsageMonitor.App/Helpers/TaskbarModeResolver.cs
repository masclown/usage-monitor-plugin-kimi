using UsageMonitor.Core.Models;
using UsageMonitor.Core.Services;

namespace UsageMonitor.App.Helpers;

/// <summary>
/// req-022：任务栏显示模式解析器。
/// <para>
/// 给定 <see cref="AppSettings"/> + providerId，按"先 Provider 单独覆盖 → 后全局默认"优先级
/// 返回最终生效的 <see cref="TaskbarDisplayMode"/>。抽离出来便于 MainViewModel /
/// TaskbarWindow / 设置 UI 三处共享同一解析逻辑，避免散落判断。
/// </para>
/// </summary>
public static class TaskbarModeResolver
{
    /// <summary>
    /// 解析某 Provider 最终生效的任务栏显示模式。
    /// </summary>
    /// <param name="settings">应用配置（不能为 null）</param>
    /// <param name="providerId">服务商 Id（不区分大小写）</param>
    /// <returns>该 Provider 当前生效的显示模式</returns>
    public static TaskbarDisplayMode Resolve(AppSettings settings, string providerId)
    {
        if (settings == null) return TaskbarDisplayMode.Text;
        if (!string.IsNullOrEmpty(providerId) &&
            settings.ProviderTaskbarModes.TryGetValue(providerId, out var mode))
        {
            return mode;
        }
        return settings.GlobalTaskbarMode;
    }
}