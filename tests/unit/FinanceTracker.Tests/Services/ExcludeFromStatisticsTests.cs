using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「不计入统计」用例的共用夹具：真实 SQLite + 迁移 + 一个默认账本；
/// 自建分类、账单都走真实的 <see cref="CategoryService"/> / <see cref="BillService"/>，开关也是经服务改的（不直接写库），
/// 这样"服务层确实把开关落库"也一并被覆盖。
/// </summary>
public abstract class ExcludeFromStatisticsTestBase : IAsyncLifetime
{
    protected SqliteTestDatabase Db { get; private set; } = null!;
    protected BillService Bills { get; private set; } = null!;
    protected CategoryService Categories { get; private set; } = null!;

    /// <summary>默认用户名下的默认账本</summary>
    protected Ledger Ledger { get; private set; } = null!;

    /// <summary>预设的收入分类</summary>
    protected Guid IncomeCategoryId { get; private set; }

    /// <summary>预设的支出分类（与 <see cref="Db"/>.ExpenseCategory 不是同一个）</summary>
    protected Guid OtherExpenseCategoryId { get; private set; }

    /// <summary>三月中旬的一个中午（应用时区的墙上时间）</summary>
    protected static readonly DateTime MidMarch = new(2026, 3, 10, 12, 0, 0);

    public virtual async Task InitializeAsync()
    {
        Db = await SqliteTestDatabase.CreateAsync();
        Bills = new BillService(Db.Context, NullLogger<BillService>.Instance);
        Categories = new CategoryService(Db.Context, NullLogger<CategoryService>.Instance);
        Ledger = await Db.GetDefaultLedgerAsync();
        IncomeCategoryId = Db.Context.Categories
            .First(c => c.Type == BillType.Income && c.UserId == null).Id;
        OtherExpenseCategoryId = Db.Context.Categories
            .First(c => c.Type == BillType.Expense && c.UserId == null && c.Id != Db.ExpenseCategory.Id).Id;
    }

    public virtual async Task DisposeAsync() => await Db.DisposeAsync();

    protected static IDisposable UseUtcPlus8() => AppTimeZone.UseZone(TestZones.UtcPlus8);

    /// <summary>新建一个自建分类（默认用户名下）</summary>
    protected Task<Category> AddCategoryAsync(
        string name,
        BillType type = BillType.Expense,
        bool exclude = false,
        Guid? userId = null) =>
        Categories.CreateCategoryAsync(new Category
        {
            UserId = userId ?? Db.User.Id,
            Name = name,
            Icon = "mdi-piggy-bank",
            Type = type,
            SortOrder = 0,
            ExcludeFromStatistics = exclude
        });

    /// <summary>经服务层改一个自建分类的「不计入统计」开关</summary>
    protected Task SetExcludedAsync(Category category, bool exclude) =>
        Categories.UpdateCategoryAsync(new Category
        {
            Id = category.Id,
            UserId = category.UserId,
            Name = category.Name,
            Icon = category.Icon,
            Type = category.Type,
            SortOrder = category.SortOrder,
            ExcludeFromStatistics = exclude
        });

    /// <summary>以应用时区的墙上时间记一笔账单（和记一笔表单一致：Kind=Unspecified）</summary>
    protected Task<Bill> AddBillAsync(
        Guid categoryId,
        decimal amount,
        DateTime wallTime,
        BillType type = BillType.Expense,
        Guid? ledgerId = null) =>
        Bills.CreateBillAsync(new Bill
        {
            UserId = Db.User.Id,
            Amount = amount,
            Type = type,
            CategoryId = categoryId,
            PaymentChannelId = Db.Channel.Id,
            LedgerId = ledgerId ?? Ledger.Id,
            TransactionTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });

    /// <summary>
    /// 经服务层编辑账单、只把分类换掉（其余字段原样带回，和编辑账单的表单一致）。
    /// <paramref name="wallTime"/> 是这笔账单的应用时区墙上时间——编辑入口按「Kind=Unspecified 的墙上时间」重新入库为 UTC
    /// </summary>
    protected Task<Bill> ChangeCategoryAsync(Bill bill, Guid categoryId, DateTime wallTime) =>
        Bills.UpdateBillAsync(new Bill
        {
            Id = bill.Id,
            UserId = bill.UserId,
            Amount = bill.Amount,
            Type = bill.Type,
            CategoryId = categoryId,
            PaymentChannelId = bill.PaymentChannelId,
            LedgerId = bill.LedgerId,
            TransactionTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified),
            Note = bill.Note
        });

    /// <summary>绕过服务层直接执行 SQL（构造服务层不会产生的数据），并清掉变更跟踪，让后续查询读到最新值</summary>
    protected async Task ExecuteSqlAsync(FormattableString sql)
    {
        await Db.Context.Database.ExecuteSqlInterpolatedAsync(sql);
        Db.Context.ChangeTracker.Clear();
    }
}

/// <summary>
/// 「不计入统计」（<see cref="Category.ExcludeFromStatistics"/>）在统计服务里的口径：
/// 月度 / 分类 / 账本 / 趋势 / 年度 / 同比 / 自定义 / 环比一律不含被排除分类的账单；
/// 账单列表与它的汇总不受影响（被排除的账单照常显示）。规则只写在 <c>StatisticsScope</c>，这里逐个统计入口验证它都经过了那一处。
/// </summary>
public sealed class ExcludeFromStatisticsTests : ExcludeFromStatisticsTestBase
{
    private StatisticsService _statistics = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _statistics = new StatisticsService(Db.Context);
    }

    // ───────────────────────── 各统计入口都不含被排除的账单 ─────────────────────────

    [Fact]
    public async Task 月度统计不含被排除分类的账单_收入与其他分类照常()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));
        await AddBillAsync(IncomeCategoryId, 500m, MidMarch.AddHours(2), BillType.Income);

        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3);

        Assert.Equal(100m, monthly.TotalExpense);
        Assert.Equal(500m, monthly.TotalIncome);
        Assert.Equal(2, monthly.BillCount);
    }

    [Fact]
    public async Task 没开开关的自建分类和别的分类一样计入统计()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款");
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));

        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3);

        Assert.False(savings.ExcludeFromStatistics);
        Assert.Equal(1100m, monthly.TotalExpense);
        Assert.Equal(2, monthly.BillCount);
    }

    [Fact]
    public async Task 分类统计里没有被排除的分类_占比按剩下的重算()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 60m, MidMarch);
        await AddBillAsync(OtherExpenseCategoryId, 40m, MidMarch.AddHours(1));
        await AddBillAsync(savings.Id, 900m, MidMarch.AddHours(2));

        var stats = await _statistics.GetCategoryStatisticsAsync(Db.User.Id, 2026, 3, BillType.Expense);

        Assert.DoesNotContain(stats, s => s.CategoryId == savings.Id);
        Assert.Equal(new[] { 60m, 40m }, stats.Select(s => s.Amount));
        Assert.Equal(new[] { 60m, 40m }, stats.Select(s => s.Percentage));
    }

    [Fact]
    public async Task 账本统计不含被排除的分类_各账本合计与月度总额一致()
    {
        using var _ = UseUtcPlus8();
        var travel = await Db.CreateLedgerAsync(Db.User.Id, "旅行");
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 50m, MidMarch);
        await AddBillAsync(savings.Id, 700m, MidMarch.AddHours(1));
        await AddBillAsync(Db.ExpenseCategory.Id, 30m, MidMarch.AddHours(2), ledgerId: travel.Id);
        await AddBillAsync(savings.Id, 500m, MidMarch.AddHours(3), ledgerId: travel.Id);

        var ledgers = await _statistics.GetLedgerStatisticsAsync(Db.User.Id, 2026, 3, BillType.Expense);
        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3);

        Assert.Equal(new[] { 50m, 30m }, ledgers.Select(l => l.Amount));
        Assert.Equal(monthly.TotalExpense, ledgers.Sum(l => l.Amount));
    }

    [Fact]
    public async Task 趋势不含被排除的分类_只剩被排除账单的那天没有数据点()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));
        await AddBillAsync(savings.Id, 2000m, MidMarch.AddDays(1));     // 这天只有被排除的账单

        var trend = await _statistics.GetTrendDataAsync(
            Db.User.Id, new DateTime(2026, 3, 1), TimeBoundaries.EndOfDay(new DateTime(2026, 3, 31)), TrendDimension.Day);

        var point = Assert.Single(trend);
        Assert.Equal(MidMarch.Date, point.Date.Date);
        Assert.Equal(100m, point.Expense);
    }

    [Fact]
    public async Task 年度统计不含被排除的分类_月度柱与三份分类构成都不含()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        var loanIn = await AddCategoryAsync("借入", BillType.Income, exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));
        await AddBillAsync(IncomeCategoryId, 500m, MidMarch.AddHours(2), BillType.Income);
        await AddBillAsync(loanIn.Id, 3000m, MidMarch.AddHours(3), BillType.Income);

        var annual = await _statistics.GetAnnualStatisticsAsync(Db.User.Id, 2026);

        Assert.Equal(100m, annual.TotalExpense);
        Assert.Equal(500m, annual.TotalIncome);
        Assert.Equal(2, annual.BillCount);
        var march = annual.MonthlyData.Single(m => m.Month == 3);
        Assert.Equal(100m, march.TotalExpense);
        Assert.Equal(500m, march.TotalIncome);

        Assert.DoesNotContain(annual.CategoryStats, s => s.CategoryId == savings.Id || s.CategoryId == loanIn.Id);
        Assert.Equal(Db.ExpenseCategory.Id, Assert.Single(annual.ExpenseCategoryStats).CategoryId);
        Assert.Equal(IncomeCategoryId, Assert.Single(annual.IncomeCategoryStats).CategoryId);
    }

    [Fact]
    public async Task 自定义区间统计不含被排除的分类_汇总_分类构成_日趋势都不含()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));

        var custom = await _statistics.GetCustomStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        Assert.Equal(100m, custom.TotalExpense);
        Assert.Equal(1, custom.BillCount);
        Assert.Equal(Db.ExpenseCategory.Id, Assert.Single(custom.ExpenseCategoryStats).CategoryId);
        Assert.Equal(100m, Assert.Single(custom.DailyTrend).Expense);
    }

    [Fact]
    public async Task 分类环比不含被排除的分类_本期与上期都不含()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));
        // 上一等长周期（3/1–3/31 的上一周期是 1/29–2/28）里也有被排除的账单
        await AddBillAsync(Db.ExpenseCategory.Id, 80m, new DateTime(2026, 2, 10, 12, 0, 0));
        await AddBillAsync(savings.Id, 400m, new DateTime(2026, 2, 11, 12, 0, 0));

        var comparison = await _statistics.GetCategoryComparisonAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        var item = Assert.Single(comparison);
        Assert.Equal(Db.ExpenseCategory.Id, item.CategoryId);
        Assert.Equal(100m, item.CurrentAmount);
        Assert.Equal(80m, item.PreviousAmount);
    }

    [Fact]
    public async Task 同比不含被排除的分类_本期与去年同期都不含()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));
        await AddBillAsync(Db.ExpenseCategory.Id, 80m, new DateTime(2025, 3, 10, 12, 0, 0));
        await AddBillAsync(savings.Id, 400m, new DateTime(2025, 3, 11, 12, 0, 0));

        var yoy = await _statistics.GetYearOverYearDataAsync(Db.User.Id, 2026, 3);

        Assert.Equal(100m, yoy.CurrentExpense);
        Assert.Equal(80m, yoy.PreviousYearExpense);
    }

    [Fact]
    public async Task 收入类自建分类也能不计入_不算进收入合计()
    {
        using var _ = UseUtcPlus8();
        var loanIn = await AddCategoryAsync("借入", BillType.Income, exclude: true);
        await AddBillAsync(IncomeCategoryId, 500m, MidMarch, BillType.Income);
        await AddBillAsync(loanIn.Id, 2000m, MidMarch.AddHours(1), BillType.Income);

        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3);
        var income = await _statistics.GetCategoryStatisticsAsync(Db.User.Id, 2026, 3, BillType.Income);

        Assert.Equal(500m, monthly.TotalIncome);
        Assert.Equal(IncomeCategoryId, Assert.Single(income).CategoryId);
    }

    [Fact]
    public async Task 账本筛选下同样不含被排除的账单()
    {
        using var _ = UseUtcPlus8();
        var travel = await Db.CreateLedgerAsync(Db.User.Id, "旅行");
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 30m, MidMarch, ledgerId: travel.Id);
        await AddBillAsync(savings.Id, 500m, MidMarch.AddHours(1), ledgerId: travel.Id);

        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3, travel.Id);

        Assert.Equal(30m, monthly.TotalExpense);
        Assert.Equal(1, monthly.BillCount);
    }

    // ───────────────────────── 开关语义 ─────────────────────────

    [Fact]
    public async Task 开关随时可改_历史账单一并重新计入或不再计入()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款");
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));

        Assert.Equal(1100m, (await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3)).TotalExpense);

        await SetExcludedAsync(savings, true);
        Assert.Equal(100m, (await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3)).TotalExpense);

        await SetExcludedAsync(savings, false);
        Assert.Equal(1100m, (await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3)).TotalExpense);
    }

    [Fact]
    public async Task 编辑账单换到被排除的分类_它从统计里消失并出现在被排除汇总里_换回来又计入()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        var bill = await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(Db.ExpenseCategory.Id, 20m, MidMarch.AddHours(1));
        var marchStart = new DateTime(2026, 3, 1);
        var aprilStart = new DateTime(2026, 4, 1);

        Assert.Equal(120m, (await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3)).TotalExpense);
        Assert.Empty(await _statistics.GetExcludedStatisticsAsync(Db.User.Id, marchStart, aprilStart));

        // 开关按「账单当前的分类」判定，不是记账那一刻的快照：把已有的一笔账单挪进「存款」，它立刻不再计入
        await ChangeCategoryAsync(bill, savings.Id, MidMarch);

        Assert.Equal(20m, (await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3)).TotalExpense);
        var excluded = Assert.Single(await _statistics.GetExcludedStatisticsAsync(Db.User.Id, marchStart, aprilStart));
        Assert.Equal(savings.Id, excluded.CategoryId);
        Assert.Equal(100m, excluded.Amount);
        Assert.Equal(1, excluded.Count);

        // 再挪回普通分类：重新计入，被排除汇总清空
        await ChangeCategoryAsync(bill, Db.ExpenseCategory.Id, MidMarch);

        Assert.Equal(120m, (await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3)).TotalExpense);
        Assert.Empty(await _statistics.GetExcludedStatisticsAsync(Db.User.Id, marchStart, aprilStart));
    }

    [Fact]
    public async Task 开关只认当前用户自己的分类_别人的开关影响不到我()
    {
        using var _ = UseUtcPlus8();
        var other = await Db.CreateUserAsync("13900139000");
        var othersSavings = await AddCategoryAsync("别人的存款", exclude: true, userId: other.Id);

        // 数据异常时的口径：我的账单挂在别人的分类下（服务层不会产生，只能直接入库）——开关是分类主人的个人偏好，不能波及我
        Db.Context.Bills.Add(new Bill
        {
            Id = Guid.NewGuid(),
            UserId = Db.User.Id,
            Amount = 300m,
            Type = BillType.Expense,
            CategoryId = othersSavings.Id,
            PaymentChannelId = Db.Channel.Id,
            LedgerId = Ledger.Id,
            TransactionTime = AppTimeZone.ToUtc(MidMarch),
            Source = BillSource.Manual
        });
        await Db.Context.SaveChangesAsync();

        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3);

        Assert.Equal(300m, monthly.TotalExpense);
    }

    [Fact]
    public async Task 预设分类没有开关_即使库里被错误地标记也照常计入()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        var presetId = Db.ExpenseCategory.Id;
        await ExecuteSqlAsync($"UPDATE Categories SET ExcludeFromStatistics = 1 WHERE Id = {presetId}");

        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3);

        Assert.Equal(100m, monthly.TotalExpense);
    }

    [Fact]
    public async Task 已软删除的被排除分类_残留账单仍不计入统计()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));

        // 另一台设备删掉分类后同步过来：分类被软删除，名下还残留账单（本机的 DeleteCategoryAsync 会拦截，只能直接改库）
        var savingsId = savings.Id;
        await ExecuteSqlAsync($"UPDATE Categories SET IsDeleted = 1 WHERE Id = {savingsId}");

        var monthly = await _statistics.GetMonthlyStatisticsAsync(Db.User.Id, 2026, 3);

        Assert.Equal(100m, monthly.TotalExpense);
    }

    // ───────────────────────── 账单列表不受影响 ─────────────────────────

    [Fact]
    public async Task 账单列表与汇总不受开关影响_被排除的账单照常显示()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));

        var list = await Bills.GetBillsAsync(Db.User.Id, new BillFilter());
        var summary = await Bills.GetBillSummaryAsync(Db.User.Id, new BillFilter());

        Assert.Equal(2, list.Count);
        Assert.Contains(list, b => b.CategoryId == savings.Id);
        Assert.Equal(1100m, summary.TotalExpense);
        Assert.Equal(2, summary.Count);
    }

    // ───────────────────────── 被排除账单汇总（统计页底部的提示） ─────────────────────────

    [Fact]
    public async Task 被排除汇总_每个被排除的分类一项_带类型与笔数_金额降序()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        var loanIn = await AddCategoryAsync("借入", BillType.Income, exclude: true);
        await AddBillAsync(Db.ExpenseCategory.Id, 100m, MidMarch);                       // 没被排除：不出现
        await AddBillAsync(savings.Id, 1000m, MidMarch.AddHours(1));
        await AddBillAsync(savings.Id, 200m, MidMarch.AddHours(2));
        await AddBillAsync(loanIn.Id, 500m, MidMarch.AddHours(3), BillType.Income);

        var excluded = await _statistics.GetExcludedStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1));

        Assert.Equal(new[] { "存款", "借入" }, excluded.Select(e => e.CategoryName));
        var first = excluded[0];
        Assert.Equal(savings.Id, first.CategoryId);
        Assert.Equal(BillType.Expense, first.Type);
        Assert.Equal(1200m, first.Amount);
        Assert.Equal(2, first.Count);
        Assert.False(string.IsNullOrEmpty(first.CategoryIcon));
        var second = excluded[1];
        Assert.Equal(BillType.Income, second.Type);
        Assert.Equal(500m, second.Amount);
        Assert.Equal(1, second.Count);
    }

    [Fact]
    public async Task 被排除汇总_金额相同时按分类名称排序_结果稳定()
    {
        using var _ = UseUtcPlus8();
        // 期望顺序用与实现相同的默认字符串比较器算出来；分类按"期望顺序的反序"建，
        // 让库里的插入顺序与期望顺序相反——没有按名称兜底排序的话，这里会按插入顺序出结果而失败
        var expected = new[] { "甲", "乙" }.OrderBy(n => n, StringComparer.CurrentCulture).ToArray();
        foreach (var name in Enumerable.Reverse(expected))
        {
            var category = await AddCategoryAsync(name, exclude: true);
            await AddBillAsync(category.Id, 100m, MidMarch);
        }

        var excluded = await _statistics.GetExcludedStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1));

        Assert.Equal(expected, excluded.Select(e => e.CategoryName));
    }

    [Fact]
    public async Task 被排除汇总的区间左闭右开且按应用时区()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(savings.Id, 1m, new DateTime(2026, 2, 28, 23, 59, 0));    // 2 月最后一刻
        await AddBillAsync(savings.Id, 2m, new DateTime(2026, 3, 1, 0, 0, 0));       // 3 月第一刻（UTC 2/28 16:00，按 UTC 分月会误归 2 月）
        await AddBillAsync(savings.Id, 4m, new DateTime(2026, 3, 31, 23, 59, 0));    // 3 月最后一刻
        await AddBillAsync(savings.Id, 8m, new DateTime(2026, 4, 1, 0, 0, 0));       // 右端点，不属于 3 月

        var excluded = await _statistics.GetExcludedStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1));

        var march = Assert.Single(excluded);
        Assert.Equal(6m, march.Amount);
        Assert.Equal(2, march.Count);
    }

    [Fact]
    public async Task 被排除汇总按账本过滤_不含软删除的账单()
    {
        using var _ = UseUtcPlus8();
        var travel = await Db.CreateLedgerAsync(Db.User.Id, "旅行");
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(savings.Id, 700m, MidMarch);                                  // 默认账本
        await AddBillAsync(savings.Id, 500m, MidMarch.AddHours(1), ledgerId: travel.Id);
        var deleted = await AddBillAsync(savings.Id, 9000m, MidMarch.AddHours(2), ledgerId: travel.Id);
        Assert.True(await Bills.DeleteBillAsync(deleted.Id, Db.User.Id));

        var inTravel = await _statistics.GetExcludedStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1), travel.Id);
        var everywhere = await _statistics.GetExcludedStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1));

        Assert.Equal(500m, Assert.Single(inTravel).Amount);
        Assert.Equal(1200m, Assert.Single(everywhere).Amount);
    }

    [Fact]
    public async Task 被排除汇总_没有被排除的账单时为空_关掉开关后也为空()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(savings.Id, 1000m, MidMarch);

        Assert.Single(await _statistics.GetExcludedStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1)));

        await SetExcludedAsync(savings, false);

        Assert.Empty(await _statistics.GetExcludedStatisticsAsync(
            Db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1)));
    }

    [Fact]
    public async Task 被排除汇总只算当前用户自己的账单()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(savings.Id, 1000m, MidMarch);
        var other = await Db.CreateUserAsync("13900139000");

        var excluded = await _statistics.GetExcludedStatisticsAsync(
            other.Id, new DateTime(2026, 3, 1), new DateTime(2026, 4, 1));

        Assert.Empty(excluded);
    }
}
