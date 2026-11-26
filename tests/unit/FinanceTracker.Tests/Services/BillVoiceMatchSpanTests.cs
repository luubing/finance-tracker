using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 语音解析的「命中片段」（<see cref="ParsedBillDraft.Matches"/>）单元测试：
/// 界面（17 号语音覆盖层）据此在转写文本里高亮识别出的金额 / 分类 / 支付渠道 / 时间。
/// 片段只是解析结果的位置标注，解析规则本身由 <see cref="BillVoiceParserTests"/> 覆盖、不受影响。
/// </summary>
public class BillVoiceMatchSpanTests
{
    private readonly BillVoiceParser _parser = new();

    private static readonly Guid DiningCategoryId = Guid.NewGuid();
    private static readonly Guid TransportCategoryId = Guid.NewGuid();
    private static readonly Guid SalaryCategoryId = Guid.NewGuid();

    private static readonly Guid WeChatChannelId = Guid.NewGuid();
    private static readonly Guid AlipayChannelId = Guid.NewGuid();

    private static readonly List<Category> Categories =
    [
        new() { Id = DiningCategoryId, Name = "餐饮", Type = BillType.Expense },
        new() { Id = TransportCategoryId, Name = "交通", Type = BillType.Expense },
        new() { Id = SalaryCategoryId, Name = "工资", Type = BillType.Income },
    ];

    private static readonly List<PaymentChannel> Channels =
    [
        new() { Id = WeChatChannelId, Name = "微信支付" },
        new() { Id = AlipayChannelId, Name = "支付宝" },
    ];

    private ParsedBillDraft Parse(string text) => _parser.Parse(text, Categories, Channels);

    /// <summary>取出某一种片段在原文里对应的文字；没有这种片段返回 null</summary>
    private static string? TextOf(ParsedBillDraft draft, VoiceMatchKind kind)
    {
        foreach (var span in draft.Matches)
        {
            if (span.Kind == kind)
            {
                return draft.RawText.Substring(span.Start, span.Length);
            }
        }

        return null;
    }

    [Fact]
    public void 四类字段都识别出来_各自定位到原文里的那一段()
    {
        var draft = Parse("昨天午饭花了三十五块，用微信支付");

        Assert.Equal("昨天", TextOf(draft, VoiceMatchKind.Time));
        Assert.Equal("午饭", TextOf(draft, VoiceMatchKind.Category));
        Assert.Equal("三十五块", TextOf(draft, VoiceMatchKind.Amount));
        Assert.Equal("微信支付", TextOf(draft, VoiceMatchKind.PaymentChannel));
    }

    [Fact]
    public void 片段按出现位置排序()
    {
        var draft = Parse("昨天午饭花了三十五块，用微信支付");

        Assert.Equal(
            [VoiceMatchKind.Time, VoiceMatchKind.Category, VoiceMatchKind.Amount, VoiceMatchKind.PaymentChannel],
            draft.Matches.Select(m => m.Kind).ToArray());
        Assert.Equal(draft.Matches.OrderBy(m => m.Start).ToArray(), draft.Matches.ToArray());
    }

    [Fact]
    public void 解析没认出的字段不产生片段()
    {
        // 只有时间：没有金额 / 分类 / 渠道
        var draft = Parse("今天中午吃的火锅");

        Assert.Equal("今天", TextOf(draft, VoiceMatchKind.Time));
        Assert.Null(TextOf(draft, VoiceMatchKind.Amount));
        Assert.Null(TextOf(draft, VoiceMatchKind.Category));
        Assert.Null(TextOf(draft, VoiceMatchKind.PaymentChannel));
        Assert.Single(draft.Matches);
    }

    [Fact]
    public void 什么都没认出时片段为空()
    {
        var draft = Parse("嗯那个");

        Assert.Empty(draft.Matches);
    }

    [Theory]
    [InlineData("午饭花了35块", "35块")]
    [InlineData("打车花费12.5元", "12.5元")]
    [InlineData("花了￥88.90", "￥88.90")]
    [InlineData("人民币200", "人民币200")]
    [InlineData("房租三千五元", "三千五元")]
    [InlineData("外卖三十五点五块", "三十五点五块")]
    public void 金额的各种写法都整段高亮(string text, string expected)
    {
        Assert.Equal(expected, TextOf(Parse(text), VoiceMatchKind.Amount));
    }

    [Fact]
    public void 块后跟一位数字_是金额的一部分_取更长的那一段()
    {
        // 「两块五」= 2.5：片段是整个「两块五」，而不是只有「两块」
        Assert.Equal("两块五", TextOf(Parse("下午茶两块五"), VoiceMatchKind.Amount));
        Assert.Equal("三十五块五", TextOf(Parse("花了三十五块五"), VoiceMatchKind.Amount));
    }

    [Fact]
    public void 无单位数字的兜底_高亮最后一个数字()
    {
        var draft = Parse("地铁2号线花了50");

        Assert.Equal(50m, draft.Amount);
        Assert.Equal("50", TextOf(draft, VoiceMatchKind.Amount));
    }

    [Theory]
    [InlineData("大前天买了东西30块", "大前天")]
    [InlineData("前天打车20块", "前天")]
    [InlineData("昨日午饭30块", "昨日")]
    [InlineData("今天午饭30块", "今天")]
    [InlineData("上周五买了件衣服200块", "上周五")]
    [InlineData("上周买了件衣服200块", "上周")]
    [InlineData("上个月交了房租3000块", "上个月")]
    [InlineData("5月20号买了件衣服300块", "5月20号")]
    [InlineData("15号买了件衣服300块", "15号")]
    public void 时间的各种说法都高亮(string text, string expected)
    {
        Assert.Equal(expected, TextOf(Parse(text), VoiceMatchKind.Time));
    }

    [Fact]
    public void 渠道说同义词_高亮同义词本身()
    {
        // 「微信」→ 微信支付；片段是用户实际说的「微信」，不是渠道全名
        var draft = Parse("用微信付了28块");

        Assert.Equal(WeChatChannelId, draft.PaymentChannelId);
        Assert.Equal("微信", TextOf(draft, VoiceMatchKind.PaymentChannel));
    }

    [Fact]
    public void 渠道说全名_高亮全名_而不是更短的同义词()
    {
        var draft = Parse("用微信支付付了28块");

        Assert.Equal("微信支付", TextOf(draft, VoiceMatchKind.PaymentChannel));
    }

    [Fact]
    public void 同义词匹配不区分大小写_片段保留用户说的写法()
    {
        var draft = Parse("VX付了20块");

        Assert.Equal(WeChatChannelId, draft.PaymentChannelId);
        Assert.Equal("VX", TextOf(draft, VoiceMatchKind.PaymentChannel));
    }

    [Fact]
    public void 分类说同义词_高亮同义词本身()
    {
        var draft = Parse("打车花了20块");

        Assert.Equal(TransportCategoryId, draft.CategoryId);
        Assert.Equal("打车", TextOf(draft, VoiceMatchKind.Category));
    }

    [Fact]
    public void 分类说名称本身_高亮名称()
    {
        var draft = Parse("收到工资8000块");

        Assert.Equal(BillType.Income, draft.Type);
        Assert.Equal(SalaryCategoryId, draft.CategoryId);
        Assert.Equal("工资", TextOf(draft, VoiceMatchKind.Category));
    }

    [Fact]
    public void 同义词命中多个_取最长的那个()
    {
        // 「骑车」「共享单车」都是交通的同义词且都出现了，「共享单车」更长，解析按它命中
        var draft = Parse("骑车去，共享单车花了3块");

        Assert.Equal(TransportCategoryId, draft.CategoryId);
        Assert.Equal("共享单车", TextOf(draft, VoiceMatchKind.Category));
    }

    [Fact]
    public void 用户自定义分类_说出名称就能高亮()
    {
        var customId = Guid.NewGuid();
        var categories = new List<Category>(Categories)
        {
            new() { Id = customId, Name = "宠物", Type = BillType.Expense },
        };

        var draft = _parser.Parse("给猫买了宠物零食30块", categories, Channels);

        Assert.Equal(customId, draft.CategoryId);
        Assert.Equal("宠物", TextOf(draft, VoiceMatchKind.Category));
    }

    [Fact]
    public void 片段位置指向原文_长度不越界()
    {
        var draft = Parse("前天晚饭在支付宝花了一百二十块");

        Assert.NotEmpty(draft.Matches);
        Assert.All(draft.Matches, span =>
        {
            Assert.True(span.Start >= 0);
            Assert.True(span.Length > 0);
            Assert.True(span.Start + span.Length <= draft.RawText.Length);
        });
        Assert.Equal("一百二十块", TextOf(draft, VoiceMatchKind.Amount));
        Assert.Equal("支付宝", TextOf(draft, VoiceMatchKind.PaymentChannel));
        Assert.Equal("前天", TextOf(draft, VoiceMatchKind.Time));
        Assert.Equal("晚饭", TextOf(draft, VoiceMatchKind.Category));
    }

    [Fact]
    public async Task 异步入口同样带片段()
    {
        var draft = await _parser.ParseAsync("昨天午饭花了35块", Categories, Channels);

        Assert.Equal("35块", TextOf(draft, VoiceMatchKind.Amount));
    }

    [Fact]
    public void 命中片段不改变解析结果()
    {
        // 与 BillVoiceParserTests 的口径一致：片段只是标注位置
        var draft = Parse("昨天午饭花了三十五块，用微信支付");

        Assert.Equal(35m, draft.Amount);
        Assert.Equal(BillType.Expense, draft.Type);
        Assert.Equal(DiningCategoryId, draft.CategoryId);
        Assert.Equal(WeChatChannelId, draft.PaymentChannelId);
        Assert.Equal(DateTime.Today.AddDays(-1), draft.TransactionTime!.Value.Date);
        Assert.Equal("昨天午饭花了三十五块，用微信支付", draft.RawText);
    }
}
