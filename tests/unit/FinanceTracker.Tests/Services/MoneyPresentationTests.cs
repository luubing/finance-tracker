using System.Globalization;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 金额的展示规则（币种符号、千分位、两位小数、正负号）：规则集中在 <see cref="MoneyPresentation"/>，页面与组件只负责显示
/// </summary>
public class MoneyPresentationTests
{
    [Theory]
    [InlineData(1234.5, "¥1,234.50")]
    [InlineData(0, "¥0.00")]
    [InlineData(8713.5, "¥8,713.50")]
    [InlineData(12345678.9, "¥12,345,678.90")]
    [InlineData(0.005, "¥0.01")]
    [InlineData(-1234.5, "-¥1,234.50")]
    [InlineData(-0.001, "¥0.00")]
    public void 金额格式化为币种符号加千分位加两位小数_负数带负号_舍入为零不带符号(double amount, string expected) =>
        Assert.Equal(expected, MoneyPresentation.Format((decimal)amount));

    [Fact]
    public void 拆分金额为符号_整数_小数三段()
    {
        Assert.Equal(new AmountParts(string.Empty, "8,713", ".50"), MoneyPresentation.Split(8713.5m));
        Assert.Equal(new AmountParts("-", "1,286", ".50"), MoneyPresentation.Split(-1286.5m));
        Assert.Equal(new AmountParts(string.Empty, "0", ".00"), MoneyPresentation.Split(0m));
    }

    [Fact]
    public void 要求带正号时只有正数带加号_零与负数不受影响()
    {
        Assert.Equal("+", MoneyPresentation.Split(12000m, plusForPositive: true).Sign);
        Assert.Equal(string.Empty, MoneyPresentation.Split(0m, plusForPositive: true).Sign);
        Assert.Equal(string.Empty, MoneyPresentation.Split(0.004m, plusForPositive: true).Sign);
        Assert.Equal("-", MoneyPresentation.Split(-5m, plusForPositive: true).Sign);
    }

    [Fact]
    public void 四舍五入到分按远离零取整_与金额的常规认知一致()
    {
        Assert.Equal(".01", MoneyPresentation.Split(0.005m).Fraction);
        Assert.Equal(".02", MoneyPresentation.Split(0.015m).Fraction);
        Assert.Equal(".01", MoneyPresentation.Split(0.0149m).Fraction);
        Assert.Equal("1", MoneyPresentation.Split(0.999m).Integer);
    }

    [Theory]
    [InlineData(35, false, "-¥35.00")]
    [InlineData(35.5, false, "-¥35.50")]
    [InlineData(12000, true, "+¥12,000.00")]
    [InlineData(0, false, "¥0.00")]
    [InlineData(0, true, "¥0.00")]
    [InlineData(0.004, true, "¥0.00")]
    [InlineData(-35, false, "-¥35.00")]
    [InlineData(-12000, true, "+¥12,000.00")]
    public void 账单金额按收支方向带符号_支出负号_收入正号_金额恒取绝对值(double amount, bool isIncome, string expected) =>
        Assert.Equal(expected, MoneyPresentation.Signed((decimal)amount, isIncome));

    [Fact]
    public void 格式不随运行环境的区域设置变化_千分位与小数点固定()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // 德语区域的千分位是 . 、小数点是 ,：若实现依赖当前区域，这里就会得到 1.234,50
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("¥1,234.50", MoneyPresentation.Format(1234.5m));
            Assert.Equal("-¥1,234.50", MoneyPresentation.Signed(1234.5m, isIncome: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
