using System;
using System.Security.Cryptography;
using System.Text;

namespace UsageMonitor.Core.Services;

/// <summary>
/// req-088 Phase1：账号身份哈希工具。
/// <para>把"插件 ID + 平台稳定身份 ID（如 MiniMax 的 group_id、其它 Provider 的 uid/邮箱等）"哈希为稳定、非 PII 的
/// <c>account_id</c>，作为数据库多账号隔离主键。明文身份绝不入库/日志——只存本方法产出的哈希值。</para>
/// <para>稳定性保证：同一网页账号每次刷新都算出相同 account_id，因此"删除账号但未删数据库数据、之后重加相同账号"时，
/// 能凭相同哈希自动重新关联历史数据（见计划 Phase1 决策）。</para>
/// </summary>
public static class AccountIdHasher
{
    /// <summary>account_id 十六进制长度（取 SHA-256 前 N 个 hex 字符；16 字符=64bit，足够区分单用户的少量账号且更简洁）。</summary>
    private const int HexLength = 16;

    /// <summary>
    /// 计算账号哈希 ID。
    /// </summary>
    /// <param name="providerId">插件唯一标识（如 "MiniMax"）。</param>
    /// <param name="stableId">平台稳定身份 ID（如 group_id / uid / 邮箱），明文仅参与哈希、不返回、不入库。</param>
    /// <returns>小写十六进制哈希 ID；<paramref name="stableId"/> 为空时返回 "default"（无身份场景兜底）。</returns>
    public static string Compute(string? providerId, string? stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
            return "default";

        var input = $"{providerId?.Trim().ToLowerInvariant()}:{stableId.Trim()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        // 取前 HexLength/2 字节转小写 hex（16 hex 字符）。
        var sb = new StringBuilder(HexLength);
        for (var i = 0; i < HexLength / 2 && i < bytes.Length; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}
