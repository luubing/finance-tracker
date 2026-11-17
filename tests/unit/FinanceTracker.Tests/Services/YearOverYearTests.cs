using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 同比分析（P10 / P11）。
/// <para>P11：去年同期为 0 时没有对比基数，变化率为 <c>null</c>（界面显示"—"），而不是会被误读成"没有变化"的 0%。</para>
/// <para>
/// P10：年度报告的同比必须是"整年"口径——已结束的年份整年 vs 上一整年；当年是"年初至今 vs 去年同月同日"，
/// 而不是旧实现里"所选年份的当前月份 vs 去年同月"（在年度页里既不是整年，对历史年份还会拿今天的月份去比）。
/// </para>
/// 所有用例固定在东八区，"今天"通过注入的 <see cref="TimeProvider"/> 控制。
/// </summary>
public sealed class YearOverYearTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _billService = null!;
    private Guid _incomeCategoryId;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _billService = new BillService(_db.Context, NullLogger<BillService>.Instance);
        _incomeCategoryId = _db.Context.Categories
            .First(c => c.Type == BillType.Income && c.UserId == null).Id;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static IDisposable UseUtcPlus8() => AppTimeZone.UseZone(TestZones.UtcPlus8);

    /// <summary>构造一个"此刻是东八区的 y-m-d h:mm"的统计服务</summary>
    private StatisticsService ServiceAt(int y, int m, int d, int hour = 12)
    {
        var localNow = new DateTime(y, m, d, hour, 0, 0, DateTimeKind.Unspecified);
        var utcNow = new DateTimeOffset(AppTimeZone.ToUtc(localNow), TimeSpan.Zero);
        return new StatisticsService(_db.Context, new FixedTimeProvider(utcNow));
    }

    private Task<Bill> AddBillAsync(
        DateTime wallTime,
        decimal amount = 10m,
        BillType type = BillType.Expense,
        Guid? ledgerId = null) =>
        _billService.CreateBillAsync(new Bill
        {
            UserId = _db.User.Id,
            Amount = amount,
            Type = type,
            CategoryId = type == BillType.Expense ? _db.ExpenseCategory.Id : _incomeCategoryId,
            PaymentChannelId = _db.Channel.Id,
            LedgerId = ledgerId,
            TransactionTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });

    // ───────────────────────── P11：变化率 ─────────────────────────

    [Fact]
    public void 去年同期为0_变化率为null_而不是0()
    {
        var data = new YearOverYearData
        {
            CurrentExpense = 100m,
            CurrentIncome = 200m,
            PreviousYearExpense = 0m,
            PreviousYearIncome = 0m
        };

        Assert.Null(data.ExpenseChangeRate);
        Assert.Null(data.IncomeChangeRate);
    }

    [Theory]
    [InlineData(150, 100, 50)]
    [InlineData(50, 100, -50)]
    [InlineData(0, 100, -100)]
    [InlineData(100, 100, 0)]
    [InlineData(300, 100, 200)]
    public void 去年同期大于0_变化率按百分比计算(int current, int previous, int expectedRate)
    {
        var data = new YearOverYearData
        {
            CurrentExpense = current,
            PreviousYearExpense = previous,
            CurrentIncome = current,
            PreviousYearIncome = previous
        };

        Assert.Equal(expectedRate, data.ExpenseChangeRate);
        Assert.Equal(expectedRate, data.IncomeChangeRate);
    }

    [Fact]
    public void 支出与收入的变化率相互独立()
    {
        var data = new YearOverYearData
        {
            CurrentExpense = 150m,
            PreviousYearExpense = 100m,   // 支出有基数
            CurrentIncome = 500m,
            PreviousYearIncome = 0m       // 收入没有基数
        };

        Assert.Equal(50m, data.ExpenseChangeRate);
        Assert.Null(data.IncomeChangeRate);
    }

    // ───────────────────────── 月度同比 ─────────────────────────

    [Fact]
    public async Task 月度同比_去年同月没有账单_变化率为null_且带对比期间()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 3, 10, 12, 0, 0), amount: 60m);

        var data = await ServiceAt(2026, 11, 17).GetYearOverYearDataAsync(_db.User.Id, 2026, 3);

        Assert.Equal(60m, data.CurrentExpense);
        Assert.Equal(0m, data.PreviousYearExpense);
        Assert.Null(data.ExpenseChangeRate);
        Assert.Equal(3, data.CurrentMonth);
        Assert.Equal(new DateTime(2026, 3, 1), data.CurrentPeriodStart);
        Assert.Equal(new DateTime(2026, 3, 31), data.CurrentPeriodEnd);
        Assert.Equal(new DateTime(2025, 3, 1), data.PreviousPeriodStart);
        Assert.Equal(new DateTime(2025, 3, 31), data.PreviousPeriodEnd);
    }

    [Fact]
    public async Task 月度同比_去年闰年2月29日的账单_计入同比()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2024, 2, 29, 12, 0, 0), amount: 40m);   // 2024 是闰年
        await AddBillAsync(new DateTime(2025, 2, 10, 12, 0, 0), amount: 60m);

        // 2025 年 2 月 vs 2024 年 2 月（29 天）
        var data = await ServiceAt(2026, 11, 17).GetYearOverYearDataAsync(_db.User.Id, 2025, 2);

        Assert.Equal(40m, data.PreviousYearExpense);
        Assert.Equal(new DateTime(2024, 2, 29), data.PreviousPeriodEnd);
        Assert.Equal(new DateTime(2025, 2, 28), data.CurrentPeriodEnd);
        Assert.Equal(50m, data.ExpenseChangeRate);   // (60 - 40) / 40 * 100
    }

    // ───────────────────────── P10：整年同比 ─────────────────────────

    [Fact]
    public async Task 已结束的年份_整年对比上一整年()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2025, 1, 1, 0, 30, 0), amount: 100m);    // 2025 年初（UTC 是 2024-12-31）
        await AddBillAsync(new DateTime(2025, 12, 31, 23, 30, 0), amount: 200m); // 2025 年末
        await AddBillAsync(new DateTime(2026, 1, 1, 0, 30, 0), amount: 999m);    // 2026 年，不属于 2025
        await AddBillAsync(new DateTime(2024, 1, 1, 0, 30, 0), amount: 50m);     // 2024 年初
        await AddBillAsync(new DateTime(2024, 12, 31, 23, 30, 0), amount: 150m); // 2024 年末
        await AddBillAsync(new DateTime(2023, 12, 31, 23, 30, 0), amount: 777m); // 2023 年，不属于 2024

        // 此刻是 2026 年 11 月 17 日：2025 已经结束
        var data = await ServiceAt(2026, 11, 17).GetAnnualYearOverYearDataAsync(_db.User.Id, 2025);

        Assert.Equal(300m, data.CurrentExpense);
        Assert.Equal(200m, data.PreviousYearExpense);
        Assert.Equal(50m, data.ExpenseChangeRate);
        Assert.Null(data.CurrentMonth);
        Assert.Equal(new DateTime(2025, 1, 1), data.CurrentPeriodStart);
        Assert.Equal(new DateTime(2025, 12, 31), data.CurrentPeriodEnd);
        Assert.Equal(new DateTime(2024, 1, 1), data.PreviousPeriodStart);
        Assert.Equal(new DateTime(2024, 12, 31), data.PreviousPeriodEnd);
    }

    [Fact]
    public async Task 当年_年初至今对比去年同月同日_含今天()
    {
        using var _ = UseUtcPlus8();
        // 今年（截至 2026-11-17 含当天）
        await AddBillAsync(new DateTime(2026, 1, 1, 0, 30, 0), amount: 100m);
        await AddBillAsync(new DateTime(2026, 11, 17, 9, 0, 0), amount: 20m);    // 今天，计入
        await AddBillAsync(new DateTime(2026, 11, 18, 0, 30, 0), amount: 999m);  // 明天，不计入
        // 去年同期（截至 2025-11-17 含当天）
        await AddBillAsync(new DateTime(2025, 1, 1, 0, 30, 0), amount: 50m);
        await AddBillAsync(new DateTime(2025, 11, 17, 23, 0, 0), amount: 30m);   // 去年同一天晚上，计入
        await AddBillAsync(new DateTime(2025, 11, 18, 0, 30, 0), amount: 888m);  // 去年同日之后，不计入
        await AddBillAsync(new DateTime(2025, 12, 25, 12, 0, 0), amount: 777m);  // 去年年底，不计入

        var data = await ServiceAt(2026, 11, 17).GetAnnualYearOverYearDataAsync(_db.User.Id, 2026);

        Assert.Equal(120m, data.CurrentExpense);
        Assert.Equal(80m, data.PreviousYearExpense);
        Assert.Equal(50m, data.ExpenseChangeRate);
        Assert.Null(data.CurrentMonth);
        Assert.Equal(new DateTime(2026, 1, 1), data.CurrentPeriodStart);
        Assert.Equal(new DateTime(2026, 11, 17), data.CurrentPeriodEnd);
        Assert.Equal(new DateTime(2025, 1, 1), data.PreviousPeriodStart);
        Assert.Equal(new DateTime(2025, 11, 17), data.PreviousPeriodEnd);
    }

    [Fact]
    public async Task 当年_收入同比同样按年初至今口径()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 6, 1, 12, 0, 0), amount: 300m, type: BillType.Income);
        await AddBillAsync(new DateTime(2025, 6, 1, 12, 0, 0), amount: 200m, type: BillType.Income);
        await AddBillAsync(new DateTime(2025, 12, 1, 12, 0, 0), amount: 999m, type: BillType.Income); // 去年同期之后

        var data = await ServiceAt(2026, 11, 17).GetAnnualYearOverYearDataAsync(_db.User.Id, 2026);

        Assert.Equal(300m, data.CurrentIncome);
        Assert.Equal(200m, data.PreviousYearIncome);
        Assert.Equal(50m, data.IncomeChangeRate);
    }

    [Fact]
    public async Task 当年_今天是闰日_去年同期回退到2月28日()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2028, 2, 29, 9, 0, 0), amount: 30m);      // 今天（闰日）
        await AddBillAsync(new DateTime(2027, 2, 28, 10, 0, 0), amount: 20m);     // 去年同期的最后一天，计入
        await AddBillAsync(new DateTime(2027, 3, 1, 0, 30, 0), amount: 999m);     // 之后，不计入

        var data = await ServiceAt(2028, 2, 29).GetAnnualYearOverYearDataAsync(_db.User.Id, 2028);

        Assert.Equal(30m, data.CurrentExpense);
        Assert.Equal(20m, data.PreviousYearExpense);
        Assert.Equal(new DateTime(2028, 2, 29), data.CurrentPeriodEnd);
        Assert.Equal(new DateTime(2027, 2, 28), data.PreviousPeriodEnd);
    }

    [Fact]
    public async Task 当年_今天是1月1日_只比较这一天()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 1, 1, 0, 30, 0), amount: 12m);      // 今天凌晨（UTC 是 2025-12-31）
        await AddBillAsync(new DateTime(2026, 1, 2, 0, 30, 0), amount: 999m);
        await AddBillAsync(new DateTime(2025, 1, 1, 8, 0, 0), amount: 10m);
        await AddBillAsync(new DateTime(2025, 1, 2, 8, 0, 0), amount: 888m);

        var data = await ServiceAt(2026, 1, 1, hour: 8).GetAnnualYearOverYearDataAsync(_db.User.Id, 2026);

        Assert.Equal(12m, data.CurrentExpense);
        Assert.Equal(10m, data.PreviousYearExpense);
        Assert.Equal(new DateTime(2026, 1, 1), data.CurrentPeriodEnd);
        Assert.Equal(new DateTime(2025, 1, 1), data.PreviousPeriodEnd);
    }

    [Fact]
    public async Task 此刻的本地日期与UTC日期不同_以应用时区的今天为准()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 11, 18, 0, 30, 0), amount: 5m);     // 本地 11-18 凌晨（UTC 11-17 16:30）

        // 此刻 UTC 2026-11-17 16:40 = 本地 2026-11-18 00:40：UTC 日期还是 17 号，本地已经是 18 号
        var service = new StatisticsService(
            _db.Context,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 17, 16, 40, 0, TimeSpan.Zero)));

        var data = await service.GetAnnualYearOverYearDataAsync(_db.User.Id, 2026);

        Assert.Equal(new DateTime(2026, 11, 18), data.CurrentPeriodEnd);   // 若按 UTC 日期取"今天"，这里会是 11-17
        Assert.Equal(5m, data.CurrentExpense);
    }

    [Fact]
    public async Task 去年同期没有数据_变化率为null_当前有数据()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 5, 1, 12, 0, 0), amount: 100m);
        await AddBillAsync(new DateTime(2026, 5, 2, 12, 0, 0), amount: 500m, type: BillType.Income);

        var data = await ServiceAt(2026, 11, 17).GetAnnualYearOverYearDataAsync(_db.User.Id, 2026);

        Assert.Equal(100m, data.CurrentExpense);
        Assert.Null(data.ExpenseChangeRate);
        Assert.Null(data.IncomeChangeRate);
    }

    [Fact]
    public async Task 未来年份_当前期间为空_比较整年()
    {
        using var _ = UseUtcPlus8();
        await AddBillAsync(new DateTime(2026, 6, 1, 12, 0, 0), amount: 100m);

        var data = await ServiceAt(2026, 11, 17).GetAnnualYearOverYearDataAsync(_db.User.Id, 2027);

        Assert.Equal(0m, data.CurrentExpense);
        Assert.Equal(100m, data.PreviousYearExpense);
        Assert.Equal(-100m, data.ExpenseChangeRate);
        Assert.Equal(new DateTime(2027, 12, 31), data.CurrentPeriodEnd);
        Assert.Equal(new DateTime(2026, 12, 31), data.PreviousPeriodEnd);
    }

    [Fact]
    public async Task 整年同比_按账本筛选()
    {
        using var _ = UseUtcPlus8();
        var ledger = new Ledger { UserId = _db.User.Id, Name = "旅行账本", Icon = "mdi-book" };
        _db.Context.Ledgers.Add(ledger);
        await _db.Context.SaveChangesAsync();

        await AddBillAsync(new DateTime(2026, 6, 1, 12, 0, 0), amount: 100m, ledgerId: ledger.Id);
        await AddBillAsync(new DateTime(2026, 6, 2, 12, 0, 0), amount: 7m);                          // 未归属账本
        await AddBillAsync(new DateTime(2025, 6, 1, 12, 0, 0), amount: 50m, ledgerId: ledger.Id);
        await AddBillAsync(new DateTime(2025, 6, 2, 12, 0, 0), amount: 3m);

        var service = ServiceAt(2026, 11, 17);
        var inLedger = await service.GetAnnualYearOverYearDataAsync(_db.User.Id, 2026, ledger.Id);
        var all = await service.GetAnnualYearOverYearDataAsync(_db.User.Id, 2026);

        Assert.Equal((100m, 50m), (inLedger.CurrentExpense, inLedger.PreviousYearExpense));
        Assert.Equal((107m, 53m), (all.CurrentExpense, all.PreviousYearExpense));
    }

    [Fact]
    public async Task 默认构造的服务使用系统时钟_不抛异常()
    {
        using var _ = UseUtcPlus8();

        // 年份只取一次：分两次读系统时钟，恰好跨年那一刻会让断言随机失败
        var year = DateTime.UtcNow.Year;

        var data = await new StatisticsService(_db.Context)
            .GetAnnualYearOverYearDataAsync(_db.User.Id, year);

        Assert.Equal(year, data.CurrentYear);
        Assert.Null(data.ExpenseChangeRate);
    }
}
