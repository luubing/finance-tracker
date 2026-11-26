using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 年度统计的分类统计：统计页「年度」分段把原来的「分类统计」拆成「支出构成 / 收入构成」两个圆环，
/// 所以 <see cref="Core.Models.AnnualStatistics"/> 除了收支混合的 <c>CategoryStats</c>（REST 接口沿用），
/// 还要分别给出只含支出的 <c>ExpenseCategoryStats</c> 与只含收入的 <c>IncomeCategoryStats</c>——
/// 占比各自占本类合计，金额降序；年份按应用时区、区间左闭右开，账本筛选对三份统计同时生效。
/// </summary>
public sealed class AnnualCategoryStatsTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _bills = null!;
    private StatisticsService _statistics = null!;
    private Category _expenseA = null!;
    private Category _expenseB = null!;
    private Category _incomeA = null!;
    private Category _incomeB = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _bills = new BillService(_db.Context, NullLogger<BillService>.Instance);
        _statistics = new StatisticsService(_db.Context);

        var expenses = await _db.Context.Categories
            .Where(c => c.Type == BillType.Expense && c.UserId == null)
            .OrderBy(c => c.Name)
            .Take(2)
            .ToListAsync();
        var incomes = await _db.Context.Categories
            .Where(c => c.Type == BillType.Income && c.UserId == null)
            .OrderBy(c => c.Name)
            .Take(2)
            .ToListAsync();
        (_expenseA, _expenseB) = (expenses[0], expenses[1]);
        (_incomeA, _incomeB) = (incomes[0], incomes[1]);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static IDisposable UseUtcPlus8() => AppTimeZone.UseZone(TestZones.UtcPlus8);

    /// <summary>以应用时区的墙上时间创建账单（和记一笔页面表单一致：Kind=Unspecified）</summary>
    private Task<Bill> AddBillAsync(DateTime wallTime, decimal amount, Category category, Guid ledgerId) =>
        _bills.CreateBillAsync(new Bill
        {
            UserId = _db.User.Id,
            Amount = amount,
            Type = category.Type,
            CategoryId = category.Id,
            PaymentChannelId = _db.Channel.Id,
            LedgerId = ledgerId,
            TransactionTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });

    /// <summary>造数：支出 A 100 + 50 + 50（3 笔，共 200）、支出 B 200（1 笔）；收入 A 3000（1 笔）、收入 B 1000 + 1000（2 笔，共 2000）</summary>
    private async Task SeedTypicalYearAsync(Guid ledgerId)
    {
        await AddBillAsync(new DateTime(2026, 2, 3, 12, 0, 0), 100m, _expenseA, ledgerId);
        await AddBillAsync(new DateTime(2026, 5, 9, 12, 0, 0), 50m, _expenseA, ledgerId);
        await AddBillAsync(new DateTime(2026, 8, 1, 12, 0, 0), 50m, _expenseA, ledgerId);
        await AddBillAsync(new DateTime(2026, 6, 15, 12, 0, 0), 200m, _expenseB, ledgerId);
        await AddBillAsync(new DateTime(2026, 3, 31, 12, 0, 0), 3000m, _incomeA, ledgerId);
        await AddBillAsync(new DateTime(2026, 4, 30, 12, 0, 0), 1000m, _incomeB, ledgerId);
        await AddBillAsync(new DateTime(2026, 10, 1, 12, 0, 0), 1000m, _incomeB, ledgerId);
    }

    [Fact]
    public async Task 支出分类统计只含支出_占比占总支出_金额降序_笔数按分类()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.CreateLedgerAsync(_db.User.Id, "日常");
        await SeedTypicalYearAsync(ledger.Id);

        var annual = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2026);

        // 支出合计 400：A 200（3 笔）、B 200（1 笔）——同额时 LINQ 稳定排序保持首次出现的顺序，所以只断言集合与各自的数值
        Assert.Equal(2, annual.ExpenseCategoryStats.Count);
        Assert.All(annual.ExpenseCategoryStats, s => Assert.Equal(50m, s.Percentage));
        Assert.Equal(new[] { 200m, 200m }, annual.ExpenseCategoryStats.Select(s => s.Amount));
        Assert.Equal(
            new[] { (_expenseA.Id, 3), (_expenseB.Id, 1) }.OrderBy(t => t.Item1),
            annual.ExpenseCategoryStats.Select(s => (s.CategoryId, s.Count)).OrderBy(t => t.CategoryId));
        Assert.DoesNotContain(annual.ExpenseCategoryStats, s => s.CategoryId == _incomeA.Id || s.CategoryId == _incomeB.Id);
    }

    [Fact]
    public async Task 收入分类统计只含收入_占比占总收入_金额降序()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.CreateLedgerAsync(_db.User.Id, "日常");
        await SeedTypicalYearAsync(ledger.Id);

        var annual = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2026);

        // 收入合计 5000：A 3000（60%，1 笔）、B 2000（40%，2 笔）
        Assert.Equal(new[] { _incomeA.Id, _incomeB.Id }, annual.IncomeCategoryStats.Select(s => s.CategoryId));
        Assert.Equal(new[] { 3000m, 2000m }, annual.IncomeCategoryStats.Select(s => s.Amount));
        Assert.Equal(new[] { 60m, 40m }, annual.IncomeCategoryStats.Select(s => s.Percentage));
        Assert.Equal(new[] { 1, 2 }, annual.IncomeCategoryStats.Select(s => s.Count));
        Assert.DoesNotContain(annual.IncomeCategoryStats, s => s.CategoryId == _expenseA.Id || s.CategoryId == _expenseB.Id);
    }

    [Fact]
    public async Task 两份分类统计的合计等于全年的总支出与总收入()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.CreateLedgerAsync(_db.User.Id, "日常");
        await SeedTypicalYearAsync(ledger.Id);

        var annual = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2026);

        Assert.Equal(annual.TotalExpense, annual.ExpenseCategoryStats.Sum(s => s.Amount));
        Assert.Equal(annual.TotalIncome, annual.IncomeCategoryStats.Sum(s => s.Amount));
        Assert.Equal(400m, annual.TotalExpense);
        Assert.Equal(5000m, annual.TotalIncome);
    }

    [Fact]
    public async Task 收支混合的分类统计保持原口径_REST接口沿用_占比占两者合计()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.CreateLedgerAsync(_db.User.Id, "日常");
        await SeedTypicalYearAsync(ledger.Id);

        var annual = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2026);

        // 四个分类放在一起：合计 5400（收入 A 3000 → 55.56%，收入 B 2000 → 37.04%，支出 A / B 各 200 → 3.70%）
        Assert.Equal(4, annual.CategoryStats.Count);
        Assert.Equal(5400m, annual.CategoryStats.Sum(s => s.Amount));
        Assert.Equal(new[] { 3000m, 2000m, 200m, 200m }, annual.CategoryStats.Select(s => s.Amount));
        Assert.Equal(55.56m, annual.CategoryStats[0].Percentage);
        Assert.Equal(37.04m, annual.CategoryStats[1].Percentage);
        Assert.Equal(3.70m, annual.CategoryStats[2].Percentage);
    }

    [Fact]
    public async Task 账本筛选对三份分类统计同时生效()
    {
        using var _ = UseUtcPlus8();
        var daily = await _db.CreateLedgerAsync(_db.User.Id, "日常");
        var travel = await _db.CreateLedgerAsync(_db.User.Id, "旅行");
        await SeedTypicalYearAsync(daily.Id);
        await AddBillAsync(new DateTime(2026, 7, 7, 12, 0, 0), 80m, _expenseB, travel.Id);
        await AddBillAsync(new DateTime(2026, 7, 8, 12, 0, 0), 500m, _incomeA, travel.Id);

        var annual = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2026, travel.Id);

        var expense = Assert.Single(annual.ExpenseCategoryStats);
        Assert.Equal((_expenseB.Id, 80m, 100m, 1), (expense.CategoryId, expense.Amount, expense.Percentage, expense.Count));
        var income = Assert.Single(annual.IncomeCategoryStats);
        Assert.Equal((_incomeA.Id, 500m, 100m, 1), (income.CategoryId, income.Amount, income.Percentage, income.Count));
        Assert.Equal(2, annual.CategoryStats.Count);
    }

    [Fact]
    public async Task 年份按应用时区归属_跨年的账单不串年()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.CreateLedgerAsync(_db.User.Id, "日常");
        await AddBillAsync(new DateTime(2025, 12, 31, 23, 59, 0), 11m, _expenseA, ledger.Id);   // 应用时区的 2025 年最后一分钟
        await AddBillAsync(new DateTime(2026, 1, 1, 0, 0, 0), 22m, _expenseA, ledger.Id);        // 应用时区的 2026 年第一分钟
        await AddBillAsync(new DateTime(2026, 1, 1, 0, 1, 0), 33m, _incomeA, ledger.Id);

        var y2025 = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2025);
        var y2026 = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2026);

        Assert.Equal(new[] { 11m }, y2025.ExpenseCategoryStats.Select(s => s.Amount));
        Assert.Empty(y2025.IncomeCategoryStats);
        Assert.Equal(new[] { 22m }, y2026.ExpenseCategoryStats.Select(s => s.Amount));
        Assert.Equal(new[] { 33m }, y2026.IncomeCategoryStats.Select(s => s.Amount));
    }

    [Fact]
    public async Task 只有支出没有收入时_收入分类统计是空列表_支出照常()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.CreateLedgerAsync(_db.User.Id, "日常");
        await AddBillAsync(new DateTime(2026, 3, 3, 12, 0, 0), 66m, _expenseA, ledger.Id);

        var annual = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2026);

        Assert.Empty(annual.IncomeCategoryStats);
        var only = Assert.Single(annual.ExpenseCategoryStats);
        Assert.Equal((66m, 100m), (only.Amount, only.Percentage));
    }

    [Fact]
    public async Task 没有任何账单的年份_三份分类统计都是空列表()
    {
        using var _ = UseUtcPlus8();

        var annual = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2031);

        Assert.Empty(annual.CategoryStats);
        Assert.Empty(annual.ExpenseCategoryStats);
        Assert.Empty(annual.IncomeCategoryStats);
    }

    [Fact]
    public async Task 只统计当前用户自己的账单_别人的账单不进分类统计()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.CreateLedgerAsync(_db.User.Id, "日常");
        await AddBillAsync(new DateTime(2026, 3, 3, 12, 0, 0), 66m, _expenseA, ledger.Id);

        var annual = await _statistics.GetAnnualStatisticsAsync(Guid.NewGuid(), 2026);

        Assert.Empty(annual.ExpenseCategoryStats);
        Assert.Empty(annual.IncomeCategoryStats);
    }
}
