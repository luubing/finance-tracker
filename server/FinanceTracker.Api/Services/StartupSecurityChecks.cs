using System.Text.RegularExpressions;

namespace FinanceTracker.Api.Services;

/// <summary>
/// 启动期安全自检。
/// 仓库提交的 appsettings.json 带有公开的占位 JWT 密钥与默认数据库口令，仅供本地开发；
/// 非 Development 环境若漏覆盖，任何人都能凭公开的占位密钥伪造 JWT（README 已要求生产覆盖，这里把约定变成强制）。
/// </summary>
public static class StartupSecurityChecks
{
    /// <summary>
    /// appsettings.json 中占位 JWT 密钥的公共前缀（该前缀随仓库公开，不是机密）。
    /// 用前缀而不是整串比较，即使占位文本末尾被改动也仍能识别。
    /// </summary>
    public const string PlaceholderJwtSecretKeyPrefix = "YourSuperSecretKey";

    private static readonly Regex DefaultDatabasePasswordPattern = new(
        @"(^|;)\s*(password|pwd)\s*=\s*postgres\s*(;|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// 是否为仓库内公开的占位 JWT 密钥
    /// </summary>
    public static bool IsPlaceholderJwtSecretKey(string? secretKey)
    {
        return !string.IsNullOrEmpty(secretKey)
            && secretKey.StartsWith(PlaceholderJwtSecretKeyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 非 Development 环境下，JWT 密钥仍是占位值时抛出异常，拒绝启动
    /// </summary>
    /// <exception cref="InvalidOperationException">非 Development 环境使用了占位密钥</exception>
    public static void EnsureJwtSecretKeyIsNotPlaceholder(string? secretKey, bool isDevelopment)
    {
        if (isDevelopment || !IsPlaceholderJwtSecretKey(secretKey))
        {
            return;
        }

        throw new InvalidOperationException(
            "非 Development 环境检测到 Jwt:SecretKey 仍是仓库内的公开占位值，已拒绝启动（否则任何人都能伪造 JWT）。" +
            "请通过环境变量 Jwt__SecretKey 或 appsettings.Production.json 配置至少 32 位的随机密钥。");
    }

    /// <summary>
    /// 连接串是否仍在使用默认的数据库口令 postgres
    /// </summary>
    public static bool UsesDefaultDatabasePassword(string? connectionString)
    {
        return !string.IsNullOrWhiteSpace(connectionString)
            && DefaultDatabasePasswordPattern.IsMatch(connectionString);
    }
}
