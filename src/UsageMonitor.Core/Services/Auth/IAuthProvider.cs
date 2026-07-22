namespace UsageMonitor.Core.Services.Auth;

/// <summary>
/// 鉴权提供者接口 - 定义统一的鉴权数据获取、验证、刷新能力
/// <para>req-096：所有鉴权方式（ApiKey/Cookie/OAuth）均通过此接口统一管理。</para>
/// </summary>
public interface IAuthProvider
{
    /// <summary>获取当前鉴权方式</summary>
    AuthKind Kind { get; }

    /// <summary>
    /// 获取鉴权数据（如 API Key、Cookie 字符串等）
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="accountId">账号标识（多账号支持，默认 "default"）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>鉴权数据字符串，未找到时返回 null</returns>
    Task<string?> GetAuthDataAsync(string providerId, string accountId = "default", CancellationToken ct = default);

    /// <summary>
    /// 验证鉴权数据是否有效
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>验证是否通过</returns>
    Task<bool> ValidateAsync(string providerId, string accountId = "default", CancellationToken ct = default);

    /// <summary>
    /// 刷新鉴权数据（如 Cookie 过期后重新获取）
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>刷新是否成功</returns>
    Task<bool> RefreshAsync(string providerId, string accountId = "default", CancellationToken ct = default);

    /// <summary>
    /// 保存鉴权数据
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="authData">鉴权数据</param>
    /// <param name="ct">取消令牌</param>
    Task SaveAuthDataAsync(string providerId, string accountId, string authData, CancellationToken ct = default);

    /// <summary>
    /// 删除鉴权数据
    /// </summary>
    /// <param name="providerId">服务商唯一标识</param>
    /// <param name="accountId">账号标识</param>
    /// <param name="ct">取消令牌</param>
    Task DeleteAuthDataAsync(string providerId, string accountId = "default", CancellationToken ct = default);
}
