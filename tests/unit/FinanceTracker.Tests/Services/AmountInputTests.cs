using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 记一笔的金额输入规则（自定义数字键盘）：每按一个键算出新的金额文本，规则集中在 <see cref="AmountInput"/>，
/// 界面只负责渲染与转发按键。覆盖 spec「金额规则」：最多两位小数、上限 99,999,999.99、不允许多个小数点、长按退格清空。
/// </summary>
public class AmountInputTests
{
    /// <summary>依次按键，返回最终文本</summary>
    private static string Press(string start, params string[] keys)
    {
        var text = start;
        foreach (var key in keys)
        {
            text = AmountInput.Apply(text, key);
        }

        return text;
    }

    // ------------------------------------------------------------------ 数字与小数点

    [Fact]
    public void 依次按数字_追加在末尾()
    {
        Assert.Equal("35", Press("", "3", "5"));
        Assert.Equal("1234567", Press("", "1", "2", "3", "4", "5", "6", "7"));
    }

    [Fact]
    public void 小数点_空文本补前导零_已有小数点再按被忽略()
    {
        Assert.Equal("0.", Press("", "."));
        Assert.Equal("35.", Press("35", "."));
        Assert.Equal("35.", Press("35.", "."));
        Assert.Equal("35.5", Press("35", ".", "5", "."));
        Assert.Equal("0.5", Press("", ".", "5", ".", "."));
    }

    [Fact]
    public void 第三位小数被忽略_最多两位小数()
    {
        Assert.Equal("35.12", Press("35", ".", "1", "2", "3"));
        Assert.Equal("0.99", Press("", ".", "9", "9", "9", "9"));
        Assert.Equal("35.12", AmountInput.Apply("35.12", "9"));
    }

    [Fact]
    public void 没有前导零_零后面再按数字是替换_零后面再按零还是零()
    {
        Assert.Equal("0", Press("", "0"));
        Assert.Equal("0", Press("", "0", "0", "0"));
        Assert.Equal("5", Press("", "0", "5"));
        Assert.Equal("0.05", Press("", "0", ".", "0", "5"));
        Assert.Equal("10", Press("1", "0"));
    }

    [Fact]
    public void 零点开头的小数正常输入()
    {
        Assert.Equal("0.5", Press("", "0", ".", "5"));
        Assert.Equal("0.50", Press("", "0", ".", "5", "0"));
    }

    // ------------------------------------------------------------------ 00 键

    [Fact]
    public void 双零键_等于连按两次零()
    {
        Assert.Equal("1200", Press("12", "00"));
        Assert.Equal("0", Press("", "00"));
        Assert.Equal("0", Press("0", "00"));
        Assert.Equal("100", Press("1", "00"));
    }

    [Fact]
    public void 双零键_在小数部分按剩余位数补零()
    {
        Assert.Equal("5.00", Press("5.", "00"));
        Assert.Equal("5.50", Press("5.5", "00")); // 只剩一位，补一个零，第二个被忽略
        Assert.Equal("5.55", Press("5.55", "00"));
    }

    // ------------------------------------------------------------------ 上限

    [Fact]
    public void 整数部分最多八位_第九位被忽略()
    {
        Assert.Equal("99999999", Press("", "9", "9", "9", "9", "9", "9", "9", "9", "9", "9"));
        Assert.Equal("12345678", Press("1234567", "8", "9"));
    }

    [Fact]
    public void 上限恰好是九千九百九十九万九千九百九十九点九九()
    {
        Assert.Equal(99_999_999.99m, AmountInput.MaxAmount);

        var text = Press("99999999", ".", "9", "9");
        Assert.Equal("99999999.99", text);
        Assert.Equal(AmountInput.MaxAmount, AmountInput.Parse(text));

        // 到顶之后再按数字、双零都被忽略；小数点已存在也被忽略
        Assert.Equal("99999999.99", Press(text, "9", "00", "."));
    }

    [Fact]
    public void 双零键碰到整数位上限_只补得下的那一个零()
    {
        Assert.Equal("99999999", AmountInput.Apply("99999999", "00"));
        Assert.Equal("12345670", Press("1234567", "00")); // 第 8 位补上，第 9 位被忽略
    }

    // ------------------------------------------------------------------ 退格与清空

    [Fact]
    public void 退格删最后一个字符_空文本退格还是空()
    {
        Assert.Equal("3", Press("35", "backspace"));
        Assert.Equal("35", Press("35.", "backspace"));
        Assert.Equal("35.", Press("35.5", "backspace"));
        Assert.Equal("", Press("5", "backspace"));
        Assert.Equal("", Press("", "backspace"));
    }

    [Fact]
    public void 退格后再输入_像普通计算器一样继续()
    {
        Assert.Equal("37", Press("35", "backspace", "7"));
        Assert.Equal("0", Press("0.", "backspace"));
        Assert.Equal("5", Press("0.", "backspace", "5"));
    }

    [Fact]
    public void 清空_长按退格触发_任何文本都变空()
    {
        Assert.Equal("", Press("12345.67", "clear"));
        Assert.Equal("", Press("", "clear"));
    }

    // ------------------------------------------------------------------ 防御

    [Theory]
    [InlineData("a")]
    [InlineData("-")]
    [InlineData("12")]
    [InlineData("")]
    [InlineData(null)]
    public void 不认识的键什么也不做(string? key) =>
        Assert.Equal("35", AmountInput.Apply("35", key));

    [Fact]
    public void 外部传入的脏文本先清洗再计算_只留数字和第一个小数点()
    {
        Assert.Equal("123.45", AmountInput.Apply("1,23.4", "5"));
        Assert.Equal("123.45", AmountInput.Apply("1,23.4.5abc", "5")); // 清洗成 123.45，已满两位小数，按键被忽略
        Assert.Equal("3", AmountInput.Apply(null, "3"));
    }

    // ------------------------------------------------------------------ 解析

    [Theory]
    [InlineData("", null)]
    [InlineData(".", null)]
    [InlineData("0", 0d)]
    [InlineData("35", 35d)]
    [InlineData("35.", 35d)]
    [InlineData("35.5", 35.5)]
    [InlineData("0.05", 0.05)]
    [InlineData("99999999.99", 99999999.99)]
    public void 解析金额文本(string text, double? expected)
    {
        decimal? want = expected is null ? null : (decimal)expected.Value;
        Assert.Equal(want, AmountInput.Parse(text));
    }

    [Fact]
    public void 解析空引用是没有金额() => Assert.Null(AmountInput.Parse(null));

    // ------------------------------------------------------------------ 数值 → 文本

    [Theory]
    [InlineData(35, "35")]
    [InlineData(35.5, "35.50")]
    [InlineData(35.05, "35.05")]
    [InlineData(12.3, "12.30")]
    [InlineData(0.01, "0.01")]
    [InlineData(99999999.99, "99999999.99")]
    public void 数值转金额文本_整数不带小数点_有小数补满两位(double amount, string expected) =>
        Assert.Equal(expected, AmountInput.FromDecimal((decimal)amount));

    [Fact]
    public void 数值转金额文本_零和负数是没有金额_超上限夹到上限_多余小数四舍五入()
    {
        Assert.Equal("", AmountInput.FromDecimal(0m));
        Assert.Equal("", AmountInput.FromDecimal(-5m));
        Assert.Equal("99999999.99", AmountInput.FromDecimal(1_000_000_000m));
        Assert.Equal("35.13", AmountInput.FromDecimal(35.125m));
        Assert.Equal("", AmountInput.FromDecimal(0.004m));
    }

    [Fact]
    public void 数值转文本再解析_往返一致()
    {
        foreach (var amount in new[] { 1m, 12.5m, 100m, 0.99m, 4321.01m, 99_999_999.99m })
        {
            Assert.Equal(amount, AmountInput.Parse(AmountInput.FromDecimal(amount)));
        }
    }

    // ------------------------------------------------------------------ 展示

    [Theory]
    [InlineData("", "0")]
    [InlineData("0", "0")]
    [InlineData("5", "5")]
    [InlineData("123", "123")]
    [InlineData("1234", "1,234")]
    [InlineData("12000", "12,000")]
    [InlineData("12000.5", "12,000.5")]
    [InlineData("12000.50", "12,000.50")]
    [InlineData("35.", "35.")]
    [InlineData("0.", "0.")]
    [InlineData("1234567", "1,234,567")]
    [InlineData("99999999.99", "99,999,999.99")]
    public void 展示文本_整数部分加千分位_小数部分与小数点原样(string text, string expected) =>
        Assert.Equal(expected, AmountInput.Format(text));

    [Fact]
    public void 还没敲就是空_敲了零不是空()
    {
        Assert.True(AmountInput.IsEmpty(""));
        Assert.True(AmountInput.IsEmpty(null));
        Assert.False(AmountInput.IsEmpty("0"));
        Assert.False(AmountInput.IsEmpty("0."));
    }

    [Theory]
    [InlineData("", "xl")]
    [InlineData("35.00", "xl")]
    [InlineData("12345", "xl")]
    [InlineData("1234567", "xl")]       // 1,234,567：9 个字符
    [InlineData("12345678", "lg")]      // 12,345,678：10 个字符
    [InlineData("12345678.9", "lg")]    // 12,345,678.9：12 个字符
    [InlineData("99999999.99", "md")]   // 99,999,999.99：13 个字符（上限）
    public void 字号档位随长度变小(string text, string expected) =>
        Assert.Equal(expected, AmountInput.SizeOf(text));

    [Theory]
    [InlineData("", "¥0.00")]
    [InlineData("35", "¥35.00")]
    [InlineData("35.5", "¥35.50")]
    [InlineData("12000.5", "¥12,000.50")]
    [InlineData(".", "¥0.00")]
    public void 读法带货币符号和两位小数(string text, string expected) =>
        Assert.Equal(expected, AmountInput.Spoken(text));
}
