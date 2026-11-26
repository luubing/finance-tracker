using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 首页预算概览（P14）：首页原先调用 <c>GetBudgetStatusAsync</c> 且不传账本，只会拿到"全部账本"的预算，
/// 所以只设了账本级预算的用户，首页永远没有预算进度条。
/// 概览口径：有"全部账本"预算就只展示它们（行为不变）；一个都没有时回退展示用户自己账本的账本级预算。
/// </summary>
public sealed class BudgetOverviewTests : IAsyncLifetime
{
    private const int Year = 2026;
    private const int Month = 11;

    private SqliteTestDatabase _db = null!;
    private BudgetService _budgets = null!;
    private LedgerService _ledgers = null!;
    private BillService _bills = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _budgets = new BudgetService(_db.Context, NullLogger<BudgetService>.Instance);
        _ledgers = new LedgerService(_db.Context, NullLogger<LedgerService>.Instance);
        _bills = new BillService(_db.Context, NullLogger<BillService>.Instance);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /// <summary>预算月份按应用时区划分：固定东八区，账单放在月中，结果不依赖运行机器的时区</summary>
    private static IDisposable UseUtcPlus8() => AppTimeZone.UseZone(TestZones.UtcPlus8);

    private Task<Ledger> AddLedgerAsync(string name, Guid? ownerId = null) =>
        _ledgers.CreateLedgerAsync(new Ledger
        {
            UserId = ownerId ?? _db.User.Id,
            Name = name,
            Icon = "mdi-book-open-variant"
        });

    private Task<Budget> AddBudgetAsync(
        decimal amount,
        Guid? ledgerId = null,
        Guid? categoryId = null,
        Guid? userId = null,
        int month = Month) =>
        _budgets.CreateBudgetAsync(new Budget
        {
            UserId = userId ?? _db.User.Id,
            LedgerId = ledgerId,
            CategoryId = categoryId,
            Year = Year,
            Month = month,
            Amount = amount
        });

    /// <param name="ledgerId">账单所属账本；账本必填，不指定时记在默认账本里</param>
    private async Task AddExpenseAsync(decimal amount, Guid? ledgerId = null)
    {
        ledgerId ??= (await _db.GetDefaultLedgerAsync()).Id;

        await _bills.CreateBillAsync(new Bill
        {
            UserId = _db.User.Id,
            Amount = amount,
            Type = BillType.Expense,
            CategoryId = _db.ExpenseCategory.Id,
            PaymentChannelId = _db.Channel.Id,
            LedgerId = ledgerId,
            TransactionTime = new DateTime(Year, Month, 15, 12, 0, 0, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });
    }

    /// <summary>
    /// 历史账单：账本必填之前创建的、没有账本的账单。BillService 已不允许再创建这样的账单，
    /// 只能绕过它直接入库来模拟（保证历史数据在预算/统计里的口径不被回归破坏）
    /// </summary>
    private async Task AddLegacyExpenseWithoutLedgerAsync(decimal amount)
    {
        _db.Context.Bills.Add(new Bill
        {
            Id = Guid.NewGuid(),
            UserId = _db.User.Id,
            Amount = amount,
            Type = BillType.Expense,
            CategoryId = _db.ExpenseCategory.Id,
            PaymentChannelId = _db.Channel.Id,
            LedgerId = null,
            TransactionTime = new DateTime(Year, Month, 15, 4, 0, 0, DateTimeKind.Utc),   // 东八区 12:00
            Source = BillSource.Manual
        });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task 没有任何预算_返回空列表()
    {
        using var _ = UseUtcPlus8();

        Assert.Empty(await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month));
    }

    [Fact]
    public async Task 有全部账本预算_只展示全部账本预算_不混入账本级预算()
    {
        using var _ = UseUtcPlus8();
        var travel = await AddLedgerAsync("旅行");
        await AddBudgetAsync(1000m);                                         // 全部账本 · 总预算
        await AddBudgetAsync(300m, categoryId: _db.ExpenseCategory.Id);      // 全部账本 · 分类预算
        await AddBudgetAsync(5000m, ledgerId: travel.Id);                    // 旅行账本 · 总预算

        var statuses = await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month);

        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, s =>
        {
            Assert.Null(s.LedgerId);
            Assert.Null(s.LedgerName);
        });
    }

    [Fact]
    public async Task 设了全部账本预算时_概览与不传账本的预算状态完全一致()
    {
        using var _ = UseUtcPlus8();
        var travel = await AddLedgerAsync("旅行");
        await AddBudgetAsync(1000m);
        await AddBudgetAsync(300m, categoryId: _db.ExpenseCategory.Id);
        await AddBudgetAsync(5000m, ledgerId: travel.Id);
        await AddExpenseAsync(120m);
        await AddExpenseAsync(80m, travel.Id);

        var overview = await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month);
        var classic = await _budgets.GetBudgetStatusAsync(_db.User.Id, Year, Month);

        Assert.Equal(
            classic.Select(s => (s.BudgetId, s.UsedAmount)),
            overview.Select(s => (s.BudgetId, s.UsedAmount)));
        Assert.Equal(200m, overview.Single(s => s.CategoryId == null).UsedAmount);   // 总预算统计所有账单
    }

    [Fact]
    public async Task 只设了账本级预算_回退展示账本级预算_带账本名_总预算在前()
    {
        using var _ = UseUtcPlus8();
        var travel = await AddLedgerAsync("旅行");
        await AddBudgetAsync(500m, ledgerId: travel.Id, categoryId: _db.ExpenseCategory.Id);   // 先建分类预算
        await AddBudgetAsync(2000m, ledgerId: travel.Id);                                     // 后建总预算

        var statuses = await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month);

        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, s =>
        {
            Assert.Equal(travel.Id, s.LedgerId);
            Assert.Equal("旅行", s.LedgerName);
        });
        Assert.Null(statuses[0].CategoryId);            // 总预算排在分类预算前面
        Assert.Equal("旅行 · 总预算", statuses[0].DisplayName);
    }

    [Fact]
    public async Task 回退时已用额只统计该账本下的支出()
    {
        using var _ = UseUtcPlus8();
        var travel = await AddLedgerAsync("旅行");
        var family = await AddLedgerAsync("家庭");
        await AddBudgetAsync(1000m, ledgerId: travel.Id);
        await AddBudgetAsync(1000m, ledgerId: family.Id);
        await AddExpenseAsync(100m, travel.Id);
        await AddExpenseAsync(50m, travel.Id);
        await AddExpenseAsync(30m, family.Id);
        await AddLegacyExpenseWithoutLedgerAsync(999m);   // 历史上未归属账本的账单不属于任何账本预算

        var statuses = await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month);

        Assert.Equal(150m, statuses.Single(s => s.LedgerId == travel.Id).UsedAmount);
        Assert.Equal(30m, statuses.Single(s => s.LedgerId == family.Id).UsedAmount);
    }

    [Fact]
    public async Task 多个账本各设总预算_都展示_按账本名排序_与创建顺序无关()
    {
        using var _ = UseUtcPlus8();
        var beta = await AddLedgerAsync("Beta");
        var alpha = await AddLedgerAsync("Alpha");
        await AddBudgetAsync(100m, ledgerId: beta.Id);     // 先建 Beta，排序若退化成"插入顺序"就会先出 Beta
        await AddBudgetAsync(100m, ledgerId: alpha.Id);

        var statuses = await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month);

        Assert.Equal(new[] { "Alpha", "Beta" }, statuses.Select(s => s.LedgerName).ToArray());
    }

    [Fact]
    public async Task 指定账本查询时_全部账本的总预算排在该账本的总预算前面()
    {
        using var _ = UseUtcPlus8();
        var travel = await AddLedgerAsync("旅行");
        await AddBudgetAsync(5000m, ledgerId: travel.Id);   // 先建账本级
        await AddBudgetAsync(1000m);                        // 后建全部账本

        var statuses = await _budgets.GetBudgetStatusAsync(_db.User.Id, Year, Month, travel.Id);

        Assert.Equal(2, statuses.Count);
        Assert.Null(statuses[0].LedgerId);
        Assert.Equal(travel.Id, statuses[1].LedgerId);
    }

    [Fact]
    public async Task 全部账本预算已删除_视为没有_回退展示账本级预算()
    {
        using var _ = UseUtcPlus8();
        var travel = await AddLedgerAsync("旅行");
        var global = await AddBudgetAsync(1000m);
        await AddBudgetAsync(5000m, ledgerId: travel.Id);
        Assert.True(await _budgets.DeleteBudgetAsync(global.Id, _db.User.Id));

        var statuses = await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month);

        var only = Assert.Single(statuses);
        Assert.Equal(travel.Id, only.LedgerId);
    }

    [Fact]
    public async Task 账本已删除_其遗留的预算不回退展示()
    {
        using var _ = UseUtcPlus8();
        var travel = await AddLedgerAsync("旅行");
        await AddBudgetAsync(5000m, ledgerId: travel.Id);
        // 删除账本只校验"没有账单"，不会连带删除预算——遗留的预算若被展示，会因取不到账本名而被误认成全部账本预算
        Assert.True(await _ledgers.DeleteLedgerAsync(travel.Id, _db.User.Id));

        Assert.Empty(await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month));
    }

    [Fact]
    public async Task 回退只展示自己账本的预算_不展示他人账本的预算()
    {
        using var _ = UseUtcPlus8();
        var other = await _db.CreateUserAsync("13900139000");
        var othersLedger = await AddLedgerAsync("别人的账本", other.Id);
        await AddBudgetAsync(5000m, ledgerId: othersLedger.Id, userId: other.Id);        // 别人账本上的别人的预算
        await AddBudgetAsync(7000m, ledgerId: othersLedger.Id, userId: _db.User.Id);     // 脏数据：自己的预算指向别人的账本

        Assert.Empty(await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month));
        Assert.Single(await _budgets.GetOverviewBudgetStatusAsync(other.Id, Year, Month));   // 账本主人自己仍能看到自己的
    }

    [Fact]
    public async Task 其它月份的账本级预算不展示()
    {
        using var _ = UseUtcPlus8();
        var travel = await AddLedgerAsync("旅行");
        await AddBudgetAsync(5000m, ledgerId: travel.Id, month: Month - 1);

        Assert.Empty(await _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, Year, Month));
    }

    [Theory]
    [InlineData(1999, 11)]
    [InlineData(2101, 11)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public async Task 年月无效_抛出ArgumentException(int year, int month)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _budgets.GetOverviewBudgetStatusAsync(_db.User.Id, year, month));
    }
}
