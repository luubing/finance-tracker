using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 首页的展示规则（日期行、待确认横幅与角标文案、预算重点项、引导状态）：规则集中在 <see cref="HomePresentation"/>
/// </summary>
public class HomePresentationTests
{
    private static PendingBill Pending(string channel, BillSource source = BillSource.SmsRecognition) => new()
    {
        Amount = 10m,
        Channel = channel,
        Source = source,
    };

    private static BudgetStatus TotalBudget(string? ledger = null) => new()
    {
        BudgetId = Guid.NewGuid(),
        Amount = 5000m,
        UsedAmount = 1000m,
        CategoryName = "总预算",
        LedgerName = ledger,
    };

    private static BudgetStatus CategoryBudget(string name, decimal used) => new()
    {
        BudgetId = Guid.NewGuid(),
        Amount = 1000m,
        UsedAmount = used,
        CategoryId = Guid.NewGuid(),
        CategoryName = name,
    };

    /// <summary>按预算 Id 比较顺序（预算对象本身没有值相等语义）</summary>
    private static List<Guid> Ids(IEnumerable<BudgetStatus> budgets) => budgets.Select(b => b.BudgetId).ToList();

    // ---- 日期行 ----

    [Theory]
    [InlineData(2026, 11, 25, "11月25日 周三")]
    [InlineData(2026, 11, 29, "11月29日 周日")]
    [InlineData(2026, 1, 5, "1月5日 周一")]
    [InlineData(2026, 12, 12, "12月12日 周六")]
    public void 日期行是月日加星期_不补零(int year, int month, int day, string expected) =>
        Assert.Equal(expected, HomePresentation.DateLine(new DateTime(year, month, day, 13, 0, 0)));

    // ---- 通知入口 / 横幅 ----

    [Theory]
    [InlineData(1, "1")]
    [InlineData(3, "3")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(250, "99+")]
    public void 角标超过99显示99加(int count, string expected) =>
        Assert.Equal(expected, HomePresentation.BellBadge(count));

    [Fact]
    public void 通知入口的读屏名称带上笔数()
    {
        Assert.Equal("待确认账单", HomePresentation.BellLabel(0));
        Assert.Equal("待确认账单，3 笔", HomePresentation.BellLabel(3));
    }

    [Fact]
    public void 横幅标题带笔数()
    {
        Assert.Equal("3 笔支付等待确认", HomePresentation.BannerTitle(3));
        Assert.Equal("1 笔支付等待确认", HomePresentation.BannerTitle(1));
    }

    [Fact]
    public void 横幅副标题按捕获顺序列出渠道_重复的只列一次()
    {
        var subtitle = HomePresentation.BannerSubtitle([Pending("微信支付"), Pending("支付宝"), Pending("微信支付"), Pending("银行短信")]);

        Assert.Equal("微信支付 · 支付宝 · 银行短信", subtitle);
    }

    [Fact]
    public void 横幅副标题多于三种渠道时加等()
    {
        var subtitle = HomePresentation.BannerSubtitle([Pending("微信支付"), Pending("支付宝"), Pending("银行短信"), Pending("云闪付")]);

        Assert.Equal("微信支付 · 支付宝 · 银行短信 等", subtitle);
    }

    [Fact]
    public void 渠道为空时用数据来源代替_都没有信息时给通用提示()
    {
        Assert.Equal("短信识别 · 通知栏", HomePresentation.BannerSubtitle([
            Pending(string.Empty, BillSource.SmsRecognition),
            Pending("  ", BillSource.Notification),
        ]));

        Assert.Equal("点按查看并确认入账", HomePresentation.BannerSubtitle([]));
        Assert.Equal("点按查看并确认入账", HomePresentation.BannerSubtitle([Pending(string.Empty, BillSource.Manual)]));
    }

    [Theory]
    [InlineData(BillSource.SmsRecognition, "短信识别")]
    [InlineData(BillSource.Notification, "通知栏")]
    [InlineData(BillSource.Manual, "")]
    public void 数据来源文字(BillSource source, string expected) =>
        Assert.Equal(expected, HomePresentation.SourceLabel(source));

    // ---- 预算重点项 ----

    [Fact]
    public void 重点项是总预算在前_再按使用率从高到低取分类预算_一共最多三项()
    {
        var total = TotalBudget();
        var low = CategoryBudget("交通出行", 100m);
        var high = CategoryBudget("餐饮美食", 1200m);
        var middle = CategoryBudget("购物消费", 600m);

        var picked = HomePresentation.PickBudgetHighlights([low, total, middle, high]);

        Assert.Equal(Ids([total, high, middle]), Ids(picked));
    }

    [Fact]
    public void 使用率相同按分类名排序_结果稳定()
    {
        var b = CategoryBudget("B类", 500m);
        var a = CategoryBudget("A类", 500m);

        var picked = HomePresentation.PickBudgetHighlights([b, a]);

        Assert.Equal(Ids([a, b]), Ids(picked));
    }

    [Fact]
    public void 多个账本各有总预算时总预算都排在前面()
    {
        var first = TotalBudget("日常");
        var second = TotalBudget("旅行");
        var category = CategoryBudget("餐饮美食", 900m);

        var picked = HomePresentation.PickBudgetHighlights([first, second, category]);

        Assert.Equal(Ids([first, second, category]), Ids(picked));
    }

    [Fact]
    public void 没有总预算时只展示分类预算_最大条数为零或负数时为空()
    {
        var a = CategoryBudget("餐饮美食", 900m);

        Assert.Equal(Ids([a]), Ids(HomePresentation.PickBudgetHighlights([a])));
        Assert.Empty(HomePresentation.PickBudgetHighlights([a], max: 0));
        Assert.Empty(HomePresentation.PickBudgetHighlights([a], max: -1));
        Assert.Empty(HomePresentation.PickBudgetHighlights([]));
    }

    [Fact]
    public void 预算脚注只在还有更多预算时出现()
    {
        Assert.Equal("共 5 项预算，点「管理」查看全部", HomePresentation.BudgetFooter(5, 3));
        Assert.Null(HomePresentation.BudgetFooter(3, 3));
        Assert.Null(HomePresentation.BudgetFooter(0, 0));
    }

    // ---- 引导状态 ----

    [Theory]
    [InlineData(0, false, HomeGuide.CreateLedger)]
    [InlineData(0, true, HomeGuide.CreateLedgerHint)]
    [InlineData(1, false, HomeGuide.FirstBill)]
    [InlineData(1, true, HomeGuide.None)]
    [InlineData(5, true, HomeGuide.None)]
    [InlineData(-1, false, HomeGuide.CreateLedger)]
    public void 引导状态由账本数与有无账单决定(int ledgerCount, bool hasBills, HomeGuide expected) =>
        Assert.Equal(expected, HomePresentation.ResolveGuide(ledgerCount, hasBills));
}
