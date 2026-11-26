using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「账本必填」：账单必须归属某个账本。
/// <list type="bullet">
/// <item>新建/编辑账单时账本为空（null / Guid.Empty）一律拒绝，提示"请选择账本"；账本不存在同样拒绝</item>
/// <item>历史上没有账本的账单依然合法存在（实体、数据库、同步仍可空，不迁移），但下次编辑必须补上账本</item>
/// <item>批量"归属账本"只能归到自己名下未删除的账本，不再支持"移出账本"（那会产生新的未归属账单）</item>
/// </list>
/// 校验统一放在 <see cref="BillService"/>：界面（AddBill/待确认/短信识别/导入）和 REST 接口共用同一个把关点。
/// </summary>
public sealed class BillLedgerRequiredTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _bills = null!;
    private Ledger _ledger = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _bills = new BillService(_db.Context, NullLogger<BillService>.Instance);
        _ledger = await _db.GetDefaultLedgerAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private Bill NewBill(Guid? ledgerId, Guid? userId = null, Guid? billId = null) => new()
    {
        Id = billId ?? Guid.Empty,
        UserId = userId ?? _db.User.Id,
        Amount = 12.34m,
        Type = BillType.Expense,
        CategoryId = _db.ExpenseCategory.Id,
        PaymentChannelId = _db.Channel.Id,
        LedgerId = ledgerId,
        TransactionTime = new DateTime(2026, 3, 1, 12, 0, 0),
        Source = BillSource.Manual
    };

    /// <summary>
    /// 造一笔历史账单：账本必填之前创建、没有账本的账单。
    /// BillService 已不允许再创建这样的账单，只能绕过它直接入库来模拟
    /// </summary>
    private async Task<Bill> SeedLegacyBillAsync(Guid? userId = null)
    {
        var bill = new Bill
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? _db.User.Id,
            Amount = 8m,
            Type = BillType.Expense,
            CategoryId = _db.ExpenseCategory.Id,
            PaymentChannelId = _db.Channel.Id,
            LedgerId = null,
            TransactionTime = new DateTime(2026, 3, 1, 4, 0, 0, DateTimeKind.Utc),
            Source = BillSource.Manual,
            SyncStatus = SyncStatus.Synced
        };

        _db.Context.Bills.Add(bill);
        await _db.Context.SaveChangesAsync();

        return bill;
    }

    private async Task<Bill> ReadBillAsync(Guid billId)
    {
        // 清空跟踪器，确保读到的是库里真正存下来的值
        _db.Context.ChangeTracker.Clear();
        return (await _bills.GetBillByIdAsync(billId))!;
    }

    private Task<int> CountBillsAsync() => _db.Context.Bills.IgnoreQueryFilters().CountAsync();

    // ───────────────────────── 新建 ─────────────────────────

    [Fact]
    public async Task 创建账单_没有账本_提示请选择账本且不入库()
    {
        foreach (var missing in new Guid?[] { null, Guid.Empty })
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _bills.CreateBillAsync(NewBill(missing)));

            Assert.Equal("请选择账本", ex.Message);
        }

        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 创建账单_账本不存在_提示账本不存在或尚未同步()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _bills.CreateBillAsync(NewBill(Guid.NewGuid())));

        Assert.Contains("账本不存在", ex.Message);
        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 创建账单_校验失败之后_合法的保存不受污染()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _bills.CreateBillAsync(NewBill(null)));

        // 校验失败不能把脏实体留在共享 DbContext 的跟踪器里，否则之后每次保存都会连带失败
        var saved = await _bills.CreateBillAsync(NewBill(_ledger.Id));

        Assert.Equal(_ledger.Id, saved.LedgerId);
        Assert.Equal(1, await CountBillsAsync());
    }

    [Fact]
    public async Task 创建账单_有账本_保存成功并带回账本信息()
    {
        var saved = await _bills.CreateBillAsync(NewBill(_ledger.Id));

        Assert.Equal(_ledger.Id, saved.LedgerId);
        Assert.Equal("默认账本", saved.Ledger?.Name);
    }

    // ───────────────────────── 编辑 ─────────────────────────

    [Fact]
    public async Task 编辑账单_清空账本_校验失败且账单保持原账本()
    {
        var created = await _bills.CreateBillAsync(NewBill(_ledger.Id));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _bills.UpdateBillAsync(NewBill(null, billId: created.Id)));

        Assert.Equal("请选择账本", ex.Message);
        Assert.Equal(_ledger.Id, (await ReadBillAsync(created.Id)).LedgerId);
    }

    [Fact]
    public async Task 编辑账单_改到另一个账本_保存成功()
    {
        var created = await _bills.CreateBillAsync(NewBill(_ledger.Id));
        var travel = await _db.CreateLedgerAsync(_db.User.Id, "旅行");

        await _bills.UpdateBillAsync(NewBill(travel.Id, billId: created.Id));

        Assert.Equal(travel.Id, (await ReadBillAsync(created.Id)).LedgerId);
    }

    [Fact]
    public async Task 历史未归属账本的账单_仍然可以正常列出()
    {
        var legacy = await SeedLegacyBillAsync();

        var bills = await _bills.GetBillsAsync(_db.User.Id);

        var listed = Assert.Single(bills);
        Assert.Equal(legacy.Id, listed.Id);
        Assert.Null(listed.LedgerId);
    }

    [Fact]
    public async Task 历史未归属账本的账单_编辑时不补账本被拒绝_补上账本后保存成功()
    {
        var legacy = await SeedLegacyBillAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _bills.UpdateBillAsync(NewBill(null, billId: legacy.Id)));
        Assert.Equal("请选择账本", ex.Message);
        Assert.Null((await ReadBillAsync(legacy.Id)).LedgerId);

        var saved = await _bills.UpdateBillAsync(NewBill(_ledger.Id, billId: legacy.Id));

        Assert.Equal(_ledger.Id, saved.LedgerId);
        Assert.Equal(_ledger.Id, (await ReadBillAsync(legacy.Id)).LedgerId);
    }

    // ───────────────────────── 批量归属账本 ─────────────────────────

    [Fact]
    public async Task 批量归属账本_历史账单和其他账本的账单一并归到目标账本_标记待同步()
    {
        var travel = await _db.CreateLedgerAsync(_db.User.Id, "旅行");
        var legacy = await SeedLegacyBillAsync();
        var inDefault = await _bills.CreateBillAsync(NewBill(_ledger.Id));

        var count = await _bills.AssignBillsToLedgerAsync(
            new List<Guid> { legacy.Id, inDefault.Id }, _db.User.Id, travel.Id);

        Assert.Equal(2, count);
        foreach (var id in new[] { legacy.Id, inDefault.Id })
        {
            var bill = await ReadBillAsync(id);
            Assert.Equal(travel.Id, bill.LedgerId);
            Assert.Equal(SyncStatus.Pending, bill.SyncStatus);
        }
    }

    [Fact]
    public async Task 批量归属账本_没选账本_提示请选择账本且账单不变()
    {
        var legacy = await SeedLegacyBillAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _bills.AssignBillsToLedgerAsync(new List<Guid> { legacy.Id }, _db.User.Id, Guid.Empty));

        Assert.Equal("请选择账本", ex.Message);
        Assert.Null((await ReadBillAsync(legacy.Id)).LedgerId);
    }

    [Fact]
    public async Task 批量归属账本_目标是别人的账本_提示账本不存在且账单不变()
    {
        var legacy = await SeedLegacyBillAsync();
        var stranger = await _db.CreateUserAsync("13900000002");
        var strangerLedger = await _db.CreateLedgerAsync(stranger.Id, "别人的账本");

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _bills.AssignBillsToLedgerAsync(new List<Guid> { legacy.Id }, _db.User.Id, strangerLedger.Id));

        Assert.Equal("账本不存在", ex.Message);
        Assert.Null((await ReadBillAsync(legacy.Id)).LedgerId);
    }

    [Fact]
    public async Task 批量归属账本_目标账本已删除_提示账本不存在()
    {
        var legacy = await SeedLegacyBillAsync();
        var temporary = await _db.CreateLedgerAsync(_db.User.Id, "临时账本");
        Assert.True(await new LedgerService(_db.Context, NullLogger<LedgerService>.Instance)
            .DeleteLedgerAsync(temporary.Id, _db.User.Id));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _bills.AssignBillsToLedgerAsync(new List<Guid> { legacy.Id }, _db.User.Id, temporary.Id));

        Assert.Equal("账本不存在", ex.Message);
        Assert.Null((await ReadBillAsync(legacy.Id)).LedgerId);
    }

    [Fact]
    public async Task 批量归属账本_不会改动别人的账单()
    {
        var stranger = await _db.CreateUserAsync("13900000002");
        var theirs = await SeedLegacyBillAsync(stranger.Id);
        var mine = await SeedLegacyBillAsync();

        var count = await _bills.AssignBillsToLedgerAsync(
            new List<Guid> { theirs.Id, mine.Id }, _db.User.Id, _ledger.Id);

        Assert.Equal(1, count);
        Assert.Equal(_ledger.Id, (await ReadBillAsync(mine.Id)).LedgerId);
        Assert.Null((await ReadBillAsync(theirs.Id)).LedgerId);
    }

    [Fact]
    public async Task 批量归属账本_没有选中账单_返回0()
    {
        var count = await _bills.AssignBillsToLedgerAsync(new List<Guid>(), _db.User.Id, _ledger.Id);

        Assert.Equal(0, count);
    }
}
