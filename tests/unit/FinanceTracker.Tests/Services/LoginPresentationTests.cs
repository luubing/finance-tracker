using FinanceTracker.Shared.Validators;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 登录页的文案与手机号校验（22 号）：认证行为没有变（手机号手动输入、无验证码、首次使用自动注册），
/// 这里锁住的是「面板上写什么」「手机号怎样才算合法」，以及它与账本成员页邀请手机号用的是同一套规则。
/// </summary>
public sealed class LoginPresentationTests
{
    [Theory]
    [InlineData("13800000000")]
    [InlineData("18912345678")]
    [InlineData("  13800000000  ")]
    [InlineData("\t13800000000\n")]
    public void 合法手机号通过校验_首尾空格先被去掉(string input)
    {
        Assert.Null(LoginPresentation.ValidatePhone(input));
    }

    [Theory]
    [InlineData(null, "手机号不能为空")]
    [InlineData("", "手机号不能为空")]
    [InlineData("   ", "手机号不能为空")]
    [InlineData("1380000000", "手机号必须是11位")]
    [InlineData("138000000000", "手机号必须是11位")]
    [InlineData("138 0000 0000", "手机号必须是11位")]
    [InlineData("23800000000", "手机号格式不正确")]
    public void 非法手机号给出面向用户的提示(string? input, string expected)
    {
        Assert.Equal(expected, LoginPresentation.ValidatePhone(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("13800000000")]
    [InlineData("  13800000000 ")]
    [InlineData("1380000000")]
    [InlineData("23800000000")]
    public void 登录与邀请成员共用同一套手机号规则(string? input)
    {
        Assert.Equal(LedgerMemberPresentation.ValidatePhone(input), LoginPresentation.ValidatePhone(input));
    }

    [Fact]
    public void 提交前的整理只去掉首尾空白_空输入是空串()
    {
        Assert.Equal("13800000000", LoginPresentation.Normalize("  13800000000\t"));
        Assert.Equal("138 0000", LoginPresentation.Normalize(" 138 0000 "));
        Assert.Equal(string.Empty, LoginPresentation.Normalize(null));
        Assert.Equal(string.Empty, LoginPresentation.Normalize("   "));
    }

    [Fact]
    public void 输入框的长度上限就是手机号长度()
    {
        Assert.Equal(PhoneValidator.PhoneNumberLength, LoginPresentation.PhoneMaxLength);
        Assert.Equal(11, LoginPresentation.PhoneMaxLength);
    }

    [Fact]
    public void 面板说明无需验证码且首次使用自动注册()
    {
        Assert.Contains("无需验证码", LoginPresentation.PanelHint);
        Assert.Contains("自动注册", LoginPresentation.PanelHint);
        Assert.Equal("登录 / 注册", LoginPresentation.SubmitText);
    }

    [Fact]
    public void 登录失败的提示带上面向用户的原因()
    {
        Assert.Equal("登录失败: 网络连接失败", LoginPresentation.FailureMessage("网络连接失败"));
    }

    [Fact]
    public void 界面文案遵守领域术语_不出现被禁用的词()
    {
        var texts = new[]
        {
            LoginPresentation.AppName, LoginPresentation.Slogan, LoginPresentation.PanelTitle, LoginPresentation.PanelHint,
            LoginPresentation.CountryCode, LoginPresentation.PhoneLabel, LoginPresentation.PhonePlaceholder,
            LoginPresentation.SubmitText, LoginPresentation.SubmittingLabel, LoginPresentation.FailureMessage("原因"),
            LoginPresentation.ValidatePhone("") ?? string.Empty, LoginPresentation.ValidatePhone("1") ?? string.Empty,
            LoginPresentation.ValidatePhone("23800000000") ?? string.Empty,
        };

        foreach (var text in texts)
        {
            foreach (var banned in new[] { "交易", "记录", "流水", "标签", "类型", "支付方式", "付款方式", "账簿", "笔记本", "录入方式" })
            {
                Assert.DoesNotContain(banned, text);
            }
        }
    }
}
