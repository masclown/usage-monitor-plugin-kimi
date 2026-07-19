using System.Net;
using System.Net.Sockets;

namespace UsageMonitor.Core.Services;

/// <summary>
/// BaseUrl 安全校验工具 - 防止 SSRF 攻击
/// 校验 HTTPS scheme、拒绝内网/环回/链路本地地址
/// </summary>
public static class BaseUrlValidator
{
    /// <summary>
    /// 校验 BaseUrl 是否安全（HTTPS + 非公网IP拒绝）
    /// </summary>
    /// <param name="url">待校验的URL</param>
    /// <param name="errorMessage">校验失败时的错误信息</param>
    /// <returns>是否通过校验</returns>
    public static bool TryValidate(string? url, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            errorMessage = "BaseUrl 不能为空";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            errorMessage = "BaseUrl 格式无效";
            return false;
        }

        // 强制 HTTPS（拒绝 http://）
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            errorMessage = "BaseUrl 必须使用 HTTPS 协议（拒绝 HTTP）";
            return false;
        }

        // 解析主机名/IP
        var host = uri.Host;

        // 拒绝环回地址
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsLoopback(ip))
            {
                errorMessage = "BaseUrl 不允许使用环回地址（localhost/127.x.x.x/::1）";
                return false;
            }

            if (IsPrivateOrLinkLocal(ip))
            {
                errorMessage = "BaseUrl 不允许使用内网或链路本地地址";
                return false;
            }
        }
        else
        {
            // 主机名包含 localhost 也拒绝
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "BaseUrl 不允许使用本地主机名";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 快速校验并返回规范化的 BaseUrl（失败时返回默认值）
    /// </summary>
    /// <param name="rawUrl">原始URL</param>
    /// <param name="defaultUrl">校验失败时的默认URL</param>
    /// <returns>规范化后的安全URL</returns>
    public static string ValidateOrDefault(string? rawUrl, string defaultUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return defaultUrl;

        var trimmed = rawUrl.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        return TryValidate(trimmed, out _) ? trimmed : defaultUrl;
    }

    /// <summary>判断是否为环回地址</summary>
    private static bool IsLoopback(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            // 127.0.0.0/8
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 127;
        }
        // IPv6 ::1
        return IPAddress.IsLoopback(ip);
    }

    /// <summary>判断是否为内网或链路本地地址</summary>
    private static bool IsPrivateOrLinkLocal(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return
                // 10.0.0.0/8
                bytes[0] == 10 ||
                // 172.16.0.0/12
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                // 192.168.0.0/16
                (bytes[0] == 192 && bytes[1] == 168) ||
                // 169.254.0.0/16 (链路本地，含云元数据)
                (bytes[0] == 169 && bytes[1] == 254);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fe80::/10 链路本地
            if (ip.IsIPv6LinkLocal)
                return true;
            // fc00::/7 唯一本地地址 (ULA)
            var bytes = ip.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;
        }

        return false;
    }
}
