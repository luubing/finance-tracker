using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 同步的信任边界：服务端不能相信客户端推送里的"我是预设"标记；客户端对云端下发的数据则可以自愈。
///
/// 预设分类/支付渠道（UserId 为空）对云端所有用户可见。账单推送时服务端会为缺失的分类/渠道"补建"以满足外键，
/// 如果直接按 DTO 里的 CategoryIsPreset 建行，任何登录用户都能在云端造出对所有人可见的"预设"。
/// </summary>
public sealed class SyncTrustBoundaryTests
{
    // ───────────── 服务端：推送 ─────────────

    [Fact]
    public async Task 推送账单引用云端没有的预设分类_补建为推送者的私有分类而不是全局预设()
    {
        await using var cloud = await FakeCloud.CreateAsync();
        var forgedCategoryId = Guid.NewGuid();
        var dto = await BuildBillAsync(cloud, d =>
        {
            d.CategoryId = forgedCategoryId;
            d.CategoryName = "伪造预设";
            d.CategoryIsPreset = true;
        });

        var response = await PushAsync(cloud, dto);

        Assert.Equal(0, response.FailedCount);
        await using var context = cloud.CreateContext();
        var category = await context.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == forgedCategoryId);
        Assert.False(category.IsPreset);
        Assert.Equal(cloud.AccountId, category.UserId);
        Assert.Equal("伪造预设", category.Name);
    }

    [Fact]
    public async Task 推送账单引用云端没有的预设支付渠道_补建为推送者的私有渠道而不是全局预设()
    {
        await using var cloud = await FakeCloud.CreateAsync();
        var forgedChannelId = Guid.NewGuid();
        var dto = await BuildBillAsync(cloud, d =>
        {
            d.PaymentChannelId = forgedChannelId;
            d.PaymentChannelName = "伪造渠道";
            d.PaymentChannelIsPreset = true;
        });

        var response = await PushAsync(cloud, dto);

        Assert.Equal(0, response.FailedCount);
        await using var context = cloud.CreateContext();
        var channel = await context.PaymentChannels.IgnoreQueryFilters().SingleAsync(c => c.Id == forgedChannelId);
        Assert.False(channel.IsPreset);
        Assert.Equal(cloud.AccountId, channel.UserId);
        Assert.Equal("伪造渠道", channel.Name);
    }

    [Fact]
    public async Task 推送账单引用云端已有的预设_不新增任何分类或渠道()
    {
        await using var cloud = await FakeCloud.CreateAsync();
        var dto = await BuildBillAsync(cloud, _ => { });
        int categoriesBefore, channelsBefore;
        await using (var before = cloud.CreateContext())
        {
            categoriesBefore = await before.Categories.IgnoreQueryFilters().CountAsync();
            channelsBefore = await before.PaymentChannels.IgnoreQueryFilters().CountAsync();
        }

        var response = await PushAsync(cloud, dto);

        Assert.Equal(1, response.SyncedCount);
        await using var after = cloud.CreateContext();
        Assert.Equal(categoriesBefore, await after.Categories.IgnoreQueryFilters().CountAsync());
        Assert.Equal(channelsBefore, await after.PaymentChannels.IgnoreQueryFilters().CountAsync());
    }

    // ───────────── 客户端：拉取云端下发的数据 ─────────────

    [Fact]
    public async Task 拉取到引用本地缺失预设分类的云端账单_本地按预设补建()
    {
        await using var cloud = await FakeCloud.CreateAsync();
        await using var a = await cloud.AddDeviceAsync();
        await using var b = await cloud.AddDeviceAsync();
        var bill = await a.AddBillAsync(8m);
        await SyncOkAsync(a);

        // B 的本地库缺这个预设分类（例如历史上以随机 GUID 建过预设）；云端是可信来源，应按预设补回
        await b.Db.Context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Categories WHERE Id = {bill.CategoryId}");
        b.Db.Context.ChangeTracker.Clear();

        await SyncOkAsync(b);

        var category = await b.Db.Context.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == bill.CategoryId);
        Assert.True(category.IsPreset);
        Assert.Null(category.UserId);
    }

    [Fact]
    public async Task 拉取到引用本地缺失预设支付渠道的云端账单_本地按预设补建()
    {
        await using var cloud = await FakeCloud.CreateAsync();
        await using var a = await cloud.AddDeviceAsync();
        await using var b = await cloud.AddDeviceAsync();
        var bill = await a.AddBillAsync(8m);
        await SyncOkAsync(a);

        await b.Db.Context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PaymentChannels WHERE Id = {bill.PaymentChannelId}");
        b.Db.Context.ChangeTracker.Clear();

        await SyncOkAsync(b);

        var channel = await b.Db.Context.PaymentChannels.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == bill.PaymentChannelId);
        Assert.True(channel.IsPreset);
        Assert.Null(channel.UserId);
    }

    // ───────────── 辅助 ─────────────

    private static async Task SyncOkAsync(SyncDevice device)
    {
        var result = await device.Sync.SyncBillsAsync(device.UserId);
        Assert.True(result.Success, result.ErrorMessage);
    }

    private static Task<SyncPushResponse> PushAsync(FakeCloud cloud, BillSyncDto dto) =>
        new DirectCloudSyncClient(cloud, cloud.AccountId).PushBillsAsync(cloud.AccountId, new[] { dto });

    /// <summary>构造一条引用云端已有预设分类/渠道的合法账单推送，再由调用方改成"伪造"的样子</summary>
    private static async Task<BillSyncDto> BuildBillAsync(FakeCloud cloud, Action<BillSyncDto> customize)
    {
        await using var context = cloud.CreateContext();
        var category = await context.Categories.IgnoreQueryFilters()
            .FirstAsync(c => c.IsPreset && c.Type == BillType.Expense);
        var channel = await context.PaymentChannels.IgnoreQueryFilters().FirstAsync(c => c.IsPreset);

        var now = DateTime.UtcNow;
        var dto = new BillSyncDto
        {
            Id = Guid.NewGuid(),
            UserId = cloud.AccountId,
            Amount = 5m,
            Type = BillType.Expense,
            CategoryId = category.Id,
            CategoryName = category.Name,
            CategoryIsPreset = true,
            PaymentChannelId = channel.Id,
            PaymentChannelName = channel.Name,
            PaymentChannelIsPreset = true,
            TransactionTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            Source = BillSource.Manual,
            SyncStatus = SyncStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        customize(dto);
        return dto;
    }
}
