namespace FinanceTracker.Core.Services;

/// <summary>
/// 手机号脱敏：写日志、报错信息时使用，避免把完整手机号落盘。
/// 手机号是本应用的唯一标识且没有验证码，等同于登录凭据。
/// </summary>
public static class PhoneNumberMasker
{
    /// <summary>
    /// 11 位手机号保留前 3 后 4（138****8000，与"我的"页面的脱敏展示一致）；
    /// 其他长度属于异常输入，只保留末 2 位，避免把完整输入原样写进日志。
    /// </summary>
    public static string Mask(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return string.Empty;
        }

        if (phoneNumber.Length == 11)
        {
            return $"{phoneNumber[..3]}****{phoneNumber[7..]}";
        }

        return phoneNumber.Length > 2 ? $"****{phoneNumber[^2..]}" : "****";
    }
}
