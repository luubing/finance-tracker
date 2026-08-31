namespace FinanceTracker.Api.Models;

/// <summary>
/// JWT 配置
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// 密钥
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// 发行者
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// 接收者
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// 过期时间（分钟）
    /// </summary>
    public int ExpiryInMinutes { get; set; } = 60 * 24 * 7; // 默认7天
}
