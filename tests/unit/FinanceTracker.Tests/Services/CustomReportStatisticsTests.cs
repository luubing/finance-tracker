using System.Globalization;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 自定义报表用到的统计口径（21 号）：分类环比里本期归零的分类不能叫「未知分类」；
/// 页面写在环比列表下面的「上一等长周期」说明，必须正好是统计服务真正对比的那一段。
/// 时区覆盖用 AsyncLocal 实现，必须在测试方法本身里 using。
/// </summary>
public sealed class CustomReportStatisticsTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _billService = null!;
    private StatisticsService _statistics = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _billService = new BillService(_db.Context, NullLogger<BillService>.Instance);
        _statistics = new StatisticsService(_db.Context);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /// <summary>以应用时区的墙上时间创建一笔支出（与表单一致：Kind=Unspecified）</summary>
    private async Task AddExpenseAsync(DateTime wallTime, decimal amount)
    {
        var ledger = await _db.GetDefaultLedgerAsync();

        await _billService.CreateBillAsync(new Bill
        {
            UserId = _db.User.Id,
            Amount = amount,
            Type = BillType.Expense,
            CategoryId = _db.ExpenseCategory.Id,
            PaymentChannelId = _db.Channel.Id,
            LedgerId = ledger.Id,
            TransactionTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });
    }

    [Fact]
    public async Task 分类对比_上期有而本期归零的分类沿用上期的名称与图标_不叫未知分类()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        await AddExpenseAsync(new DateTime(2026, 3, 3, 12, 0, 0), 50m);   // 本期 3/8 ~ 3/14 的上一期是 3/1 ~ 3/7

        var comparison = await _statistics.GetCategoryComparisonAsync(_db.User.Id, new DateTime(2026, 3, 8), new DateTime(2026, 3, 14));

        var only = Assert.Single(comparison);
        Assert.Equal(_db.ExpenseCategory.Name, only.CategoryName);
        Assert.Equal(_db.ExpenseCategory.Icon, only.CategoryIcon);
        Assert.NotEqual("未知分类", only.CategoryName);
        Assert.Equal(0m, only.CurrentAmount);
        Assert.Equal(50m, only.PreviousAmount);
        Assert.Equal(-100m, only.ChangeRate);
    }

    [Fact]
    public async Task 分类对比_本期有而上期没有的分类照常带名称_变化率算不出来()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        await AddExpenseAsync(new DateTime(2026, 3, 9, 12, 0, 0), 80m);

        var comparison = await _statistics.GetCategoryComparisonAsync(_db.User.Id, new DateTime(2026, 3, 8), new DateTime(2026, 3, 14));

        var only = Assert.Single(comparison);
        Assert.Equal(_db.ExpenseCategory.Name, only.CategoryName);
        Assert.Equal(80m, only.CurrentAmount);
        Assert.Equal(0m, only.PreviousAmount);
        Assert.Null(only.ChangeRate);
    }

    [Theory]
    [InlineData("2026-03-08", "2026-03-14")]   // 7 天
    [InlineData("2026-03-01", "2026-03-31")]   // 整个 3 月：上一周期是往前 31 天（1/29 ~ 2/28），不是「上个月」
    [InlineData("2026-03-10", "2026-03-10")]   // 单日
    public async Task 分类对比的上一周期正好是页面说明里写的那一段(string startText, string endText)
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var start = DateTime.Parse(startText, CultureInfo.InvariantCulture);
        var end = DateTime.Parse(endText, CultureInfo.InvariantCulture);
        var (previousStart, previousEnd) = CustomReportPresentation.PreviousPeriod(start, end);

        await AddExpenseAsync(previousStart.AddMinutes(30), 1m);              // 上期第一天凌晨：计入上期
        await AddExpenseAsync(previousEnd.AddHours(23).AddMinutes(30), 2m);   // 上期最后一天深夜：计入上期
        await AddExpenseAsync(previousStart.AddMinutes(-30), 100m);           // 上期之前一天深夜：都不计
        await AddExpenseAsync(start.AddMinutes(30), 4m);                      // 本期第一天凌晨：计入本期
        await AddExpenseAsync(end.AddHours(23).AddMinutes(30), 8m);           // 本期最后一天深夜：计入本期
        await AddExpenseAsync(end.AddDays(1).AddMinutes(30), 1000m);          // 本期之后：都不计

        var comparison = await _statistics.GetCategoryComparisonAsync(_db.User.Id, start, end);

        var only = Assert.Single(comparison);
        Assert.Equal(3m, only.PreviousAmount);
        Assert.Equal(12m, only.CurrentAmount);
    }
}
