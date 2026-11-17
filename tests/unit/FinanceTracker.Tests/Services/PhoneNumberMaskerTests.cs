using FinanceTracker.Core.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

public class PhoneNumberMaskerTests
{
    [Theory]
    [InlineData("13812345678", "138****5678")]
    [InlineData("19900000001", "199****0001")]
    public void 十一位手机号_保留前三后四(string input, string expected) =>
        Assert.Equal(expected, PhoneNumberMasker.Mask(input));

    [Theory]
    [InlineData("1381234567", "****67")]        // 10 位：异常输入，只留末 2 位
    [InlineData("138123456789", "****89")]      // 12 位
    [InlineData("abc", "****bc")]
    [InlineData("12", "****")]                  // 太短：全部遮住
    [InlineData("1", "****")]
    public void 非十一位输入_只保留末两位且不泄漏原文(string input, string expected)
    {
        var masked = PhoneNumberMasker.Mask(input);

        Assert.Equal(expected, masked);
        if (input.Length > 2)
        {
            Assert.DoesNotContain(input, masked);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空输入_返回空串(string? input) => Assert.Equal(string.Empty, PhoneNumberMasker.Mask(input));
}
