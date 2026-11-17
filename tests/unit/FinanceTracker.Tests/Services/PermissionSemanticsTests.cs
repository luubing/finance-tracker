using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 错误码语义测试（CLAUDE.md 关键架构决策 #7）：
/// 「已认证但无权操作」必须抛 <see cref="ForbiddenAccessException"/>（403），
/// 绝不能抛 <see cref="UnauthorizedAccessException"/>（401，客户端会误判登录过期而登出）
/// 或 <see cref="InvalidOperationException"/>（会被控制器映射成 400）。
/// </summary>
public sealed class PermissionSemanticsTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await SqliteTestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // ───────────────────────── S1：预设分类 / 支付渠道只读 ─────────────────────────

    [Fact]
    public async Task 修改预设分类_抛出ForbiddenAccessException()
    {
        var service = new CategoryService(_db.Context, NullLogger<CategoryService>.Instance);
        var preset = _db.ExpenseCategory;
        // 预设分类的 UserId 为 null，调用方按「分类自身的 UserId」定位
        var request = new Category { Id = preset.Id, UserId = preset.UserId, Name = "改名", Icon = preset.Icon, Type = preset.Type };

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.UpdateCategoryAsync(request));

        Assert.Contains("预设分类", ex.Message);
    }

    [Fact]
    public async Task 删除预设分类_抛出ForbiddenAccessException()
    {
        var service = new CategoryService(_db.Context, NullLogger<CategoryService>.Instance);

        // DeleteCategoryAsync 按 (id, userId) 定位，而真正的预设分类 UserId 为 null；
        // 这里构造一条归属当前用户且标记为预设的分类，验证服务层对 IsPreset 的拦截语义
        var presetOwnedByUser = new Category
        {
            UserId = _db.User.Id,
            Name = "伪预设",
            Icon = "mdi-tag",
            Type = BillType.Expense,
            IsPreset = true
        };
        _db.Context.Categories.Add(presetOwnedByUser);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => service.DeleteCategoryAsync(presetOwnedByUser.Id, _db.User.Id));
    }

    [Fact]
    public async Task 修改预设支付渠道_抛出ForbiddenAccessException()
    {
        var service = new PaymentChannelService(_db.Context, NullLogger<PaymentChannelService>.Instance);
        var preset = _db.Channel;
        var request = new PaymentChannel { Id = preset.Id, UserId = preset.UserId, Name = "改名", Icon = preset.Icon };

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.UpdatePaymentChannelAsync(request));

        Assert.Contains("预设支付渠道", ex.Message);
    }

    [Fact]
    public async Task 删除预设支付渠道_抛出ForbiddenAccessException()
    {
        var service = new PaymentChannelService(_db.Context, NullLogger<PaymentChannelService>.Instance);
        var presetOwnedByUser = new PaymentChannel
        {
            UserId = _db.User.Id,
            Name = "伪预设渠道",
            Icon = "mdi-credit-card",
            IsPreset = true
        };
        _db.Context.PaymentChannels.Add(presetOwnedByUser);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => service.DeletePaymentChannelAsync(presetOwnedByUser.Id, _db.User.Id));
    }

    [Fact]
    public async Task 权限类异常不会是UnauthorizedAccessException_避免客户端误登出()
    {
        var service = new CategoryService(_db.Context, NullLogger<CategoryService>.Instance);
        var presetOwnedByUser = new Category
        {
            UserId = _db.User.Id,
            Name = "伪预设",
            Icon = "mdi-tag",
            Type = BillType.Expense,
            IsPreset = true
        };
        _db.Context.Categories.Add(presetOwnedByUser);
        await _db.Context.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(
            () => service.DeleteCategoryAsync(presetOwnedByUser.Id, _db.User.Id));

        Assert.NotNull(ex);
        Assert.IsNotType<UnauthorizedAccessException>(ex);
        Assert.IsType<ForbiddenAccessException>(ex);
    }

    // ───────────────────────── S2：共享账本只读成员（Viewer）记账 ─────────────────────────

    [Fact]
    public async Task Viewer向共享账本记账_抛出ForbiddenAccessException()
    {
        var (ledger, viewer) = await CreateSharedLedgerAsync(LedgerMemberRole.Viewer);
        var service = new LedgerMemberService(_db.Context);

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => service.EnsureCanWriteAsync(ledger.Id, viewer.Id));

        Assert.Contains("只读成员", ex.Message);
    }

    [Fact]
    public async Task 非成员向共享账本记账_抛出ForbiddenAccessException()
    {
        var (ledger, _) = await CreateSharedLedgerAsync(LedgerMemberRole.Viewer);
        var stranger = await _db.CreateUserAsync("13900000001");
        var service = new LedgerMemberService(_db.Context);

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => service.EnsureCanWriteAsync(ledger.Id, stranger.Id));
    }

    [Theory]
    [InlineData(LedgerMemberRole.Editor)]
    [InlineData(LedgerMemberRole.Owner)]
    public async Task Editor与Owner向共享账本记账_允许(LedgerMemberRole role)
    {
        var (ledger, member) = await CreateSharedLedgerAsync(role);
        var service = new LedgerMemberService(_db.Context);

        await service.EnsureCanWriteAsync(ledger.Id, member.Id);
    }

    [Fact]
    public async Task 账本所有者向自己的账本记账_允许()
    {
        var (ledger, _) = await CreateSharedLedgerAsync(LedgerMemberRole.Viewer);
        var service = new LedgerMemberService(_db.Context);

        await service.EnsureCanWriteAsync(ledger.Id, _db.User.Id);
    }

    /// <summary>
    /// 创建一个由默认用户拥有的账本，并让另一个用户以指定角色成为生效成员
    /// </summary>
    private async Task<(Ledger Ledger, User Member)> CreateSharedLedgerAsync(LedgerMemberRole role)
    {
        var member = await _db.CreateUserAsync($"139{Random.Shared.Next(10000000, 99999999)}");
        var ledger = new Ledger { UserId = _db.User.Id, Name = "共享账本", Icon = "mdi-book" };
        _db.Context.Ledgers.Add(ledger);
        await _db.Context.SaveChangesAsync();

        _db.Context.LedgerMembers.Add(new LedgerMember
        {
            LedgerId = ledger.Id,
            UserId = member.Id,
            Role = role,
            Status = LedgerMemberStatus.Active
        });
        await _db.Context.SaveChangesAsync();
        return (ledger, member);
    }
}
