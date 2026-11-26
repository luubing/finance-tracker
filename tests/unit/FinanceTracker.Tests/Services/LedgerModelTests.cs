using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 录入页面的账本默认选中规则（账本必填后，所有录入入口共用 <see cref="LedgerModel"/> 的两个帮助方法）：
/// <list type="bullet">
/// <item>默认账本优先，没有就取第一个自己的账本，一个都没有返回 null（页面据此引导去创建账本）</item>
/// <item>只考虑自己的账本：共享账本既不能被当作默认，也不能在用户没选时替他往别人的账本里记账</item>
/// <item>页面当前选中项仍然有效就沿用（从账本管理页返回刷新列表时不能把用户的选择顶掉）</item>
/// </list>
/// </summary>
public sealed class LedgerModelTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private LedgerService _ledgers = null!;
    private UserDefaultService _userDefaults = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _ledgers = new LedgerService(_db.Context, NullLogger<LedgerService>.Instance);
        _userDefaults = new UserDefaultService(_db.Context);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static LedgerModel Own(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static LedgerModel Shared(string name) => new() { Id = Guid.NewGuid(), Name = name, IsShared = true };

    /// <summary>按 SortOrder 明确"第一个"是谁，不依赖创建时间的先后</summary>
    private Task<Ledger> CreateLedgerAsync(string name, int sortOrder) =>
        _ledgers.CreateLedgerAsync(new Ledger
        {
            UserId = _db.User.Id,
            Name = name,
            Icon = "mdi-book-open-variant",
            SortOrder = sortOrder
        });

    // ───────────────────────── PickDefault ─────────────────────────

    [Fact]
    public void 用户设置的默认账本有效_优先选它()
    {
        var first = Own("日常");
        var preferred = Own("旅行");

        Assert.Equal(preferred.Id, LedgerModel.PickDefault(new[] { first, preferred }, preferred.Id));
    }

    [Fact]
    public void 没有设置默认账本_取第一个自己的账本()
    {
        var first = Own("日常");
        var second = Own("旅行");

        Assert.Equal(first.Id, LedgerModel.PickDefault(new[] { first, second }, null));
    }

    [Fact]
    public void 默认账本已不在列表里_回落到第一个自己的账本()
    {
        var first = Own("日常");

        Assert.Equal(first.Id, LedgerModel.PickDefault(new[] { first }, Guid.NewGuid()));
    }

    [Fact]
    public void 默认账本指向共享账本_不予采纳_回落到自己的账本()
    {
        var shared = Shared("别人共享的");
        var own = Own("日常");

        Assert.Equal(own.Id, LedgerModel.PickDefault(new[] { shared, own }, shared.Id));
    }

    [Fact]
    public void 共享账本排在前面_也不会被当作第一个账本选中()
    {
        var shared = Shared("别人共享的");
        var own = Own("日常");

        Assert.Equal(own.Id, LedgerModel.PickDefault(new[] { shared, own }, null));
    }

    [Fact]
    public void 只有共享账本_不替用户选择()
    {
        Assert.Null(LedgerModel.PickDefault(new[] { Shared("别人共享的") }, null));
    }

    [Fact]
    public void 一个账本都没有_返回null()
    {
        Assert.Null(LedgerModel.PickDefault(Array.Empty<LedgerModel>(), Guid.NewGuid()));
    }

    // ───────────────────────── LoadOwnAsync ─────────────────────────

    [Fact]
    public async Task 加载自己的账本_没有设置默认账本_选中第一个()
    {
        var first = await CreateLedgerAsync("日常", sortOrder: 0);
        await CreateLedgerAsync("旅行", sortOrder: 1);

        var (ledgers, selected) = await LedgerModel.LoadOwnAsync(_ledgers, _userDefaults, _db.User.Id);

        Assert.Equal(new[] { "日常", "旅行" }, ledgers.Select(l => l.Name));
        Assert.All(ledgers, l => Assert.False(l.IsShared));
        Assert.Equal(first.Id, selected);
    }

    [Fact]
    public async Task 加载自己的账本_用户设置了默认账本_选中默认账本()
    {
        await CreateLedgerAsync("日常", sortOrder: 0);
        var travel = await CreateLedgerAsync("旅行", sortOrder: 1);
        await _userDefaults.SetDefaultLedgerAsync(_db.User.Id, travel.Id);

        var (_, selected) = await LedgerModel.LoadOwnAsync(_ledgers, _userDefaults, _db.User.Id);

        Assert.Equal(travel.Id, selected);
    }

    [Fact]
    public async Task 加载自己的账本_当前选中仍然有效_沿用而不是被默认账本顶掉()
    {
        var daily = await CreateLedgerAsync("日常", sortOrder: 0);
        var travel = await CreateLedgerAsync("旅行", sortOrder: 1);
        await _userDefaults.SetDefaultLedgerAsync(_db.User.Id, travel.Id);

        var (_, selected) = await LedgerModel.LoadOwnAsync(
            _ledgers, _userDefaults, _db.User.Id, currentLedgerId: daily.Id);

        Assert.Equal(daily.Id, selected);
    }

    [Fact]
    public async Task 加载自己的账本_当前选中已失效_回落到默认账本()
    {
        await CreateLedgerAsync("日常", sortOrder: 0);
        var travel = await CreateLedgerAsync("旅行", sortOrder: 1);
        await _userDefaults.SetDefaultLedgerAsync(_db.User.Id, travel.Id);

        var (_, selected) = await LedgerModel.LoadOwnAsync(
            _ledgers, _userDefaults, _db.User.Id, currentLedgerId: Guid.NewGuid());

        Assert.Equal(travel.Id, selected);
    }

    [Fact]
    public async Task 加载自己的账本_不包含别人的账本()
    {
        var mine = await CreateLedgerAsync("日常", sortOrder: 0);
        var stranger = await _db.CreateUserAsync("13900000002");
        await _db.CreateLedgerAsync(stranger.Id, "别人的账本");

        var (ledgers, selected) = await LedgerModel.LoadOwnAsync(_ledgers, _userDefaults, _db.User.Id);

        Assert.Equal(mine.Id, Assert.Single(ledgers).Id);
        Assert.Equal(mine.Id, selected);
    }

    [Fact]
    public async Task 加载自己的账本_一个账本都没有_返回空列表和null()
    {
        var (ledgers, selected) = await LedgerModel.LoadOwnAsync(_ledgers, _userDefaults, _db.User.Id);

        Assert.Empty(ledgers);
        Assert.Null(selected);
    }
}
