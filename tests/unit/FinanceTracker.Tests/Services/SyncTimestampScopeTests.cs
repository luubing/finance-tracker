using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 同步作用域：作用域外保存照常盖 UpdatedAt（用户的编辑必须有"编辑时间"），
/// 作用域内保存不盖（同步代码自己决定时间戳）；且只影响当前异步流程。
/// </summary>
public sealed class SyncTimestampScopeTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await SqliteTestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<(Guid BillId, DateTime OldTimestamp)> AddBillWithOldTimestampsAsync()
    {
        var oldTimestamp = DateTime.UtcNow.AddDays(-30);
        var bill = new Bill
        {
            UserId = _db.User.Id,
            Amount = 1m,
            Type = BillType.Expense,
            CategoryId = _db.ExpenseCategory.Id,
            PaymentChannelId = _db.Channel.Id,
            TransactionTime = new DateTime(2026, 3, 1, 12, 0, 0),
            Source = BillSource.Manual
        };
        _db.Context.Bills.Add(bill);
        await _db.Context.SaveChangesAsync();

        // 绕过自动盖章，把创建/更新时间都改成 30 天前
        await _db.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Bills SET CreatedAt = {oldTimestamp}, UpdatedAt = {oldTimestamp} WHERE Id = {bill.Id}");
        _db.Context.ChangeTracker.Clear();
        return (bill.Id, oldTimestamp);
    }

    /// <summary>改备注并保存，返回保存后库里的更新时间（备注必须与当前值不同，否则 EF 认为没有变化、不会保存也不会盖章）</summary>
    private async Task<DateTime> EditNoteAndReadUpdatedAtAsync(Guid billId, string note)
    {
        var tracked = await _db.Context.Bills.SingleAsync(b => b.Id == billId);
        tracked.Note = note;
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();
        return (await _db.Context.Bills.AsNoTracking().SingleAsync(b => b.Id == billId)).UpdatedAt;
    }

    [Fact]
    public async Task 作用域外保存_照常盖更新时间()
    {
        var (billId, oldTimestamp) = await AddBillWithOldTimestampsAsync();

        var updatedAt = await EditNoteAndReadUpdatedAtAsync(billId, "作用域外的编辑");

        Assert.True(updatedAt > oldTimestamp.AddDays(1), $"更新时间应被盖成当前时间，实际 {updatedAt:O}");
    }

    [Fact]
    public async Task 作用域内保存_不盖更新时间()
    {
        var (billId, oldTimestamp) = await AddBillWithOldTimestampsAsync();

        DateTime updatedAt;
        using (SyncTimestampScope.Begin())
        {
            updatedAt = await EditNoteAndReadUpdatedAtAsync(billId, "作用域内的同步写入");
        }

        Assert.Equal(oldTimestamp.Ticks, updatedAt.Ticks);
    }

    [Fact]
    public async Task 作用域结束后_保存恢复盖章()
    {
        var (billId, oldTimestamp) = await AddBillWithOldTimestampsAsync();
        using (SyncTimestampScope.Begin())
        {
            await EditNoteAndReadUpdatedAtAsync(billId, "作用域内的同步写入");
        }

        var updatedAt = await EditNoteAndReadUpdatedAtAsync(billId, "作用域结束后的编辑");

        Assert.True(updatedAt > oldTimestamp.AddDays(1));
    }

    [Fact]
    public async Task 新增实体在作用域内保留调用方给定的创建与更新时间()
    {
        var given = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var category = new Category
        {
            UserId = _db.User.Id,
            Name = "云端来的分类",
            Icon = "mdi-cloud",
            Type = BillType.Expense,
            CreatedAt = given,
            UpdatedAt = given
        };

        using (SyncTimestampScope.Begin())
        {
            _db.Context.Categories.Add(category);
            await _db.Context.SaveChangesAsync();
        }

        _db.Context.ChangeTracker.Clear();
        var saved = await _db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id);
        Assert.Equal(given.Ticks, saved.CreatedAt.Ticks);
        Assert.Equal(given.Ticks, saved.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 作用域可嵌套_内层结束后外层仍有效_全部结束后恢复()
    {
        Assert.False(SyncTimestampScope.IsActive);

        var outer = SyncTimestampScope.Begin();
        var inner = SyncTimestampScope.Begin();
        Assert.True(SyncTimestampScope.IsActive);

        inner.Dispose();
        Assert.True(SyncTimestampScope.IsActive); // 内层结束不影响外层

        outer.Dispose();
        Assert.False(SyncTimestampScope.IsActive);

        await Task.CompletedTask;
    }

    [Fact]
    public void 重复释放不会让计数变成负数()
    {
        var scope = SyncTimestampScope.Begin();
        scope.Dispose();
        scope.Dispose();

        using (SyncTimestampScope.Begin())
        {
            Assert.True(SyncTimestampScope.IsActive);
        }

        Assert.False(SyncTimestampScope.IsActive);
    }

    [Fact]
    public async Task 作用域跨await仍然有效()
    {
        using var scope = SyncTimestampScope.Begin();

        await Task.Delay(1);
        await Task.Yield();

        Assert.True(SyncTimestampScope.IsActive);
    }

    [Fact]
    public async Task 别的异步流程里开启的作用域_不会泄漏给当前流程()
    {
        // Blazor Server 里多个会话共用一个进程：一个会话在同步，另一个会话的保存必须照常盖章
        await Task.Run(() => SyncTimestampScope.Begin()); // 故意不释放

        Assert.False(SyncTimestampScope.IsActive);
    }
}
