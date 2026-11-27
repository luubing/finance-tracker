using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账单 Tab 列表的展示规则：按天分组（应用时区）、组头文案、收支合计、空状态与批量选择 / 删除文案、分段控件取值，
/// 以及筛选状态到服务层查询条件的转换
/// </summary>
public class BillListPresentationTests
{
    // 2026-11-25（周三）20:00，应用时区的「现在」
    private static readonly DateTime Now = new(2026, 11, 25, 20, 0, 0);
    private static readonly Guid Me = Guid.NewGuid();

    private static BillListItem Item(
        string localTime,
        decimal amount = 10m,
        bool income = false,
        bool canModify = true,
        string title = "餐饮美食")
    {
        var local = DateTime.Parse(localTime, System.Globalization.CultureInfo.InvariantCulture);
        var row = new BillRowModel(
            Guid.NewGuid(), title, "微信支付 · 工作日午餐",
            MoneyPresentation.Signed(amount, income), income, local.ToString("HH:mm"), "mdi-tag", "orange", title);

        return new BillListItem(row, local, amount, income, canModify);
    }

    // ───────────────────────── 分组 ─────────────────────────

    [Fact]
    public void 按应用时区的日期分组_日期晚的在前_同日内时间晚的在前()
    {
        var a = Item("2026-11-25 08:41");
        var b = Item("2026-11-25 12:08");
        var c = Item("2026-11-24 10:00");
        var d = Item("2026-11-25 09:32");

        var groups = BillListPresentation.Group([a, b, c, d], Now);

        Assert.Equal(new[] { new DateTime(2026, 11, 25), new DateTime(2026, 11, 24) }, groups.Select(g => g.Date));
        Assert.Equal(new[] { b.Id, d.Id, a.Id }, groups[0].Items.Select(i => i.Id));
        Assert.Equal(new[] { c.Id }, groups[1].Items.Select(i => i.Id));
    }

    [Fact]
    public void 同一时刻的账单保持传入顺序_不打乱服务层排好的次序()
    {
        var first = Item("2026-11-25 12:00", title: "先");
        var second = Item("2026-11-25 12:00", title: "后");

        var groups = BillListPresentation.Group([first, second], Now);

        Assert.Equal(new[] { first.Id, second.Id }, groups[0].Items.Select(i => i.Id));
    }

    [Fact]
    public void 当日合计支出与收入分开累加()
    {
        var groups = BillListPresentation.Group(
            [
                Item("2026-11-24 09:00", 35m),
                Item("2026-11-24 10:00", 28.5m),
                Item("2026-11-24 11:00", 12000m, income: true),
                Item("2026-11-25 08:00", 6m),
            ],
            Now);

        var yesterday = groups.Single(g => g.Date == new DateTime(2026, 11, 24));
        Assert.Equal(63.5m, yesterday.Expense);
        Assert.Equal(12000m, yesterday.Income);
        Assert.Equal("支出 ¥63.50 · 收入 ¥12,000.00", yesterday.TotalsText);

        var today = groups.Single(g => g.Date == new DateTime(2026, 11, 25));
        Assert.Equal("支出 ¥6.00", today.TotalsText);
    }

    [Fact]
    public void 没有账单时没有分组()
    {
        Assert.Empty(BillListPresentation.Group([], Now));
    }

    [Fact]
    public void 分组的日期取应用时区_UTC的前一天晚上在东八区是今天()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        var bill = new Bill
        {
            Id = Guid.NewGuid(),
            UserId = Me,
            Amount = 18m,
            Type = BillType.Expense,
            TransactionTime = new DateTime(2026, 11, 24, 16, 30, 0, DateTimeKind.Utc), // 东八区 11-25 00:30
            CategoryId = Guid.NewGuid(),
            Category = new Category { Name = "餐饮美食", Icon = "mdi-silverware-fork-knife" },
            PaymentChannel = new PaymentChannel { Name = "微信支付" },
            Ledger = new Ledger { Name = "日常开销" },
        };

        var item = BillListPresentation.ToItem(bill, Now, Me);
        var group = Assert.Single(BillListPresentation.Group([item], Now));

        Assert.Equal(new DateTime(2026, 11, 25, 0, 30, 0), item.LocalTime);
        Assert.Equal("今天", group.Title);
        Assert.Equal("00:30", item.Row.TimeText);
    }

    // ───────────────────────── 组头文案 ─────────────────────────

    [Theory]
    [InlineData("2026-11-25", "今天")]
    [InlineData("2026-11-24", "昨天")]
    [InlineData("2026-11-23", "11月23日")]
    [InlineData("2026-01-02", "1月2日")]
    [InlineData("2025-12-31", "2025年12月31日")]
    public void 组头标题_今天_昨天_更早的是日期_跨年带年份(string day, string expected) =>
        Assert.Equal(expected, BillListPresentation.DayTitle(DateTime.Parse(day), Now));

    [Theory]
    [InlineData("2026-11-25", "11月25日 周三")]
    [InlineData("2026-11-24", "11月24日 周二")]
    [InlineData("2026-11-22", "周日")]
    [InlineData("2026-11-23", "周一")]
    [InlineData("2025-12-31", "周三")]
    public void 组头日期_今天昨天补具体日期与星期_更早的只补星期(string day, string expected) =>
        Assert.Equal(expected, BillListPresentation.DayDateText(DateTime.Parse(day), Now));

    [Fact]
    public void 昨天跨年时组头日期带年份()
    {
        var newYear = new DateTime(2027, 1, 1, 9, 0, 0);

        Assert.Equal("昨天", BillListPresentation.DayTitle(new DateTime(2026, 12, 31), newYear));
        Assert.Equal("2026年12月31日 周四", BillListPresentation.DayDateText(new DateTime(2026, 12, 31), newYear));
    }

    [Theory]
    [InlineData(0, "日")]
    [InlineData(1, "一")]
    [InlineData(6, "六")]
    public void 星期按固定中文字符_不依赖区域设置(int offsetFromSunday, string expected)
    {
        var sunday = new DateTime(2026, 11, 22);

        Assert.Equal($"周{expected}", BillListPresentation.Weekday(sunday.AddDays(offsetFromSunday)));
    }

    [Theory]
    [InlineData(69, 0, "支出 ¥69.00")]
    [InlineData(0, 12000, "收入 ¥12,000.00")]
    [InlineData(485, 12000, "支出 ¥485.00 · 收入 ¥12,000.00")]
    [InlineData(0, 0, "支出 ¥0.00")]
    [InlineData(1234.5, 0, "支出 ¥1,234.50")]
    public void 收支合计只列出有金额的一边(double expense, double income, string expected) =>
        Assert.Equal(expected, BillListPresentation.TotalsText((decimal)expense, (decimal)income));

    [Fact]
    public void 组头带完整的标题_日期与合计文案()
    {
        var group = Assert.Single(BillListPresentation.Group([Item("2026-11-25 12:08", 69m)], Now));

        Assert.Equal("今天", group.Title);
        Assert.Equal("11月25日 周三", group.DateText);
        Assert.Equal("支出 ¥69.00", group.TotalsText);
        Assert.Equal("2026-11-25", group.Key);
    }

    // ───────────────────────── 行与权限 ─────────────────────────

    [Fact]
    public void 自己记的账单可以编辑删除_其他成员记的只读()
    {
        Bill NewBill(Guid owner) => new()
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            Amount = 35m,
            Type = BillType.Expense,
            TransactionTime = new DateTime(2026, 11, 25, 6, 30, 0, DateTimeKind.Utc),
            CategoryId = Guid.NewGuid(),
            Category = new Category { Name = "餐饮美食" },
            PaymentChannel = new PaymentChannel { Name = "微信支付" },
            Ledger = new Ledger { Name = "共享账本" },
        };

        Assert.True(BillListPresentation.ToItem(NewBill(Me), Now, Me).CanModify);
        Assert.False(BillListPresentation.ToItem(NewBill(Guid.NewGuid()), Now, Me).CanModify);
    }

    // ───────────────────────── 只读账本里自己记的账单 ─────────────────────────

    private static Bill BillInLedger(Guid owner, Guid? ledgerId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = owner,
        Amount = 35m,
        Type = BillType.Expense,
        TransactionTime = new DateTime(2026, 11, 25, 6, 30, 0, DateTimeKind.Utc),
        CategoryId = Guid.NewGuid(),
        Category = new Category { Name = "餐饮美食" },
        PaymentChannel = new PaymentChannel { Name = "微信支付" },
        LedgerId = ledgerId,
    };

    [Fact]
    public void 自己记在只读共享账本里的账单_能编辑_不能删除_不能批量归属()
    {
        var viewerLedger = Guid.NewGuid();
        var item = BillListPresentation.ToItem(BillInLedger(Me, viewerLedger), Now, Me, new HashSet<Guid> { viewerLedger });

        // 编辑照常能打开（保存时才被拒绝）；删除 / 批量归属服务层会 403，界面不给入口
        Assert.True(item.CanModify);
        Assert.True(item.InReadOnlyLedger);
        Assert.False(item.CanDelete);
        Assert.False(item.Selectable);
    }

    [Fact]
    public void 自己记在别的账本里的账单_可以删除_可以批量归属()
    {
        var viewerLedger = Guid.NewGuid();
        var readOnly = new HashSet<Guid> { viewerLedger };

        foreach (var ledgerId in new Guid?[] { Guid.NewGuid(), null })
        {
            var item = BillListPresentation.ToItem(BillInLedger(Me, ledgerId), Now, Me, readOnly);

            Assert.False(item.InReadOnlyLedger);
            Assert.True(item.CanDelete);
            Assert.True(item.Selectable);
        }

        // 没读到成员关系（null / 空集合）时不拦：服务层才是最终把关点
        Assert.True(BillListPresentation.ToItem(BillInLedger(Me, viewerLedger), Now, Me).CanDelete);
        Assert.True(BillListPresentation.ToItem(BillInLedger(Me, viewerLedger), Now, Me, new HashSet<Guid>()).CanDelete);
    }

    [Fact]
    public void 别人记的账单_既不能编辑也不能删除_不论账本是不是只读()
    {
        var ledger = Guid.NewGuid();
        var item = BillListPresentation.ToItem(BillInLedger(Guid.NewGuid(), ledger), Now, Me, new HashSet<Guid> { ledger });

        Assert.False(item.CanModify);
        Assert.False(item.CanDelete);
        Assert.False(item.Selectable);
    }

    [Fact]
    public void 列表行不带日期_只显示时分()
    {
        var bill = new Bill
        {
            Id = Guid.NewGuid(),
            UserId = Me,
            Amount = 28m,
            Type = BillType.Expense,
            TransactionTime = AppTimeZone.ToUtc(new DateTime(2026, 11, 20, 9, 32, 0)),
            CategoryId = Guid.NewGuid(),
            Category = new Category { Name = "餐饮美食", Icon = "mdi-silverware-fork-knife" },
            PaymentChannel = new PaymentChannel { Name = "微信支付" },
            Ledger = null,
            Note = "咖啡",
        };

        var item = BillListPresentation.ToItem(bill, Now, Me);

        Assert.Equal("09:32", item.Row.TimeText);
        Assert.Equal("餐饮美食 · 未归属账本", item.Row.Title);
        Assert.Equal("微信支付 · 咖啡", item.Row.Subtitle);
        Assert.Equal("-¥28.00", item.Row.AmountText);
        Assert.False(item.IsIncome);
        Assert.Equal(28m, item.Amount);
    }

    // ───────────────────────── 分段控件 ─────────────────────────

    [Theory]
    [InlineData(null, "All")]
    [InlineData(BillType.Expense, "Expense")]
    [InlineData(BillType.Income, "Income")]
    public void 账单类型与分段取值互相转换(BillType? type, string segment)
    {
        Assert.Equal(segment, BillListPresentation.SegmentOf(type));
        Assert.Equal(type, BillListPresentation.TypeOf(segment));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("未知")]
    public void 未知的分段取值当作全部(string? segment) =>
        Assert.Null(BillListPresentation.TypeOf(segment));

    // ───────────────────────── 文案 ─────────────────────────

    [Fact]
    public void 空状态文案_区分没有账单与没有符合条件的账单()
    {
        Assert.Equal("还没有账单", BillListPresentation.EmptyState(hasConditions: false).Title);
        Assert.Equal("没有符合条件的账单", BillListPresentation.EmptyState(hasConditions: true).Title);
        Assert.Contains("清除筛选", BillListPresentation.EmptyState(hasConditions: true).Text);
    }

    [Fact]
    public void 空状态文案_只受默认的本月限制时说本月还没有账单_不能说还没有账单()
    {
        // 更早的月份里可能有账单：「还没有账单」会让用户误以为一笔都没记过
        var onlyMonth = BillListPresentation.EmptyState(hasConditions: false, hasKeyword: false, onlyDefaultMonth: true);
        Assert.Equal("本月还没有账单", onlyMonth.Title);
        Assert.Contains("查看全部账单", onlyMonth.Text);
        Assert.Equal("查看全部账单", BillListPresentation.ShowAllTimeText);

        // 有筛选条件 / 搜索关键字时，以它们的文案为准，不提「本月」
        Assert.Equal("没有符合条件的账单", BillListPresentation.EmptyState(hasConditions: true, hasKeyword: false, onlyDefaultMonth: true).Title);
        Assert.Equal("没有找到相关账单", BillListPresentation.EmptyState(hasConditions: false, hasKeyword: true, onlyDefaultMonth: true).Title);
    }

    [Fact]
    public void 空状态文案_搜了关键字说没有找到_并提示清除搜索()
    {
        // 只有关键字：提示换关键字或清除搜索，不提筛选（用户没设筛选）
        var keywordOnly = BillListPresentation.EmptyState(hasConditions: false, hasKeyword: true);
        Assert.Equal("没有找到相关账单", keywordOnly.Title);
        Assert.Contains("清除搜索", keywordOnly.Text);
        Assert.DoesNotContain("筛选", keywordOnly.Text);

        // 关键字 + 其他条件：两样都点明（空结果可能是其中任何一个造成的）
        var both = BillListPresentation.EmptyState(hasConditions: true, hasKeyword: true);
        Assert.Equal("没有找到相关账单", both.Title);
        Assert.Contains("清除搜索与筛选", both.Text);
    }

    [Theory]
    [InlineData(true, false, "清除筛选")]
    [InlineData(false, true, "清除搜索")]
    [InlineData(true, true, "清除搜索与筛选")]
    [InlineData(false, false, "清除筛选")]
    public void 空状态的清除按钮文案与生效的条件对应(bool hasConditions, bool hasKeyword, string expected) =>
        Assert.Equal(expected, BillListPresentation.ClearConditionsText(hasConditions, hasKeyword));

    [Theory]
    [InlineData(0, "没有找到相关账单")]
    [InlineData(1, "找到 1 笔账单")]
    [InlineData(1234, "找到 1,234 笔账单")]
    public void 搜索结果的读屏播报(int count, string expected) =>
        Assert.Equal(expected, BillListPresentation.SearchResultText(count));

    [Fact]
    public void 搜索框文案固定_占位文字说清楚能搜什么()
    {
        Assert.Equal("搜索备注、分类或金额", BillListPresentation.SearchPlaceholder);
        Assert.Equal("搜索账单", BillListPresentation.SearchLabel);
        Assert.Equal("清除搜索", BillListPresentation.SearchClearLabel);
        Assert.True(BillListPresentation.SearchMaxLength > 0);
    }

    [Theory]
    [InlineData(0, "未选择")]
    [InlineData(1, "已选 1 笔")]
    [InlineData(12, "已选 12 笔")]
    public void 批量选择计数文案(int selected, string expected) =>
        Assert.Equal(expected, BillListPresentation.SelectionText(selected));

    [Theory]
    [InlineData(0, "筛选")]
    [InlineData(2, "筛选，已设置 2 项")]
    public void 筛选按钮读屏名称带上条件数(int count, string expected) =>
        Assert.Equal(expected, BillListPresentation.FilterButtonLabel(count));

    [Fact]
    public void 删除确认点名是哪一笔并说明会随同步传播()
    {
        var item = Item("2026-11-25 12:08", 35m, title: "餐饮美食 · 午餐");

        Assert.Equal("删除这笔账单？", BillListPresentation.DeleteTitle);
        Assert.Equal("餐饮美食 · 午餐，-¥35.00。删除后不再显示，并会随同步传播到其他设备。", BillListPresentation.DeleteMessage(item));
    }

    [Theory]
    [InlineData("咖啡", "餐饮美食 · 咖啡，-¥28.00。删除后不再显示，并会随同步传播到其他设备。")]
    [InlineData("  咖啡\n拿铁 ", "餐饮美食 · 咖啡 拿铁，-¥28.00。删除后不再显示，并会随同步传播到其他设备。")]
    [InlineData(null, "餐饮美食，-¥28.00。删除后不再显示，并会随同步传播到其他设备。")]
    [InlineData("   ", "餐饮美食，-¥28.00。删除后不再显示，并会随同步传播到其他设备。")]
    public void 删除确认点名是分类加备注_不用行标题里的账本(string? note, string expected)
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var bill = new Bill
        {
            Id = Guid.NewGuid(),
            UserId = Me,
            Amount = 28m,
            Type = BillType.Expense,
            TransactionTime = AppTimeZone.ToUtc(new DateTime(2026, 11, 20, 9, 32, 0)),
            CategoryId = Guid.NewGuid(),
            Category = new Category { Name = "餐饮美食", Icon = "mdi-silverware-fork-knife" },
            PaymentChannel = new PaymentChannel { Name = "微信支付" },
            Ledger = new Ledger { Name = "日常开销" },
            Note = note,
        };

        var item = BillListPresentation.ToItem(bill, Now, Me);

        Assert.Equal("餐饮美食 · 日常开销", item.Row.Title);
        Assert.Equal(expected, BillListPresentation.DeleteMessage(item));
        Assert.DoesNotContain("日常开销", BillListPresentation.DeleteMessage(item));
    }

    // ───────────────────────── 筛选状态 ─────────────────────────

    [Fact]
    public void 没有任何条件时_筛选为空()
    {
        var filter = new BillListFilter();

        Assert.False(filter.HasAny);
        Assert.Equal(0, filter.PanelCount);
        Assert.True(filter.ToQuery().IsEmpty);
    }

    [Fact]
    public void 筛选面板条数_日期范围算一项_账单类型不计()
    {
        var filter = new BillListFilter { StartDate = "2026-11-01", EndDate = "2026-11-30" };
        Assert.Equal(1, filter.PanelCount);

        filter.LedgerId = Guid.NewGuid();
        filter.ToggleCategory(Guid.NewGuid());
        filter.PaymentChannelId = Guid.NewGuid();
        Assert.Equal(4, filter.PanelCount);

        // 分类多选：选几个都只算一项
        filter.ToggleCategory(Guid.NewGuid());
        Assert.Equal(4, filter.PanelCount);

        filter.Type = BillType.Expense;
        Assert.Equal(4, filter.PanelCount);
    }

    [Fact]
    public void 只有账单类型时_面板条数为零但有筛选()
    {
        var filter = new BillListFilter { Type = BillType.Income };

        Assert.Equal(0, filter.PanelCount);
        Assert.True(filter.HasAny);
    }

    [Fact]
    public void 起止日期转成查询条件_结束日期含当天最后一刻()
    {
        var filter = new BillListFilter { StartDate = "2026-11-01", EndDate = "2026-11-30" };

        var query = filter.ToQuery();

        Assert.Equal(new DateTime(2026, 11, 1), query.StartDate);
        Assert.Equal(new DateTime(2026, 12, 1).AddTicks(-1), query.EndDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-11")]
    [InlineData("2026-13-45")]
    [InlineData("not a date")]
    public void 解析不了的日期文本当作没填_不抛异常(string text)
    {
        var filter = new BillListFilter { StartDate = text, EndDate = text };

        var query = filter.ToQuery();

        Assert.Null(query.StartDate);
        Assert.Null(query.EndDate);
        Assert.False(filter.HasDateRange);
        Assert.Equal(0, filter.PanelCount);
    }

    [Fact]
    public void 其余条件原样带入查询()
    {
        var ledger = Guid.NewGuid();
        var category = Guid.NewGuid();
        var channel = Guid.NewGuid();
        var filter = new BillListFilter { LedgerId = ledger, PaymentChannelId = channel, Type = BillType.Income };
        filter.ToggleCategory(category);

        var query = filter.ToQuery();

        Assert.Equal(ledger, query.LedgerId);
        Assert.Equal(new[] { category }, query.CategoryIds);
        Assert.Equal(channel, query.PaymentChannelId);
        Assert.Equal(BillType.Income, query.Type);
    }

    [Fact]
    public void 清除面板条件保留账单类型_清除全部则一并清掉()
    {
        var filter = new BillListFilter
        {
            StartDate = "2026-11-01",
            LedgerId = Guid.NewGuid(),
            PaymentChannelId = Guid.NewGuid(),
            Type = BillType.Expense,
        };
        filter.ToggleCategory(Guid.NewGuid());

        filter.ClearPanel();
        Assert.Equal(0, filter.PanelCount);
        Assert.Equal(BillType.Expense, filter.Type);

        filter.Clear();
        Assert.False(filter.HasAny);
    }
}
