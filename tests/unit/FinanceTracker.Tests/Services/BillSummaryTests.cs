using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账单汇总（账单 Tab 顶部汇总卡）：符合当前筛选条件的<b>全部</b>账单的支出合计、收入合计与笔数。
/// <list type="bullet">
/// <item>列表（<c>GetBillsAsync</c>）、总数（<c>GetBillCountAsync</c>）与汇总（<c>GetBillSummaryAsync</c>）共用同一份 <see cref="BillFilter"/> 与同一个查询构造，
/// 在每一种筛选条件下口径必须一致；</item>
/// <item>汇总不受分页影响（列表只加载了第一页，汇总仍覆盖全部）；</item>
/// <item>遵守软删除与账本可见性（共享账本内全部成员的账单可见；未生效的邀请不可见）；</item>
/// <item>日期边界按应用时区（含当天最后一刻，不含次日零点）。</item>
/// </list>
/// 使用真实 SQLite 迁移（不是 InMemory）：分组求和与 decimal 聚合要走真实 Provider 的翻译。
/// </summary>
public sealed class BillSummaryTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _bills = null!;
    private Ledger _daily = null!;
    private Ledger _travel = null!;
    private Category _food = null!;
    private Category _traffic = null!;
    private Category _salary = null!;
    private PaymentChannel _wechat = null!;
    private PaymentChannel _alipay = null!;

    private Guid UserId => _db.User.Id;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _bills = new BillService(_db.Context, NullLogger<BillService>.Instance);

        _daily = await _db.GetDefaultLedgerAsync();
        _travel = await _db.CreateLedgerAsync(UserId, "旅行");

        var expenseCategories = await _db.Context.Categories
            .Where(c => c.UserId == null && c.Type == BillType.Expense)
            .OrderBy(c => c.Name)
            .Take(2)
            .ToListAsync();
        _food = expenseCategories[0];
        _traffic = expenseCategories[1];
        _salary = await _db.Context.Categories.FirstAsync(c => c.UserId == null && c.Type == BillType.Income);

        var channels = await _db.Context.PaymentChannels
            .Where(c => c.UserId == null)
            .OrderBy(c => c.Name)
            .Take(2)
            .ToListAsync();
        _wechat = channels[0];
        _alipay = channels[1];
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // ───────────────────────── 造数 ─────────────────────────

    private static readonly DateTime Noon = new(2026, 3, 10, 12, 0, 0);

    /// <summary>经 BillService 以应用时区的墙上时间创建账单</summary>
    private Task<Bill> AddAsync(
        decimal amount,
        BillType type = BillType.Expense,
        Ledger? ledger = null,
        Category? category = null,
        PaymentChannel? channel = null,
        DateTime? wallTime = null,
        Guid? userId = null)
    {
        return _bills.CreateBillAsync(new Bill
        {
            UserId = userId ?? UserId,
            Amount = amount,
            Type = type,
            CategoryId = (category ?? (type == BillType.Expense ? _food : _salary)).Id,
            PaymentChannelId = (channel ?? _wechat).Id,
            LedgerId = (ledger ?? _daily).Id,
            TransactionTime = DateTime.SpecifyKind(wallTime ?? Noon, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });
    }

    /// <summary>历史账单：账本必填之前创建、没有账本（BillService 已不允许再创建，只能直接入库）</summary>
    private async Task<Bill> AddLegacyAsync(decimal amount, BillType type = BillType.Expense, Guid? userId = null)
    {
        var bill = new Bill
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? UserId,
            Amount = amount,
            Type = type,
            CategoryId = (type == BillType.Expense ? _food : _salary).Id,
            PaymentChannelId = _wechat.Id,
            LedgerId = null,
            TransactionTime = DateTime.SpecifyKind(Noon, DateTimeKind.Utc),
            Source = BillSource.Manual,
            SyncStatus = SyncStatus.Synced
        };
        _db.Context.Bills.Add(bill);
        await _db.Context.SaveChangesAsync();
        return bill;
    }

    /// <summary>另一个用户拥有的账本，默认用户以指定状态 / 角色加入（用来造「共享账本」）</summary>
    private async Task<(Ledger Ledger, User Owner)> CreateSharedLedgerAsync(
        LedgerMemberStatus status = LedgerMemberStatus.Active,
        LedgerMemberRole role = LedgerMemberRole.Editor)
    {
        var owner = await _db.CreateUserAsync($"139{Random.Shared.Next(10000000, 99999999)}");
        var ledger = await _db.CreateLedgerAsync(owner.Id, "共享账本");

        _db.Context.LedgerMembers.Add(new LedgerMember { LedgerId = ledger.Id, UserId = owner.Id, Role = LedgerMemberRole.Owner, Status = LedgerMemberStatus.Active });
        _db.Context.LedgerMembers.Add(new LedgerMember { LedgerId = ledger.Id, UserId = UserId, Role = role, Status = status });
        await _db.Context.SaveChangesAsync();

        return (ledger, owner);
    }

    // ───────────────────────── 断言 ─────────────────────────

    /// <summary>
    /// 汇总、总数与「把列表全部读出来再自己累加」三者一致：在这一份条件下口径没有分叉。
    /// 返回汇总，方便调用方再断言具体数字（避免「两边都是 0」这种空洞的一致）
    /// </summary>
    private async Task<BillSummary> AssertConsistentAsync(BillFilter filter)
    {
        var all = await _bills.GetBillsAsync(UserId, filter, page: 1, pageSize: 10_000);
        var summary = await _bills.GetBillSummaryAsync(UserId, filter);
        var count = await _bills.GetBillCountAsync(UserId, filter);

        Assert.Equal(all.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount), summary.TotalExpense);
        Assert.Equal(all.Where(b => b.Type == BillType.Income).Sum(b => b.Amount), summary.TotalIncome);
        Assert.Equal(all.Count, summary.Count);
        Assert.Equal(all.Count, count);

        return summary;
    }

    // ───────────────────────── 基本口径 ─────────────────────────

    [Fact]
    public async Task 没有账单_汇总全为零()
    {
        var summary = await _bills.GetBillSummaryAsync(UserId, new BillFilter());

        Assert.Equal(BillSummary.Empty, summary);
        Assert.Equal(0m, summary.TotalExpense);
        Assert.Equal(0m, summary.TotalIncome);
        Assert.Equal(0, summary.Count);
    }

    [Fact]
    public async Task 不带条件_汇总全部账单_含历史无账本的账单()
    {
        await AddAsync(10m);
        await AddAsync(20.50m, ledger: _travel);
        await AddAsync(100m, BillType.Income);
        await AddLegacyAsync(8m);

        var summary = await AssertConsistentAsync(new BillFilter());

        Assert.Equal(38.50m, summary.TotalExpense);
        Assert.Equal(100m, summary.TotalIncome);
        Assert.Equal(4, summary.Count);
    }

    [Fact]
    public async Task 汇总不受分页影响_覆盖全部页而不只是已加载的那一页()
    {
        for (var i = 1; i <= 45; i++)
        {
            await AddAsync(i, wallTime: Noon.AddMinutes(i));
        }

        for (var i = 1; i <= 5; i++)
        {
            await AddAsync(1000m, BillType.Income, wallTime: Noon.AddMinutes(100 + i));
        }

        var firstPage = await _bills.GetBillsAsync(UserId, new BillFilter(), page: 1, pageSize: 20);
        var summary = await _bills.GetBillSummaryAsync(UserId, new BillFilter());

        Assert.Equal(20, firstPage.Count);
        Assert.Equal(50, summary.Count);
        Assert.Equal(45 * 46 / 2m, summary.TotalExpense); // 1 + 2 + … + 45
        Assert.Equal(5000m, summary.TotalIncome);
        Assert.Equal(50, await _bills.GetBillCountAsync(UserId, new BillFilter()));
    }

    [Fact]
    public async Task 不含其他用户的账单()
    {
        var other = await _db.CreateUserAsync("13900009999");
        var otherLedger = await _db.CreateLedgerAsync(other.Id, "别人的账本");
        await AddAsync(10m);
        await AddAsync(999m, ledger: otherLedger, userId: other.Id);

        var summary = await AssertConsistentAsync(new BillFilter());

        Assert.Equal(10m, summary.TotalExpense);
        Assert.Equal(1, summary.Count);
    }

    // ───────────────────────── 各筛选条件下与列表一致 ─────────────────────────

    [Fact]
    public async Task 按账本筛选_汇总与列表一致()
    {
        await AddAsync(10m, ledger: _daily);
        await AddAsync(20m, ledger: _travel);
        await AddAsync(30m, BillType.Income, ledger: _travel);

        var summary = await AssertConsistentAsync(new BillFilter { LedgerId = _travel.Id });

        Assert.Equal(20m, summary.TotalExpense);
        Assert.Equal(30m, summary.TotalIncome);
        Assert.Equal(2, summary.Count);
    }

    [Fact]
    public async Task 按分类筛选_汇总与列表一致()
    {
        await AddAsync(10m, category: _food);
        await AddAsync(20m, category: _traffic);
        await AddAsync(40m, category: _traffic);

        var summary = await AssertConsistentAsync(new BillFilter { CategoryIds = [_traffic.Id] });

        Assert.Equal(60m, summary.TotalExpense);
        Assert.Equal(2, summary.Count);
    }

    [Fact]
    public async Task 分类多选_属于所选分类中任意一个的账单都计入_没选中的不计()
    {
        await AddAsync(10m, category: _food);
        await AddAsync(20m, category: _traffic);
        await AddAsync(40m, category: _traffic);
        await AddAsync(80m, category: _salary, type: BillType.Income);

        var foodAndTraffic = await AssertConsistentAsync(new BillFilter { CategoryIds = [_food.Id, _traffic.Id] });
        Assert.Equal(new BillSummary(70m, 0m, 3), foodAndTraffic);

        // 支出分类 + 收入分类也可以一起选（同一个维度内取并集）
        var trafficAndSalary = await AssertConsistentAsync(new BillFilter { CategoryIds = [_traffic.Id, _salary.Id] });
        Assert.Equal(new BillSummary(60m, 80m, 3), trafficAndSalary);

        // 重复的 Id 等价于一个；空列表等价于不限分类
        var duplicated = await AssertConsistentAsync(new BillFilter { CategoryIds = [_traffic.Id, _traffic.Id] });
        Assert.Equal(new BillSummary(60m, 0m, 2), duplicated);
        var emptyList = await AssertConsistentAsync(new BillFilter { CategoryIds = [] });
        Assert.Equal(new BillSummary(70m, 80m, 4), emptyList);

        // 多个分类与其他条件仍然取交集
        var withType = await AssertConsistentAsync(new BillFilter { CategoryIds = [_food.Id, _traffic.Id, _salary.Id], Type = BillType.Income });
        Assert.Equal(new BillSummary(0m, 80m, 1), withType);

        // 所选分类里查不到账单（或根本不存在）：空结果，不是报错
        var none = await AssertConsistentAsync(new BillFilter { CategoryIds = [Guid.NewGuid()] });
        Assert.Equal(BillSummary.Empty, none);
    }

    [Fact]
    public async Task 按支付渠道筛选_汇总与列表一致()
    {
        await AddAsync(10m, channel: _wechat);
        await AddAsync(20m, channel: _alipay);
        await AddAsync(5m, BillType.Income, channel: _alipay);

        var summary = await AssertConsistentAsync(new BillFilter { PaymentChannelId = _alipay.Id });

        Assert.Equal(20m, summary.TotalExpense);
        Assert.Equal(5m, summary.TotalIncome);
        Assert.Equal(2, summary.Count);
    }

    [Fact]
    public async Task 按账单类型筛选_另一类的合计为零()
    {
        await AddAsync(10m);
        await AddAsync(20m);
        await AddAsync(300m, BillType.Income);

        var expense = await AssertConsistentAsync(new BillFilter { Type = BillType.Expense });
        var income = await AssertConsistentAsync(new BillFilter { Type = BillType.Income });

        Assert.Equal(new BillSummary(30m, 0m, 2), expense);
        Assert.Equal(new BillSummary(0m, 300m, 1), income);
    }

    [Fact]
    public async Task 按时间范围筛选_汇总与列表一致()
    {
        await AddAsync(1m, wallTime: new DateTime(2026, 3, 1, 9, 0, 0));
        await AddAsync(2m, wallTime: new DateTime(2026, 3, 5, 9, 0, 0));
        await AddAsync(4m, BillType.Income, wallTime: new DateTime(2026, 3, 9, 9, 0, 0));
        await AddAsync(8m, wallTime: new DateTime(2026, 3, 20, 9, 0, 0));

        var summary = await AssertConsistentAsync(new BillFilter
        {
            StartDate = new DateTime(2026, 3, 2),
            EndDate = TimeBoundaries.EndOfDay(new DateTime(2026, 3, 10))
        });

        Assert.Equal(2m, summary.TotalExpense);
        Assert.Equal(4m, summary.TotalIncome);
        Assert.Equal(2, summary.Count);

        // 只给开始 / 只给结束
        Assert.Equal(10m, (await AssertConsistentAsync(new BillFilter { StartDate = new DateTime(2026, 3, 2) })).TotalExpense);
        Assert.Equal(3m, (await AssertConsistentAsync(new BillFilter { EndDate = TimeBoundaries.EndOfDay(new DateTime(2026, 3, 8)) })).TotalExpense);
    }

    [Fact]
    public async Task 多个条件取交集_汇总与列表一致()
    {
        await AddAsync(10m, ledger: _travel, category: _traffic, channel: _alipay, wallTime: Noon);
        await AddAsync(20m, ledger: _travel, category: _traffic, channel: _wechat, wallTime: Noon);   // 渠道不符
        await AddAsync(40m, ledger: _daily, category: _traffic, channel: _alipay, wallTime: Noon);    // 账本不符
        await AddAsync(80m, ledger: _travel, category: _food, channel: _alipay, wallTime: Noon);      // 分类不符
        await AddAsync(160m, ledger: _travel, category: _traffic, channel: _alipay, wallTime: Noon.AddDays(30)); // 时间不符
        await AddAsync(320m, BillType.Income, ledger: _travel, channel: _alipay, wallTime: Noon);     // 类型不符

        var summary = await AssertConsistentAsync(new BillFilter
        {
            LedgerId = _travel.Id,
            CategoryIds = [_traffic.Id],
            PaymentChannelId = _alipay.Id,
            Type = BillType.Expense,
            StartDate = Noon.Date,
            EndDate = TimeBoundaries.EndOfDay(Noon)
        });

        Assert.Equal(new BillSummary(10m, 0m, 1), summary);
    }

    [Fact]
    public async Task 没有符合条件的账单_汇总为零而不是报错()
    {
        await AddAsync(10m);

        var summary = await AssertConsistentAsync(new BillFilter { LedgerId = _travel.Id });

        Assert.Equal(BillSummary.Empty, summary);
    }

    // ───────────────────────── 未归属账本（历史上没有账本的账单） ─────────────────────────

    [Fact]
    public async Task 未归属账本_只汇总历史上没有账本的账单_与列表一致()
    {
        await AddAsync(10m, ledger: _daily);
        await AddAsync(20m, ledger: _travel);
        var oldExpense = await AddLegacyAsync(40m);
        var oldIncome = await AddLegacyAsync(300m, BillType.Income);

        var summary = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true });

        Assert.Equal(new BillSummary(40m, 300m, 2), summary);
        var listed = await _bills.GetBillsAsync(UserId, new BillFilter { UnassignedLedger = true });
        Assert.Equal(new[] { oldExpense.Id, oldIncome.Id }.OrderBy(id => id), listed.Select(b => b.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task 未归属账本_没有历史账单时汇总为零()
    {
        await AddAsync(10m, ledger: _daily);

        var summary = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true });

        Assert.Equal(BillSummary.Empty, summary);
    }

    [Fact]
    public async Task 未归属账本_不含其他用户的无账本账单_也不含共享账本里别人记的账单()
    {
        var (shared, owner) = await CreateSharedLedgerAsync();
        await AddLegacyAsync(11m);
        await AddLegacyAsync(900m, userId: owner.Id);                // 别人名下的历史账单：不可见
        await AddAsync(25m, ledger: shared, userId: owner.Id);      // 共享账本里别人记的：有账本，不属于「未归属」

        var summary = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true });

        Assert.Equal(new BillSummary(11m, 0m, 1), summary);
    }

    [Fact]
    public async Task 未归属账本_软删除的历史账单排除()
    {
        var gone = await AddLegacyAsync(40m);
        await AddLegacyAsync(5m);
        Assert.True(await _bills.DeleteBillAsync(gone.Id, UserId));

        var summary = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true });

        Assert.Equal(new BillSummary(5m, 0m, 1), summary);
    }

    [Fact]
    public async Task 未归属账本_与账单类型_分类等其它条件取交集()
    {
        await AddLegacyAsync(40m);
        await AddLegacyAsync(300m, BillType.Income);
        await AddAsync(7m, ledger: _daily);

        var expense = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true, Type = BillType.Expense });
        var income = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true, Type = BillType.Income, CategoryIds = [_salary.Id] });
        var wrongCategory = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true, CategoryIds = [_traffic.Id] });

        Assert.Equal(new BillSummary(40m, 0m, 1), expense);
        Assert.Equal(new BillSummary(0m, 300m, 1), income);
        Assert.Equal(BillSummary.Empty, wrongCategory);
    }

    [Fact]
    public async Task 未归属账本与具体账本互斥_同时给出时结果必然为空_共享账本也一样()
    {
        var (shared, owner) = await CreateSharedLedgerAsync();
        await AddLegacyAsync(40m);
        await AddAsync(10m, ledger: _daily);
        await AddAsync(25m, ledger: shared, userId: owner.Id);

        var own = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true, LedgerId = _daily.Id });
        var sharedLedger = await AssertConsistentAsync(new BillFilter { UnassignedLedger = true, LedgerId = shared.Id });

        Assert.Equal(BillSummary.Empty, own);
        Assert.Equal(BillSummary.Empty, sharedLedger);
    }

    [Fact]
    public async Task 归属账本之后_历史账单不再出现在未归属账本里()
    {
        var old = await AddLegacyAsync(40m);
        Assert.Equal(1, (await _bills.GetBillSummaryAsync(UserId, new BillFilter { UnassignedLedger = true })).Count);

        Assert.Equal(1, await _bills.AssignBillsToLedgerAsync(new List<Guid> { old.Id }, UserId, _travel.Id));

        Assert.Equal(BillSummary.Empty, await AssertConsistentAsync(new BillFilter { UnassignedLedger = true }));
        Assert.Equal(new BillSummary(40m, 0m, 1), await AssertConsistentAsync(new BillFilter { LedgerId = _travel.Id }));
    }

    // ───────────────────────── 软删除 ─────────────────────────

    [Fact]
    public async Task 软删除的账单不计入列表_总数与汇总()
    {
        var keep = await AddAsync(10m);
        var removed = await AddAsync(20m);
        var removedIncome = await AddAsync(500m, BillType.Income);

        Assert.True(await _bills.DeleteBillAsync(removed.Id, UserId));
        Assert.True(await _bills.DeleteBillAsync(removedIncome.Id, UserId));

        var summary = await AssertConsistentAsync(new BillFilter());

        Assert.Equal(new BillSummary(10m, 0m, 1), summary);
        Assert.Equal(keep.Id, Assert.Single(await _bills.GetBillsAsync(UserId, new BillFilter())).Id);
    }

    // ───────────────────────── 账本可见性（共享账本） ─────────────────────────

    [Fact]
    public async Task 共享账本_生效成员筛选该账本时汇总含账本内全部成员的账单()
    {
        var (shared, owner) = await CreateSharedLedgerAsync();
        await AddAsync(10m, ledger: shared);                       // 我自己记的
        await AddAsync(25m, ledger: shared, userId: owner.Id);     // 所有者记的
        await AddAsync(70m, BillType.Income, ledger: shared, userId: owner.Id);
        await AddAsync(5m, ledger: _daily);                        // 我自己别的账本里的，不在这个账本的汇总里

        var summary = await AssertConsistentAsync(new BillFilter { LedgerId = shared.Id });

        Assert.Equal(new BillSummary(35m, 70m, 3), summary);
    }

    [Fact]
    public async Task 共享账本_不筛选账本时只汇总我自己的账单()
    {
        var (shared, owner) = await CreateSharedLedgerAsync();
        await AddAsync(10m, ledger: shared);
        await AddAsync(25m, ledger: shared, userId: owner.Id);

        var summary = await AssertConsistentAsync(new BillFilter());

        Assert.Equal(new BillSummary(10m, 0m, 1), summary);
    }

    [Fact]
    public async Task 共享账本_邀请还没接受时看不到别人的账单()
    {
        var (shared, owner) = await CreateSharedLedgerAsync(status: LedgerMemberStatus.Pending);
        await AddAsync(25m, ledger: shared, userId: owner.Id);

        var summary = await AssertConsistentAsync(new BillFilter { LedgerId = shared.Id });

        Assert.Equal(BillSummary.Empty, summary);
    }

    [Fact]
    public async Task 共享账本_软删除的成员账单同样排除()
    {
        var (shared, owner) = await CreateSharedLedgerAsync();
        var gone = await AddAsync(25m, ledger: shared, userId: owner.Id);
        await AddAsync(5m, ledger: shared, userId: owner.Id);
        await _bills.DeleteBillAsync(gone.Id, owner.Id);

        var summary = await AssertConsistentAsync(new BillFilter { LedgerId = shared.Id });

        Assert.Equal(new BillSummary(5m, 0m, 1), summary);
    }

    // ───────────────────────── 时区边界 ─────────────────────────

    [Fact]
    public async Task 日期边界按应用时区_含当天最后一刻_不含次日零点()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        await AddAsync(1m, wallTime: new DateTime(2026, 3, 1, 0, 0, 0));          // 当天第一刻（UTC 2/28 16:00）
        await AddAsync(2m, wallTime: new DateTime(2026, 3, 1, 23, 59, 59, 500));  // 当天最后一秒内
        await AddAsync(4m, wallTime: new DateTime(2026, 3, 2, 0, 0, 0));          // 次日零点，不属于 3/1
        await AddAsync(8m, wallTime: new DateTime(2026, 2, 28, 23, 59, 59));      // 前一天最后一秒

        var day = new DateTime(2026, 3, 1);
        var summary = await AssertConsistentAsync(new BillFilter { StartDate = day, EndDate = TimeBoundaries.EndOfDay(day) });

        Assert.Equal(new BillSummary(3m, 0m, 2), summary);
    }

    // ───────────────────────── 列表分页稳定 ─────────────────────────

    [Fact]
    public async Task 交易时间相同的账单分页时不重复_不遗漏()
    {
        // 导入、同一分钟里连记几笔都会产生相同的交易时间；排序没有确定的次序时，分页会让同一笔重复出现、另一笔被漏掉
        for (var i = 0; i < 30; i++)
        {
            await AddAsync(1m + i, wallTime: Noon);
        }

        var ids = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var items = await _bills.GetBillsAsync(UserId, new BillFilter(), page, pageSize: 10);
            Assert.Equal(10, items.Count);
            ids.AddRange(items.Select(b => b.Id));
        }

        Assert.Equal(30, ids.Distinct().Count());
        Assert.Empty(await _bills.GetBillsAsync(UserId, new BillFilter(), page: 4, pageSize: 10));
    }

    [Fact]
    public async Task 列表按交易时间倒序()
    {
        var early = await AddAsync(1m, wallTime: Noon.AddHours(-3));
        var late = await AddAsync(2m, wallTime: Noon.AddHours(3));
        var mid = await AddAsync(3m, wallTime: Noon);

        var listed = await _bills.GetBillsAsync(UserId, new BillFilter());

        Assert.Equal(new[] { late.Id, mid.Id, early.Id }, listed.Select(b => b.Id));
    }

    // ───────────────────────── 旧签名与条件对象等价 ─────────────────────────

    [Fact]
    public async Task 旧的逐项参数重载与条件对象口径一致()
    {
        await AddAsync(10m, ledger: _travel, category: _traffic, wallTime: new DateTime(2026, 3, 5, 9, 0, 0));
        await AddAsync(20m, ledger: _daily, category: _traffic, wallTime: new DateTime(2026, 3, 6, 9, 0, 0));
        await AddAsync(50m, BillType.Income, ledger: _travel, wallTime: new DateTime(2026, 3, 7, 9, 0, 0));

        var start = new DateTime(2026, 3, 1);
        var end = TimeBoundaries.EndOfDay(new DateTime(2026, 3, 31));
        var filter = new BillFilter { StartDate = start, EndDate = end, LedgerId = _travel.Id, Type = BillType.Expense };

        var legacy = await _bills.GetBillsAsync(UserId, start, end, ledgerId: _travel.Id, type: BillType.Expense);
        var modern = await _bills.GetBillsAsync(UserId, filter);

        Assert.Equal(modern.Select(b => b.Id), legacy.Select(b => b.Id));
        Assert.Equal(
            await _bills.GetBillCountAsync(UserId, filter),
            await _bills.GetBillCountAsync(UserId, start, end, ledgerId: _travel.Id, type: BillType.Expense));
    }

    [Fact]
    public void 条件对象_全部为空才算没有条件()
    {
        Assert.True(new BillFilter().IsEmpty);
        Assert.False(new BillFilter { Type = BillType.Income }.IsEmpty);
        Assert.False(new BillFilter { StartDate = DateTime.Today }.IsEmpty);
        Assert.False(new BillFilter { LedgerId = Guid.NewGuid() }.IsEmpty);
        Assert.False(new BillFilter { CategoryIds = [Guid.NewGuid()] }.IsEmpty);
        Assert.True(new BillFilter { CategoryIds = [] }.IsEmpty); // 空列表 = 不限分类
        Assert.False(new BillFilter { PaymentChannelId = Guid.NewGuid() }.IsEmpty);
        Assert.False(new BillFilter { UnassignedLedger = true }.IsEmpty);
        Assert.False(new BillFilter { EndDate = DateTime.Today }.IsEmpty);
    }
}
