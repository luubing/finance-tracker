using System.Globalization;
using FinanceTracker.Core.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 支付内容解析器（P12）：短信识别页、短信广播接收器、通知栏监听三个入口共用同一套解析规则。
/// 此前短信识别页自带一份重复实现（还用了与区域相关的 <c>decimal.Parse</c>），同一条短信在不同入口可能得到不同结果；
/// 现在三处都走 <see cref="PaymentContentParser.Parse"/>，规则只在这里维护、只在这里测试。
/// </summary>
public class PaymentContentParserTests
{
    private static decimal AmountOf(string content) => PaymentContentParser.Parse(content).Amount;

    private static decimal Dec(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    // ───────────────────────── 金额 ─────────────────────────

    [Theory]
    [InlineData("您尾号1234的储蓄卡支出35.50元，请注意资金安全", "35.50")]
    [InlineData("您尾号1234的储蓄卡支出35元", "35")]
    [InlineData("消费人民币200，请注意资金安全", "200")]
    [InlineData("微信支付 ¥88.90", "88.90")]
    [InlineData("交易金额 66.60 已完成", "66.60")]
    [InlineData("支出0.99元", "0.99")]
    public void 金额_支持元_人民币_半角符号_金额四种写法(string content, string expected)
    {
        Assert.Equal(Dec(expected), AmountOf(content));
    }

    [Theory]
    [InlineData("您的账户消费￥88.90", "88.90")]
    [InlineData("支付宝付款 ￥ 12.5", "12.5")]
    public void 金额_全角货币符号也能识别(string content, string expected)
    {
        // 银行/支付类短信里的"￥"多半是全角（U+FFE5），与半角"¥"（U+00A5）不是同一个字符
        Assert.Equal(Dec(expected), AmountOf(content));
    }

    [Theory]
    [InlineData("转账1,234.50元，已到账", "1234.50")]
    [InlineData("您尾号1234的储蓄卡支出人民币12,345,678.90元", "12345678.90")]
    [InlineData("消费￥1,000", "1000")]
    [InlineData("您的信用卡消费1,234元", "1234")]
    [InlineData("交易金额 2,500.00 元", "2500.00")]
    public void 金额_千分位逗号按完整数字解析_而不是只取最后一段(string content, string expected)
    {
        // 只认 \d+\.?\d* 的话，"1,234.50元" 会被截成 "234.50"，金额静默少掉一个千位
        Assert.Equal(Dec(expected), AmountOf(content));
    }

    [Fact]
    public void 金额_同时出现多种写法时先取数字加元()
    {
        // 既有优先级：先找"数字+元"，再找"人民币"、"¥"、"金额"。改动优先级要同步评估三个入口的预期
        Assert.Equal(50m, AmountOf("人民币100，实付50元"));
        Assert.Equal(100m, AmountOf("人民币100，金额200"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("您的验证码是123456，5分钟内有效")]
    [InlineData("您的账单已生成，请及时查看")]
    public void 提取不到金额_Amount为0_调用方据此跳过或让用户手动补全(string content)
    {
        Assert.Equal(0m, AmountOf(content));
    }

    [Fact]
    public void 数字大到超出decimal范围_不抛异常_按提取不到金额处理()
    {
        // 此前 decimal.Parse 会在这里抛 OverflowException，把短信广播/通知监听的处理流程打断
        var huge = new string('9', 30);

        var result = PaymentContentParser.Parse($"支出{huge}元");

        Assert.Equal(0m, result.Amount);
    }

    [Fact]
    public void 解析与当前区域无关_德语区域下小数点和千分位仍按约定解析()
    {
        // 德语区域里"."是千分位、","才是小数点：区域相关的 decimal.Parse 会把 "35.5" 解析成 355
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal(35.5m, AmountOf("支出35.5元"));
            Assert.Equal(1234.5m, AmountOf("支出1,234.5元"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ───────────────────────── 收入 / 支出 ─────────────────────────

    [Theory]
    [InlineData("工资收入5000元", true)]
    [InlineData("您尾号1234的账户到账100元", true)]
    [InlineData("资金转入200元", true)]
    [InlineData("退款35元已原路返回", true)]
    [InlineData("您尾号1234的储蓄卡支出35元", false)]
    [InlineData("微信支付成功，消费12.8元", false)]
    public void 收入关键词_收入_到账_转入_退款(string content, bool expectedIsIncome)
    {
        Assert.Equal(expectedIsIncome, PaymentContentParser.Parse(content).IsIncome);
    }

    // ───────────────────────── 支付渠道 ─────────────────────────

    [Theory]
    [InlineData("微信支付成功，消费35元", "微信支付")]
    [InlineData("支付宝付款35元", "支付宝")]
    [InlineData("花呗还款35元", "支付宝")]
    [InlineData("余额宝转入35元", "支付宝")]
    [InlineData("京东白条还款35元", "京东支付")]
    [InlineData("美团外卖消费35元", "美团支付")]
    [InlineData("云闪付消费35元", "云闪付")]
    [InlineData("银联入账35元", "云闪付")]
    [InlineData("您尾号1234的储蓄卡支出35元", "银行卡")]
    public void 渠道识别_关键词映射到支付渠道名(string content, string expectedChannel)
    {
        Assert.Equal(expectedChannel, PaymentContentParser.Parse(content).Channel);
    }

    [Fact]
    public void 同时含多个渠道关键词_按微信_支付宝_京东_美团_云闪付的顺序取第一个命中()
    {
        Assert.Equal("微信支付", PaymentContentParser.Parse("微信里用京东白条消费35元").Channel);
        Assert.Equal("支付宝", PaymentContentParser.Parse("支付宝里的美团消费35元").Channel);
    }

    // ───────────────────────── 其它 ─────────────────────────

    [Fact]
    public void Description保留短信原文()
    {
        const string content = "您尾号1234的储蓄卡支出35.50元";

        Assert.Equal(content, PaymentContentParser.Parse(content).Description);
    }

    [Fact]
    public void 通知栏只处理微信和支付宝的包名()
    {
        Assert.Contains("com.tencent.mm", PaymentContentParser.PaymentAppPackageNames);
        Assert.Contains("com.eg.android.AlipayGphone", PaymentContentParser.PaymentAppPackageNames);
        Assert.Equal(2, PaymentContentParser.PaymentAppPackageNames.Count);
    }
}
