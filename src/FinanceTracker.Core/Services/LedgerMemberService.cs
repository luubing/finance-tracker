using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 账本成员服务实现（云端真相源：直接操作数据库，供 FinanceTracker.Api 使用）
/// </summary>
public class LedgerMemberService : ILedgerMemberService
{
    private readonly IApplicationDbContext _context;

    public LedgerMemberService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<LedgerMember>> GetMembersAsync(Guid ledgerId, Guid operatorUserId)
    {
        if (!await CanViewLedgerAsync(ledgerId, operatorUserId))
        {
            throw new ForbiddenAccessException("无权查看该账本成员");
        }

        // 兼容共享功能上线前创建的历史账本：懒补 Owner 成员行
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
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("手机号不能为空");
        }

        if (role == LedgerMemberRole.Owner)
        {
            throw new InvalidOperationException("不能邀请 Owner 角色，请使用转让所有权");
        }

        var ledger = await RequireLedgerAsync(ledgerId);

        // 历史账本懒补 Owner 成员行（幂等）：共享功能上线前创建的账本尚无成员行，
        // 不补齐的话 Owner 判定会误判为"只有账本所有者可以邀请成员"
        await EnsureOwnerMemberRowAsync(ledgerId);

        var myRole = await GetMyRoleAsync(ledgerId, operatorUserId);
        if (myRole != LedgerMemberRole.Owner)
        {
            throw new ForbiddenAccessException("只有账本所有者可以邀请成员");
        }

        var invitedUser = await _context.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber.Trim() && !u.IsDeleted);

        if (invitedUser == null)
        {
            throw new ArgumentException("该手机号尚未注册");
        }

        if (invitedUser.Id == ledger.UserId)
        {
            throw new InvalidOperationException("该用户是账本所有者");
        }

        // (LedgerId, UserId) 唯一：曾拒绝/被移除的记录复用，重新进入 Pending
        var existing = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.LedgerId == ledgerId && m.UserId == invitedUser.Id);

        if (existing != null)
        {
            if (!existing.IsDeleted)
            {
                throw new InvalidOperationException(existing.Status == LedgerMemberStatus.Pending
                    ? "该用户已被邀请，等待确认"
                    : "该用户已是账本成员");
            }

            existing.Role = role;
            existing.Status = LedgerMemberStatus.Pending;
            existing.IsDeleted = false;
            await _context.SaveChangesAsync();
            return existing;
        }

        var member = new LedgerMember
        {
            LedgerId = ledgerId,
            UserId = invitedUser.Id,
            Role = role,
            Status = LedgerMemberStatus.Pending
        };

        _context.LedgerMembers.Add(member);
        await _context.SaveChangesAsync();

        return member;
    }

    /// <inheritdoc />
    public async Task RespondAsync(Guid memberId, Guid userId, bool accept)
    {
        var member = await _context.LedgerMembers
            .Include(m => m.Ledger)
            .FirstOrDefaultAsync(m => m.Id == memberId);

        if (member == null || member.IsDeleted || member.UserId != userId ||
            member.Status != LedgerMemberStatus.Pending)
        {
            throw new ArgumentException("邀请不存在或已处理");
        }

        if (member.Ledger == null || member.Ledger.IsDeleted)
        {
            throw new InvalidOperationException("该账本已被删除，邀请失效");
        }

        if (accept)
        {
            member.Status = LedgerMemberStatus.Active;
        }
        else
        {
            member.IsDeleted = true;
        }

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task RemoveMemberAsync(Guid ledgerId, Guid memberId, Guid operatorUserId)
    {
        await RequireOwnerAsync(ledgerId, operatorUserId);

        var member = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.LedgerId == ledgerId && !m.IsDeleted);

        if (member == null)
        {
            throw new ArgumentException("成员不存在");
        }

        if (member.Role == LedgerMemberRole.Owner)
        {
            throw new InvalidOperationException("不能移除账本所有者，请先转让所有权");
        }

        member.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task ChangeRoleAsync(Guid ledgerId, Guid memberId, LedgerMemberRole newRole, Guid operatorUserId)
    {
        await RequireOwnerAsync(ledgerId, operatorUserId);

        if (newRole == LedgerMemberRole.Owner)
        {
            throw new InvalidOperationException("请使用转让所有权变更 Owner");
        }

        var member = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.LedgerId == ledgerId &&
                m.Status == LedgerMemberStatus.Active && !m.IsDeleted);

        if (member == null)
        {
            throw new ArgumentException("成员不存在");
        }

        if (member.Role == LedgerMemberRole.Owner)
        {
            throw new InvalidOperationException("不能修改账本所有者的角色");
        }

        member.Role = newRole;
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task TransferOwnershipAsync(Guid ledgerId, Guid operatorUserId, Guid toUserId)
    {
        await RequireOwnerAsync(ledgerId, operatorUserId);

        if (toUserId == operatorUserId)
        {
            throw new InvalidOperationException("不能转让给自己");
        }

        await EnsureOwnerMemberRowAsync(ledgerId);

        var target = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.LedgerId == ledgerId && m.UserId == toUserId &&
                m.Status == LedgerMemberStatus.Active && !m.IsDeleted);

        if (target == null)
        {
            throw new ArgumentException("目标用户不是该账本的生效成员");
        }

        var ledger = await _context.Ledgers.FirstAsync(l => l.Id == ledgerId);
        var ownerRow = await _context.LedgerMembers
            .FirstAsync(m => m.LedgerId == ledgerId && m.UserId == operatorUserId);

        // 账本归属 + 双方角色交换：原 Owner 降为 Editor
        ledger.UserId = toUserId;
        ownerRow.Role = LedgerMemberRole.Editor;
        target.Role = LedgerMemberRole.Owner;

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task ExitAsync(Guid ledgerId, Guid userId)
    {
        var ledger = await RequireLedgerAsync(ledgerId);

        if (ledger.UserId == userId)
        {
            throw new InvalidOperationException("账本所有者不能退出账本，请先转让所有权");
        }

        var member = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.LedgerId == ledgerId && m.UserId == userId &&
                m.Status == LedgerMemberStatus.Active && !m.IsDeleted);

        if (member == null)
        {
            throw new ArgumentException("你不是该账本成员");
        }

        member.IsDeleted = true;
        await _context.SaveChangesAsync();
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
        var ledger = await RequireLedgerAsync(ledgerId);

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
        return await _context.LedgerMembers
            .Where(m => m.UserId == userId && m.Status == LedgerMemberStatus.Active &&
                !m.IsDeleted && m.Role != LedgerMemberRole.Owner)
            .Where(m => !m.Ledger.IsDeleted)
            .Select(m => m.Ledger)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 查询用户的生效角色（非成员返回 null）
    /// </summary>
    private async Task<LedgerMemberRole?> GetRoleCoreAsync(Guid ledgerId, Guid userId)
    {
        var member = await _context.LedgerMembers
            .FirstOrDefaultAsync(m => m.LedgerId == ledgerId && m.UserId == userId &&
                m.Status == LedgerMemberStatus.Active && !m.IsDeleted);

        return member?.Role;
    }

    /// <summary>
    /// 校验账本存在且未删除
    /// </summary>
    private async Task<Ledger> RequireLedgerAsync(Guid ledgerId)
    {
        var ledger = await _context.Ledgers
            .FirstOrDefaultAsync(l => l.Id == ledgerId && !l.IsDeleted);

        if (ledger == null)
        {
            throw new ArgumentException("账本不存在");
        }

        return ledger;
    }

    /// <summary>
    /// 校验操作者是账本所有者
    /// </summary>
    private async Task RequireOwnerAsync(Guid ledgerId, Guid userId)
    {
        var ledger = await RequireLedgerAsync(ledgerId);

        if (ledger.UserId != userId)
        {
            throw new ForbiddenAccessException("只有账本所有者可以执行此操作");
        }
    }

    /// <summary>
    /// 兼容共享功能上线前创建的历史账本：懒补 Owner 成员行（幂等）
    /// </summary>
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
