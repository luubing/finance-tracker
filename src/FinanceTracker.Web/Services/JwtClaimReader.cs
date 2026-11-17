using System.Text;
using System.Text.Json;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 从 JWT 载荷里读取云端用户 Id 与手机号。
/// 只解码、不校验签名（校验是服务端的事；客户端只是想知道"这枚令牌属于谁"）：
/// 1. 用户 Id：同步时据此把云端用户 Id 映射回本地用户；
/// 2. 手机号：判断本地保存的令牌是否仍属于当前登录的手机号（同一设备换号登录后，旧令牌必须作废，
///    否则新账号的数据会被同步进旧账号，旧账号的数据还会被映射成"自己的"）。
///
/// 服务端用 ClaimTypes.NameIdentifier / ClaimTypes.MobilePhone 写入，
/// JwtSecurityTokenHandler 出站时会把它们缩写成短名；这里同时兼容短名和完整的 claim URI，
/// 避免服务端换 token 库或关闭缩写映射后客户端悄悄失效。
/// </summary>
public static class JwtClaimReader
{
    private static readonly string[] UserIdClaimNames =
    {
        "nameid",
        "sub",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
    };

    private static readonly string[] PhoneNumberClaimNames =
    {
        "phone_number",
        "mobilephone",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/mobilephone"
    };

    /// <summary>
    /// 读取 token 中的用户 Id；token 为空、格式不对或没有可解析的用户声明时返回 null
    /// </summary>
    public static Guid? TryReadUserId(string? token)
    {
        var text = TryReadFirstString(token, UserIdClaimNames);
        return Guid.TryParse(text, out var userId) && userId != Guid.Empty ? userId : null;
    }

    /// <summary>
    /// 读取 token 中的手机号；token 为空、格式不对或没有手机号声明时返回 null
    /// </summary>
    public static string? TryReadPhoneNumber(string? token)
    {
        var text = TryReadFirstString(token, PhoneNumberClaimNames);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? TryReadFirstString(string? token, IEnumerable<string> claimNames)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64UrlDecode(segments[1]));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var claimName in claimNames)
            {
                if (document.RootElement.TryGetProperty(claimName, out var element) &&
                    element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            // 不是合法的 JWT：当作读不到
        }

        return null;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
