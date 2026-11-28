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
    /// 输出一律去掉换行等控制字符（<see cref="LogSanitizer.Clean"/>）：<c>PhoneValidator</c> 只校验长度和首位，
    /// 并不保证全是数字，脱敏后的结果写进日志时不能带着换行去伪造日志行。
    /// </summary>
    public static string Mask(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return string.Empty;
        }

        if (phoneNumber.Length == 11)
        {
            return LogSanitizer.Clean($"{phoneNumber[..3]}****{phoneNumber[7..]}");
        }

        return LogSanitizer.Clean(phoneNumber.Length > 2 ? $"****{phoneNumber[^2..]}" : "****");
    }
}
