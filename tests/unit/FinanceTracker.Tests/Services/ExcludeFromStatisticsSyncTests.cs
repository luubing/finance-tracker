using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「不计入统计」开关随分类走同步：两台设备 + 一个云端账号（云端是真实 SyncController，设备是真实 SyncService + 各自的 SQLite）。
/// 开关是统计口径的一部分——它没同步过去，同一个账号在两台设备上的"本月支出"就对不上。
/// </summary>
public sealed class ExcludeFromStatisticsSyncTests : IAsyncLifetime
{
    private FakeCloud _cloud = null!;
    private SyncDevice _a = null!;
    private SyncDevice _b = null!;

    public async Task InitializeAsync()
    {
        _cloud = await FakeCloud.CreateAsync();
        _a = await _cloud.AddDeviceAsync();
        _b = await _cloud.AddDeviceAsync();
    }

    public async Task DisposeAsync()
    {
        await _b.DisposeAsync();
        await _a.DisposeAsync();
        await _cloud.DisposeAsync();
    }

    private static Task<Category> AddSavingsAsync(SyncDevice device, bool exclude) =>
        device.Categories.CreateCategoryAsync(new Category
        {
            UserId = device.UserId,
            Name = "存款",
            Icon = "mdi-piggy-bank",
            Type = BillType.Expense,
            ExcludeFromStatistics = exclude
        });

    private static Task SetExcludedAsync(SyncDevice device, Category category, bool exclude) =>
        device.Categories.UpdateCategoryAsync(new Category
        {
            Id = category.Id,
            UserId = device.UserId,
            Name = category.Name,
            Icon = category.Icon,
            Type = category.Type,
            SortOrder = category.SortOrder,
            ExcludeFromStatistics = exclude
        });

    private async Task<bool> CloudFlagAsync(Guid categoryId)
    {
        await using var cloudContext = _cloud.CreateContext();
        return (await cloudContext.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == categoryId))
            .ExcludeFromStatistics;
    }

    private static Task<Category> LocalAsync(SyncDevice device, Guid categoryId) =>
        device.Db.Context.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == categoryId);

    [Fact]
    public async Task 开关随分类推到云端_另一台设备拉取后也带着开关()
    {
        var savings = await AddSavingsAsync(_a, exclude: true);

        await _a.Sync.SyncCategoriesAsync(_a.UserId);
        Assert.True(await CloudFlagAsync(savings.Id));

        await _b.Sync.SyncCategoriesAsync(_b.UserId);

        var onB = await LocalAsync(_b, savings.Id);
        Assert.True(onB.ExcludeFromStatistics);
        Assert.Equal(_b.UserId, onB.UserId);
    }

    [Fact]
    public async Task 在一台设备上改开关_后写入优先_云端与另一台设备跟着变()
    {
        var savings = await AddSavingsAsync(_a, exclude: true);
        await _a.Sync.SyncCategoriesAsync(_a.UserId);
        await _b.Sync.SyncCategoriesAsync(_b.UserId);
        Assert.True((await LocalAsync(_b, savings.Id)).ExcludeFromStatistics);

        await SetExcludedAsync(_a, savings, false);
        await _a.Sync.SyncCategoriesAsync(_a.UserId);
        await _b.Sync.SyncCategoriesAsync(_b.UserId);

        Assert.False(await CloudFlagAsync(savings.Id));
        Assert.False((await LocalAsync(_b, savings.Id)).ExcludeFromStatistics);
    }

    [Fact]
    public async Task 开关同步后两台设备的统计口径一致()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var savings = await AddSavingsAsync(_a, exclude: true);
        await _a.Sync.SyncCategoriesAsync(_a.UserId);
        await _b.Sync.SyncCategoriesAsync(_b.UserId);

        // 设备 A 记两笔：一笔普通、一笔存款；账单同步到两台设备
        await _a.AddBillAsync(100m);
        var ledgerA = await _a.Db.GetDefaultLedgerAsync();
        await _a.Bills.CreateBillAsync(new Bill
        {
            UserId = _a.UserId,
            Amount = 1000m,
            Type = BillType.Expense,
            CategoryId = savings.Id,
            PaymentChannelId = _a.Db.Channel.Id,
            LedgerId = ledgerA.Id,
            TransactionTime = new DateTime(2026, 3, 1, 12, 0, 0),
            Source = BillSource.Manual
        });
        Assert.True((await _a.Sync.SyncBillsAsync(_a.UserId)).Success);
        Assert.True((await _b.Sync.SyncBillsAsync(_b.UserId)).Success);

        var onA = await new StatisticsService(_a.Db.Context).GetMonthlyStatisticsAsync(_a.UserId, 2026, 3);
        var onB = await new StatisticsService(_b.Db.Context).GetMonthlyStatisticsAsync(_b.UserId, 2026, 3);

        Assert.Equal(100m, onA.TotalExpense);
        Assert.Equal(onA.TotalExpense, onB.TotalExpense);
        Assert.Equal(onA.BillCount, onB.BillCount);
    }
}
