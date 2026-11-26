using FinanceTracker.Shared.Validators;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 登录页的文案与手机号校验（纯函数，单元测试覆盖）。页面只负责渲染与调用 <c>IAuthService</c>，规则集中在这里。
/// <para>
/// 认证方式不变：手机号手动输入、无验证码、首次使用即注册（<c>RegisterOrLoginAsync</c>），会话存在本地。
/// 这里没有任何认证逻辑——只有「面板上写什么」与「手机号怎样才算合法」。
/// </para>
/// </summary>
public static class LoginPresentation
{
    /// <summary>应用名（Logo 下方的大字）</summary>
    public const string AppName = "记账本";

    /// <summary>一句话介绍</summary>
    public const string Slogan = "简单、安心的个人记账";

    /// <summary>白色面板的标题</summary>
    public const string PanelTitle = "手机号登录";

    /// <summary>面板标题下的说明：无需验证码，首次使用自动注册</summary>
    public const string PanelHint = "无需验证码；首次使用将自动注册账号";

    /// <summary>国家区号（只做展示：目前只支持中国大陆 11 位手机号，规则见 <see cref="PhoneValidator"/>）</summary>
    public const string CountryCode = "+86";

    /// <summary>手机号输入框的读屏名称</summary>
    public const string PhoneLabel = "手机号";

    /// <summary>手机号输入框的占位文字</summary>
    public const string PhonePlaceholder = "请输入手机号";

    /// <summary>主按钮文案</summary>
    public const string SubmitText = "登录 / 注册";

    /// <summary>按钮的读屏名称（进行中）：按钮文字被转圈替代时仍要读出在做什么</summary>
    public const string SubmittingLabel = "正在登录";

    /// <summary>手机号的输入长度上限（<see cref="PhoneValidator.PhoneNumberLength"/>）</summary>
    public const int PhoneMaxLength = PhoneValidator.PhoneNumberLength;

    /// <summary>提交前的整理：去掉首尾空格（与账本成员页邀请手机号同一做法）</summary>
    public static string Normalize(string? input) => input?.Trim() ?? string.Empty;

    /// <summary>
    /// 校验手机号：整理后按 <see cref="PhoneValidator"/>（登录与邀请成员共用的规则）判断，
    /// 合法返回 null，否则返回给用户看的提示。
    /// </summary>
    public static string? ValidatePhone(string? input)
    {
        var result = PhoneValidator.Validate(Normalize(input));
        return result.IsValid ? null : result.ErrorMessage;
    }

    /// <summary>登录失败的 Toast 文案（detail 是 <c>GetUserMessage()</c> 给出的面向用户的原因）</summary>
    public static string FailureMessage(string detail) => $"登录失败: {detail}";
}
