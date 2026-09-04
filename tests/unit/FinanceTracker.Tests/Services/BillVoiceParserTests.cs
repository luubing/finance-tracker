using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账单语音解析器单元测试
/// </summary>
public class BillVoiceParserTests
{
    private readonly BillVoiceParser _parser = new();

    private static readonly Guid DiningCategoryId = Guid.NewGuid();
    private static readonly Guid TransportCategoryId = Guid.NewGuid();
    private static readonly Guid SalaryCategoryId = Guid.NewGuid();

    private static readonly Guid WeChatChannelId = Guid.NewGuid();
    private static readonly Guid AlipayChannelId = Guid.NewGuid();
    private static readonly Guid CashChannelId = Guid.NewGuid();

    /// <summary>预设分类 + 渠道（与 PresetDataService 初始化的数据同名）</summary>
    private static readonly List<Category> Categories = new()
    {
        new Category { Id = DiningCategoryId, Name = "餐饮", Type = BillType.Expense },
        new Category { Id = TransportCategoryId, Name = "交通", Type = BillType.Expense },
        new Category { Id = SalaryCategoryId, Name = "工资", Type = BillType.Income }
    };

    private static readonly List<PaymentChannel> Channels = new()
    {
        new PaymentChannel { Id = WeChatChannelId, Name = "微信支付" },
        new PaymentChannel { Id = AlipayChannelId, Name = "支付宝" },
        new PaymentChannel { Id = CashChannelId, Name = "现金" }
    };

    private ParsedBillDraft Parse(string text)
        => _parser.Parse(text, Categories, Channels);

    // ===== 金额 =====

    [Theory]
    [InlineData("午饭花了35块", 35)]
    [InlineData("打车花费12.5元", 12.5)]
    [InlineData("花了￥88", 88)]
    [InlineData("花了￥88.90", 88.90)]
    [InlineData("人民币200", 200)]
    [InlineData("花了三十五块", 35)]
    [InlineData("下午茶两块五", 2.5)]
    [InlineData("房租三千五元", 3500)]
    [InlineData("外卖三十五点五块", 35.5)]
    [InlineData("地铁2号线花了50", 50)] // 无单位兜底：取最后一个数字
    public async Task ParseAsync_AmountExtracted(string text, double expected)
    {
        var draft = await _parser.ParseAsync(text, Categories, Channels);
        Assert.NotNull(draft.Amount);
        Assert.Equal((decimal)expected, draft.Amount.Value);
    }

    [Fact]
    public async Task ParseAsync_NoAmount_ReturnsNull()
    {
        var draft = await _parser.ParseAsync("今天中午吃的火锅", Categories, Channels);
        Assert.Null(draft.Amount);
    }

    // ===== 账单类型 =====

    [Fact]
    public async Task ParseAsync_DefaultExpense()
    {
        var draft = await _parser.ParseAsync("午饭花了35块", Categories, Channels);
        Assert.Equal(BillType.Expense, draft.Type);
    }

    [Fact]
    public async Task ParseAsync_IncomeKeywords()
    {
        var draft = await _parser.ParseAsync("今天工资到账8000块", Categories, Channels);
        Assert.Equal(BillType.Income, draft.Type);
    }

    [Fact]
    public async Task ParseAsync_ExpenseKeywordWins()
    {
        // "发红包" 是支出，尽管出现"红包"
        var draft = await _parser.ParseAsync("给侄子发红包花了200块", Categories, Channels);
        Assert.Equal(BillType.Expense, draft.Type);
    }

    // ===== 交易时间 =====

    [Fact]
    public async Task ParseAsync_Yesterday()
    {
        var draft = await _parser.ParseAsync("昨天午饭花了35块", Categories, Channels);
        Assert.NotNull(draft.TransactionTime);
        Assert.Equal(DateTime.Today.AddDays(-1), draft.TransactionTime.Value.Date);
    }

    [Fact]
    public async Task ParseAsync_NoTime_KeepsNull()
    {
        var draft = await _parser.ParseAsync("午饭花了35块", Categories, Channels);
        Assert.Null(draft.TransactionTime);
    }

    [Fact]
    public async Task ParseAsync_MonthDayInFuture_AssumesLastYear()
    {
        var draft = await _parser.ParseAsync("12月30号买了件衣服300块", Categories, Channels);
        Assert.NotNull(draft.TransactionTime);
        // 若今年 12 月 30 日尚未到来，应解析为去年
        Assert.True(draft.TransactionTime.Value.Date <= DateTime.Today);
    }

    // ===== 分类 / 支付渠道 =====

    [Fact]
    public async Task ParseAsync_CategoryBySynonym()
    {
        var draft = await _parser.ParseAsync("昨天午饭花了35块", Categories, Channels);
        Assert.Equal(DiningCategoryId, draft.CategoryId);
    }

    [Fact]
    public async Task ParseAsync_CategoryByName()
    {
        var draft = await _parser.ParseAsync("交通费花了20块", Categories, Channels);
        Assert.Equal(TransportCategoryId, draft.CategoryId);
    }

    [Fact]
    public async Task ParseAsync_IncomeCategoryMatchesType()
    {
        var draft = await _parser.ParseAsync("工资到账8000块", Categories, Channels);
        Assert.Equal(SalaryCategoryId, draft.CategoryId);
    }

    [Fact]
    public async Task ParseAsync_CategoryNotMatched_ReturnsNull()
    {
        var draft = await _parser.ParseAsync("花了35块", Categories, Channels);
        Assert.Null(draft.CategoryId);
    }

    [Theory]
    [InlineData("微信")]
    [InlineData("支付宝")]
    [InlineData("现金钞票")]
    public async Task ParseAsync_ChannelMatched(string keyword)
    {
        var draft = await _parser.ParseAsync($"买了杯咖啡25块，{keyword}付款", Categories, Channels);
        Assert.NotNull(draft.PaymentChannelId);
    }

    [Fact]
    public async Task ParseAsync_ChannelWeChat()
    {
        var draft = await _parser.ParseAsync("昨天午饭花了35块，微信支付", Categories, Channels);
        Assert.Equal(WeChatChannelId, draft.PaymentChannelId);
    }

    [Fact]
    public async Task ParseAsync_ChannelNotMatched_ReturnsNull()
    {
        var draft = await _parser.ParseAsync("花了35块", Categories, Channels);
        Assert.Null(draft.PaymentChannelId);
    }

    // ===== 完整场景 =====

    [Fact]
    public async Task ParseAsync_FullSentence()
    {
        var draft = await _parser.ParseAsync("昨天午饭花了三十五块，微信支付", Categories, Channels);

        Assert.Equal(35m, draft.Amount);
        Assert.Equal(BillType.Expense, draft.Type);
        Assert.Equal(DiningCategoryId, draft.CategoryId);
        Assert.Equal(WeChatChannelId, draft.PaymentChannelId);
        Assert.Equal(DateTime.Today.AddDays(-1), draft.TransactionTime!.Value.Date);
        Assert.Equal("昨天午饭花了三十五块，微信支付", draft.RawText);
    }
}