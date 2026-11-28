using FinanceTracker.Core.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

public class LogSanitizerTests
{
    [Theory]
    [InlineData("午餐", "午餐")]
    [InlineData("Lunch & Coffee (办公室) #1", "Lunch & Coffee (办公室) #1")]
    [InlineData("  前后有空格  ", "  前后有空格  ")]
    public void 正常文本_原样保留(string input, string expected) =>
        Assert.Equal(expected, LogSanitizer.Clean(input));

    [Theory]
    [InlineData("a\nb", "ab")]
    [InlineData("a\r\nb", "ab")]
    [InlineData("a\rb", "ab")]
    [InlineData("名字\n2026-01-01 ERROR 伪造的日志行", "名字2026-01-01 ERROR 伪造的日志行")]
    public void 换行_被去掉_无法伪造日志行(string input, string expected)
    {
        var cleaned = LogSanitizer.Clean(input);

        Assert.Equal(expected, cleaned);
        Assert.DoesNotContain('\n', cleaned);
        Assert.DoesNotContain('\r', cleaned);
    }

    [Theory]
    [InlineData("a\tb", "ab")]
    [InlineData("a\u001b[31mb", "a[31mb")]   // 终端转义序列开头的 ESC 字符
    [InlineData("a\u2028b\u2029c", "abc")]   // Unicode 行分隔符 / 段分隔符
    public void 其他控制字符_也被去掉(string input, string expected) =>
        Assert.Equal(expected, LogSanitizer.Clean(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空输入_返回空串(string? input) =>
        Assert.Equal(string.Empty, LogSanitizer.Clean(input));
}
