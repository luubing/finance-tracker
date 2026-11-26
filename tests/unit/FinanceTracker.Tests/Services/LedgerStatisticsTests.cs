using System.Security.Claims;
using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账本统计：统计页选"全部账本"时，"支出分类统计"改为"支出账本统计"，数据来自 <c>GetLedgerStatisticsAsync</c>
/// （REST：<c>GET /api/statistics/ledger</c>）。
/// 口径与分类统计一致：只统计当前用户自己的、未删除的账单；自然月按应用时区、左闭右开。
/// </summary>
public sealed class LedgerStatisticsTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _bills = null!;
    private StatisticsService _statistics = null!;
    private Guid _incomeCategoryId;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _bills = new BillService(_db.Context, NullLogger<BillService>.Instance);
        _statistics = new StatisticsService(_db.Context);
        _incomeCategoryId = _db.Context.Categories
            .First(c => c.Type == BillType.Income && c.UserId == null).Id;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static IDisposable UseUtcPlus8() => AppTimeZone.UseZone(TestZones.UtcPlus8);

    /// <summary>以应用时区的墙上时间创建账单（和 AddBill 页面表单一致：Kind=Unspecified）</summary>
    private Task<Bill> AddBillAsync(
        DateTime wallTime,
        decimal amount,
        Guid ledgerId,
        BillType type = BillType.Expense,
        Guid? userId = null) =>
        _bills.CreateBillAsync(new Bill
        {
            UserId = userId ?? _db.User.Id,
            Amount = amount,
            Type = type,
            CategoryId = type == BillType.Expense ? _db.ExpenseCategory.Id : _incomeCategoryId,
            PaymentChannelId = _db.Channel.Id,
            LedgerId = ledgerId,
            TransactionTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });

    /// <summary>历史账单：账本必填之前创建、没有账本的账单（BillService 已不允许创建，只能直接入库来模拟）</summary>
    private async Task AddLegacyBillWithoutLedgerAsync(DateTime utcTime, decimal amount)
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
            TransactionTime = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc),
            Source = BillSource.Manual
        });
        await _db.Context.SaveChangesAsync();
    }

    private Task<List<LedgerStatistics>> ExpenseOfAsync(int year, int month) =>
        _statistics.GetLedgerStatisticsAsync(_db.User.Id, year, month, BillType.Expense);

    // ───────────────────────── 汇总口径 ─────────────────────────

    [Fact]
    public async Task 按账本汇总支出_金额_占比_笔数_按金额降序()
    {
        using var _ = UseUtcPlus8();
        var travel = await _db.CreateLedgerAsync(_db.User.Id, "旅行");
        var family = await _db.CreateLedgerAsync(_db.User.Id, "家庭");
        var daily = await _db.GetDefaultLedgerAsync();
        await AddBillAsync(new DateTime(2026, 3, 5, 12, 0, 0), 80m, travel.Id);
        await AddBillAsync(new DateTime(2026, 3, 9, 12, 0, 0), 40m, travel.Id);
        await AddBillAsync(new DateTime(2026, 3, 12, 12, 0, 0), 50m, daily.Id);
        await AddBillAsync(new DateTime(2026, 3, 20, 12, 0, 0), 30m, family.Id);

        var result = await ExpenseOfAsync(2026, 3);

        Assert.Equal(new[] { "旅行", "默认账本", "家庭" }, result.Select(r => r.LedgerName));
        Assert.Equal(new[] { 120m, 50m, 30m }, result.Select(r => r.Amount));
        Assert.Equal(new[] { 60m, 25m, 15m }, result.Select(r => r.Percentage));
        Assert.Equal(new[] { 2, 1, 1 }, result.Select(r => r.Count));
        Assert.Equal(new Guid?[] { travel.Id, daily.Id, family.Id }, result.Select(r => r.LedgerId));
        Assert.All(result, r => Assert.False(string.IsNullOrEmpty(r.LedgerIcon)));
    }

    [Fact]
    public async Task 只统计所选类型_收入不混进支出账本统计()
    {
        using var _ = UseUtcPlus8();
        var travel = await _db.CreateLedgerAsync(_db.User.Id, "旅行");
        await AddBillAsync(new DateTime(2026, 3, 5, 12, 0, 0), 80m, travel.Id);
        await AddBillAsync(new DateTime(2026, 3, 6, 12, 0, 0), 500m, travel.Id, BillType.Income);

        var expense = await ExpenseOfAsync(2026, 3);
        var income = await _statistics.GetLedgerStatisticsAsync(_db.User.Id, 2026, 3, BillType.Income);

        Assert.Equal(80m, Assert.Single(expense).Amount);
        var incomeItem = Assert.Single(income);
        Assert.Equal(500m, incomeItem.Amount);
        Assert.Equal(100m, incomeItem.Percentage);
    }

    [Fact]
    public async Task 自然月按应用时区左闭右开_月初零点算本月_下月零点不算()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.GetDefaultLedgerAsync();
        await AddBillAsync(new DateTime(2026, 2, 28, 23, 59, 0), 1m, ledger.Id);    // 2 月最后一刻
        await AddBillAsync(new DateTime(2026, 3, 1, 0, 0, 0), 2m, ledger.Id);       // 3 月第一刻（UTC 2/28 16:00，按 UTC 分月会误归 2 月）
        await AddBillAsync(new DateTime(2026, 3, 31, 23, 59, 0), 4m, ledger.Id);    // 3 月最后一刻
        await AddBillAsync(new DateTime(2026, 4, 1, 0, 0, 0), 8m, ledger.Id);       // 4 月第一刻（右端点，不属于 3 月）

        var march = Assert.Single(await ExpenseOfAsync(2026, 3));

        Assert.Equal(6m, march.Amount);
        Assert.Equal(2, march.Count);
    }

    [Fact]
    public async Task 当月没有账单_返回空列表()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.GetDefaultLedgerAsync();
        await AddBillAsync(new DateTime(2026, 2, 5, 12, 0, 0), 10m, ledger.Id);

        Assert.Empty(await ExpenseOfAsync(2026, 3));
    }

    [Fact]
    public async Task 已删除的账单不计入统计()
    {
        using var _ = UseUtcPlus8();
        var ledger = await _db.GetDefaultLedgerAsync();
        await AddBillAsync(new DateTime(2026, 3, 5, 12, 0, 0), 10m, ledger.Id);
        var removed = await AddBillAsync(new DateTime(2026, 3, 6, 12, 0, 0), 99m, ledger.Id);
        Assert.True(await _bills.DeleteBillAsync(removed.Id, _db.User.Id));

        var item = Assert.Single(await ExpenseOfAsync(2026, 3));

        Assert.Equal(10m, item.Amount);
        Assert.Equal(1, item.Count);
    }

    [Fact]
    public async Task 不统计其他用户的账单()
    {
        using var _ = UseUtcPlus8();
        var mine = await _db.GetDefaultLedgerAsync();
        var stranger = await _db.CreateUserAsync("13900000002");
        var theirs = await _db.CreateLedgerAsync(stranger.Id, "别人的账本");
        await AddBillAsync(new DateTime(2026, 3, 5, 12, 0, 0), 10m, mine.Id);
        await AddBillAsync(new DateTime(2026, 3, 5, 12, 0, 0), 999m, theirs.Id, userId: stranger.Id);

        var item = Assert.Single(await ExpenseOfAsync(2026, 3));

        Assert.Equal(mine.Id, item.LedgerId);
        Assert.Equal(10m, item.Amount);
    }

    // ───────────────────────── 历史数据 ─────────────────────────

    [Fact]
    public async Task 历史未归属账本的账单_汇总为未归属账本_合计不丢金额()
    {
        using var _ = UseUtcPlus8();
        var daily = await _db.GetDefaultLedgerAsync();
        await AddBillAsync(new DateTime(2026, 3, 5, 12, 0, 0), 75m, daily.Id);
        await AddLegacyBillWithoutLedgerAsync(new DateTime(2026, 3, 10, 4, 0, 0), 25m);   // 本地 3/10 12:00

        var result = await ExpenseOfAsync(2026, 3);

        var unassigned = Assert.Single(result, r => r.LedgerId == null);
        Assert.Equal(LedgerStatistics.UnassignedLedgerName, unassigned.LedgerName);
        Assert.Equal(25m, unassigned.Amount);
        Assert.Equal(25m, unassigned.Percentage);
        Assert.Equal(100m, result.Sum(r => r.Amount));
    }

    [Fact]
    public async Task 账单引用的账本已被软删除_仍计入统计且保留账本名称()
    {
        using var _ = UseUtcPlus8();
        var travel = await _db.CreateLedgerAsync(_db.User.Id, "旅行");
        await AddBillAsync(new DateTime(2026, 3, 5, 12, 0, 0), 60m, travel.Id);

        // 模拟另一台设备删除了这个账本、同步到本机：账本行被软删除，但账单仍引用它。
        // 统计不能因此把这些账单悄悄丢掉（否则账本合计与月度总额对不上）
        var row = await _db.Context.Ledgers.IgnoreQueryFilters().FirstAsync(l => l.Id == travel.Id);
        row.IsDeleted = true;
        await _db.Context.SaveChangesAsync();

        var item = Assert.Single(await ExpenseOfAsync(2026, 3));

        Assert.Equal(travel.Id, item.LedgerId);
        Assert.Equal("旅行", item.LedgerName);
        Assert.Equal(60m, item.Amount);
    }

    // ───────────────────────── REST：GET /api/statistics/ledger ─────────────────────────

    private StatisticsController CreateController() =>
        new(_statistics)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, _db.User.Id.ToString()) },
                        "test"))
                }
            }
        };

    [Fact]
    public async Task 接口返回按账本汇总的数据_历史账单的ledgerId序列化为null()
    {
        using var _ = UseUtcPlus8();
        var daily = await _db.GetDefaultLedgerAsync();
        await AddBillAsync(new DateTime(2026, 3, 5, 12, 0, 0), 75m, daily.Id);
        await AddLegacyBillWithoutLedgerAsync(new DateTime(2026, 3, 10, 4, 0, 0), 25m);

        var result = await CreateController().GetLedgerStatistics(2026, 3, BillType.Expense);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = JsonSerializer.SerializeToElement(ok.Value).EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        Assert.Equal(daily.Id, items[0].GetProperty("ledgerId").GetGuid());
        Assert.Equal("默认账本", items[0].GetProperty("ledgerName").GetString());
        Assert.Equal(75m, items[0].GetProperty("amount").GetDecimal());
        Assert.Equal(75m, items[0].GetProperty("percentage").GetDecimal());
        Assert.Equal(1, items[0].GetProperty("count").GetInt32());

        // 历史上没有账本的账单：ledgerId 是 JSON null，客户端据此识别"未归属账本"
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("ledgerId").ValueKind);
        Assert.Equal(LedgerStatistics.UnassignedLedgerName, items[1].GetProperty("ledgerName").GetString());
    }

    [Theory]
    [InlineData(1999, 3)]
    [InlineData(2101, 3)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public async Task 接口年月超出范围_返回400(int year, int month)
    {
        using var _ = UseUtcPlus8();

        var result = await CreateController().GetLedgerStatistics(year, month, BillType.Expense);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
