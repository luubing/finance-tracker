using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>把自动识别到的支付变成正式账单的规则（19 号）：选分类、匹配支付渠道、账单字段。</summary>
public sealed class PendingBillConversionTests
{
    private static Category Cat(string name, BillType type) => new() { Name = name, Type = type };

    private static PaymentChannel Channel(string name) => new() { Name = name };

    // ---- 选分类 ----

    [Fact]
    public void 选分类_优先预设的其他支出_即使自建的其他分类排在前面()
    {
        var custom = Cat("其他人代付", BillType.Expense);
        var preset = Cat("其他支出", BillType.Expense);

        var picked = PendingBillConversion.PickCategory(new[] { Cat("餐饮", BillType.Expense), custom, preset }, BillType.Expense);

        Assert.Same(preset, picked);
    }

    [Fact]
    public void 选分类_没有预设时取名称含其他的第一个()
    {
        var custom = Cat("其他杂项", BillType.Expense);

        var picked = PendingBillConversion.PickCategory(new[] { Cat("餐饮", BillType.Expense), custom, Cat("交通", BillType.Expense) }, BillType.Expense);

        Assert.Same(custom, picked);
    }

    [Fact]
    public void 选分类_既没有预设也没有其他分类时取该类型的第一个()
    {
        var first = Cat("餐饮", BillType.Expense);

        var picked = PendingBillConversion.PickCategory(new[] { first, Cat("交通", BillType.Expense) }, BillType.Expense);

        Assert.Same(first, picked);
    }

    [Fact]
    public void 选分类_收入账单不会取到支出分类_反之亦然()
    {
        // 旧的短信识别页给收入账单选分类时会取到名叫「其他支出」的支出分类
        var categories = new[] { Cat("其他支出", BillType.Expense), Cat("工资", BillType.Income), Cat("其他收入", BillType.Income) };

        var income = PendingBillConversion.PickCategory(categories, BillType.Income);
        var expense = PendingBillConversion.PickCategory(categories, BillType.Expense);

        Assert.Equal("其他收入", income!.Name);
        Assert.Equal(BillType.Income, income.Type);
        Assert.Equal("其他支出", expense!.Name);
        Assert.Equal(BillType.Expense, expense.Type);
    }

    [Fact]
    public void 选分类_该类型一个分类都没有返回空()
    {
        Assert.Null(PendingBillConversion.PickCategory(new[] { Cat("其他支出", BillType.Expense) }, BillType.Income));
        Assert.Null(PendingBillConversion.PickCategory(Array.Empty<Category>(), BillType.Expense));
    }

    // ---- 匹配支付渠道 ----

    [Fact]
    public void 匹配渠道_名称完全一致优先于包含()
    {
        var contains = Channel("微信支付（备用）");
        var exact = Channel("微信支付");

        Assert.Same(exact, PendingBillConversion.MatchChannel(new[] { contains, exact }, "微信支付"));
    }

    [Fact]
    public void 匹配渠道_没有完全一致时取名称包含识别名的第一个()
    {
        var contains = Channel("招商银行卡");

        Assert.Same(contains, PendingBillConversion.MatchChannel(new[] { Channel("现金"), contains }, "银行卡"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("花呗")]
    public void 匹配渠道_识别名为空或匹配不到时返回空_由用户在确认账单里选(string? name)
    {
        Assert.Null(PendingBillConversion.MatchChannel(new[] { Channel("微信支付"), Channel("支付宝") }, name));
    }

    [Fact]
    public void 匹配渠道_识别名两端的空白被忽略()
    {
        var wechat = Channel("微信支付");

        Assert.Same(wechat, PendingBillConversion.MatchChannel(new[] { wechat }, "  微信支付 "));
    }

    // ---- 组装账单 ----

    [Fact]
    public void 组装账单_字段沿用识别结果_来源按识别来源标记_同步状态为待同步()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var time = new DateTime(2026, 11, 25, 12, 8, 0);

        var bill = PendingBillConversion.NewBill(userId, 35.5m, BillType.Expense, categoryId, channelId, ledgerId, time, "向 星巴克 付款", BillSource.Notification);

        Assert.Equal(userId, bill.UserId);
        Assert.Equal(35.5m, bill.Amount);
        Assert.Equal(BillType.Expense, bill.Type);
        Assert.Equal(categoryId, bill.CategoryId);
        Assert.Equal(channelId, bill.PaymentChannelId);
        Assert.Equal(ledgerId, bill.LedgerId);
        Assert.Equal(time, bill.TransactionTime);
        Assert.Equal("向 星巴克 付款", bill.Note);
        Assert.Equal(BillSource.Notification, bill.Source);
        Assert.Equal(SyncStatus.Pending, bill.SyncStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n")]
    public void 组装账单_空白备注存成空(string? note)
    {
        var bill = PendingBillConversion.NewBill(Guid.NewGuid(), 10m, BillType.Income, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.Now, note, BillSource.SmsRecognition);

        Assert.Null(bill.Note);
        Assert.Equal(BillSource.SmsRecognition, bill.Source);
    }
}
