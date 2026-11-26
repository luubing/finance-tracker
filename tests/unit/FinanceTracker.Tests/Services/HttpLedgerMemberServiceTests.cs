using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 共享账本在客户端的这条链路：页面 → HttpLedgerMemberService → 云端成员关系 → 本地缓存。
///
/// 云端是真实的 SyncController / LedgerMemberService + SQLite，客户端是真实的 HttpLedgerMemberService + SyncService + 本地 SQLite，
/// 只把"HTTP"换成直接调用云端服务的替身。要点：
/// 1. 本地与云端的用户 Id 各自生成、互不相同，云端下发的成员行带的是云端 Id，必须映射回本地用户，
///    否则按本地用户 Id 查询的"待确认邀请 / 我的共享账本"永远是空的；
/// 2. 成员关系引用的账本本地可能还没有（刚收到别人的邀请），要能补建存根；
/// 3. 云端成员的手机号可能与本机另一个账号相同（手机号有唯一索引），不能因此合并失败。
/// </summary>
public sealed class HttpLedgerMemberServiceTests : IAsyncLifetime
{
    private const string OwnerPhone = "13800138000";
    private const string InviteePhone = "13900139000";

    private FakeCloud _cloud = null!;
    private Guid _inviteeCloudId;

    public async Task InitializeAsync()
    {
        _cloud = await FakeCloud.CreateAsync(OwnerPhone);

        await using var context = _cloud.CreateContext();
        var invitee = new User { PhoneNumber = InviteePhone };
        context.Users.Add(invitee);
        await context.SaveChangesAsync();
        _inviteeCloudId = invitee.Id;
    }

    public async Task DisposeAsync() => await _cloud.DisposeAsync();

    // ───────────── 受邀人 ─────────────

    [Fact]
    public async Task 受邀人_待确认邀请挂在本地用户名下且能看到账本()
    {
        var ledgerId = await CreateCloudLedgerAsync("家庭");
        var memberId = await InviteInCloudAsync(ledgerId, LedgerMemberRole.Editor);
        await using var invitee = await _cloud.AddDeviceAsync(_inviteeCloudId, InviteePhone);
        var service = CreateService(invitee, _inviteeCloudId);

        var invitations = await service.GetPendingInvitationsAsync(invitee.UserId);

        var invitation = Assert.Single(invitations);
        Assert.Equal(memberId, invitation.Id);
        Assert.Equal(invitee.UserId, invitation.UserId);              // 本地用户，而不是云端用户 Id
        Assert.Equal(LedgerMemberStatus.Pending, invitation.Status);
        Assert.Equal("家庭", invitation.Ledger.Name);                  // 本地原先没有这个账本：补建了存根
        Assert.Equal(_cloud.AccountId, invitation.Ledger.UserId);     // 账本所有者是云端的账号（他人）
    }

    [Fact]
    public async Task 受邀人接受邀请后_共享账本出现在列表且角色可判定()
    {
        var ledgerId = await CreateCloudLedgerAsync("家庭");
        var memberId = await InviteInCloudAsync(ledgerId, LedgerMemberRole.Editor);
        await using var invitee = await _cloud.AddDeviceAsync(_inviteeCloudId, InviteePhone);
        var service = CreateService(invitee, _inviteeCloudId);
        Assert.Single(await service.GetPendingInvitationsAsync(invitee.UserId));

        await service.RespondAsync(memberId, invitee.UserId, accept: true);

        var shared = Assert.Single(await service.GetSharedLedgersAsync(invitee.UserId));
        Assert.Equal(ledgerId, shared.Id);
        Assert.Equal(LedgerMemberRole.Editor, await service.GetMyRoleAsync(ledgerId, invitee.UserId));
        Assert.True(await service.CanViewLedgerAsync(ledgerId, invitee.UserId));
        Assert.Empty(await service.GetPendingInvitationsAsync(invitee.UserId));
    }

    [Fact]
    public async Task 共享账本列表带着所有者_账本页据此展示所有者手机号()
    {
        var ledgerId = await CreateCloudLedgerAsync("家庭");
        var memberId = await InviteInCloudAsync(ledgerId, LedgerMemberRole.Viewer);
        await using var invitee = await _cloud.AddDeviceAsync(_inviteeCloudId, InviteePhone);
        var service = CreateService(invitee, _inviteeCloudId);
        await service.GetPendingInvitationsAsync(invitee.UserId);
        await service.RespondAsync(memberId, invitee.UserId, accept: true);

        var shared = Assert.Single(await service.GetSharedLedgersAsync(invitee.UserId));

        Assert.NotNull(shared.User);                                  // 账本页要用所有者展示「所有者 138****0099」
        Assert.Equal(_cloud.AccountId, shared.User.Id);
        Assert.Equal(OwnerPhone, shared.User.PhoneNumber);
    }

    [Fact]
    public async Task 成员数只统计已生效成员_待确认邀请时不暴露账本的成员数()
    {
        var ledgerId = await CreateCloudLedgerAsync("家庭");
        var memberId = await InviteInCloudAsync(ledgerId, LedgerMemberRole.Editor);
        await using var invitee = await _cloud.AddDeviceAsync(_inviteeCloudId, InviteePhone);
        var service = CreateService(invitee, _inviteeCloudId);
        Assert.Single(await service.GetPendingInvitationsAsync(invitee.UserId));

        // 还只是待确认邀请：我不是这个账本的生效成员，不能借此知道它有几个成员
        var beforeAccept = await service.GetActiveMemberCountsAsync(invitee.UserId, new[] { ledgerId });
        Assert.Empty(beforeAccept);

        await service.RespondAsync(memberId, invitee.UserId, accept: true);

        var other = Guid.NewGuid();
        var afterAccept = await service.GetActiveMemberCountsAsync(invitee.UserId, new[] { ledgerId, other });
        Assert.Equal(2, afterAccept[ledgerId]);                       // 所有者 + 我
        Assert.False(afterAccept.ContainsKey(other));                 // 不是我的账本 / 没有成员行：不出现
        Assert.Empty(await service.GetActiveMemberCountsAsync(invitee.UserId, Array.Empty<Guid>()));
    }

    [Theory]
    [InlineData(LedgerMemberRole.Viewer, true)]
    [InlineData(LedgerMemberRole.Editor, false)]
    public async Task 只读账本只认已生效的只读成员_待确认邀请不算_角色在本地缓存里判定(LedgerMemberRole role, bool expectedReadOnly)
    {
        var ledgerId = await CreateCloudLedgerAsync("家庭");
        var memberId = await InviteInCloudAsync(ledgerId, role);
        await using var invitee = await _cloud.AddDeviceAsync(_inviteeCloudId, InviteePhone);
        var service = CreateService(invitee, _inviteeCloudId);
        Assert.Single(await service.GetPendingInvitationsAsync(invitee.UserId));

        // 还是待确认邀请：不是生效成员，不算只读成员（与服务层拒绝只读成员改账单是同一条规则）
        Assert.Empty(await service.GetReadOnlyLedgerIdsAsync(invitee.UserId, new[] { ledgerId }));

        await service.RespondAsync(memberId, invitee.UserId, accept: true);

        var readOnly = await service.GetReadOnlyLedgerIdsAsync(invitee.UserId, new[] { ledgerId, Guid.NewGuid() });
        Assert.Equal(expectedReadOnly, readOnly.Contains(ledgerId));
        Assert.Equal(expectedReadOnly ? 1 : 0, readOnly.Count);
        Assert.Empty(await service.GetReadOnlyLedgerIdsAsync(invitee.UserId, Array.Empty<Guid>()));
    }

    [Fact]
    public async Task 同机另一个账号占用了账本所有者的手机号_合并成员关系不失败()
    {
        var ledgerId = await CreateCloudLedgerAsync("家庭");
        await InviteInCloudAsync(ledgerId, LedgerMemberRole.Editor);
        await using var invitee = await _cloud.AddDeviceAsync(_inviteeCloudId, InviteePhone);

        // 同一台设备上还有另一个本地账号，手机号恰好等于云端账本所有者的手机号（手机号有唯一索引）
        invitee.Db.Context.Users.Add(new User { PhoneNumber = OwnerPhone });
        await invitee.Db.Context.SaveChangesAsync();

        await invitee.Sync.SyncLedgerMembersAsync(invitee.UserId);
        // 再同步一次也不能失败：占位存根不能去抢已被占用的真实手机号
        await invitee.Sync.SyncLedgerMembersAsync(invitee.UserId);

        var users = await invitee.Db.Context.Users.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var ownerStub = Assert.Single(users, u => u.Id == _cloud.AccountId);
        Assert.StartsWith("stub_", ownerStub.PhoneNumber);           // 真实手机号被占用：退回占位手机号
        Assert.Single(users, u => u.PhoneNumber == OwnerPhone);       // 另一个账号的手机号没被动过
        var members = await invitee.Db.Context.LedgerMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.LedgerId == ledgerId).ToListAsync();
        Assert.Equal(2, members.Count);                               // 所有者行 + 我的待确认邀请
    }

    // ───────────── 所有者 ─────────────

    [Fact]
    public async Task 所有者邀请成员后_本地缓存里有被邀请人的待确认成员行()
    {
        await using var owner = await _cloud.AddDeviceAsync();
        var ledger = await owner.AddLedgerAsync("家庭");
        var syncResult = await owner.Sync.SyncBillsAsync(owner.UserId);   // 账本推到云端
        Assert.True(syncResult.Success, syncResult.ErrorMessage);
        var service = CreateService(owner, _cloud.AccountId);

        var member = await service.InviteAsync(ledger.Id, owner.UserId, InviteePhone, LedgerMemberRole.Viewer);

        Assert.Equal(_inviteeCloudId, member.UserId);                 // 被邀请的是另一个人，保持云端 Id
        Assert.Equal(LedgerMemberStatus.Pending, member.Status);
        Assert.Equal(LedgerMemberRole.Viewer, member.Role);
        var stub = await owner.Db.Context.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == _inviteeCloudId);
        Assert.Equal(InviteePhone, stub.PhoneNumber);
    }

    [Fact]
    public async Task 邀请成功说明云端可达_清除此前的退避()
    {
        await using var owner = await _cloud.AddDeviceAsync();
        var ledger = await owner.AddLedgerAsync("家庭");
        var syncResult = await owner.Sync.SyncBillsAsync(owner.UserId);
        Assert.True(syncResult.Success, syncResult.ErrorMessage);
        var availability = new CloudAvailability();
        availability.ReportFailure();                                 // 此前读路径刷新失败，正处于退避期
        Assert.True(availability.IsInBackoff);
        var service = CreateService(owner, _cloud.AccountId, availability);

        await service.InviteAsync(ledger.Id, owner.UserId, InviteePhone, LedgerMemberRole.Viewer);

        Assert.False(availability.IsInBackoff);
    }

    [Fact]
    public async Task 所有者移除成员后_本地缓存里该成员行同步标记为删除()
    {
        await using var owner = await _cloud.AddDeviceAsync();
        var ledger = await owner.AddLedgerAsync("家庭");
        var syncResult = await owner.Sync.SyncBillsAsync(owner.UserId);
        Assert.True(syncResult.Success, syncResult.ErrorMessage);
        var service = CreateService(owner, _cloud.AccountId);
        var invited = await service.InviteAsync(ledger.Id, owner.UserId, InviteePhone, LedgerMemberRole.Editor);
        await RespondInCloudAsync(invited.Id, accept: true);               // 被邀请人在自己的设备上接受
        var beforeRemove = await service.GetMembersAsync(ledger.Id, owner.UserId);
        Assert.Equal(2, beforeRemove.Count);                                // 所有者 + 新成员
        var updatedAtBefore = beforeRemove.Single(m => m.Id == invited.Id).UpdatedAt;

        await service.RemoveMemberAsync(ledger.Id, invited.Id, owner.UserId);

        // 云端对「别人被移除」的拉取不再下发那条成员行：缓存必须由本地自己对齐，否则被移除的成员会一直留在列表里
        var members = await service.GetMembersAsync(ledger.Id, owner.UserId);
        Assert.Equal(LedgerMemberRole.Owner, Assert.Single(members).Role);
        var row = await owner.Db.Context.LedgerMembers.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == invited.Id);
        Assert.True(row.IsDeleted);                                        // 软删除，不是物理删除
        Assert.Equal(updatedAtBefore, row.UpdatedAt);                      // 对齐云端不算编辑：更新时间不被盖成本机时钟

        var counts = await service.GetActiveMemberCountsAsync(owner.UserId, new[] { ledger.Id });
        Assert.Equal(1, counts[ledger.Id]);                                // 账本页的成员数 / 「共享」标记随之更新
    }

    [Fact]
    public async Task 被移除的成员可以被重新邀请_云端更晚的成员行不会被本地的删除标记压住()
    {
        await using var owner = await _cloud.AddDeviceAsync();
        var ledger = await owner.AddLedgerAsync("家庭");
        var syncResult = await owner.Sync.SyncBillsAsync(owner.UserId);
        Assert.True(syncResult.Success, syncResult.ErrorMessage);
        var service = CreateService(owner, _cloud.AccountId);
        var invited = await service.InviteAsync(ledger.Id, owner.UserId, InviteePhone, LedgerMemberRole.Editor);
        await RespondInCloudAsync(invited.Id, accept: true);
        await service.GetMembersAsync(ledger.Id, owner.UserId);
        await service.RemoveMemberAsync(ledger.Id, invited.Id, owner.UserId);

        var reinvited = await service.InviteAsync(ledger.Id, owner.UserId, InviteePhone, LedgerMemberRole.Viewer);

        Assert.Equal(invited.Id, reinvited.Id);                            // 复用原成员行（ADR 0004）
        Assert.False(reinvited.IsDeleted);
        Assert.Equal(LedgerMemberStatus.Pending, reinvited.Status);
        Assert.Equal(LedgerMemberRole.Viewer, reinvited.Role);
    }

    // ───────────── 辅助 ─────────────

    /// <summary>被邀请人在云端响应邀请（模拟对方在自己的设备上接受 / 拒绝）</summary>
    private async Task RespondInCloudAsync(Guid memberId, bool accept)
    {
        await using var context = _cloud.CreateContext();
        await new LedgerMemberService(context).RespondAsync(memberId, _inviteeCloudId, accept);
    }

    /// <summary>直接在云端库里建一个归云端账号所有的账本</summary>
    private async Task<Guid> CreateCloudLedgerAsync(string name)
    {
        await using var context = _cloud.CreateContext();
        var ledger = new Ledger { UserId = _cloud.AccountId, Name = name, Icon = "mdi-home" };
        context.Ledgers.Add(ledger);
        await context.SaveChangesAsync();
        return ledger.Id;
    }

    /// <summary>以云端账本所有者的身份，通过真实的云端成员服务邀请受邀人，返回成员关系 Id</summary>
    private async Task<Guid> InviteInCloudAsync(Guid ledgerId, LedgerMemberRole role)
    {
        await using var context = _cloud.CreateContext();
        var member = await new LedgerMemberService(context).InviteAsync(ledgerId, _cloud.AccountId, InviteePhone, role);
        return member.Id;
    }

    private HttpLedgerMemberService CreateService(
        SyncDevice device, Guid cloudAccountId, CloudAvailability? availability = null)
    {
        var httpClient = new HttpClient(new CloudMemberApiHandler(_cloud, cloudAccountId))
        {
            BaseAddress = new Uri("http://cloud.test/")
        };
        var http = new HttpService(httpClient, new AuthenticationService(new FakeLocalStorageJsRuntime()));
        return new HttpLedgerMemberService(
            http, device.Db.Context, device.Sync, availability ?? new CloudAvailability(),
            NullLogger<HttpLedgerMemberService>.Instance);
    }

    /// <summary>
    /// 替身云端 HTTP：把成员相关的写接口转给真实的云端 LedgerMemberService（以指定云端账号的身份）。
    /// 只实现用到的三个接口：邀请成员、响应邀请、移除成员。
    /// </summary>
    private sealed class CloudMemberApiHandler(FakeCloud cloud, Guid cloudAccountId) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            await using var context = cloud.CreateContext();
            var service = new LedgerMemberService(context);

            var invite = Regex.Match(path, @"/api/ledgers/(?<ledger>[0-9a-fA-F-]{36})/members$");
            if (request.Method == HttpMethod.Post && invite.Success)
            {
                var payload = JsonSerializer.Deserialize<LedgerMemberRequest>(body, Json)!;
                var member = await service.InviteAsync(
                    Guid.Parse(invite.Groups["ledger"].Value), cloudAccountId, payload.PhoneNumber!, payload.Role);

                var loaded = await context.LedgerMembers
                    .Include(m => m.User).Include(m => m.Ledger)
                    .SingleAsync(m => m.Id == member.Id, cancellationToken);
                return Ok(LedgerMemberSyncDto.FromEntity(loaded));
            }

            var remove = Regex.Match(path, @"/api/ledgers/(?<ledger>[0-9a-fA-F-]{36})/members/(?<member>[0-9a-fA-F-]{36})$");
            if (request.Method == HttpMethod.Delete && remove.Success)
            {
                await service.RemoveMemberAsync(
                    Guid.Parse(remove.Groups["ledger"].Value), Guid.Parse(remove.Groups["member"].Value), cloudAccountId);
                return Ok(new { });
            }

            var respond = Regex.Match(path, @"/api/invitations/(?<member>[0-9a-fA-F-]{36})/respond$");
            if (request.Method == HttpMethod.Post && respond.Success)
            {
                var payload = JsonSerializer.Deserialize<LedgerInvitationRespondRequest>(body, Json)!;
                await service.RespondAsync(Guid.Parse(respond.Groups["member"].Value), cloudAccountId, payload.Accept);
                return Ok(new { });
            }

            throw new NotSupportedException($"替身云端不支持 {request.Method} {path}");
        }

        private static HttpResponseMessage Ok(object payload) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
            };
    }
}
