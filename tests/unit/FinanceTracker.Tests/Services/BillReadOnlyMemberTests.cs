using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 只读成员（Viewer）不能修改共享账本里的账单（ADR 0004「不能修改任何账单」）——删除 / 批量归属账本 / 编辑，
/// 统一由 <see cref="BillService"/> 在服务层把关，界面隐藏入口只是体验，REST 接口也走同一个把关点。
/// <list type="bullet">
/// <item>被拒绝抛 <see cref="ForbiddenAccessException"/>（已认证但无权操作，403 语义），绝不能是 <see cref="UnauthorizedAccessException"/>（客户端会误判为登录过期而登出）</item>
/// <item>只拦「现役只读成员」：所有者 / 可编辑成员、被移除的成员、账本已删除、历史无账本账单都照常放行</item>
/// <item>被拒绝时账单一个字节都不改（整批归属是全有或全无）</item>
/// </list>
/// </summary>
public sealed class BillReadOnlyMemberTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _bills = null!;

    /// <summary>当前登录用户（也就是那位成员）</summary>
    private User _me = null!;

    /// <summary>共享账本的所有者</summary>
    private User _owner = null!;

    private Ledger _shared = null!;
    private Ledger _mine = null!;
    private LedgerMember _myMembership = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _bills = new BillService(_db.Context, NullLogger<BillService>.Instance);
        _me = _db.User;
        _owner = await _db.CreateUserAsync("13900000042");
        _shared = await _db.CreateLedgerAsync(_owner.Id, "共享账本");
        _mine = await _db.CreateLedgerAsync(_me.Id, "我的账本");

        // 我先是可编辑成员（记过账），之后被改成只读：账单留在共享账本里，处置权由现在的角色决定
        _myMembership = new LedgerMember
        {
            LedgerId = _shared.Id,
            UserId = _me.Id,
            Role = LedgerMemberRole.Editor,
            Status = LedgerMemberStatus.Active,
        };
        _db.Context.LedgerMembers.Add(_myMembership);
        await _db.Context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private Bill NewBill(Guid? ledgerId, decimal amount = 12.34m, Guid? userId = null, Guid? billId = null) => new()
    {
        Id = billId ?? Guid.Empty,
        UserId = userId ?? _me.Id,
        Amount = amount,
        Type = BillType.Expense,
        CategoryId = _db.ExpenseCategory.Id,
        PaymentChannelId = _db.Channel.Id,
        LedgerId = ledgerId,
        TransactionTime = new DateTime(2026, 3, 1, 12, 0, 0),
        Source = BillSource.Manual,
    };

    /// <summary>用服务层建一笔账单（账单是我记的），再把状态标成已同步，方便断言「没被动过」</summary>
    private async Task<Bill> SeedBillAsync(Guid? ledgerId, decimal amount = 12.34m)
    {
        var saved = await _bills.CreateBillAsync(NewBill(ledgerId, amount));
        saved.SyncStatus = SyncStatus.Synced;
        await _db.Context.SaveChangesAsync();
        return saved;
    }

    /// <summary>造一笔历史账单：账本必填之前创建、没有账本的账单（服务层已不允许再创建，只能直接入库）</summary>
    private async Task<Bill> SeedLegacyBillAsync()
    {
        var bill = NewBill(null);
        bill.Id = Guid.NewGuid();
        bill.TransactionTime = new DateTime(2026, 3, 1, 4, 0, 0, DateTimeKind.Utc);
        bill.SyncStatus = SyncStatus.Synced;
        _db.Context.Bills.Add(bill);
        await _db.Context.SaveChangesAsync();
        return bill;
    }

    private async Task SetMyRoleAsync(LedgerMemberRole role)
    {
        _myMembership.Role = role;
        await _db.Context.SaveChangesAsync();
    }

    /// <summary>清空跟踪器后读库里账单的真实内容（含已软删除的）</summary>
    private async Task<Bill> ReadRawAsync(Guid billId)
    {
        _db.Context.ChangeTracker.Clear();
        return await _db.Context.Bills.IgnoreQueryFilters().AsNoTracking().FirstAsync(b => b.Id == billId);
    }

    // ───────────────────────── 删除 ─────────────────────────

    [Fact]
    public async Task 只读成员删除共享账本里自己的账单_被拒绝且账单原样不动()
    {
        var bill = await SeedBillAsync(_shared.Id);
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => _bills.DeleteBillAsync(bill.Id, _me.Id));

        Assert.Contains("只读成员", ex.Message);
        Assert.Contains("删除", ex.Message);

        var raw = await ReadRawAsync(bill.Id);
        Assert.False(raw.IsDeleted);
        Assert.Equal(SyncStatus.Synced, raw.SyncStatus);
    }

    [Fact]
    public async Task 只读成员被拒绝是权限错误_不是登录失效()
    {
        var bill = await SeedBillAsync(_shared.Id);
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        var ex = await Record.ExceptionAsync(() => _bills.DeleteBillAsync(bill.Id, _me.Id));

        // 401 与 403 严格区分：UnauthorizedAccessException 会让客户端登出，权限拒绝绝不能走它
        Assert.IsType<ForbiddenAccessException>(ex);
        Assert.IsNotType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task 可编辑成员删除共享账本里自己的账单_成功()
    {
        var bill = await SeedBillAsync(_shared.Id);

        Assert.True(await _bills.DeleteBillAsync(bill.Id, _me.Id));

        var raw = await ReadRawAsync(bill.Id);
        Assert.True(raw.IsDeleted);
        Assert.Equal(SyncStatus.Pending, raw.SyncStatus);
    }

    [Fact]
    public async Task 删除自己账本里的账单_成功()
    {
        var bill = await SeedBillAsync(_mine.Id);

        Assert.True(await _bills.DeleteBillAsync(bill.Id, _me.Id));
        Assert.True((await ReadRawAsync(bill.Id)).IsDeleted);
    }

    [Fact]
    public async Task 历史无账本账单_删除成功()
    {
        var legacy = await SeedLegacyBillAsync();

        Assert.True(await _bills.DeleteBillAsync(legacy.Id, _me.Id));
        Assert.True((await ReadRawAsync(legacy.Id)).IsDeleted);
    }

    [Fact]
    public async Task 被移除的成员_仍可删除自己留在原共享账本里的账单()
    {
        // ADR 0004：成员被移除或退出后，其本地账单副本保留——处置权在自己手里，不能因为「不是成员了」就成了谁也动不了的死数据
        var bill = await SeedBillAsync(_shared.Id);
        _myMembership.Role = LedgerMemberRole.Viewer;
        _myMembership.IsDeleted = true;
        await _db.Context.SaveChangesAsync();

        Assert.True(await _bills.DeleteBillAsync(bill.Id, _me.Id));
        Assert.True((await ReadRawAsync(bill.Id)).IsDeleted);
    }

    [Fact]
    public async Task 还没接受邀请的成员_不算只读成员()
    {
        var bill = await SeedBillAsync(_shared.Id);
        _myMembership.Role = LedgerMemberRole.Viewer;
        _myMembership.Status = LedgerMemberStatus.Pending;
        await _db.Context.SaveChangesAsync();

        Assert.True(await _bills.DeleteBillAsync(bill.Id, _me.Id));
    }

    [Fact]
    public async Task 共享账本已删除_原只读成员仍可删除自己的账单()
    {
        var bill = await SeedBillAsync(_shared.Id);
        await SetMyRoleAsync(LedgerMemberRole.Viewer);
        _shared.IsDeleted = true;
        await _db.Context.SaveChangesAsync();

        Assert.True(await _bills.DeleteBillAsync(bill.Id, _me.Id));
    }

    [Fact]
    public async Task 别人的账单_仍是读不到而不是无权限()
    {
        // 既有语义不变：只能处置自己的账单；别人记的账单对我「不存在」（返回 false），不会泄露它存在
        var theirs = await _bills.CreateBillAsync(NewBill(_shared.Id, userId: _owner.Id));
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        Assert.False(await _bills.DeleteBillAsync(theirs.Id, _me.Id));
        Assert.False((await ReadRawAsync(theirs.Id)).IsDeleted);
    }

    // ───────────────────────── 批量归属账本 ─────────────────────────

    [Fact]
    public async Task 只读成员把共享账本里的账单改归属到自己的账本_被拒绝()
    {
        var bill = await SeedBillAsync(_shared.Id);
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => _bills.AssignBillsToLedgerAsync([bill.Id], _me.Id, _mine.Id));

        Assert.Contains("只读成员", ex.Message);

        var raw = await ReadRawAsync(bill.Id);
        Assert.Equal(_shared.Id, raw.LedgerId);
        Assert.Equal(SyncStatus.Synced, raw.SyncStatus);
    }

    [Fact]
    public async Task 批量归属里混着只读账本的账单_整批拒绝一笔都不改()
    {
        var inShared = await SeedBillAsync(_shared.Id, 1m);
        var legacy = await SeedLegacyBillAsync();
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => _bills.AssignBillsToLedgerAsync([legacy.Id, inShared.Id], _me.Id, _mine.Id));

        // 全有或全无：那笔本来可以归属的历史账单也没有被改
        Assert.Null((await ReadRawAsync(legacy.Id)).LedgerId);
        Assert.Equal(_shared.Id, (await ReadRawAsync(inShared.Id)).LedgerId);
    }

    [Fact]
    public async Task 只读成员批量归属不在只读账本里的账单_成功()
    {
        var legacy = await SeedLegacyBillAsync();
        var other = await SeedBillAsync(_mine.Id);
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        var count = await _bills.AssignBillsToLedgerAsync([legacy.Id, other.Id], _me.Id, _mine.Id);

        Assert.Equal(2, count);
        Assert.Equal(_mine.Id, (await ReadRawAsync(legacy.Id)).LedgerId);
    }

    [Fact]
    public async Task 可编辑成员把共享账本里的账单改归属到自己的账本_成功()
    {
        var bill = await SeedBillAsync(_shared.Id);

        var count = await _bills.AssignBillsToLedgerAsync([bill.Id], _me.Id, _mine.Id);

        Assert.Equal(1, count);
        var raw = await ReadRawAsync(bill.Id);
        Assert.Equal(_mine.Id, raw.LedgerId);
        Assert.Equal(SyncStatus.Pending, raw.SyncStatus);
    }

    // ───────────────────────── 编辑 ─────────────────────────

    [Fact]
    public async Task 只读成员编辑共享账本里自己的账单_被拒绝_把它挪去自己的账本也不行()
    {
        var bill = await SeedBillAsync(_shared.Id, 10m);
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        // 目标账本是自己的（界面 / 控制器校验目标账本写权限会放行），但账单现在还在只读账本里：挪走也是在修改它
        var edited = NewBill(_mine.Id, 99m, billId: bill.Id);
        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => _bills.UpdateBillAsync(edited));

        Assert.Contains("只读成员", ex.Message);

        var raw = await ReadRawAsync(bill.Id);
        Assert.Equal(_shared.Id, raw.LedgerId);
        Assert.Equal(10m, raw.Amount);
        Assert.Equal(SyncStatus.Synced, raw.SyncStatus);
    }

    [Fact]
    public async Task 只读成员保存失败之后_合法的保存不受污染()
    {
        var blocked = await SeedBillAsync(_shared.Id, 10m);
        var fine = await SeedBillAsync(_mine.Id, 20m);
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => _bills.UpdateBillAsync(NewBill(_shared.Id, 99m, billId: blocked.Id)));

        // 校验失败时不能把改了一半的实体留在共享 DbContext 的跟踪器里，否则之后每次保存都会连带失败
        var saved = await _bills.UpdateBillAsync(NewBill(_mine.Id, 21m, billId: fine.Id));

        Assert.Equal(21m, saved.Amount);
        Assert.Equal(10m, (await ReadRawAsync(blocked.Id)).Amount);
    }

    [Fact]
    public async Task 可编辑成员编辑共享账本里自己的账单_成功()
    {
        var bill = await SeedBillAsync(_shared.Id, 10m);

        var saved = await _bills.UpdateBillAsync(NewBill(_shared.Id, 55m, billId: bill.Id));

        Assert.Equal(55m, saved.Amount);
        Assert.Equal(_shared.Id, saved.LedgerId);
    }

    [Fact]
    public async Task 只读成员编辑自己账本里的账单_成功()
    {
        var bill = await SeedBillAsync(_mine.Id, 10m);
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        var saved = await _bills.UpdateBillAsync(NewBill(_mine.Id, 77m, billId: bill.Id));

        Assert.Equal(77m, saved.Amount);
    }

    [Fact]
    public async Task 历史无账本账单_补上账本保存_成功()
    {
        var legacy = await SeedLegacyBillAsync();
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        var saved = await _bills.UpdateBillAsync(NewBill(_mine.Id, 8m, billId: legacy.Id));

        Assert.Equal(_mine.Id, saved.LedgerId);
    }

    // ───────────────────────── 成员关系查询 ─────────────────────────

    [Fact]
    public async Task 只读账本查询_只认现役的只读成员()
    {
        var other = await _db.CreateLedgerAsync(_owner.Id, "另一个共享账本");
        var deletedLedger = await _db.CreateLedgerAsync(_owner.Id, "已删除的共享账本");
        var pending = await _db.CreateLedgerAsync(_owner.Id, "待接受的共享账本");
        var removed = await _db.CreateLedgerAsync(_owner.Id, "被移除的共享账本");

        _db.Context.LedgerMembers.AddRange(
            new LedgerMember { LedgerId = other.Id, UserId = _me.Id, Role = LedgerMemberRole.Editor, Status = LedgerMemberStatus.Active },
            new LedgerMember { LedgerId = deletedLedger.Id, UserId = _me.Id, Role = LedgerMemberRole.Viewer, Status = LedgerMemberStatus.Active },
            new LedgerMember { LedgerId = pending.Id, UserId = _me.Id, Role = LedgerMemberRole.Viewer, Status = LedgerMemberStatus.Pending },
            new LedgerMember { LedgerId = removed.Id, UserId = _me.Id, Role = LedgerMemberRole.Viewer, Status = LedgerMemberStatus.Active, IsDeleted = true });
        deletedLedger.IsDeleted = true;
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        var all = new List<Guid> { _shared.Id, _mine.Id, other.Id, deletedLedger.Id, pending.Id, removed.Id, Guid.NewGuid() };
        var readOnly = await LedgerMemberQueries.GetReadOnlyLedgerIdsAsync(_db.Context, _me.Id, all);

        // 只有「我是现役只读成员、账本还在」的那一个
        Assert.Equal(_shared.Id, Assert.Single(readOnly));
    }

    [Fact]
    public async Task 只读账本查询_别人的只读不算我的_空输入返回空()
    {
        await SetMyRoleAsync(LedgerMemberRole.Viewer);

        // 同一个账本，对所有者本人 / 对另一个用户都不是「只读」
        Assert.Empty(await LedgerMemberQueries.GetReadOnlyLedgerIdsAsync(_db.Context, _owner.Id, [_shared.Id]));
        Assert.Empty(await LedgerMemberQueries.GetReadOnlyLedgerIdsAsync(_db.Context, Guid.NewGuid(), [_shared.Id]));
        Assert.Empty(await LedgerMemberQueries.GetReadOnlyLedgerIdsAsync(_db.Context, _me.Id, []));
    }
}
