using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 账本成员服务客户端实现：
/// - 管理/邀请等操作走云端 API（云端数据库为真相源）
/// - 权限判定（GetMyRole/CanView/EnsureCanWrite）与共享账本列表读本地 SQLite 缓存（离线可用）
/// - 本地缓存通过 sync/ledgermembers/pull 合并维护，同时补建用户/账本存根避免外键违例
/// </summary>
public class HttpLedgerMemberService : ILedgerMemberService
{
    private readonly HttpService _http;
    private readonly IApplicationDbContext _context;

    public HttpLedgerMemberService(HttpService http, IApplicationDbContext context)
    {
        _http = http;
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<LedgerMember>> GetMembersAsync(Guid ledgerId, Guid operatorUserId)
    {
        await RefreshCacheAsync();

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
        await RefreshCacheAsync();

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

        if (response == null)
        {
            throw new InvalidOperationException("邀请失败，请稍后重试");
        }

        await MergeMemberAsync(response);
        return (await _context.LedgerMembers.FirstAsync(m => m.Id == response.Id));
    }

    /// <inheritdoc />
    public async Task RespondAsync(Guid memberId, Guid userId, bool accept)
    {
        await _http.PostAsync<object>(
            $"api/invitations/{memberId}/respond",
            new LedgerInvitationRespondRequest { Accept = accept });

        await RefreshCacheAsync();
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(Guid ledgerId, Guid memberId, Guid operatorUserId)
    {
        await _http.DeleteAsync<object>($"api/ledgers/{ledgerId}/members/{memberId}");
        await RefreshCacheAsync();
    }

    /// <inheritdoc />
    public async Task ChangeRoleAsync(Guid ledgerId, Guid memberId, LedgerMemberRole newRole, Guid operatorUserId)
    {
        await _http.PutAsync<object>(
            $"api/ledgers/{ledgerId}/members/{memberId}/role",
            new LedgerMemberRequest { Role = newRole });

        await RefreshCacheAsync();
    }

    /// <inheritdoc />
    public async Task TransferOwnershipAsync(Guid ledgerId, Guid operatorUserId, Guid toUserId)
    {
        await _http.PostAsync<object>(
            $"api/ledgers/{ledgerId}/transfer-ownership",
            new LedgerTransferOwnershipRequest { ToUserId = toUserId });

        await RefreshCacheAsync();
    }

    /// <inheritdoc />
    public async Task ExitAsync(Guid ledgerId, Guid userId)
    {
        await _http.PostAsync<object>($"api/ledgers/{ledgerId}/exit", null);
        await RefreshCacheAsync();
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
            throw new InvalidOperationException("只读成员不能在共享账本中记账");
        }
    }

    /// <inheritdoc />
    public async Task<List<Ledger>> GetSharedLedgersAsync(Guid userId)
    {
        await RefreshCacheAsync();

        return await _context.LedgerMembers
            .Where(m => m.UserId == userId && m.Status == LedgerMemberStatus.Active &&
                !m.IsDeleted && m.Role != LedgerMemberRole.Owner)
            .Where(m => !m.Ledger.IsDeleted)
            .Select(m => m.Ledger)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();
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
    /// 从云端拉取成员关系并合并到本地缓存（静默失败：离线时使用本地缓存，不影响页面展示）
    /// </summary>
    private async Task RefreshCacheAsync()
    {
        try
        {
            var cloudMembers = await _http.PostAsync<LedgerMemberSyncPullResponse>(
                "api/sync/ledgermembers/pull",
                new LedgerMemberSyncPullRequest());

            if (cloudMembers == null)
            {
                return;
            }

            foreach (var dto in cloudMembers.Members)
            {
                await MergeMemberAsync(dto);
            }
        }
        catch
        {
            // 离线或云端不可用时使用本地缓存
        }
    }

    /// <summary>
    /// 合并单条成员关系到本地缓存（含用户/账本存根补建）
    /// </summary>
    private async Task MergeMemberAsync(LedgerMemberSyncDto dto)
    {
        if (dto.UserId != Guid.Empty)
        {
            await EnsureUserStubAsync(dto.UserId, dto.UserPhoneNumber);
        }

        if (dto.LedgerOwnerId.HasValue && dto.LedgerOwnerId.Value != Guid.Empty)
        {
            await EnsureUserStubAsync(dto.LedgerOwnerId.Value);
        }

        await EnsureLedgerStubAsync(dto);

        var existing = await _context.LedgerMembers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == dto.Id);

        // 本地懒补的 Owner 行 Id 与云端不同：按 (账本, 用户) 唯一约束兜底匹配，
        // 避免按 Id 查不到时重复插入而违反 (LedgerId, UserId) 唯一索引导致整个拉取合并失败
        if (existing == null)
        {
            existing = await _context.LedgerMembers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.LedgerId == dto.LedgerId && m.UserId == dto.UserId);
        }

        if (existing == null)
        {
            _context.LedgerMembers.Add(new LedgerMember
            {
                Id = dto.Id,
                LedgerId = dto.LedgerId,
                UserId = dto.UserId,
                Role = dto.Role,
                Status = dto.Status,
                IsDeleted = dto.IsDeleted,
                CreatedAt = dto.CreatedAt,
                UpdatedAt = dto.UpdatedAt
            });
        }
        else if (dto.UpdatedAt > existing.UpdatedAt && !dto.ContentEquals(existing))
        {
            existing.Role = dto.Role;
            existing.Status = dto.Status;
            existing.IsDeleted = dto.IsDeleted;
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 确保本地存在用户存根（无手机号时生成确定性占位手机号，后续可升级为真实手机号）
    /// </summary>
    private async Task EnsureUserStubAsync(Guid userId, string? phoneNumber = null)
    {
        var exists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id == userId);

        if (!exists)
        {
            _context.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = string.IsNullOrEmpty(phoneNumber)
                    ? BuildStubPhoneNumber(userId)
                    : phoneNumber
            });
            await _context.SaveChangesAsync();
        }
        else if (!string.IsNullOrEmpty(phoneNumber))
        {
            // 存根已存在但为占位手机号时升级为真实手机号，便于成员列表展示
            var user = await _context.Users
                .IgnoreQueryFilters()
                .FirstAsync(u => u.Id == userId);

            if (user.PhoneNumber.StartsWith("stub_") && user.PhoneNumber != phoneNumber)
            {
                user.PhoneNumber = phoneNumber;
                await _context.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// 生成确定性占位手机号（stub_ + 15 位 hex，共 20 字符，满足唯一索引且与真实 11 位手机号格式天然区分）
    /// </summary>
    private static string BuildStubPhoneNumber(Guid userId)
    {
        return $"stub_{userId.ToString("N")[..15]}";
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

    /// <summary>
    /// 确保本地存在成员关系引用的账本（云端拉取的账本实体缺位时按 DTO 附带信息补建存根）
    /// </summary>
    private async Task EnsureLedgerStubAsync(LedgerMemberSyncDto dto)
    {
        var exists = await _context.Ledgers
            .IgnoreQueryFilters()
            .AnyAsync(l => l.Id == dto.LedgerId);

        if (exists)
        {
            return;
        }

        _context.Ledgers.Add(new Ledger
        {
            Id = dto.LedgerId,
            UserId = dto.LedgerOwnerId ?? dto.UserId,
            Name = string.IsNullOrWhiteSpace(dto.LedgerName) ? "共享账本" : dto.LedgerName!,
            Icon = string.IsNullOrWhiteSpace(dto.LedgerIcon) ? "mdi-book" : dto.LedgerIcon!,
            SortOrder = 99
        });
        await _context.SaveChangesAsync();
    }
}
