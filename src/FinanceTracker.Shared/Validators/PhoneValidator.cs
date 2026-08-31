namespace FinanceTracker.Shared.Validators;

/// <summary>
/// 手机号验证器
/// </summary>
public static class PhoneValidator
{
    /// <summary>
    /// 手机号长度
    /// </summary>
    public const int PhoneNumberLength = 11;

    /// <summary>
    /// 手机号前缀
    /// </summary>
    public const string PhoneNumberPrefix = "1";

    /// <summary>
    /// 验证手机号是否有效
    /// </summary>
    /// <param name="phoneNumber">手机号</param>
    /// <returns>验证结果</returns>
    public static ValidationResult Validate(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new ValidationResult(false, "手机号不能为空");
        }

        if (phoneNumber.Length != PhoneNumberLength)
        {
            return new ValidationResult(false, "手机号必须是11位");
        }

        if (!phoneNumber.StartsWith(PhoneNumberPrefix))
        {
            return new ValidationResult(false, "手机号格式不正确");
        }

        return new ValidationResult(true, null);
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    public record ValidationResult(bool IsValid, string? ErrorMessage);
}
