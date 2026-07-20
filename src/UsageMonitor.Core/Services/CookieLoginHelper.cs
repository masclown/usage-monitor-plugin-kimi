using UsageMonitor.Core.Models;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-086：统一 Cookie 提取/验证/刷新流程，面向插件内部复用。
/// <para>
/// 与 <see cref="BrowserLoginService"/> 协作，但提供更简洁的插件侧 API：
/// 插件只需传入 <see cref="BrowserLoginConfig"/>，即可获取有效 Cookie 或触发重新登录。
/// </para>
/// </summary>
public class CookieLoginHelper
{
    private readonly BrowserLoginService _browserLoginService;
    private readonly string _providerId;

    /// <summary>
    /// 创建 CookieLoginHelper 实例。
    /// </summary>
    /// <param name="providerId">插件 ProviderId</param>
    /// <param name="configService">可选 ConfigService（用于登录成功后自动重载配置）</param>
    public CookieLoginHelper(string providerId, ConfigService? configService = null)
    {
        _providerId = providerId;
        _browserLoginService = new BrowserLoginService(configService);
    }

    /// <summary>
    /// 获取有效 Cookie，如无效则触发浏览器登录流程。
    /// </summary>
    /// <param name="config">浏览器登录配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>有效 Cookie 数据，失败返回 null</returns>
    public async Task<BrowserCookieData?> GetOrRefreshCookieAsync(
        BrowserLoginConfig config,
        CancellationToken ct = default)
    {
        // 先检查本地 Cookie 是否有效
        if (await BrowserLoginService.CheckCookieValidAsync(config, ct))
        {
            var existing = BrowserLoginService.LoadCookieData(_providerId);
            if (existing != null)
            {
                FileLogger.Info("CookieLoginHelper", $"[{_providerId}] Cookie 有效，直接使用");
                return existing;
            }
        }

        // Cookie 无效或不存在，触发登录
        FileLogger.Info("CookieLoginHelper", $"[{_providerId}] Cookie 无效，启动浏览器登录");
        return await _browserLoginService.LoginAndExtractCookieAsync(config, ct);
    }

    /// <summary>
    /// 仅加载本地 Cookie，不验证有效性。
    /// </summary>
    public BrowserCookieData? LoadLocalCookie()
    {
        return BrowserLoginService.LoadCookieData(_providerId);
    }

    /// <summary>
    /// 保存 Cookie 到本地（新格式：HMAC 签名）。
    /// </summary>
    public void SaveCookie(BrowserCookieData data)
    {
        BrowserLoginService.SaveCookieData(data);
    }

    /// <summary>
    /// 删除本地 Cookie。
    /// </summary>
    public bool DeleteCookie()
    {
        return BrowserLoginService.DeleteCookieData(_providerId);
    }

    /// <summary>
    /// 获取 Cookie 字符串（HTTP Header 格式）。
    /// </summary>
    public string? GetCookieString()
    {
        return BrowserLoginService.GetCookieString(_providerId);
    }

    /// <summary>
    /// 最近一次登录失败的错误信息。
    /// </summary>
    public string? LastError => _browserLoginService.LastError;
}
