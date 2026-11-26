using System.Globalization;
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
/// 账单搜索关键字的解析（纯函数）：文本小写化、数字识别、与系统区域设置无关。
/// </summary>
public class BillKeywordParseTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void 空_全空白或null_没有关键字条件(string? input)
    {
        Assert.Null(BillKeyword.Parse(input));
        Assert.True(new BillFilter { Keyword = input }.IsEmpty);
    }

    [Fact]
    public void 有关键字的条件对象不算没有条件()
    {
        Assert.False(new BillFilter { Keyword = "咖啡" }.IsEmpty);
    }

    [Fact]
    public void 文本取首尾去空白后转小写_中间的空白原样保留()
    {
        var keyword = BillKeyword.Parse("  Costco  Run \n");

        Assert.NotNull(keyword);
        Assert.Equal("costco  run", keyword.Text);
        Assert.Null(keyword.Amount);
    }

    [Theory]
    [InlineData("35", "35")]
    [InlineData("12.5", "12.5")]
    [InlineData("12.50", "12.5")]
    [InlineData(" 12.5 ", "12.5")]
    [InlineData(".5", "0.5")]
    [InlineData("5.", "5")]
    [InlineData("0", "0")]
    [InlineData("007", "7")]
    [InlineData("+8", "8")]
    [InlineData("1000000", "1000000")]
    public void 整体能解析为数字时带上金额_数值相等而不是文本相同(string input, string expected)
    {
        var keyword = BillKeyword.Parse(input);

        Assert.NotNull(keyword);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), keyword.Amount);
        Assert.Equal(input.Trim(), keyword.Text);
    }

    [Theory]
    [InlineData("35元")]
    [InlineData("¥35")]
    [InlineData("$12.5")]
    [InlineData("1e3")]
    [InlineData("12,5")]
    [InlineData("1,234")]
    [InlineData("12.5.1")]
    [InlineData("1 2")]
    [InlineData("--1")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("∞")]
    [InlineData("0x10")]
    [InlineData("１２")]        // 全角数字
    [InlineData("٣")]            // 阿拉伯-印度数字
    [InlineData("-")]
    [InlineData(".")]
    public void 含非数字字符的关键字没有金额_只做文本匹配(string input)
    {
        var keyword = BillKeyword.Parse(input);

        Assert.NotNull(keyword);
        Assert.Null(keyword.Amount);
    }

    [Fact]
    public void 超出金额范围的数字没有金额_不抛异常()
    {
        var keyword = BillKeyword.Parse("99999999999999999999999999999999");

        Assert.NotNull(keyword);
        Assert.Null(keyword.Amount);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("zh-CN")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void 解析与系统区域设置无关(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            // 小数点永远是「.」，逗号永远不当数字
            Assert.Equal(12.5m, BillKeyword.Parse("12.5")!.Amount);
            Assert.Null(BillKeyword.Parse("12,5")!.Amount);
            Assert.Null(BillKeyword.Parse("1.234,5")!.Amount);

            // 土耳其语里 "I".ToLower() 是 "ı"：这里必须是固定区域的小写，否则 TITLE 搜不到 title
            Assert.Equal("title", BillKeyword.Parse("TITLE")!.Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }
}

/// <summary>
/// 账单关键字搜索（账单 Tab 的搜索框）：备注或分类名称（不区分大小写的包含匹配），关键字整体是数字时再加上「金额等于该数值」。
/// <list type="bullet">
/// <item>与时间 / 账本 / 分类 / 支付渠道 / 账单类型 / 未归属账本取交集；仍遵守软删除与账本可见性（含共享账本）；</item>
/// <item>列表、总数与汇总共用同一份 <see cref="BillFilter"/>，带关键字时口径同样一致，汇总不受分页影响；</item>
/// <item>关键字为空时行为与不带关键字完全一致。</item>
/// </list>
/// 使用真实 SQLite 迁移：decimal 的数值比较、<c>lower()</c> / <c>instr()</c> 的翻译都要走真实 Provider。
/// </summary>
public sealed class BillKeywordSearchTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _bills = null!;
    private Ledger _daily = null!;
    private Ledger _travel = null!;
    private Category _food = null!;
    private Category _gym = null!;
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

        // 自定义分类：名字里有大小写字母、也有中文，搜分类名称时不依赖预设数据
        _gym = await CreateCategoryAsync("Gym 健身", BillType.Expense);
        _food = await CreateCategoryAsync("Groceries 买菜", BillType.Expense);
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

    private async Task<Category> CreateCategoryAsync(string name, BillType type)
    {
        var category = new Category
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Name = name,
            Icon = "mdi-tag",
            Type = type,
            IsPreset = false,
        };
        _db.Context.Categories.Add(category);
        await _db.Context.SaveChangesAsync();
        return category;
    }

    /// <summary>经 BillService 以应用时区的墙上时间创建账单</summary>
    private Task<Bill> AddAsync(
        decimal amount,
        string? note = null,
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
            Note = note,
            Source = BillSource.Manual
        });
    }

    /// <summary>历史账单：账本必填之前创建、没有账本（BillService 已不允许再创建，只能直接入库）</summary>
    private async Task<Bill> AddLegacyAsync(decimal amount, string? note)
    {
        var bill = new Bill
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Amount = amount,
            Type = BillType.Expense,
            CategoryId = _food.Id,
            PaymentChannelId = _wechat.Id,
            LedgerId = null,
            TransactionTime = DateTime.SpecifyKind(Noon, DateTimeKind.Utc),
            Note = note,
            Source = BillSource.Manual,
            SyncStatus = SyncStatus.Synced
        };
        _db.Context.Bills.Add(bill);
        await _db.Context.SaveChangesAsync();
        return bill;
    }

    /// <summary>另一个用户拥有的账本，默认用户以指定状态 / 角色加入（用来造「共享账本」）</summary>
    private async Task<(Ledger Ledger, User Owner)> CreateSharedLedgerAsync(LedgerMemberStatus status = LedgerMemberStatus.Active)
    {
        var owner = await _db.CreateUserAsync($"139{Random.Shared.Next(10000000, 99999999)}");
        var ledger = await _db.CreateLedgerAsync(owner.Id, "共享账本");

        _db.Context.LedgerMembers.Add(new LedgerMember { LedgerId = ledger.Id, UserId = owner.Id, Role = LedgerMemberRole.Owner, Status = LedgerMemberStatus.Active });
        _db.Context.LedgerMembers.Add(new LedgerMember { LedgerId = ledger.Id, UserId = UserId, Role = LedgerMemberRole.Editor, Status = status });
        await _db.Context.SaveChangesAsync();

        return (ledger, owner);
    }

    // ───────────────────────── 断言 ─────────────────────────

    /// <summary>
    /// 汇总、总数与「把列表全部读出来再自己累加」三者一致：带关键字时口径同样没有分叉。
    /// 返回命中的账单 Id，方便调用方断言具体是哪几笔
    /// </summary>
    private async Task<List<Guid>> SearchAsync(BillFilter filter)
    {
        var all = await _bills.GetBillsAsync(UserId, filter, page: 1, pageSize: 10_000);
        var summary = await _bills.GetBillSummaryAsync(UserId, filter);
        var count = await _bills.GetBillCountAsync(UserId, filter);

        Assert.Equal(all.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount), summary.TotalExpense);
        Assert.Equal(all.Where(b => b.Type == BillType.Income).Sum(b => b.Amount), summary.TotalIncome);
        Assert.Equal(all.Count, summary.Count);
        Assert.Equal(all.Count, count);

        return all.Select(b => b.Id).ToList();
    }

    private Task<List<Guid>> SearchAsync(string? keyword) => SearchAsync(new BillFilter { Keyword = keyword });

    private static void AssertSameBills(IEnumerable<Bill> expected, IEnumerable<Guid> actual) =>
        Assert.Equal(expected.Select(b => b.Id).OrderBy(id => id), actual.OrderBy(id => id));

    // ───────────────────────── 备注 ─────────────────────────

    [Fact]
    public async Task 备注包含关键字即命中_不区分大小写()
    {
        var costco = await AddAsync(10m, "Costco Run");
        var latte = await AddAsync(20m, "星巴克拿铁");
        await AddAsync(30m, null);
        await AddAsync(40m, string.Empty);
        await AddAsync(50m, "午餐");

        AssertSameBills([costco], await SearchAsync("costco"));
        AssertSameBills([costco], await SearchAsync("COSTCO"));
        AssertSameBills([costco], await SearchAsync("cOsT"));
        AssertSameBills([costco], await SearchAsync("Costco Run"));
        AssertSameBills([latte], await SearchAsync("拿铁"));
        AssertSameBills([latte], await SearchAsync("星巴克拿铁"));
        AssertSameBills([latte], await SearchAsync("巴克"));
        Assert.Empty(await SearchAsync("不存在的备注"));
    }

    [Fact]
    public async Task 首尾空白忽略_中间的空白参与匹配()
    {
        var spaced = await AddAsync(10m, "iced latte");
        await AddAsync(20m, "icedlatte");

        AssertSameBills([spaced], await SearchAsync("  iced latte \n"));
        AssertSameBills([spaced], await SearchAsync("ICED LATTE"));
    }

    // ───────────────────────── 分类名称 ─────────────────────────

    [Fact]
    public async Task 分类名称包含关键字即命中_备注为空也能搜到_不区分大小写()
    {
        var gymBill = await AddAsync(10m, null, category: _gym);
        var gymBill2 = await AddAsync(20m, "月卡", category: _gym);
        await AddAsync(30m, "午餐", category: _food);

        AssertSameBills([gymBill, gymBill2], await SearchAsync("gym"));
        AssertSameBills([gymBill, gymBill2], await SearchAsync("GYM"));
        AssertSameBills([gymBill, gymBill2], await SearchAsync("健身"));
        AssertSameBills([gymBill, gymBill2], await SearchAsync("Gym 健"));
        Assert.Empty(await SearchAsync("游泳"));
    }

    [Fact]
    public async Task 备注与分类名称任一命中即可_同一笔账单不会重复出现()
    {
        var both = await AddAsync(10m, "gym 月卡", category: _gym);
        var onlyNote = await AddAsync(20m, "和朋友去 gym 门口吃饭", category: _food);
        await AddAsync(30m, "午餐", category: _food);

        var ids = await SearchAsync("gym");

        Assert.Equal(2, ids.Count);
        AssertSameBills([both, onlyNote], ids);
    }

    // ───────────────────────── 金额 ─────────────────────────

    [Fact]
    public async Task 数字关键字匹配金额相等_12点5与12点50相等_不匹配112点5_不是文本包含()
    {
        var a = await AddAsync(12.5m);
        var b = await AddAsync(12.50m);     // 数值相同、小数位数不同：库里是 "12.50" 与 "12.5" 两个不同的文本
        var c = await AddAsync(112.5m);
        await AddAsync(12.55m);
        var d = await AddAsync(12m);
        var e = await AddAsync(125m);
        await AddAsync(1250m);

        AssertSameBills([a, b], await SearchAsync("12.5"));
        AssertSameBills([a, b], await SearchAsync("12.50"));
        AssertSameBills([a, b], await SearchAsync(" 12.500 "));
        AssertSameBills([d], await SearchAsync("12"));
        AssertSameBills([c], await SearchAsync("112.5"));
        AssertSameBills([e], await SearchAsync("125"));
    }

    [Fact]
    public async Task 库里的金额文本格式不规整时也按数值比较()
    {
        // SQLite 里 decimal 是文本。EF 写入时把 12.50 规整成 "12.5"，但数据也可能来自别的路径（旧版本、手工导入），
        // 文本格式不一定规整：金额匹配必须是数值比较，不能依赖字符串恰好相同
        var a = await AddAsync(12.5m);
        var b = await AddAsync(12m);
        await using (var command = _db.Connection.CreateCommand())
        {
            command.CommandText = "UPDATE Bills SET Amount = '12.500' WHERE Amount = '12.5'";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        AssertSameBills([a], await SearchAsync("12.5"));
        AssertSameBills([a], await SearchAsync("12.50"));
        AssertSameBills([b], await SearchAsync("12"));
    }

    [Fact]
    public async Task 数字关键字对收入同样按金额匹配_汇总与列表一致()
    {
        var income = await AddAsync(12000m, null, BillType.Income);
        await AddAsync(1200m, null, BillType.Income);

        var filter = new BillFilter { Keyword = "12000" };
        AssertSameBills([income], await SearchAsync(filter));
        Assert.Equal(new BillSummary(0m, 12000m, 1), await _bills.GetBillSummaryAsync(UserId, filter));
    }

    [Fact]
    public async Task 数字关键字同时也按文本匹配备注与分类名称()
    {
        var byNote = await AddAsync(5m, "618大促");
        var byAmount = await AddAsync(618m, null);
        await AddAsync(1618m, null);
        var promoCategory = await CreateCategoryAsync("618专区", BillType.Expense);
        var byCategory = await AddAsync(7m, null, category: promoCategory);

        AssertSameBills([byNote, byAmount, byCategory], await SearchAsync("618"));
    }

    [Fact]
    public async Task 含非数字字符的关键字只做文本匹配_不碰金额()
    {
        await AddAsync(12.5m, null);
        var byNote = await AddAsync(99m, "午餐12.5元");
        await AddAsync(1000m, null);

        AssertSameBills([byNote], await SearchAsync("12.5元"));
        Assert.Empty(await SearchAsync("1e3"));     // 不能把 1e3 当成 1000
        Assert.Empty(await SearchAsync("¥12.5"));
        Assert.Empty(await SearchAsync("12,5"));
    }

    [Fact]
    public async Task 金额是小数时关键字没有小数点就不匹配()
    {
        await AddAsync(12.5m, null);

        Assert.Empty(await SearchAsync("12"));
        Assert.Empty(await SearchAsync("125"));
        Assert.Single(await SearchAsync("12.5"));
    }

    // ───────────────────────── 特殊字符 ─────────────────────────

    [Fact]
    public async Task 百分号与下划线当字面量_不是LIKE通配符()
    {
        var percent = await AddAsync(10m, "100%纯果汁");
        var underscore = await AddAsync(20m, "a_b");
        await AddAsync(30m, "ab");
        await AddAsync(40m, "纯果汁");

        AssertSameBills([percent], await SearchAsync("%"));
        AssertSameBills([percent], await SearchAsync("100%"));
        AssertSameBills([underscore], await SearchAsync("_"));
        AssertSameBills([underscore], await SearchAsync("a_b"));
    }

    [Fact]
    public async Task 引号等特殊字符照常搜索_不会破坏查询()
    {
        var quoted = await AddAsync(10m, "Tom's 小店");
        await AddAsync(20m, "午餐");

        AssertSameBills([quoted], await SearchAsync("tom's"));
        Assert.Empty(await SearchAsync("'; DROP TABLE Bills; --"));
        Assert.Empty(await SearchAsync("\\"));

        // 查询仍然可用
        Assert.Equal(2, (await SearchAsync((string?)null)).Count);
    }

    // ───────────────────────── 与其它条件取交集 ─────────────────────────

    [Fact]
    public async Task 关键字与时间_账本_分类_支付渠道_账单类型逐个取交集()
    {
        // 基准：搜「咖啡」，账本 travel、分类 gym、渠道 alipay、支出、3 月 10 日
        var hit = await AddAsync(10m, "咖啡", ledger: _travel, category: _gym, channel: _alipay, wallTime: Noon);
        var otherLedger = await AddAsync(20m, "咖啡", ledger: _daily, category: _gym, channel: _alipay, wallTime: Noon);
        var otherCategory = await AddAsync(40m, "咖啡", ledger: _travel, category: _food, channel: _alipay, wallTime: Noon);
        var otherChannel = await AddAsync(80m, "咖啡", ledger: _travel, category: _gym, channel: _wechat, wallTime: Noon);
        var otherTime = await AddAsync(160m, "咖啡", ledger: _travel, category: _gym, channel: _alipay, wallTime: Noon.AddDays(30));
        var income = await AddAsync(320m, "咖啡", BillType.Income, ledger: _travel, channel: _alipay, wallTime: Noon);
        var noMatch = await AddAsync(640m, "午餐", ledger: _travel, category: _gym, channel: _alipay, wallTime: Noon);

        // 逐个条件：只有这一个条件 + 关键字
        AssertSameBills([hit, otherCategory, otherChannel, otherTime, income],
            await SearchAsync(new BillFilter { Keyword = "咖啡", LedgerId = _travel.Id }));
        AssertSameBills([hit, otherLedger, otherChannel, otherTime],
            await SearchAsync(new BillFilter { Keyword = "咖啡", CategoryIds = [_gym.Id] }));
        AssertSameBills([hit, otherLedger, otherCategory, otherTime, income],
            await SearchAsync(new BillFilter { Keyword = "咖啡", PaymentChannelId = _alipay.Id }));
        AssertSameBills([income],
            await SearchAsync(new BillFilter { Keyword = "咖啡", Type = BillType.Income }));
        AssertSameBills([hit, otherLedger, otherCategory, otherChannel, income],
            await SearchAsync(new BillFilter { Keyword = "咖啡", StartDate = Noon.Date, EndDate = TimeBoundaries.EndOfDay(Noon) }));

        // 全部条件一起
        var all = await SearchAsync(new BillFilter
        {
            Keyword = "咖啡",
            LedgerId = _travel.Id,
            CategoryIds = [_gym.Id],
            PaymentChannelId = _alipay.Id,
            Type = BillType.Expense,
            StartDate = Noon.Date,
            EndDate = TimeBoundaries.EndOfDay(Noon),
        });
        AssertSameBills([hit], all);

        // 关键字不命中的那一笔，任何条件下都不会出现
        Assert.DoesNotContain(noMatch.Id, all);
    }

    [Fact]
    public async Task 关键字与未归属账本取交集()
    {
        var legacyHit = await AddLegacyAsync(10m, "旧账单咖啡");
        await AddLegacyAsync(20m, "旧账单午餐");
        await AddAsync(30m, "咖啡", ledger: _daily);

        AssertSameBills([legacyHit], await SearchAsync(new BillFilter { Keyword = "咖啡", UnassignedLedger = true }));
    }

    [Fact]
    public async Task 关键字只在我自己的账单里搜_看不到其他用户的账单()
    {
        var other = await _db.CreateUserAsync("13900009999");
        var otherLedger = await _db.CreateLedgerAsync(other.Id, "别人的账本");
        var mine = await AddAsync(10m, "咖啡");
        await AddAsync(999m, "咖啡", ledger: otherLedger, userId: other.Id);

        AssertSameBills([mine], await SearchAsync("咖啡"));
        Assert.Empty(await SearchAsync("999"));
    }

    // ───────────────────────── 软删除 ─────────────────────────

    [Fact]
    public async Task 软删除的账单搜不到_列表_总数与汇总都排除()
    {
        var keep = await AddAsync(10m, "咖啡");
        var removed = await AddAsync(20m, "咖啡");
        var removedByAmount = await AddAsync(35m, null);
        await AddAsync(35m, "保留", category: _gym);

        Assert.True(await _bills.DeleteBillAsync(removed.Id, UserId));
        Assert.True(await _bills.DeleteBillAsync(removedByAmount.Id, UserId));

        AssertSameBills([keep], await SearchAsync("咖啡"));
        Assert.Single(await SearchAsync("35"));
    }

    // ───────────────────────── 共享账本 ─────────────────────────

    [Fact]
    public async Task 共享账本_筛选该账本时关键字在账本内全部成员的账单里搜()
    {
        var (shared, owner) = await CreateSharedLedgerAsync();
        var mine = await AddAsync(10m, "共享咖啡", ledger: shared);
        var theirs = await AddAsync(25m, "共享咖啡豆", ledger: shared, userId: owner.Id);
        await AddAsync(70m, "共享午餐", ledger: shared, userId: owner.Id);
        await AddAsync(5m, "共享咖啡", ledger: _daily);     // 我自己别的账本里的：不在这个账本的搜索范围

        var ids = await SearchAsync(new BillFilter { Keyword = "咖啡", LedgerId = shared.Id });

        AssertSameBills([mine, theirs], ids);
    }

    [Fact]
    public async Task 共享账本_不筛选账本时只搜我自己的账单()
    {
        var (shared, owner) = await CreateSharedLedgerAsync();
        var mine = await AddAsync(10m, "咖啡", ledger: shared);
        await AddAsync(25m, "咖啡", ledger: shared, userId: owner.Id);

        AssertSameBills([mine], await SearchAsync("咖啡"));
    }

    [Fact]
    public async Task 共享账本_邀请还没接受时搜不到别人的账单()
    {
        var (shared, owner) = await CreateSharedLedgerAsync(LedgerMemberStatus.Pending);
        await AddAsync(25m, "咖啡", ledger: shared, userId: owner.Id);

        Assert.Empty(await SearchAsync(new BillFilter { Keyword = "咖啡", LedgerId = shared.Id }));
    }

    [Fact]
    public async Task 共享账本_软删除的成员账单同样搜不到_金额匹配也一样()
    {
        var (shared, owner) = await CreateSharedLedgerAsync();
        var gone = await AddAsync(25m, "咖啡", ledger: shared, userId: owner.Id);
        var keep = await AddAsync(25m, "午餐", ledger: shared, userId: owner.Id);
        await _bills.DeleteBillAsync(gone.Id, owner.Id);

        AssertSameBills([keep], await SearchAsync(new BillFilter { Keyword = "25", LedgerId = shared.Id }));
        Assert.Empty(await SearchAsync(new BillFilter { Keyword = "咖啡", LedgerId = shared.Id }));
    }

    // ───────────────────────── 空关键字 ─────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task 空关键字与没有关键字等价(string? keyword)
    {
        await AddAsync(10m, "咖啡");
        await AddAsync(20m, null, category: _gym);
        await AddAsync(300m, null, BillType.Income);
        await AddLegacyAsync(8m, "旧账单");

        var withoutKeyword = await _bills.GetBillsAsync(UserId, new BillFilter(), page: 1, pageSize: 100);
        var withKeyword = await _bills.GetBillsAsync(UserId, new BillFilter { Keyword = keyword }, page: 1, pageSize: 100);

        Assert.Equal(withoutKeyword.Select(b => b.Id), withKeyword.Select(b => b.Id));
        Assert.Equal(
            await _bills.GetBillSummaryAsync(UserId, new BillFilter()),
            await _bills.GetBillSummaryAsync(UserId, new BillFilter { Keyword = keyword }));
        Assert.Equal(
            await _bills.GetBillCountAsync(UserId, new BillFilter()),
            await _bills.GetBillCountAsync(UserId, new BillFilter { Keyword = keyword }));
    }

    // ───────────────────────── 汇总与分页 ─────────────────────────

    [Fact]
    public async Task 带关键字时汇总与列表一致_且不受分页影响()
    {
        for (var i = 1; i <= 45; i++)
        {
            await AddAsync(i, "批量咖啡", wallTime: Noon.AddMinutes(i));
        }

        for (var i = 1; i <= 5; i++)
        {
            await AddAsync(1000m, "批量咖啡", BillType.Income, wallTime: Noon.AddMinutes(100 + i));
        }

        await AddAsync(7m, "午餐");

        var filter = new BillFilter { Keyword = "批量咖啡" };
        var firstPage = await _bills.GetBillsAsync(UserId, filter, page: 1, pageSize: 20);
        var summary = await _bills.GetBillSummaryAsync(UserId, filter);

        Assert.Equal(20, firstPage.Count);
        Assert.Equal(50, summary.Count);
        Assert.Equal(45 * 46 / 2m, summary.TotalExpense);
        Assert.Equal(5000m, summary.TotalIncome);
        Assert.Equal(50, await _bills.GetBillCountAsync(UserId, filter));

        // 逐页翻完正好是全部命中的账单，不重复不遗漏
        var ids = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            ids.AddRange((await _bills.GetBillsAsync(UserId, filter, page, pageSize: 20)).Select(b => b.Id));
        }

        Assert.Equal(50, ids.Distinct().Count());
    }

    [Fact]
    public async Task 没有命中的关键字_汇总为零而不是报错()
    {
        await AddAsync(10m, "咖啡");

        var summary = await _bills.GetBillSummaryAsync(UserId, new BillFilter { Keyword = "没有这个" });

        Assert.Equal(BillSummary.Empty, summary);
    }

    [Fact]
    public async Task 应用时区下的日期范围与关键字一起生效()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        // 当天第一刻（UTC 前一天 16:00）与最后一刻，都在 3/1 这一天里
        var first = await AddAsync(1m, "咖啡", wallTime: new DateTime(2026, 3, 1, 0, 0, 0));
        var last = await AddAsync(2m, "咖啡", wallTime: new DateTime(2026, 3, 1, 23, 59, 59, 500));
        await AddAsync(4m, "咖啡", wallTime: new DateTime(2026, 3, 2, 0, 0, 0));           // 次日零点：不属于 3/1
        await AddAsync(8m, "午餐", wallTime: new DateTime(2026, 3, 1, 12, 0, 0));          // 关键字不符

        var day = new DateTime(2026, 3, 1);
        var ids = await SearchAsync(new BillFilter { Keyword = "咖啡", StartDate = day, EndDate = TimeBoundaries.EndOfDay(day) });

        AssertSameBills([first, last], ids);
    }
}
