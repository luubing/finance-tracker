using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 已软删除的账单不计入任何统计 / 预算——即使查询里带着「不计入统计」的判定。
/// <para>
/// 背景（回归）：「不计入统计」要认出已被软删除的自建分类，需要对分类表 <c>IgnoreQueryFilters()</c>；
/// 而 EF Core 的 <c>IgnoreQueryFilters()</c> 是<b>整条查询</b>级的开关，把它拼进账单查询的子查询，
/// 账单自己的软删除过滤器就跟着失效，已删除的账单会重新算进统计。所以被排除的分类 Id 必须<b>单独</b>查出来再传进账单查询。
/// 这里每个统计入口都放一笔已删除的大额账单，任何一处把过滤器弄丢了都会被抓到。
/// </para>
/// </summary>
public sealed class StatisticsSoftDeleteTests : ExcludeFromStatisticsTestBase
{
    private StatisticsService _statistics = null!;
    private BudgetService _budgets = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _statistics = new StatisticsService(Db.Context);
        _budgets = new BudgetService(Db.Context, NullLogger<BudgetService>.Instance);
    }

    /// <summary>
    /// 三月里：一笔 100 的普通支出、一笔 5000 的已删除支出（同分类同账本），
    /// 以及一笔 700 的被排除分类支出；<paramref name="withFlaggedCategory"/> 为 false 时不建被排除的分类，
    /// 用来证明"哪怕一个被排除的分类都没有"，查询也不能丢掉软删除过滤。
    /// </summary>
    private async Task<Category?> SeedAsync(bool withFlaggedCategory)
    {
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        var deleted = await AddBillAsync(Db.ExpenseCategory.Id, 5000m, MidMarch.AddHours(1));
        Assert.True(await Bills.DeleteBillAsync(deleted.Id, Db.User.Id));

        if (!withFlaggedCategory)
        {
            return null;
        }

        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(savings.Id, 700m, MidMarch.AddHours(2));
        var deletedSavings = await AddBillAsync(savings.Id, 9000m, MidMarch.AddHours(3));
        Assert.True(await Bills.DeleteBillAsync(deletedSavings.Id, Db.User.Id));
        return savings;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 月度统计不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3);

        Assert.Equal(100m, monthly.TotalExpense);
        Assert.Equal(1, monthly.BillCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 分类统计不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        var stats = await _statistics.GetCategoryStatisticsAsync(Db.User.Id, 2026, 3, BillType.Expense);

        var item = Assert.Single(stats);
        Assert.Equal(100m, item.Amount);
        Assert.Equal(1, item.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 账本统计不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        var ledgers = await _statistics.GetLedgerStatisticsAsync(Db.User.Id, 2026, 3, BillType.Expense);

        Assert.Equal(100m, Assert.Single(ledgers).Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 趋势不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        var trend = await _statistics.GetTrendDataAsync(
            Db.User.Id, new DateTime(2026, 3, 1), TimeBoundaries.EndOfDay(new DateTime(2026, 3, 31)), TrendDimension.Day);

        Assert.Equal(100m, Assert.Single(trend).Expense);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 年度统计不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        var annual = await _statistics.GetAnnualStatisticsAsync(Db.User.Id, 2026);

        Assert.Equal(100m, annual.TotalExpense);
        Assert.Equal(1, annual.BillCount);
        Assert.Equal(100m, Assert.Single(annual.ExpenseCategoryStats).Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 自定义区间统计不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        var custom = await _statistics.GetCustomStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(100m, custom.TotalExpense);
        Assert.Equal(1, custom.BillCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 同比不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        var yoy = await _statistics.GetYearOverYearDataAsync(Db.User.Id, 2026, 3);

        Assert.Equal(100m, yoy.CurrentExpense);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 年度同比不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        // 此刻是 2026-11-17：当年比「年初至今」，三月的账单在区间内。年度同比与月度同比共用一个私有实现，但入口各自传区间，
        // 这里单独走一遍年度入口，免得有人只在其中一条路径上丢掉过滤
        var statistics = new StatisticsService(Db.Context, new FixedTimeProvider(new DateTimeOffset(2026, 11, 17, 4, 0, 0, TimeSpan.Zero)));
        var yoy = await statistics.GetAnnualYearOverYearDataAsync(Db.User.Id, 2026);

        Assert.Equal(100m, yoy.CurrentExpense);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 分类环比不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory);

        var comparison = await _statistics.GetCategoryComparisonAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(100m, Assert.Single(comparison).CurrentAmount);
    }

    [Fact]
    public async Task 被排除汇总不含已删除的账单()
    {
        using var _ = UseUtcPlus8();
        await SeedAsync(withFlaggedCategory: true);

        var excluded = await _statistics.GetExcludedStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1));

        var item = Assert.Single(excluded);
        Assert.Equal(700m, item.Amount);
        Assert.Equal(1, item.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 预算已用额不含已删除的账单(bool withFlaggedCategory)
    {
        using var _ = UseUtcPlus8();
        await _budgets.CreateBudgetAsync(new Budget
        {
            UserId = Db.User.Id,
            Year = 2026,
            Month = 3,
            Amount = 10000m
        });
        await SeedAsync(withFlaggedCategory);

        var total = Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, 2026, 3));

        Assert.Equal(100m, total.UsedAmount);
    }
}
