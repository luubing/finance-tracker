using System.Text;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>账单文件的编码检测（20 号：从导入页拆出来的 CsvFileDecoder，判断顺序与旧页面一致）。</summary>
public sealed class CsvFileDecoderTests
{
    private const string WeChatCsv =
        "微信支付账单明细\n" +
        "交易时间,交易类型,交易对方,商品,收/支,金额(元),支付方式,当前状态,交易单号,商户单号,备注\n" +
        "2026/10/1 12:30,商户消费,美团,午餐,支出,¥35.00,零钱,支付成功,4200001,M001,/\n";

    public CsvFileDecoderTests()
    {
        // GB18030 属于代码页编码，测试里自己编码也需要先注册（与 CsvFileDecoder 的静态构造做同一件事，重复注册无副作用）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static byte[] WithBom(byte[] bom, byte[] body) => [.. bom, .. body];

    [Fact]
    public void 带BOM的UTF8去掉BOM后解码()
    {
        var bytes = WithBom([0xEF, 0xBB, 0xBF], Encoding.UTF8.GetBytes(WeChatCsv));

        Assert.Equal(WeChatCsv, CsvFileDecoder.Decode(bytes));
    }

    [Fact]
    public void 带BOM的UTF16小端去掉BOM后解码()
    {
        var bytes = WithBom([0xFF, 0xFE], Encoding.Unicode.GetBytes(WeChatCsv));

        Assert.Equal(WeChatCsv, CsvFileDecoder.Decode(bytes));
    }

    [Fact]
    public void 带BOM的UTF16大端去掉BOM后解码()
    {
        var bytes = WithBom([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes(WeChatCsv));

        Assert.Equal(WeChatCsv, CsvFileDecoder.Decode(bytes));
    }

    [Fact]
    public void 没有BOM的UTF8有账单表头字样就按UTF8解码()
    {
        Assert.Equal(WeChatCsv, CsvFileDecoder.Decode(Encoding.UTF8.GetBytes(WeChatCsv)));

        // 只有「交易类型」一个字样也算（旧页面的判断：交易时间 或 交易类型）
        const string onlyType = "交易类型,金额\n消费,1.00\n";
        Assert.Equal(onlyType, CsvFileDecoder.Decode(Encoding.UTF8.GetBytes(onlyType)));
    }

    [Fact]
    public void 微信支付宝常见的GB18030编码能正确解码()
    {
        var bytes = Encoding.GetEncoding("gb18030").GetBytes(WeChatCsv);

        Assert.Equal(WeChatCsv, CsvFileDecoder.Decode(bytes));
    }

    [Fact]
    public void GB18030能表示的生僻字与全角符号也不丢()
    {
        const string text = "交易时间,商品\n2026-10-01 12:30:00,〇㐀 ￥35.00（元）\n";
        var bytes = Encoding.GetEncoding("gb18030").GetBytes(text);

        Assert.Equal(text, CsvFileDecoder.Decode(bytes));
    }

    [Fact]
    public void 空文件解码为空串()
    {
        Assert.Equal(string.Empty, CsvFileDecoder.Decode([]));
    }

    [Fact]
    public void 不接受null()
    {
        Assert.Throws<ArgumentNullException>(() => CsvFileDecoder.Decode(null!));
    }
}
