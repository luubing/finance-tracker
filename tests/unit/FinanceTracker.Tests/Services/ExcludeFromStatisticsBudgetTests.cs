using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「不计入统计」对预算的影响：被排除分类的账单不占用任何预算（总预算 / 账本预算 / 分类预算一视同仁），
/// 保存这类账单也不触发预算提醒。口径与统计共用 <c>StatisticsScope</c>，这里验证预算这一侧确实经过了它。
/// </summary>
public sealed class ExcludeFromStatisticsBudgetTests : ExcludeFromStatisticsTestBase
{
    private const int Year = 2026;
    private const int Month = 3;

    private BudgetService _budgets = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _budgets = new BudgetService(Db.Context, NullLogger<BudgetService>.Instance);
    }

    private Task<Budget> AddBudgetAsync(decimal amount, Guid? ledgerId = null, Guid? categoryId = null) =>
        _budgets.CreateBudgetAsync(new Budget
        {
            UserId = Db.User.Id,
            LedgerId = ledgerId,
            CategoryId = categoryId,
            Year = Year,
            Month = Month,
            Amount = amount
        });

    [Fact]
    public async Task 总预算的已用额不含被排除分类的账单()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(1000m);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 800m, MidMarch.AddHours(1));

        var total = Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month));

        Assert.Equal(100m, total.UsedAmount);
        Assert.Equal(900m, total.RemainingAmount);
    }

    [Fact]
    public async Task 分类预算_被排除的分类已用额恒为零_其他分类照常()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(1000m, categoryId: Db.ExpenseCategory.Id);
        await AddBudgetAsync(500m, categoryId: savings.Id);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 800m, MidMarch.AddHours(1));

        var statuses = await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month);

        Assert.Equal(100m, statuses.Single(s => s.CategoryId == Db.ExpenseCategory.Id).UsedAmount);
        Assert.Equal(0m, statuses.Single(s => s.CategoryId == savings.Id).UsedAmount);
    }

    [Fact]
    public async Task 账本预算的已用额也不含被排除分类的账单()
    {
        using var _ = UseUtcPlus8();
        var travel = await Db.CreateLedgerAsync(Db.User.Id, "旅行");
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(1000m, ledgerId: travel.Id);
        await AddBillAsync(Db.ExpenseCategory.Id, 30m, MidMarch, ledgerId: travel.Id);
        await AddBillAsync(savings.Id, 500m, MidMarch.AddHours(1), ledgerId: travel.Id);

        var status = Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month, travel.Id));

        Assert.Equal(30m, status.UsedAmount);
    }

    [Fact]
    public async Task 首页预算概览与预算页口径一致_都不含被排除的账单()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(1000m);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 800m, MidMarch.AddHours(1));

        var overview = Assert.Single(await _budgets.GetOverviewBudgetStatusAsync(Db.User.Id, Year, Month));
        var page = Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month));

        Assert.Equal(100m, overview.UsedAmount);
        Assert.Equal(page.UsedAmount, overview.UsedAmount);
    }

    [Fact]
    public async Task 开关改回来_已用额随之恢复()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(1000m);
        await AddBillAsync(savings.Id, 800m, MidMarch);

        Assert.Equal(0m, Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month)).UsedAmount);

        await SetExcludedAsync(savings, false);

        Assert.Equal(800m, Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month)).UsedAmount);
    }

    [Fact]
    public async Task 编辑账单换到被排除的分类_预算已用额随之减少_换回来又恢复()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(1000m);
        var bill = await AddBillAsync(Db.ExpenseCategory.Id, 300m, MidMarch);

        Assert.Equal(300m, Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month)).UsedAmount);

        await ChangeCategoryAsync(bill, savings.Id, MidMarch);

        Assert.Equal(0m, Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month)).UsedAmount);

        await ChangeCategoryAsync(bill, Db.ExpenseCategory.Id, MidMarch);

        Assert.Equal(300m, Assert.Single(await _budgets.GetBudgetStatusAsync(Db.User.Id, Year, Month)).UsedAmount);
    }

    // ───────────────────────── 超支提醒 ─────────────────────────

    [Fact]
    public async Task 预算提醒_账单属于被排除的分类时不提醒_即使总预算已接近上限()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(100m);
        await AddBillAsync(Db.ExpenseCategory.Id, 95m, MidMarch);                  // 总预算已用 95%
        await AddBillAsync(savings.Id, 5000m, MidMarch.AddHours(1));

        var forSavings = await _budgets.GetBudgetAlertAsync(Db.User.Id, Year, Month, null, savings.Id);
        var forDining = await _budgets.GetBudgetAlertAsync(Db.User.Id, Year, Month, null, Db.ExpenseCategory.Id);

        Assert.Null(forSavings);                                                    // 这笔存款没占用预算，无从提醒
        Assert.NotNull(forDining);                                                  // 没被排除的分类照常提醒
        Assert.True(forDining!.IsWarning);
        Assert.Equal(95m, forDining.UsedAmount);                                    // 被排除的 5000 不在已用额里
    }

    [Fact]
    public async Task 预算提醒_账本预算同样不把被排除分类的账单算进已用额_它自己也不触发提醒()
    {
        using var _ = UseUtcPlus8();
        var travel = await Db.CreateLedgerAsync(Db.User.Id, "旅行");
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(100m, ledgerId: travel.Id);
        await AddBillAsync(Db.ExpenseCategory.Id, 90m, MidMarch, ledgerId: travel.Id);       // 账本预算已用 90%
        await AddBillAsync(savings.Id, 5000m, MidMarch.AddHours(1), ledgerId: travel.Id);

        var forSavings = await _budgets.GetBudgetAlertAsync(Db.User.Id, Year, Month, travel.Id, savings.Id);
        var forDining = await _budgets.GetBudgetAlertAsync(Db.User.Id, Year, Month, travel.Id, Db.ExpenseCategory.Id);

        Assert.Null(forSavings);                                                              // 存款没占用账本预算，无从提醒
        Assert.NotNull(forDining);                                                            // 同账本里没被排除的分类照常提醒
        Assert.True(forDining!.IsWarning);
        Assert.Equal(90m, forDining.UsedAmount);                                              // 被排除的 5000 不在账本预算的已用额里
    }

    [Fact]
    public async Task 预算提醒_被排除的账单不会把别的分类的提醒推过阈值()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(100m);
        await AddBillAsync(Db.ExpenseCategory.Id, 70m, MidMarch);                  // 70%：未到 80% 的阈值
        await AddBillAsync(savings.Id, 500m, MidMarch.AddHours(1));                // 若被计入，早已超支

        var alert = await _budgets.GetBudgetAlertAsync(Db.User.Id, Year, Month, null, Db.ExpenseCategory.Id);

        Assert.Null(alert);
    }

    [Fact]
    public async Task 预算提醒_开关改回来后这类账单照常触发提醒()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBudgetAsync(100m);
        await AddBillAsync(savings.Id, 120m, MidMarch);

        Assert.Null(await _budgets.GetBudgetAlertAsync(Db.User.Id, Year, Month, null, savings.Id));

        await SetExcludedAsync(savings, false);

        var alert = await _budgets.GetBudgetAlertAsync(Db.User.Id, Year, Month, null, savings.Id);
        Assert.NotNull(alert);
        Assert.True(alert!.IsExceeded);
    }
}
