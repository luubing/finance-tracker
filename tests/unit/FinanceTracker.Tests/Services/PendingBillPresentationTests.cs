using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>待确认账单页的展示规则（19 号）：来源行、备注摘要、卡片内容、账本解析、校验与提示。</summary>
public sealed class PendingBillPresentationTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 15, 30, 0);

    private static PendingBill Bill(BillType type = BillType.Expense, decimal amount = 35m, string channel = "微信支付", string? note = null,
        BillSource source = BillSource.Notification, DateTime? time = null) => new()
        {
            Amount = amount,
            Type = type,
            Channel = channel,
            Note = note,
            Source = source,
            TransactionTime = time ?? new DateTime(2026, 11, 25, 12, 8, 0),
        };

    // ---- 权限状态 ----

    [Theory]
    [InlineData(true, true, CaptureStatus.Ready)]
    [InlineData(true, false, CaptureStatus.Partial)]
    [InlineData(false, true, CaptureStatus.Partial)]
    [InlineData(false, false, CaptureStatus.Off)]
    public void 两项识别的整体状态_都开_只开一项_都没开(bool notification, bool sms, CaptureStatus expected)
    {
        Assert.Equal(expected, PendingBillPresentation.CaptureStatusOf(notification, sms));
    }

    [Fact]
    public void 权限行副标题_已开启不需要说明_未开启给开启后的好处()
    {
        Assert.Null(PendingBillPresentation.NotificationSubtitle(true));
        Assert.Null(PendingBillPresentation.SmsSubtitle(true));
        Assert.Equal(PendingBillPresentation.NotificationHint, PendingBillPresentation.NotificationSubtitle(false));
        Assert.Equal(PendingBillPresentation.SmsHint, PendingBillPresentation.SmsSubtitle(false));
    }

    [Fact]
    public void 权限行读屏名称带上开启状态()
    {
        Assert.Equal("通知栏识别，已开启", PendingBillPresentation.PermissionAriaLabel(PendingBillPresentation.NotificationTitle, true));
        Assert.Equal("短信识别，未开启", PendingBillPresentation.PermissionAriaLabel(PendingBillPresentation.SmsTitle, false));
    }

    // ---- 列表文字 ----

    [Theory]
    [InlineData(0, "0 笔待确认")]
    [InlineData(3, "3 笔待确认")]
    [InlineData(-2, "0 笔待确认")]
    public void 数量文字不会出现负数(int count, string expected)
    {
        Assert.Equal(expected, PendingBillPresentation.CountText(count));
    }

    [Theory]
    [InlineData("微信支付", BillSource.Notification, "微信支付 · 通知栏")]
    [InlineData("银行卡", BillSource.SmsRecognition, "银行卡 · 短信识别")]
    [InlineData("  支付宝  ", BillSource.Notification, "支付宝 · 通知栏")]
    [InlineData("", BillSource.SmsRecognition, "短信识别")]
    [InlineData(null, BillSource.Notification, "通知栏")]
    public void 来源行是渠道加来源_渠道为空时只有来源(string? channel, BillSource source, string expected)
    {
        Assert.Equal(expected, PendingBillPresentation.SourceLine(channel, source));
    }

    [Fact]
    public void 待确认项不会有的数据来源统一叫自动识别()
    {
        Assert.Equal("自动识别", PendingBillPresentation.SourceLabel(BillSource.Manual));
    }

    [Fact]
    public void 备注摘要折叠换行与连续空白()
    {
        var summary = PendingBillPresentation.NoteSummary("  微信支付\r\n  向 星巴克\t\t付款  ");

        Assert.Equal("微信支付 向 星巴克 付款", summary);
    }

    [Fact]
    public void 备注摘要超过上限加省略号_恰好等于上限不加()
    {
        var exact = new string('字', PendingBillPresentation.NoteMaxLength);
        var longer = exact + "多";

        Assert.Equal(exact, PendingBillPresentation.NoteSummary(exact));
        Assert.Equal(exact + "…", PendingBillPresentation.NoteSummary(longer));
    }

    [Fact]
    public void 备注摘要不会把表情符号从中间截断()
    {
        // 前 39 个字 + 一个代理对表情（占 2 个 UTF-16 单元）：第 40 个单元正好落在代理对中间
        var note = new string('字', PendingBillPresentation.NoteMaxLength - 1) + "😀" + "尾巴";

        var summary = PendingBillPresentation.NoteSummary(note);

        Assert.Equal(new string('字', PendingBillPresentation.NoteMaxLength - 1) + "…", summary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n ")]
    public void 备注摘要在没有备注时为空(string? note)
    {
        Assert.Equal(string.Empty, PendingBillPresentation.NoteSummary(note));
    }

    [Theory]
    [InlineData(BillType.Expense, "支出")]
    [InlineData(BillType.Income, "收入")]
    public void 类型文字(BillType type, string expected)
    {
        Assert.Equal(expected, PendingBillPresentation.TypeLabel(type));
    }

    [Theory]
    [InlineData(35.0, BillType.Expense, "-¥35.00")]
    [InlineData(12000.0, BillType.Income, "+¥12,000.00")]
    [InlineData(-8.5, BillType.Expense, "-¥8.50")]
    public void 卡片金额文字_支出负号_收入正号(double amount, BillType type, string expected)
    {
        Assert.Equal(expected, PendingBillPresentation.AmountText((decimal)amount, type));
    }

    [Theory]
    [InlineData("-¥35.00", false)]
    [InlineData("-¥1,234.50", false)]
    [InlineData("-¥12,345,678.00", true)]
    [InlineData("+¥99,999,999.99", true)]
    public void 金额位数多时卡片缩小一档字号(string text, bool expected)
    {
        Assert.Equal(expected, PendingBillPresentation.IsLongAmount(text));
    }

    [Fact]
    public void 卡片时间文字_今天_昨天_同年_跨年()
    {
        Assert.Equal("今天 12:08", PendingBillPresentation.TimeText(new DateTime(2026, 11, 25, 12, 8, 0), Now));
        Assert.Equal("昨天 23:59", PendingBillPresentation.TimeText(new DateTime(2026, 11, 24, 23, 59, 0), Now));
        Assert.Equal("11月23日 09:30", PendingBillPresentation.TimeText(new DateTime(2026, 11, 23, 9, 30, 0), Now));
        Assert.Equal("2025年12月31日 08:00", PendingBillPresentation.TimeText(new DateTime(2025, 12, 31, 8, 0, 0), Now));
    }

    [Fact]
    public void 读屏摘要包含类型_金额_来源和时间()
    {
        var summary = PendingBillPresentation.Summary(BillType.Expense, 35m, "微信支付 · 通知栏", "今天 12:08");

        Assert.Equal("支出 ¥35.00，微信支付 · 通知栏，今天 12:08", summary);
    }

    // ---- 卡片 ----

    [Fact]
    public void 卡片内容_有分类和渠道时用它们的名称与图标()
    {
        var category = new Category { Name = "其他支出", Icon = "mdi-dots-horizontal", Type = BillType.Expense };
        var channel = new PaymentChannel { Name = "微信支付", Icon = "mdi-wechat" };
        var bill = Bill(note: "向 星巴克\n付款");

        var card = PendingBillPresentation.BuildCard(bill, category, channel, Now);

        Assert.Equal(bill.Id, card.Id);
        Assert.Equal("微信支付 · 通知栏", card.SourceLine);
        Assert.Equal("今天 12:08", card.TimeText);
        Assert.Equal("其他支出", card.CategoryName);
        Assert.Equal("mdi-dots-horizontal", card.CategoryIcon);
        Assert.Equal("mdi-wechat", card.ChannelIcon);
        Assert.Equal("向 星巴克 付款", card.NoteText);
        Assert.Equal("-¥35.00", card.AmountText);
        Assert.False(card.IsIncome);
        Assert.False(card.IsSms);
        Assert.Equal("支出 ¥35.00，微信支付 · 通知栏，今天 12:08", card.Summary);
    }

    [Fact]
    public void 卡片内容_没有分类时按类型显示其他分类_没匹配到渠道时用默认图标()
    {
        var expense = PendingBillPresentation.BuildCard(Bill(), category: null, channel: null, Now);
        var income = PendingBillPresentation.BuildCard(Bill(BillType.Income, 12000m, source: BillSource.SmsRecognition), category: null, channel: null, Now);

        Assert.Equal("其他支出", expense.CategoryName);
        Assert.Equal("其他收入", income.CategoryName);
        Assert.Equal(PaymentChannelPresentation.DefaultIcon, expense.ChannelIcon);
        Assert.Equal(BillPresentation.DefaultCategoryIcon, expense.CategoryIcon);
        Assert.True(income.IsIncome);
        Assert.True(income.IsSms);
        Assert.Equal("+¥12,000.00", income.AmountText);
    }

    // ---- 账本 ----

    [Theory]
    [InlineData(null, "选择账本")]
    [InlineData("", "选择账本")]
    [InlineData("  ", "选择账本")]
    [InlineData("日常", "日常")]
    public void 账本芯片文字(string? name, string expected)
    {
        Assert.Equal(expected, PendingBillPresentation.LedgerChipText(name));
    }

    [Fact]
    public void 卡片账本_自己选的优先_其次默认账本_都不在可选账本里就是没有()
    {
        var a = new LedgerModel { Id = Guid.NewGuid(), Name = "日常" };
        var b = new LedgerModel { Id = Guid.NewGuid(), Name = "旅行" };
        var gone = Guid.NewGuid();
        var ledgers = new[] { a, b };

        Assert.Equal(b.Id, PendingBillPresentation.ResolveLedger(b.Id, a.Id, ledgers));
        Assert.Equal(a.Id, PendingBillPresentation.ResolveLedger(null, a.Id, ledgers));
        Assert.Equal(a.Id, PendingBillPresentation.ResolveLedger(gone, a.Id, ledgers));
        Assert.Null(PendingBillPresentation.ResolveLedger(gone, gone, ledgers));
        Assert.Null(PendingBillPresentation.ResolveLedger(null, null, ledgers));
        Assert.Null(PendingBillPresentation.ResolveLedger(a.Id, a.Id, Array.Empty<LedgerModel>()));
    }

    // ---- 校验 ----

    [Fact]
    public void 校验顺序_金额_支付渠道_账本()
    {
        var channel = Guid.NewGuid();
        var ledger = Guid.NewGuid();

        Assert.Equal(PendingBillPresentation.InvalidAmountMessage, PendingBillPresentation.Validate(null, channel, ledger));
        Assert.Equal(PendingBillPresentation.InvalidAmountMessage, PendingBillPresentation.Validate(0m, channel, ledger));
        Assert.Equal(PendingBillPresentation.InvalidAmountMessage, PendingBillPresentation.Validate(-1m, null, null));
        Assert.Equal(PendingBillPresentation.ChooseChannelMessage, PendingBillPresentation.Validate(10m, null, null));
        Assert.Equal(PendingBillPresentation.ChooseChannelMessage, PendingBillPresentation.Validate(10m, Guid.Empty, ledger));
        Assert.Equal(PendingBillPresentation.ChooseLedgerMessage, PendingBillPresentation.Validate(10m, channel, null));
        Assert.Equal(PendingBillPresentation.ChooseLedgerMessage, PendingBillPresentation.Validate(10m, channel, Guid.Empty));
        Assert.Null(PendingBillPresentation.Validate(10m, channel, ledger));
    }

    // ---- 提示 ----

    [Fact]
    public void 各处提示的措辞()
    {
        Assert.Equal("3 笔待确认账单将被移除，不会计入账单。", PendingBillPresentation.IgnoreAllMessage(3));
        Assert.Equal("创建账单失败: 网络错误", PendingBillPresentation.CreateFailedMessage("网络错误"));
        Assert.Equal("没有可用的支出分类，请先在「分类」里新建一个", PendingBillPresentation.NoCategoryMessage(BillType.Expense));
        Assert.Equal("没有可用的收入分类，请先在「分类」里新建一个", PendingBillPresentation.NoCategoryMessage(BillType.Income));
        Assert.Equal("加载失败: 超时", PendingBillPresentation.LoadFailedMessage("超时"));
        Assert.Equal("忽略失败: 超时", PendingBillPresentation.IgnoreFailedMessage("超时"));
        Assert.Equal("开启失败: 被拒绝", PendingBillPresentation.PermissionFailedMessage("被拒绝"));
    }

    [Fact]
    public void 移除失败的提示明确说账单已创建_并让用户点忽略而不是重复确认()
    {
        var message = PendingBillPresentation.RemoveFailedMessage("数据库繁忙");

        Assert.StartsWith("账单已创建", message);
        Assert.Contains("忽略", message);
        Assert.Contains("不要重复确认", message);
        Assert.DoesNotContain("创建失败", message);
    }

    [Fact]
    public void 界面文案遵守领域术语_不出现被禁用的词()
    {
        var texts = new[]
        {
            PendingBillPresentation.Title, PendingBillPresentation.StatsNote, PendingBillPresentation.EmptyTitle, PendingBillPresentation.EmptyText,
            PendingBillPresentation.ChooseChannelMessage, PendingBillPresentation.ChannelNotMatchedHint, PendingBillPresentation.NotificationHint,
            PendingBillPresentation.SmsHint, PendingBillPresentation.NoCategoryMessage(BillType.Expense),
        };

        foreach (var text in texts)
        {
            Assert.DoesNotContain("支付方式", text);
            Assert.DoesNotContain("付款方式", text);
            Assert.DoesNotContain("标签", text);
            Assert.DoesNotContain("交易记录", text);
        }
    }
}
