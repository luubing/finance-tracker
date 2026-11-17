using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 两台设备 + 一个云端账号的同步回归（云端是真实 SyncController，设备是真实 SyncService + 各自的 SQLite）。
///
/// 背景：本地库与云端库各自独立生成用户 Id，同一个账号在云端是 C，在每台设备上分别是 LA / LB，三者互不相同。
/// 云端返回的账单/账本/成员关系带的是云端 Id C，必须映射回本地用户，否则换设备或重装后，
/// 拉回来的"自己的数据"会挂在存根用户名下，按本地用户过滤的列表里一条都看不到。
/// </summary>
public sealed class TwoDeviceSyncTests : IAsyncLifetime
{
    private FakeCloud _cloud = null!;
    private SyncDevice _a = null!;
    private SyncDevice _b = null!;

    public async Task InitializeAsync()
    {
        _cloud = await FakeCloud.CreateAsync();
        _a = await _cloud.AddDeviceAsync();
        _b = await _cloud.AddDeviceAsync();

        // 前提：同一个账号，各设备的本地用户 Id 互不相同，也不同于云端 Id（与生产一致）
        Assert.NotEqual(_cloud.AccountId, _a.UserId);
        Assert.NotEqual(_cloud.AccountId, _b.UserId);
        Assert.NotEqual(_a.UserId, _b.UserId);
    }

    public async Task DisposeAsync()
    {
        await _b.DisposeAsync();
        await _a.DisposeAsync();
        await _cloud.DisposeAsync();
    }

    private static async Task SyncOkAsync(SyncDevice device)
    {
        var result = await device.Sync.SyncBillsAsync(device.UserId);
        Assert.True(result.Success, result.ErrorMessage);
    }

    // ───────────── 测试台自检：本机推送这条路径本来就是通的 ─────────────

    [Fact]
    public async Task 本机推送账单_云端账单归属云端账号()
    {
        var bill = await _a.AddBillAsync(12.5m);

        await SyncOkAsync(_a);

        await using var cloudContext = _cloud.CreateContext();
        var cloudBill = await cloudContext.Bills.IgnoreQueryFilters().SingleAsync(b => b.Id == bill.Id);
        Assert.Equal(_cloud.AccountId, cloudBill.UserId);
    }

    // ───────────── J2：云端用户 Id → 本地用户 Id ─────────────

    [Fact]
    public async Task 另一台设备拉取到自己的账单_归属本地用户且列表可见()
    {
        var bill = await _a.AddBillAsync(12.5m);
        await SyncOkAsync(_a);

        await SyncOkAsync(_b);

        var list = await _b.Bills.GetBillsAsync(_b.UserId);
        var pulled = Assert.Single(list);
        Assert.Equal(bill.Id, pulled.Id);
        Assert.Equal(_b.UserId, pulled.UserId);
        // 自己的云端 Id 不该被当成"别的用户"建出存根
        Assert.DoesNotContain(await _b.Db.Context.Users.IgnoreQueryFilters().ToListAsync(), u => u.Id == _cloud.AccountId);
    }

    [Fact]
    public async Task 推送账本_云端归属云端账号而不是客户端自报的本地用户()
    {
        var ledger = await _a.AddLedgerAsync();

        await SyncOkAsync(_a);

        await using var cloudContext = _cloud.CreateContext();
        var cloudLedger = await cloudContext.Ledgers.IgnoreQueryFilters().SingleOrDefaultAsync(l => l.Id == ledger.Id);
        Assert.NotNull(cloudLedger);
        Assert.Equal(_cloud.AccountId, cloudLedger!.UserId);
    }

    [Fact]
    public async Task 推送账本_不能借客户端自报的所有者把账本建到别人名下()
    {
        // 云端另有一个无关账号（受害者）；攻击者的 DTO 把 OwnerId 填成受害者的 Id
        await using var cloudContext = _cloud.CreateContext();
        var victim = new User { Id = Guid.NewGuid(), PhoneNumber = "13900000001" };
        cloudContext.Users.Add(victim);
        await cloudContext.SaveChangesAsync();

        var ledger = await _a.AddLedgerAsync("伪造所有者");
        var client = new DirectCloudSyncClient(_cloud, _cloud.AccountId);
        var forged = FinanceTracker.Core.Models.LedgerSyncDto.FromEntity(ledger);
        forged.OwnerId = victim.Id;

        await client.PushLedgersAsync(_a.UserId, new[] { forged });

        await using var verify = _cloud.CreateContext();
        var created = await verify.Ledgers.IgnoreQueryFilters().SingleOrDefaultAsync(l => l.Id == ledger.Id);
        Assert.NotNull(created);
        Assert.Equal(_cloud.AccountId, created!.UserId);
        Assert.NotEqual(victim.Id, created.UserId);
    }

    [Fact]
    public async Task 另一台设备拉取到自己的账本_归属本地用户()
    {
        var ledger = await _a.AddLedgerAsync();
        await SyncOkAsync(_a);

        await SyncOkAsync(_b);

        var ledgers = await _b.Ledgers.GetLedgersAsync(_b.UserId);
        Assert.Equal(ledger.Id, Assert.Single(ledgers).Id);
    }

    [Fact]
    public async Task 另一台设备能识别自己是账本所有者()
    {
        var ledger = await _a.AddLedgerAsync();
        await SyncOkAsync(_a);

        await SyncOkAsync(_b);

        Assert.Equal(LedgerMemberRole.Owner, await _b.Members.GetMyRoleAsync(ledger.Id, _b.UserId));
    }

    [Fact]
    public async Task 本地已有懒补的Owner行时_同步不冲突且只保留一条()
    {
        var ledger = await _a.AddLedgerAsync();
        await _a.Members.GetMembersAsync(ledger.Id, _a.UserId); // 触发本地懒补 Owner 成员行（随机 Id）

        await SyncOkAsync(_a);
        await SyncOkAsync(_a); // 重复同步也不能报唯一索引冲突

        var owners = await _a.Db.Context.LedgerMembers.IgnoreQueryFilters()
            .Where(m => m.LedgerId == ledger.Id && m.UserId == _a.UserId && !m.IsDeleted)
            .ToListAsync();
        Assert.Single(owners);
        Assert.Equal(LedgerMemberRole.Owner, owners[0].Role);
    }

    [Fact]
    public async Task 历史上被错误归属到云端用户Id的本地数据_同步时归还给本地用户()
    {
        // 构造旧版本拉取留下的现场：存根用户（= 云端 Id）名下挂着账单、账本、成员关系
        var context = _b.Db.Context;
        context.Users.Add(new User { Id = _cloud.AccountId, PhoneNumber = "stub_misowned0000" });
        await context.SaveChangesAsync();

        var ledgerId = Guid.NewGuid();
        context.Ledgers.Add(new Ledger { Id = ledgerId, UserId = _cloud.AccountId, Name = "旧账本", Icon = "mdi-book" });
        await context.SaveChangesAsync();

        var billId = Guid.NewGuid();
        context.Bills.Add(new Bill
        {
            Id = billId,
            UserId = _cloud.AccountId,
            Amount = 5m,
            Type = BillType.Expense,
            CategoryId = _b.Db.ExpenseCategory.Id,
            PaymentChannelId = _b.Db.Channel.Id,
            LedgerId = ledgerId,
            TransactionTime = DateTime.UtcNow,
            Source = BillSource.Manual,
            SyncStatus = SyncStatus.Synced
        });
        context.LedgerMembers.Add(new LedgerMember
        {
            LedgerId = ledgerId,
            UserId = _cloud.AccountId,
            Role = LedgerMemberRole.Owner,
            Status = LedgerMemberStatus.Active
        });
        await context.SaveChangesAsync();

        await SyncOkAsync(_b);

        Assert.Contains(await _b.Bills.GetBillsAsync(_b.UserId), b => b.Id == billId);
        Assert.Contains(await _b.Ledgers.GetLedgersAsync(_b.UserId), l => l.Id == ledgerId);
        Assert.Equal(LedgerMemberRole.Owner, await _b.Members.GetMyRoleAsync(ledgerId, _b.UserId));
    }

    // ───────────── J1：同步不改写"编辑时间" ─────────────

    [Fact]
    public async Task 同步后_本地已同步账单的更新时间与云端一致()
    {
        var bill = await _a.AddBillAsync();

        await SyncOkAsync(_a);

        await using var cloudContext = _cloud.CreateContext();
        var cloudBill = await cloudContext.Bills.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        var local = await _a.Db.Context.Bills.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        Assert.Equal(SyncStatus.Synced, local.SyncStatus);
        // 已同步的账单与云端同一版本：更新时间必须一致，而不是被本机的"同步时刻"覆盖
        Assert.Equal(cloudBill.UpdatedAt.Ticks, local.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 只标记同步状态时_不改变账单的编辑时间()
    {
        var bill = await _a.AddBillAsync();
        var editedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await _a.SetBillUpdatedAtAsync(bill.Id, editedAt);

        await _a.Sync.MarkBillsAsSyncedAsync(new List<Guid> { bill.Id });

        var synced = await _a.Db.Context.Bills.AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        Assert.Equal(SyncStatus.Synced, synced.SyncStatus);
        Assert.Equal(editedAt.Ticks, synced.UpdatedAt.Ticks);

        await _a.Sync.MarkBillsAsSyncFailedAsync(new List<Guid> { bill.Id });

        var failed = await _a.Db.Context.Bills.AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        Assert.Equal(SyncStatus.Failed, failed.SyncStatus);
        Assert.Equal(editedAt.Ticks, failed.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 拉取到的账单沿用云端的更新时间()
    {
        var bill = await _a.AddBillAsync();
        await SyncOkAsync(_a);
        var cloudUpdatedAt = DateTime.UtcNow.AddHours(-2);
        await SetCloudBillAsync(bill.Id, "云端版本", cloudUpdatedAt);

        await SyncOkAsync(_b);

        var pulled = await _b.Db.Context.Bills.AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        Assert.Equal(cloudUpdatedAt.Ticks, pulled.UpdatedAt.Ticks);
        Assert.Equal("云端版本", pulled.Note);
    }

    [Fact]
    public async Task 云端更新晚于旧版本但早于本机拉取时刻_仍被采纳()
    {
        // 这就是"本机用同步时刻盖章"造成的分叉：
        // 拉取时本机把账单盖成"现在"，之后云端里更晚发生的编辑（时间戳仍早于本机的拉取时刻，
        // 例如两端时钟有偏差，或另一台设备离线编辑后才同步）会被误判为"比本机旧"而被永远忽略。
        var bill = await _a.AddBillAsync();
        await SyncOkAsync(_a);
        await SetCloudBillAsync(bill.Id, "第一版", DateTime.UtcNow.AddHours(-2));
        await SyncOkAsync(_b);

        var secondEditAt = DateTime.UtcNow.AddHours(-1);
        await SetCloudBillAsync(bill.Id, "另一台设备改的", secondEditAt);
        await SyncOkAsync(_b);

        var local = await _b.Db.Context.Bills.AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        Assert.Equal("另一台设备改的", local.Note);
        // 采用云端版本时连同云端的更新时间一起采用：本地这一版的时间就是那一版发生的时间
        Assert.Equal(secondEditAt.Ticks, local.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 推送被云端更晚的版本覆盖后_沿用云端的更新时间()
    {
        // 本机有一笔很早离线编辑、尚未推送的账单，而云端已经有更晚的版本 → 推送时服务端裁决为"云端更新"，
        // 本机采用云端内容，更新时间也必须是云端那一版的时间（不是本机那次很早的编辑，更不是同步时刻）。
        var bill = await _a.AddBillAsync();
        await SyncOkAsync(_a);

        var cloudEditAt = DateTime.UtcNow.AddHours(-1);
        await SetCloudBillAsync(bill.Id, "云端更晚的版本", cloudEditAt);

        var staleEditAt = DateTime.UtcNow.AddHours(-5);
        await _a.Db.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Bills SET Note = {"本机很早的离线编辑"}, SyncStatus = {(int)SyncStatus.Pending}, UpdatedAt = {staleEditAt} WHERE Id = {bill.Id}");
        _a.Db.Context.ChangeTracker.Clear();

        // 拉取被屏蔽，单独观察"推送被云端覆盖"这条路径（否则紧随其后的整批拉取会把同一笔账单再合并一遍）
        var sync = new SyncService(
            _a.Db.Context,
            new AlwaysOnlineNetworkService(),
            new PullBillsSuppressedClient(new DirectCloudSyncClient(_cloud, _cloud.AccountId)),
            NullLogger<SyncService>.Instance);
        var result = await sync.SyncBillsAsync(_a.UserId);
        Assert.True(result.Success, result.ErrorMessage);

        var local = await _a.Db.Context.Bills.AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        Assert.Equal("云端更晚的版本", local.Note);
        Assert.Equal(SyncStatus.Synced, local.SyncStatus);
        Assert.Equal(cloudEditAt.Ticks, local.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 拉取到的自定义分类沿用云端的更新时间()
    {
        var category = await _a.Categories.CreateCategoryAsync(new Category
        {
            UserId = _a.UserId,
            Name = "咖啡",
            Icon = "mdi-coffee",
            Type = BillType.Expense
        });
        await SyncOkAsync(_a);
        var cloudUpdatedAt = DateTime.UtcNow.AddHours(-3);
        await using (var cloudContext = _cloud.CreateContext())
        {
            await cloudContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Categories SET UpdatedAt = {cloudUpdatedAt} WHERE Id = {category.Id}");
        }

        await SyncOkAsync(_b);

        var pulled = await _b.Db.Context.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == category.Id);
        Assert.Equal(cloudUpdatedAt.Ticks, pulled.UpdatedAt.Ticks);
    }

    // ───────────── J1：分类/支付渠道/账本/成员关系同样沿用云端的更新时间 ─────────────

    [Fact]
    public async Task 拉取到的支付渠道沿用云端的更新时间()
    {
        var channel = await AddChannelAsync(_a);
        await SyncOkAsync(_a);
        var cloudUpdatedAt = DateTime.UtcNow.AddHours(-3);
        await SetCloudRowAsync("PaymentChannels", channel.Id, "饭卡", cloudUpdatedAt);

        await SyncOkAsync(_b);

        var pulled = await _b.Db.Context.PaymentChannels.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == channel.Id);
        Assert.Equal(cloudUpdatedAt.Ticks, pulled.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 拉取到的账本沿用云端的更新时间()
    {
        var ledger = await _a.AddLedgerAsync();
        await SyncOkAsync(_a);
        var cloudUpdatedAt = DateTime.UtcNow.AddHours(-3);
        await SetCloudRowAsync("Ledgers", ledger.Id, "家庭", cloudUpdatedAt);

        await SyncOkAsync(_b);

        var pulled = await _b.Db.Context.Ledgers.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == ledger.Id);
        Assert.Equal(cloudUpdatedAt.Ticks, pulled.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 推送被云端更晚的分类版本覆盖后_沿用云端的更新时间()
    {
        var category = await AddCategoryAsync(_a);
        await SyncOkAsync(_a);
        await SetLocalRowAsync(_a, "Categories", category.Id, "咖啡", DateTime.UtcNow.AddHours(-5)); // 本机版本很旧
        var cloudEditAt = DateTime.UtcNow.AddHours(-1);
        await SetCloudRowAsync("Categories", category.Id, "咖啡馆", cloudEditAt);

        await SyncOkAsync(_a);

        var local = await _a.Db.Context.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == category.Id);
        Assert.Equal("咖啡馆", local.Name);
        Assert.Equal(cloudEditAt.Ticks, local.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 推送被云端更晚的支付渠道版本覆盖后_沿用云端的更新时间()
    {
        var channel = await AddChannelAsync(_a);
        await SyncOkAsync(_a);
        await SetLocalRowAsync(_a, "PaymentChannels", channel.Id, "饭卡", DateTime.UtcNow.AddHours(-5));
        var cloudEditAt = DateTime.UtcNow.AddHours(-1);
        await SetCloudRowAsync("PaymentChannels", channel.Id, "校园卡", cloudEditAt);

        await SyncOkAsync(_a);

        var local = await _a.Db.Context.PaymentChannels.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == channel.Id);
        Assert.Equal("校园卡", local.Name);
        Assert.Equal(cloudEditAt.Ticks, local.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 推送被云端更晚的账本版本覆盖后_沿用云端的更新时间()
    {
        var ledger = await _a.AddLedgerAsync();
        await SyncOkAsync(_a);
        await SetLocalRowAsync(_a, "Ledgers", ledger.Id, "家庭", DateTime.UtcNow.AddHours(-5));
        var cloudEditAt = DateTime.UtcNow.AddHours(-1);
        await SetCloudRowAsync("Ledgers", ledger.Id, "我们的家", cloudEditAt);

        await SyncOkAsync(_a);

        var local = await _a.Db.Context.Ledgers.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == ledger.Id);
        Assert.Equal("我们的家", local.Name);
        Assert.Equal(cloudEditAt.Ticks, local.UpdatedAt.Ticks);
    }

    [Fact]
    public async Task 拉取合并已有成员关系_沿用云端的更新时间()
    {
        var ledger = await _a.AddLedgerAsync();
        await SyncOkAsync(_a);
        await SyncOkAsync(_b); // B 拉到云端的成员行（云端在被拉取时惰性补出 Owner 行）

        var cloudEditAt = DateTime.UtcNow.AddHours(1); // 比 B 本地这一版新
        await using (var cloudContext = _cloud.CreateContext())
        {
            await cloudContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE LedgerMembers SET Role = {(int)LedgerMemberRole.Editor}, UpdatedAt = {cloudEditAt} WHERE LedgerId = {ledger.Id}");
        }

        await SyncOkAsync(_b);

        var member = await _b.Db.Context.LedgerMembers.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.LedgerId == ledger.Id);
        Assert.Equal(LedgerMemberRole.Editor, member.Role);
        Assert.Equal(cloudEditAt.Ticks, member.UpdatedAt.Ticks);
    }

    private Task<Category> AddCategoryAsync(SyncDevice device) =>
        device.Categories.CreateCategoryAsync(new Category
        {
            UserId = device.UserId,
            Name = "咖啡",
            Icon = "mdi-coffee",
            Type = BillType.Expense
        });

    private static async Task<PaymentChannel> AddChannelAsync(SyncDevice device)
    {
        var channel = new PaymentChannel { UserId = device.UserId, Name = "饭卡", Icon = "mdi-card" };
        device.Db.Context.PaymentChannels.Add(channel);
        await device.Db.Context.SaveChangesAsync();
        return channel;
    }

    // table 是本文件里写死的表名常量（不是外部输入），其余值都走参数化；表名无法参数化，所以抑制 EF1002
#pragma warning disable EF1002
    /// <summary>直接改云端某张表里一行的名称与更新时间（绕过自动盖章，模拟"另一台设备在那个时刻的编辑"）</summary>
    private async Task SetCloudRowAsync(string table, Guid id, string name, DateTime updatedAtUtc)
    {
        await using var cloudContext = _cloud.CreateContext();
        await cloudContext.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET Name = {{0}}, UpdatedAt = {{1}} WHERE Id = {{2}}", name, updatedAtUtc, id);
    }

    /// <summary>直接改设备本地某张表里一行的名称与更新时间（同上，用来构造"本机这一版很旧"）</summary>
    private static async Task SetLocalRowAsync(SyncDevice device, string table, Guid id, string name, DateTime updatedAtUtc)
    {
        await device.Db.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET Name = {{0}}, UpdatedAt = {{1}} WHERE Id = {{2}}", name, updatedAtUtc, id);
        device.Db.Context.ChangeTracker.Clear();
    }

#pragma warning restore EF1002

    /// <summary>直接改云端账单的备注与更新时间（绕过自动盖章，模拟"另一台设备在那个时刻的编辑"）</summary>
    private async Task SetCloudBillAsync(Guid billId, string note, DateTime updatedAtUtc)
    {
        await using var cloudContext = _cloud.CreateContext();
        await cloudContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Bills SET Note = {note}, UpdatedAt = {updatedAtUtc} WHERE Id = {billId}");
    }
}
