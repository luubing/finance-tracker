using System.Globalization;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 时区边界回归（P6）：库里的 TransactionTime 是 UTC，而用户按自己的日历看"某天/某月/某年"。
/// 每个用例都刻意选在"UTC 日期 ≠ 本地日期"的时刻（东八区凌晨 00:xx / 深夜 23:xx），
/// 不做换算时这些账单会被归到相邻的日/月/年。
/// 所有账单都经 <see cref="BillService"/> 以应用时区的墙上时间（Kind=Unspecified，与表单输入一致）创建。
/// 注意：时区覆盖用 AsyncLocal 实现，必须在测试方法本身里 using，不能放进 InitializeAsync。
/// </summary>
public sealed class TimeZoneBoundaryTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _billService = null!;
    private StatisticsService _statistics = null!;
    private Guid _incomeCategoryId;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _billService = new BillService(_db.Context, NullLogger<BillService>.Instance);
        _statistics = new StatisticsService(_db.Context);
        _incomeCategoryId = _db.Context.Categories
            .First(c => c.Type == BillType.Income && c.UserId == null).Id;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static IDisposable UseUtcPlus8() => AppTimeZone.UseZone(TestZones.UtcPlus8);

    /// <summary>以应用时区的墙上时间创建账单（和 AddBill 页面表单一致：Kind=Unspecified）</summary>
    private Task<Bill> AddBillAsync(
        DateTime wallTime,
        decimal amount = 10m,
        BillType type = BillType.Expense,
        Guid? categoryId = null) =>
        _billService.CreateBillAsync(new Bill
        {
            UserId = _db.User.Id,
            Amount = amount,
            Type = type,
            CategoryId = categoryId ?? (type == BillType.Expense ? _db.ExpenseCategory.Id : _incomeCategoryId),
            PaymentChannelId = _db.Channel.Id,
            TransactionTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });

    private async Task<DateTime> ReadRawStoredTimeAsync()
    {
        await using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT TransactionTime FROM Bills LIMIT 1;";
        var raw = (string)(await command.ExecuteScalarAsync())!;
        return DateTime.Parse(raw, CultureInfo.InvariantCulture);
    }

    // ───────────────────────── 写入与读出 ─────────────────────────

    [Fact]
    public async Task 创建账单_应用时区墙上时间入库为UTC()
    {
        using var _ = UseUtcPlus8();

        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0));

        // 本地 3 月 1 日 00:30 = UTC 2 月 28 日 16:30
        Assert.Equal(new DateTime(2026, 2, 28, 16, 30, 0), await ReadRawStoredTimeAsync());
    }

    [Fact]
    public async Task 直接写入Kind为Local的账单时间_按应用时区而不是系统时区换算()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcMinus5);

        // 绕过 BillService，让 EF 值转换器直接面对 Kind=Local 的值（例如某处代码直接塞了 DateTime.Now）
        _db.Context.Bills.Add(new Bill
        {
            UserId = _db.User.Id,
            Amount = 10m,
            Type = BillType.Expense,
            CategoryId = _db.ExpenseCategory.Id,
            PaymentChannelId = _db.Channel.Id,
            TransactionTime = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Local),
            Source = BillSource.Manual
        });
        await _db.Context.SaveChangesAsync();

        // 应用时区 UTC-5：本地 10:00 = UTC 15:00。
        // 若转换器用 ToUniversalTime()（系统时区），结果取决于运行机器，与 AppTimeZone 的约定不一致
        Assert.Equal(new DateTime(2026, 3, 1, 15, 0, 0), await ReadRawStoredTimeAsync());
    }

    [Fact]
    public async Task 读回账单_Kind为Utc_展示时换算为本地时间()
    {
        using var _ = UseUtcPlus8();
        var created = await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0));

        // 清空跟踪器，确保真的从 SQLite 读（SQLite 读出的 DateTime 本来没有 Kind）
        _db.Context.ChangeTracker.Clear();
        var read = await _billService.GetBillByIdAsync(created.Id);

        Assert.NotNull(read);
        Assert.Equal(DateTimeKind.Utc, read!.TransactionTime.Kind);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 30, 0), AppTimeZone.ToLocal(read.TransactionTime));
    }

    [Fact]
    public async Task 更新账单_把读出的时间原样回写_不会被二次换算()
    {
        using var _ = UseUtcPlus8();
        var created = await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0));

        _db.Context.ChangeTracker.Clear();
        var read = await _billService.GetBillByIdAsync(created.Id);

        // 模拟"复制/再提交"：把读出的字段拷到一个新对象再更新（未修改时间）
        await _billService.UpdateBillAsync(new Bill
        {
            Id = read!.Id,
            UserId = read.UserId,
            Amount = read.Amount,
            Type = read.Type,
            CategoryId = read.CategoryId,
            PaymentChannelId = read.PaymentChannelId,
            TransactionTime = read.TransactionTime,
            Note = "只改备注"
        });

        // 若读出的值丢了 Kind，会被当作本地时间再减 8 小时（变成 08:30 UTC 之前的某个值）
        Assert.Equal(new DateTime(2026, 2, 28, 16, 30, 0), await ReadRawStoredTimeAsync());
    }

    [Fact]
    public async Task 编辑账单时修改时间_按应用时区墙上时间入库为UTC()
    {
        using var _ = UseUtcPlus8();
        var created = await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0));

        await _billService.UpdateBillAsync(new Bill
        {
            Id = created.Id,
            UserId = created.UserId,
            Amount = created.Amount,
            Type = created.Type,
            CategoryId = created.CategoryId,
            PaymentChannelId = created.PaymentChannelId,
            TransactionTime = new DateTime(2026, 3, 5, 9, 15, 0)
        });

        Assert.Equal(new DateTime(2026, 3, 5, 1, 15, 0), await ReadRawStoredTimeAsync());
    }

    // ───────────────────────── 账单列表筛选 ─────────────────────────

    [Fact]
    public async Task 按本地日期筛选账单_边界按应用时区而不是UTC()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 1m);   // UTC 02-28 16:30，本地 3/1
        await AddBillAsync(new DateTime(2026, 3, 1, 23, 59, 30), amount: 2m); // UTC 03-01 15:59:30，本地 3/1
        await AddBillAsync(new DateTime(2026, 3, 2, 0, 0, 30), amount: 3m);   // UTC 03-01 16:00:30，本地 3/2

        // 与 Bills 页面一致：开始 = 当天 00:00，结束 = 当天最后一个 tick（TimeBoundaries.EndOfDay）
        var start = new DateTime(2026, 3, 1);
        var end = TimeBoundaries.EndOfDay(start);

        var bills = await _billService.GetBillsAsync(_db.User.Id, start, end);
        var count = await _billService.GetBillCountAsync(_db.User.Id, start, end);

        Assert.Equal(new[] { 1m, 2m }, bills.Select(b => b.Amount).OrderBy(a => a));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task 按本地日期筛选账单_当天最后一秒内的账单不会被漏掉()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 1, 23, 59, 59, 500), amount: 1m);  // 当天最后一秒（带小数秒，短信/通知捕获会产生这种时间）
        await AddBillAsync(new DateTime(2026, 3, 2, 0, 0, 0), amount: 2m);          // 次日零点，不属于 3/1

        var start = new DateTime(2026, 3, 1);
        var end = TimeBoundaries.EndOfDay(start);

        var bills = await _billService.GetBillsAsync(_db.User.Id, start, end);
        var count = await _billService.GetBillCountAsync(_db.User.Id, start, end);

        Assert.Equal(new[] { 1m }, bills.Select(b => b.Amount));
        Assert.Equal(1, count);
    }

    // ───────────────────────── 统计：月/分类/年 ─────────────────────────

    [Fact]
    public async Task 月度统计_按应用时区的自然月归属()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 2, 28, 23, 30, 0), amount: 1m);    // 本地 2 月（UTC 2/28 15:30）
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 10m);     // 本地 3 月（UTC 2/28 16:30）
        await AddBillAsync(new DateTime(2026, 3, 31, 23, 30, 0), amount: 100m);  // 本地 3 月（UTC 3/31 15:30）
        await AddBillAsync(new DateTime(2026, 4, 1, 0, 10, 0), amount: 1000m);   // 本地 4 月（UTC 3/31 16:10）

        var february = await _statistics.GetMonthlyStatisticsAsync(_db.User.Id, 2026, 2);
        var march = await _statistics.GetMonthlyStatisticsAsync(_db.User.Id, 2026, 3);
        var april = await _statistics.GetMonthlyStatisticsAsync(_db.User.Id, 2026, 4);

        Assert.Equal((1, 1m), (february.BillCount, february.TotalExpense));
        Assert.Equal((2, 110m), (march.BillCount, march.TotalExpense));
        Assert.Equal((1, 1000m), (april.BillCount, april.TotalExpense));
    }

    [Fact]
    public async Task 月度统计_收入与支出分别汇总()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 10m);
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 40, 0), amount: 500m, type: BillType.Income);

        var march = await _statistics.GetMonthlyStatisticsAsync(_db.User.Id, 2026, 3);

        Assert.Equal(10m, march.TotalExpense);
        Assert.Equal(500m, march.TotalIncome);
    }

    [Fact]
    public async Task 分类统计_与月度统计使用同一自然月边界()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 2, 28, 23, 30, 0), amount: 1m);
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 10m);
        await AddBillAsync(new DateTime(2026, 3, 31, 23, 30, 0), amount: 100m);
        await AddBillAsync(new DateTime(2026, 4, 1, 0, 10, 0), amount: 1000m);

        var stats = await _statistics.GetCategoryStatisticsAsync(_db.User.Id, 2026, 3, BillType.Expense);

        var only = Assert.Single(stats);
        Assert.Equal((110m, 2), (only.Amount, only.Count));
    }

    [Fact]
    public async Task 年度统计_年份与月份按应用时区归属()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2025, 12, 31, 23, 30, 0), amount: 1m);   // 本地 2025 年
        await AddBillAsync(new DateTime(2026, 1, 1, 0, 30, 0), amount: 10m);     // 本地 2026 年 1 月（UTC 2025-12-31）
        await AddBillAsync(new DateTime(2026, 12, 31, 23, 30, 0), amount: 100m); // 本地 2026 年 12 月
        await AddBillAsync(new DateTime(2027, 1, 1, 0, 30, 0), amount: 1000m);   // 本地 2027 年（UTC 2026-12-31）

        var annual = await _statistics.GetAnnualStatisticsAsync(_db.User.Id, 2026);

        Assert.Equal(2, annual.BillCount);
        Assert.Equal(110m, annual.TotalExpense);
        Assert.Equal(10m, annual.MonthlyData[0].TotalExpense);    // 1 月
        Assert.Equal(100m, annual.MonthlyData[11].TotalExpense);  // 12 月
        Assert.Equal(0m, annual.MonthlyData[5].TotalExpense);
    }

    // ───────────────────────── 趋势 ─────────────────────────

    [Fact]
    public async Task 趋势按天_同一本地日的账单归入同一天()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 10m);   // UTC 2/28
        await AddBillAsync(new DateTime(2026, 3, 1, 23, 30, 0), amount: 20m);  // UTC 3/1

        var trend = await _statistics.GetTrendDataAsync(
            _db.User.Id,
            new DateTime(2026, 3, 1),
            TimeBoundaries.EndOfDay(new DateTime(2026, 3, 1)),
            TrendDimension.Day);

        // 按 UTC 日期分组会拆成 2/28 和 3/1 两天
        var day = Assert.Single(trend);
        Assert.Equal(new DateTime(2026, 3, 1), day.Date);
        Assert.Equal(30m, day.Expense);
    }

    [Fact]
    public async Task 趋势按天_月末最后一秒内的账单仍计入_且合计与月度统计一致()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 31, 23, 59, 59, 500), amount: 7m);  // 3 月的最后一秒
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 3m);
        await AddBillAsync(new DateTime(2026, 4, 1, 0, 0, 0), amount: 9m);           // 4 月，不应计入

        var trend = await _statistics.GetTrendDataAsync(
            _db.User.Id,
            new DateTime(2026, 3, 1),
            TimeBoundaries.EndOfMonth(2026, 3),
            TrendDimension.Day);
        var monthly = await _statistics.GetMonthlyStatisticsAsync(_db.User.Id, 2026, 3);

        Assert.Equal(
            new[] { (new DateTime(2026, 3, 1), 3m), (new DateTime(2026, 3, 31), 7m) },
            trend.Select(t => (t.Date, t.Expense)));
        // 趋势（含端点）与月度汇总（左闭右开）必须是同一个口径，否则同一页上两个数字对不上
        Assert.Equal(monthly.TotalExpense, trend.Sum(t => t.Expense));
    }

    [Fact]
    public async Task 趋势按周_周一凌晨的账单属于新的一周()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 10m);  // 周日（本地），周起点 2/23
        await AddBillAsync(new DateTime(2026, 3, 2, 0, 30, 0), amount: 20m);  // 周一（本地），UTC 仍是周日 3/1

        var trend = await _statistics.GetTrendDataAsync(
            _db.User.Id,
            new DateTime(2026, 2, 23),
            TimeBoundaries.EndOfDay(new DateTime(2026, 3, 8)),
            TrendDimension.Week);

        Assert.Equal(
            new[] { (new DateTime(2026, 2, 23), 10m), (new DateTime(2026, 3, 2), 20m) },
            trend.Select(t => (t.Date, t.Expense)));
    }

    [Fact]
    public async Task 趋势按月_月初凌晨的账单属于新的一个月()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 2, 28, 23, 30, 0), amount: 10m);
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 20m);   // UTC 仍是 2/28

        var trend = await _statistics.GetTrendDataAsync(
            _db.User.Id,
            new DateTime(2026, 2, 1),
            TimeBoundaries.EndOfMonth(2026, 3),
            TrendDimension.Month);

        Assert.Equal(
            new[] { (new DateTime(2026, 2, 1), 10m), (new DateTime(2026, 3, 1), 20m) },
            trend.Select(t => (t.Date, t.Expense)));
    }

    // ───────────────────────── 自定义区间 / 分类对比 / 同比 ─────────────────────────

    [Fact]
    public async Task 自定义统计_区间含端点日_每日趋势按本地日历()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 1m);
        await AddBillAsync(new DateTime(2026, 3, 1, 23, 30, 0), amount: 2m);
        await AddBillAsync(new DateTime(2026, 3, 2, 0, 30, 0), amount: 4m);

        var oneDay = await _statistics.GetCustomStatisticsAsync(
            _db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 1));
        var twoDays = await _statistics.GetCustomStatisticsAsync(
            _db.User.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 2));

        Assert.Equal((2, 3m), (oneDay.BillCount, oneDay.TotalExpense));
        Assert.Equal(
            new[] { (new DateTime(2026, 3, 1), 3m), (new DateTime(2026, 3, 2), 4m) },
            twoDays.DailyTrend.Select(t => (t.Date, t.Expense)));
    }

    [Fact]
    public async Task 分类对比_上一等长周期按本地日历紧邻回推()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 50m);    // 上期第一天凌晨（UTC 2/28）
        await AddBillAsync(new DateTime(2026, 3, 8, 0, 30, 0), amount: 80m);    // 本期第一天凌晨（UTC 3/7）
        await AddBillAsync(new DateTime(2026, 3, 14, 23, 30, 0), amount: 20m);  // 本期最后一天深夜
        await AddBillAsync(new DateTime(2026, 3, 15, 0, 30, 0), amount: 999m);  // 本期之后，不应计入

        // 本期 3/8 ~ 3/14（7 天），上期 3/1 ~ 3/7
        var comparison = await _statistics.GetCategoryComparisonAsync(
            _db.User.Id, new DateTime(2026, 3, 8), new DateTime(2026, 3, 14));

        var only = Assert.Single(comparison);
        Assert.Equal(100m, only.CurrentAmount);
        Assert.Equal(50m, only.PreviousAmount);
    }

    [Fact]
    public async Task 月度同比_上年同月边界同样按应用时区()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2025, 3, 1, 0, 30, 0), amount: 40m);   // 上年 3 月（UTC 2025-02-28）
        await AddBillAsync(new DateTime(2025, 2, 28, 23, 30, 0), amount: 7m);  // 上年 2 月，不应计入
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 60m);   // 本年 3 月

        var yoy = await _statistics.GetYearOverYearDataAsync(_db.User.Id, 2026, 3);

        Assert.Equal(60m, yoy.CurrentExpense);
        Assert.Equal(40m, yoy.PreviousYearExpense);
    }

    // ───────────────────────── 预算 ─────────────────────────

    [Fact]
    public async Task 预算已用金额_按应用时区的自然月统计()
    {
        using var _ = UseUtcPlus8();
        var budgetService = new BudgetService(_db.Context, NullLogger<BudgetService>.Instance);
        await budgetService.CreateBudgetAsync(new Budget
        {
            UserId = _db.User.Id,
            Year = 2026,
            Month = 3,
            Amount = 5000m
        });

        await AddBillAsync(new DateTime(2026, 2, 28, 23, 30, 0), amount: 1m);
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 30, 0), amount: 10m);
        await AddBillAsync(new DateTime(2026, 3, 31, 23, 30, 0), amount: 100m);
        await AddBillAsync(new DateTime(2026, 4, 1, 0, 10, 0), amount: 1000m);

        var status = Assert.Single(await budgetService.GetBudgetStatusAsync(_db.User.Id, 2026, 3));

        Assert.Equal(110m, status.UsedAmount);
    }

    // ───────────────────────── 夏令时 ─────────────────────────

    [Fact]
    public async Task 夏令时切换日_只有23小时的自然日仍完整归属()
    {
        using var _ = AppTimeZone.UseZone(TestZones.EuropeLike);
        await AddBillAsync(new DateTime(2026, 3, 29, 0, 30, 0), amount: 1m);   // 当天开始（UTC+1，UTC 3/28 23:30）
        await AddBillAsync(new DateTime(2026, 3, 29, 23, 30, 0), amount: 2m);  // 当天结束（UTC+2，UTC 3/29 21:30）
        await AddBillAsync(new DateTime(2026, 3, 30, 0, 10, 0), amount: 4m);   // 次日（UTC 3/29 22:10）

        var day = await _statistics.GetCustomStatisticsAsync(
            _db.User.Id, new DateTime(2026, 3, 29), new DateTime(2026, 3, 29));

        Assert.Equal((2, 3m), (day.BillCount, day.TotalExpense));
        Assert.Equal(new DateTime(2026, 3, 29), Assert.Single(day.DailyTrend).Date);
    }

    [Fact]
    public async Task 手填了夏令时不存在的时间_保存不崩溃()
    {
        using var _ = AppTimeZone.UseZone(TestZones.EuropeLike);

        var saved = await AddBillAsync(new DateTime(2026, 3, 29, 2, 30, 0));

        Assert.NotEqual(Guid.Empty, saved.Id);
    }
}
