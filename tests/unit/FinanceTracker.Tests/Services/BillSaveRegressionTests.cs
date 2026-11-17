using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「保存失败: An error occurred while saving the entity changes」回归测试。
///
/// 使用真实的 ApplicationDbContext + SQLite 迁移（与 MAUI / Web.Server 本地库同一套 schema，
/// 外键约束开启），而不是 InMemory Provider（InMemory 不校验外键，复现不出该问题）。
///
/// 根因：MAUI Blazor Hybrid / Blazor Server 下 DbContext 的生命周期等同于整个应用/整条电路，
/// 一次 SaveChangesAsync 失败后，失败的实体仍以 Added 状态留在 ChangeTracker 中，
/// 之后任何一次（哪怕完全合法的）保存都会连带重试这条脏数据而再次失败，
/// 表现为「保存失败」反复出现，直到重启应用。
/// </summary>
public sealed class BillSaveRegressionTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private SqliteTestDatabase _db = null!;
    private BillService _billService = null!;

    public BillSaveRegressionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private ApplicationDbContext _context => _db.Context;
    private User _user => _db.User;
    private Category _expenseCategory => _db.ExpenseCategory;
    private PaymentChannel _channel => _db.Channel;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _billService = new BillService(_context, NullLogger<BillService>.Instance);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private Bill NewBill(
        Guid? userId = null,
        Guid? categoryId = null,
        Guid? channelId = null,
        Guid? ledgerId = null) => new()
    {
        UserId = userId ?? _user.Id,
        Amount = 12.34m,
        Type = BillType.Expense,
        CategoryId = categoryId ?? _expenseCategory.Id,
        PaymentChannelId = channelId ?? _channel.Id,
        LedgerId = ledgerId,
        TransactionTime = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Local),
        Note = "回归测试",
        Source = BillSource.Manual
    };

    [Fact]
    public async Task 外键开启_测试环境与真实本地库一致()
    {
        var fk = await ScalarAsync("PRAGMA foreign_keys;");
        Assert.Equal(1L, fk);
    }

    [Fact]
    public async Task 合法账单_可以正常保存()
    {
        var saved = await _billService.CreateBillAsync(NewBill());

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Bills;"));
    }

    // ───────────────────────── 根因：失败实体残留 ChangeTracker ─────────────────────────

    [Fact]
    public async Task DbContext保存失败后_不应把失败实体残留在变更跟踪器里()
    {
        // 模拟「其它页面/服务的一次失败写入」（这里用无效用户外键代表）
        _context.Bills.Add(NewBill(userId: Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        _output.WriteLine($"外层: {ex.Message}");
        _output.WriteLine($"根因: {ex.GetBaseException().Message}");

        Assert.Contains("FOREIGN KEY", ex.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_context.ChangeTracker.Entries(), e => e.State == EntityState.Added);
    }

    [Fact]
    public async Task 一次失败写入之后_合法账单仍能保存_不被连带失败()
    {
        // 1) 另一处写入失败（外键无效），失败实体不得污染后续保存
        _context.Bills.Add(NewBill(userId: Guid.NewGuid()));
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        // 2) 用户随后保存一笔完全合法的账单，必须成功
        var saved = await _billService.CreateBillAsync(NewBill());

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Bills;"));
    }

    [Fact]
    public async Task 更新失败后_已修改的实体不再残留_账单保持原值()
    {
        var saved = await _billService.CreateBillAsync(NewBill());

        // 直接修改已跟踪实体并写入一个无效外键，模拟某处更新失败
        var tracked = await _context.Bills.FirstAsync(b => b.Id == saved.Id);
        tracked.CategoryId = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        Assert.DoesNotContain(_context.ChangeTracker.Entries(), e => e.State == EntityState.Modified);

        // 之后的合法保存不受影响
        await _billService.CreateBillAsync(NewBill());
        Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM Bills;"));
        Assert.Equal(
            _expenseCategory.Id.ToString().ToUpperInvariant(),
            Convert.ToString(await ScalarObjectAsync($"SELECT CategoryId FROM Bills WHERE Id = '{saved.Id}' COLLATE NOCASE;"))!.ToUpperInvariant());
    }

    // ───────────────────────── 保存前引用校验：给出明确提示而非 FK 错误 ─────────────────────────

    [Fact]
    public async Task 创建账单_用户不存在_提示重新登录且不污染上下文()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _billService.CreateBillAsync(NewBill(userId: Guid.NewGuid())));

        Assert.Contains("用户不存在", ex.Message);
        Assert.Empty(_context.ChangeTracker.Entries<Bill>());
    }

    [Fact]
    public async Task 创建账单_分类不存在_提示重新选择分类()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _billService.CreateBillAsync(NewBill(categoryId: Guid.NewGuid())));

        Assert.Contains("分类不存在", ex.Message);
    }

    [Fact]
    public async Task 创建账单_支付渠道不存在_提示重新选择支付渠道()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _billService.CreateBillAsync(NewBill(channelId: Guid.NewGuid())));

        Assert.Contains("支付渠道不存在", ex.Message);
    }

    [Fact]
    public async Task 创建账单_账本不存在_提示账本不存在或尚未同步()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _billService.CreateBillAsync(NewBill(ledgerId: Guid.NewGuid())));

        Assert.Contains("账本不存在", ex.Message);
    }

    [Fact]
    public async Task BillService保存失败后_合法账单仍能保存()
    {
        await Assert.ThrowsAnyAsync<Exception>(
            () => _billService.CreateBillAsync(NewBill(userId: Guid.NewGuid())));

        var saved = await _billService.CreateBillAsync(NewBill());

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM Bills;"));
    }

    [Fact]
    public async Task 编辑账单_账本不存在_校验失败且账单保持原值_不污染后续保存()
    {
        var saved = await _billService.CreateBillAsync(NewBill());

        var edited = NewBill(ledgerId: Guid.NewGuid());
        edited.Id = saved.Id;
        edited.Amount = 999m;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _billService.UpdateBillAsync(edited));
        Assert.Contains("账本不存在", ex.Message);

        // 校验发生在修改已跟踪实体之前：不得留下半改的 Modified 实体
        Assert.DoesNotContain(_context.ChangeTracker.Entries(), e => e.State == EntityState.Modified);

        // 之后的合法保存必须成功，且原账单金额未被改动
        await _billService.CreateBillAsync(NewBill());
        Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM Bills;"));
        Assert.Equal(1234L, await ScalarAsync($"SELECT CAST(ROUND(Amount * 100) AS INTEGER) FROM Bills WHERE Id = '{saved.Id}' COLLATE NOCASE;"));
    }

    [Fact]
    public async Task 编辑账单_合法账本_可以保存()
    {
        var ledger = new Ledger { UserId = _user.Id, Name = "测试账本", Icon = "mdi-book" };
        _context.Ledgers.Add(ledger);
        await _context.SaveChangesAsync();

        var saved = await _billService.CreateBillAsync(NewBill());

        var edited = NewBill(ledgerId: ledger.Id);
        edited.Id = saved.Id;
        var updated = await _billService.UpdateBillAsync(edited);

        Assert.Equal(ledger.Id, updated.LedgerId);
    }

    [Fact]
    public async Task 历史账单引用了已软删除的分类_仍可编辑备注_不被引用校验误拦截()
    {
        var saved = await _billService.CreateBillAsync(NewBill());

        // 软删除该账单使用的分类（预设分类不会被软删除，这里用自定义分类演示）
        var custom = new Category
        {
            UserId = _user.Id,
            Name = "临时分类",
            Icon = "mdi-tag",
            Type = BillType.Expense,
            IsPreset = false
        };
        _context.Categories.Add(custom);
        await _context.SaveChangesAsync();

        var billOnCustom = NewBill(categoryId: custom.Id);
        billOnCustom.Id = saved.Id;
        await _billService.UpdateBillAsync(billOnCustom);

        custom.IsDeleted = true;
        await _context.SaveChangesAsync();

        // 只改备注，其余沿用（包含已软删除的分类）：外键行仍存在，必须允许保存
        var edited = NewBill(categoryId: custom.Id);
        edited.Id = saved.Id;
        edited.Note = "只改备注";
        var updated = await _billService.UpdateBillAsync(edited);

        Assert.Equal("只改备注", updated.Note);
    }

    private async Task<long> ScalarAsync(string sql) => Convert.ToInt64(await ScalarObjectAsync(sql));

    private async Task<object?> ScalarObjectAsync(string sql)
    {
        await using var command = _db.Connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
