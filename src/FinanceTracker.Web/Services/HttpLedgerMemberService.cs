using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 账本成员服务客户端实现：
/// - 管理/邀请等操作走云端 API（云端数据库为真相源）
/// - 权限判定（GetMyRole/CanView/EnsureCanWrite）与共享账本列表读本地 SQLite 缓存（离线可用）
/// - 本地缓存的合并统一交给 <see cref="ISyncService"/>（与整体同步共用一套规则：云端用户 Id 映射回本地用户、
///   补建用户/账本存根避免外键违例），这里不再各自维护一份合并逻辑
/// - 读路径（成员列表 / 待确认邀请 / 共享账本）会顺带联网刷新缓存，但云端不可用时不能拖住页面：
///   刷新只给很短的期限，失败后短时间内直接读本地缓存，见 <see cref="CloudAvailability"/>
/// </summary>
public class HttpLedgerMemberService : ILedgerMemberService
{
    private readonly HttpService _http;
    private readonly IApplicationDbContext _context;
    private readonly ISyncService _syncService;
    private readonly CloudAvailability _availability;
    private readonly ILogger<HttpLedgerMemberService> _logger;

    public HttpLedgerMemberService(
        HttpService http,
        IApplicationDbContext context,
        ISyncService syncService,
        CloudAvailability availability,
        ILogger<HttpLedgerMemberService> logger)
    {
        _http = http;
        _context = context;
        _syncService = syncService;
        _availability = availability;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<LedgerMember>> GetMembersAsync(Guid ledgerId, Guid operatorUserId)
    {
        await RefreshCacheAsync(operatorUserId);

        // 历史账本懒补 Owner 成员行（幂等）：云端不可用/成员关系未同步时，
        // 本地按账本归属补建 Owner 行，保证成员列表展示与"邀请"入口可用
        await EnsureOwnerMemberRowAsync(ledgerId);

        return await _context.LedgerMembers
            .Where(m => m.LedgerId == ledgerId && m.Status == LedgerMemberStatus.Active && !m.IsDeleted)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.CreatedAt)
            .Include(m => m.User)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<LedgerMember>> GetPendingInvitationsAsync(Guid userId)
    {
        await RefreshCacheAsync(userId);

        return await _context.LedgerMembers
            .Where(m => m.UserId == userId && m.Status == LedgerMemberStatus.Pending && !m.IsDeleted)
            .OrderBy(m => m.CreatedAt)
            .Include(m => m.Ledger)
            .Include(m => m.Ledger.User)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<LedgerMember> InviteAsync(Guid ledgerId, Guid operatorUserId, string phoneNumber, LedgerMemberRole role)
    {
        var response = await _http.PostAsync<LedgerMemberSyncDto>(
            $"api/ledgers/{ledgerId}/members",
            new LedgerMemberRequest { PhoneNumber = phoneNumber, Role = role });

        // 云端接口调用成功，云端就是可达的：清除退避（邀请成功后不再整体刷新缓存，只合并返回的这一条）
        _availability.ReportSuccess();

        if (response == null)
        {
            throw new InvalidOperationException("云端没有返回邀请结果，请稍后重试");
        }

        // 接口返回的这一条成员关系直接合并进本地缓存（被邀请人处于待确认，不在"我的成员关系"拉取范围内）
        await _syncService.MergeLedgerMembersAsync(operatorUserId, new[] { response });
        return (await _context.LedgerMembers.FirstAsync(m => m.Id == response.Id));
    }

    /// <inheritdoc />
    public async Task RespondAsync(Guid memberId, Guid userId, bool accept)
    {
        await _http.PostAsync<object>(
            $"api/invitations/{memberId}/respond",
            new LedgerInvitationRespondRequest { Accept = accept });

        await RefreshCacheAsync(userId, afterCloudAction: true);
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(Guid ledgerId, Guid memberId, Guid operatorUserId)
    {
        await _http.DeleteAsync<object>($"api/ledgers/{ledgerId}/members/{memberId}");

        // 云端对「别人被移除」的拉取不再下发那条成员行（拉取只给我的成员行 + 账本里其他人的生效成员行，已删除的不在其中），
        // 光靠随后的刷新，本地缓存里被移除的成员会一直是生效状态——成员页、成员数、共享状态都不会变。
        // 云端已确认移除，所以本地直接把这一行标记为已删除
        await MarkMemberRemovedLocallyAsync(ledgerId, memberId);

        await RefreshCacheAsync(operatorUserId, afterCloudAction: true);
    }

    /// <summary>
    /// 把本地缓存里的成员行标记为已删除（软删除）。放在同步时间戳作用域内保存：这是在对齐云端已经发生的变化，不算一次编辑，
    /// 不能把更新时间盖成本机当前时间——否则对方被重新邀请时，云端更晚版本的成员行会因为本机时钟偏快而不被采纳
    /// </summary>
    private async Task MarkMemberRemovedLocallyAsync(Guid ledgerId, Guid memberId)
    {
        using var timestampScope = SyncTimestampScope.Begin();

        var member = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.LedgerId == ledgerId && !m.IsDeleted);
        if (member == null)
        {
            return;
        }

        member.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task ChangeRoleAsync(Guid ledgerId, Guid memberId, LedgerMemberRole newRole, Guid operatorUserId)
    {
        await _http.PutAsync<object>(
            $"api/ledgers/{ledgerId}/members/{memberId}/role",
            new LedgerMemberRequest { Role = newRole });

        await RefreshCacheAsync(operatorUserId, afterCloudAction: true);
    }

    /// <inheritdoc />
    public async Task TransferOwnershipAsync(Guid ledgerId, Guid operatorUserId, Guid toUserId)
    {
        await _http.PostAsync<object>(
            $"api/ledgers/{ledgerId}/transfer-ownership",
            new LedgerTransferOwnershipRequest { ToUserId = toUserId });

        await RefreshCacheAsync(operatorUserId, afterCloudAction: true);
    }

    /// <inheritdoc />
    public async Task ExitAsync(Guid ledgerId, Guid userId)
    {
        await _http.PostAsync<object>($"api/ledgers/{ledgerId}/exit", null);
        await RefreshCacheAsync(userId, afterCloudAction: true);
    }

    /// <inheritdoc />
    public Task<LedgerMemberRole?> GetMyRoleAsync(Guid ledgerId, Guid userId)
    {
        return GetRoleCoreAsync(ledgerId, userId);
    }

    /// <inheritdoc />
    public async Task<bool> CanViewLedgerAsync(Guid ledgerId, Guid userId)
    {
        var ledger = await _context.Ledgers
            .FirstOrDefaultAsync(l => l.Id == ledgerId && !l.IsDeleted);

        if (ledger == null)
        {
            return false;
        }

        if (ledger.UserId == userId)
        {
            return true;
        }

        return await GetRoleCoreAsync(ledgerId, userId) != null;
    }

    /// <inheritdoc />
    public async Task EnsureCanWriteAsync(Guid ledgerId, Guid userId)
    {
        var ledger = await _context.Ledgers
            .FirstOrDefaultAsync(l => l.Id == ledgerId && !l.IsDeleted);

        if (ledger == null)
        {
            throw new ArgumentException("账本不存在");
        }

        if (ledger.UserId == userId)
        {
            return;
        }

        var role = await GetRoleCoreAsync(ledgerId, userId);

        if (role == null)
        {
            throw new ForbiddenAccessException("无权向该账本记账");
        }

        if (role == LedgerMemberRole.Viewer)
        {
            // 与服务端 LedgerMemberService 一致：权限拒绝统一为 ForbiddenAccessException
            throw new ForbiddenAccessException("只读成员不能在共享账本中记账");
        }
    }

    /// <inheritdoc />
    public async Task<List<Ledger>> GetSharedLedgersAsync(Guid userId)
    {
        await RefreshCacheAsync(userId);

        return await LedgerMemberQueries.GetSharedLedgersAsync(_context, userId);
    }

    /// <inheritdoc />
    public Task<Dictionary<Guid, int>> GetActiveMemberCountsAsync(Guid userId, IReadOnlyCollection<Guid> ledgerIds)
    {
        // 只读本地缓存、不联网：调用方通常刚读过 GetSharedLedgersAsync（它已刷新过一次缓存）
        return LedgerMemberQueries.CountActiveMembersAsync(_context, userId, ledgerIds);
    }

    /// <inheritdoc />
    public Task<HashSet<Guid>> GetReadOnlyLedgerIdsAsync(Guid userId, IReadOnlyCollection<Guid> ledgerIds)
    {
        // 只读本地缓存、不联网：与 BillService 拒绝只读成员改账单用同一条查询，界面与服务层口径一致
        return LedgerMemberQueries.GetReadOnlyLedgerIdsAsync(_context, userId, ledgerIds);
    }

    /// <summary>
    /// 查询用户在账本中的生效角色（本地缓存）
    /// </summary>
    private async Task<LedgerMemberRole?> GetRoleCoreAsync(Guid ledgerId, Guid userId)
    {
        var member = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.LedgerId == ledgerId && m.UserId == userId &&
                m.Status == LedgerMemberStatus.Active && !m.IsDeleted);

        return member?.Role;
    }

    /// <summary>
    /// 从云端拉取成员关系并合并到本地缓存（离线时静默使用本地缓存，不影响页面展示）。
    /// 合并交给 SyncService：云端下发的用户 Id 要映射回本地用户 <paramref name="userId"/>，
    /// 否则按本地用户 Id 查询的"待确认邀请 / 我的共享账本"永远查不到云端下发的成员行。
    /// </summary>
    /// <param name="userId">本地当前用户 ID</param>
    /// <param name="afterCloudAction">
    /// true：用户主动发起的云端操作刚成功（响应邀请 / 移除成员…），云端明确可达，且刷新结果关系到
    /// 这次操作有没有反映到本地——不看退避、不设期限，并且立刻清除退避。
    /// false（默认）：读路径（页面加载顺带刷新）——处于退避期直接读本地缓存；否则只给
    /// <see cref="CloudAvailability.RefreshTimeout"/> 的期限，超时真正取消请求并进入退避。
    /// </param>
    private async Task RefreshCacheAsync(Guid userId, bool afterCloudAction = false)
    {
        if (afterCloudAction)
        {
            // 云端操作刚成功，云端就是可达的：先清除退避，不管后面的刷新结果如何。
            // 否则刷新时本地合并出错（走到最后的 catch、不会再上报成功），此前的退避会一直挂着，
            // 之后的读路径持续跳过联网，本地长期看不到刚在云端生效的成员关系
            _availability.ReportSuccess();
        }
        else if (_availability.IsInBackoff)
        {
            _logger.LogDebug("云端近期不可用，跳过联网刷新，直接使用本地账本成员缓存");
            return;
        }

        // 超时是"真取消"：令牌一路传到 HttpClient（含换取云端令牌的那次请求），到期请求被中止，不遗留后台任务
        using var timeout = afterCloudAction ? null : _availability.CreateRefreshTimeout();

        try
        {
            await _syncService.SyncLedgerMembersAsync(userId, timeout?.Token ?? CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException ||
            (ex is OperationCanceledException && timeout?.IsCancellationRequested == true))
        {
            // 云端确实不可用：离线 / 连接被拒绝 / 超过期限。预期内的情况，使用本地缓存即可，不记警告，
            // 并让接下来一小段时间内的读路径直接读本地缓存
            _availability.ReportFailure();
            _logger.LogDebug(ex, "云端不可用或刷新超时，使用本地账本成员缓存");
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // 被别的原因中断，与云端是否可用无关，所以不计入退避（否则一个用户关掉页面就会让所有人跳过联网）：
            // - 页面（电路）在刷新途中被销毁，HttpClient / DbContext 随作用域一起释放了
            // - HttpClient 自己的超时（只在操作后的刷新这类不设期限的路径上才会走到）
            _logger.LogDebug(ex, "刷新被中断，使用本地账本成员缓存");
        }
        catch (Exception ex)
        {
            // 不阻断页面展示，但必须留痕：此前这里一律静默吞掉，本地写库失败（如唯一索引冲突）也完全看不到
            _logger.LogWarning(ex, "刷新账本成员关系缓存失败，继续使用本地缓存");
        }
    }

    /// <inheritdoc />
    public async Task EnsureOwnerMemberRowAsync(Guid ledgerId)
    {
        var ledger = await _context.Ledgers
            .FirstOrDefaultAsync(l => l.Id == ledgerId && !l.IsDeleted);

        if (ledger == null)
        {
            return;
        }

        var ownerRow = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.LedgerId == ledgerId && m.UserId == ledger.UserId);

        if (ownerRow == null)
        {
            _context.LedgerMembers.Add(new LedgerMember
            {
                LedgerId = ledgerId,
                UserId = ledger.UserId,
                Role = LedgerMemberRole.Owner,
                Status = LedgerMemberStatus.Active
            });
            await _context.SaveChangesAsync();
        }
    }
}
