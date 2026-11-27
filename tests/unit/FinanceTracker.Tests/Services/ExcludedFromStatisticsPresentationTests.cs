using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「不计入统计」在界面上的展示规则：分类表单的开关说明、账单行 / 分类列表的标记、统计页底部「已忽略 N 笔」提示。
/// 业务口径（哪些账单被排除）由 <c>ExcludeFromStatistics*Tests</c> 覆盖；这里只验证「怎么说」和「谁看得到标记」。
/// </summary>
public class ExcludedFromStatisticsPresentationTests
{
    // 2026-11-25 20:00（应用时区的「现在」）
    private static readonly DateTime Now = new(2026, 11, 25, 20, 0, 0);

    private static readonly Guid Me = Guid.NewGuid();

    private static ExcludedCategoryStatistics Row(string name, decimal amount, int count, BillType type = BillType.Expense) => new()
    {
        CategoryId = Guid.NewGuid(),
        CategoryName = name,
        CategoryIcon = "mdi-piggy-bank",
        Type = type,
        Amount = amount,
        Count = count,
    };

    private static Bill BillOf(Guid billOwner, Category category) => new()
    {
        Id = Guid.NewGuid(),
        UserId = billOwner,
        Amount = 500m,
        Type = category.Type,
        Note = "存钱",
        TransactionTime = new DateTime(2026, 11, 25, 6, 30, 0, DateTimeKind.Utc),
        CategoryId = category.Id,
        Category = category,
        PaymentChannel = new PaymentChannel { Name = "银行卡" },
        Ledger = new Ledger { Name = "日常开销" },
    };

    private static Category Custom(Guid? owner, bool exclude, BillType type = BillType.Expense) => new()
    {
        Id = Guid.NewGuid(),
        UserId = owner,
        Name = "存款",
        Icon = "mdi-piggy-bank",
        Type = type,
        ExcludeFromStatistics = exclude,
    };

    // ---- 统计页底部的提示 ----

    [Fact]
    public void 没有被排除的账单时没有提示() =>
        Assert.Null(ExcludedFromStatisticsPresentation.NoteText([]));

    [Fact]
    public void 只有一个分类时写笔数与金额_不重复这个分类自己的笔数()
    {
        var text = ExcludedFromStatisticsPresentation.NoteText([Row("存款", 1200m, 3)]);

        Assert.Equal("已忽略 3 笔「不计入统计」的账单：存款 -¥1,200.00", text);
    }

    [Fact]
    public void 有几个分类时每个分类后面带自己的笔数_总笔数是它们之和()
    {
        var text = ExcludedFromStatisticsPresentation.NoteText(
        [
            Row("存款", 1200m, 3),
            Row("借入", 500m, 1, BillType.Income),
        ]);

        Assert.Equal("已忽略 4 笔「不计入统计」的账单：存款 -¥1,200.00（3 笔）、借入 +¥500.00（1 笔）", text);
    }

    [Fact]
    public void 金额按收支方向带符号_支出负号_收入正号()
    {
        var expense = ExcludedFromStatisticsPresentation.NoteText([Row("存款", 88.5m, 1)]);
        var income = ExcludedFromStatisticsPresentation.NoteText([Row("借入", 88.5m, 1, BillType.Income)]);

        Assert.Contains("存款 -¥88.50", expense);
        Assert.Contains("借入 +¥88.50", income);
    }

    [Fact]
    public void 点名的分类超过上限时只列前几个_其余折成等N个分类_总笔数仍然算全部()
    {
        var rows = new List<ExcludedCategoryStatistics>
        {
            Row("存款", 5000m, 5),
            Row("还信用卡", 3000m, 4),
            Row("转账", 2000m, 3),
            Row("借出", 800m, 2),
            Row("退款", 100m, 1, BillType.Income),
        };

        var text = ExcludedFromStatisticsPresentation.NoteText(rows);

        // 15 笔 = 5 + 4 + 3 + 2 + 1；只点名前 3 个（服务按金额从大到小给），后面两个折进「等 5 个分类」
        Assert.Equal(
            "已忽略 15 笔「不计入统计」的账单：存款 -¥5,000.00（5 笔）、还信用卡 -¥3,000.00（4 笔）、转账 -¥2,000.00（3 笔）等 5 个分类",
            text);
        Assert.DoesNotContain("借出", text);
        Assert.DoesNotContain("退款", text);
    }

    [Fact]
    public void 刚好等于上限个数时全部点名_不写等N个分类()
    {
        var rows = Enumerable.Range(1, ExcludedFromStatisticsPresentation.MaxNamedCategories)
            .Select(i => Row($"分类{i}", 100m * i, i))
            .ToList();

        var text = ExcludedFromStatisticsPresentation.NoteText(rows);

        Assert.DoesNotContain("等", text);
        Assert.All(rows, row => Assert.Contains(row.CategoryName, text));
    }

    [Fact]
    public void 提示里统一叫不计入统计()
    {
        var text = ExcludedFromStatisticsPresentation.NoteText([Row("存款", 1m, 1)]);

        Assert.Contains($"「{ExcludedFromStatisticsPresentation.Label}」", text);
        Assert.Equal("不计入统计", ExcludedFromStatisticsPresentation.Label);
    }

    // ---- 谁看得到标记 ----

    [Fact]
    public void 查看者自己的分类开着开关才算被排除()
    {
        Assert.True(ExcludedFromStatisticsPresentation.IsExcluded(Custom(Me, exclude: true), Me));
        Assert.False(ExcludedFromStatisticsPresentation.IsExcluded(Custom(Me, exclude: false), Me));
    }

    [Fact]
    public void 别人的分类即使开着开关_对查看者也不算被排除()
    {
        // 共享账本里其他成员用他们自己的分类记的账单：对查看者的统计本来就不算（统计只算自己记的账单），不存在「被排除」
        Assert.False(ExcludedFromStatisticsPresentation.IsExcluded(Custom(Guid.NewGuid(), exclude: true), Me));
    }

    [Fact]
    public void 预设分类没有所有者_开关没有意义_不算被排除()
    {
        Assert.False(ExcludedFromStatisticsPresentation.IsExcluded(Custom(null, exclude: true), Me));
    }

    [Fact]
    public void 不知道查看者是谁或者没有分类时一律不标记()
    {
        Assert.False(ExcludedFromStatisticsPresentation.IsExcluded(Custom(Me, exclude: true), null));
        Assert.False(ExcludedFromStatisticsPresentation.IsExcluded(null, Me));
    }

    // ---- 账单行 ----

    [Fact]
    public void 账单行默认不带标记()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var bill = BillOf(Me, Custom(Me, exclude: true));

        // 不传查看者：宁可少标也不标错
        var row = BillPresentation.ToRow(bill, Now, includeDate: true);

        Assert.False(row.ExcludedFromStatistics);
        Assert.DoesNotContain(ExcludedFromStatisticsPresentation.Label, row.AriaLabel);
    }

    [Fact]
    public void 自己的分类开着开关_账单行带标记_读屏名称末尾也会说()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var bill = BillOf(Me, Custom(Me, exclude: true));

        var row = BillPresentation.ToRow(bill, Now, includeDate: true, viewerId: Me);

        Assert.True(row.ExcludedFromStatistics);
        Assert.Equal("存款 · 日常开销，银行卡 · 存钱，支出 ¥500.00，14:30，不计入统计", row.AriaLabel);
        // 标记不改变行里的标题 / 副标题 / 金额：账单照常显示，只是多一个标记
        Assert.Equal("存款 · 日常开销", row.Title);
        Assert.Equal("银行卡 · 存钱", row.Subtitle);
        Assert.Equal("-¥500.00", row.AmountText);
    }

    [Fact]
    public void 没开开关的分类_账单行不带标记()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var bill = BillOf(Me, Custom(Me, exclude: false));

        var row = BillPresentation.ToRow(bill, Now, includeDate: true, viewerId: Me);

        Assert.False(row.ExcludedFromStatistics);
        Assert.DoesNotContain(ExcludedFromStatisticsPresentation.Label, row.AriaLabel);
    }

    [Fact]
    public void 共享账本里别人记的账单用别人的分类_查看者的账单行不带标记()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var other = Guid.NewGuid();
        var bill = BillOf(other, Custom(other, exclude: true));

        var row = BillPresentation.ToRow(bill, Now, includeDate: true, viewerId: Me);

        Assert.False(row.ExcludedFromStatistics);
    }

    [Fact]
    public void 账单Tab的列表项也按当前用户判断标记()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var mine = BillOf(Me, Custom(Me, exclude: true));
        var other = Guid.NewGuid();
        var theirs = BillOf(other, Custom(other, exclude: true));

        Assert.True(BillListPresentation.ToItem(mine, Now, Me).Row.ExcludedFromStatistics);
        Assert.False(BillListPresentation.ToItem(theirs, Now, Me).Row.ExcludedFromStatistics);
    }

    // ---- 分类表单 / 预算表单的说明 ----

    [Theory]
    [InlineData(BillType.Expense, "存款")]
    [InlineData(BillType.Income, "借入")]
    public void 开关说明写明影响范围_账单列表仍然显示_并按收支方向举例(BillType type, string example)
    {
        var footer = ExcludedFromStatisticsPresentation.SwitchFooter(type);

        Assert.Contains("统计", footer);
        Assert.Contains("首页本月概览", footer);
        Assert.Contains("预算", footer);
        Assert.Contains("账单列表仍会显示", footer);
        Assert.Contains($"「{example}」", footer);
    }

    [Fact]
    public void 预算表单里的说明点明账单不会累计到这笔预算()
    {
        Assert.Contains(ExcludedFromStatisticsPresentation.Label, ExcludedFromStatisticsPresentation.BudgetNote);
        Assert.Contains("预算", ExcludedFromStatisticsPresentation.BudgetNote);
    }
}
