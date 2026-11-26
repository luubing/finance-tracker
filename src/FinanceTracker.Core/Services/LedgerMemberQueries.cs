using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 账本成员的只读查询：云端实现（<see cref="LedgerMemberService"/>）与客户端本地缓存实现
/// （Web 项目的 HttpLedgerMemberService）共用同一份，两边口径不会分叉
/// </summary>
public static class LedgerMemberQueries
{
    /// <summary>
    /// 与用户共享的账本（用户是已生效、非所有者的成员），按创建时间排序，并带上账本所有者（<c>Ledger.User</c>）。
    /// 先从账本表查询再 Include：EF 不允许在 Select(m => m.Ledger) 之后 Include（会抛 InvalidOperationException）。
    /// </summary>
    /// <param name="context">数据库上下文（云端库或客户端本地库）</param>
    /// <param name="userId">当前用户ID</param>
    public static Task<List<Entities.Ledger>> GetSharedLedgersAsync(IApplicationDbContext context, Guid userId)
    {
        return context.Ledgers
            .Include(l => l.User)
            .Where(l => !l.IsDeleted)
            .Where(l => context.LedgerMembers.Any(m => m.LedgerId == l.Id && m.UserId == userId &&
                m.Status == LedgerMemberStatus.Active && !m.IsDeleted && m.Role != LedgerMemberRole.Owner))
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 统计账本的已生效成员数（含所有者本人）。
    /// 只统计 <paramref name="userId"/> 自己是已生效成员的账本，避免借这个接口探测别人账本的成员数；
    /// 没有成员行的账本不出现在结果里。
    /// </summary>
    /// <param name="context">数据库上下文（云端库或客户端本地库）</param>
    /// <param name="userId">当前用户ID</param>
    /// <param name="ledgerIds">要统计的账本ID</param>
    /// <returns>账本ID → 已生效成员数</returns>
    public static async Task<Dictionary<Guid, int>> CountActiveMembersAsync(
        IApplicationDbContext context, Guid userId, IReadOnlyCollection<Guid> ledgerIds)
    {
        if (ledgerIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var ids = ledgerIds.ToList();

        var myLedgerIds = context.LedgerMembers
            .Where(m => m.UserId == userId && m.Status == LedgerMemberStatus.Active && !m.IsDeleted)
            .Select(m => m.LedgerId);

        return await context.LedgerMembers
            .Where(m => ids.Contains(m.LedgerId) && myLedgerIds.Contains(m.LedgerId) &&
                m.Status == LedgerMemberStatus.Active && !m.IsDeleted)
            .GroupBy(m => m.LedgerId)
            .Select(g => new { LedgerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LedgerId, x => x.Count);
    }

    /// <summary>
    /// 在给定的账本里，用户是已生效的<b>只读成员</b>（Viewer）的有哪些。
    /// <para>
    /// 只认这一种身份：账本已删除、用户是所有者 / 可编辑成员、或者压根不是成员（例如被移除、自己退出了）都不算——
    /// 被移除的成员保留本地账单副本的处置权（ADR 0004），而只读成员按 ADR 0004「不能修改任何账单」。
    /// 修改 / 删除 / 改归属自己账单之前用它判断账单所在的账本是不是「只读」（<see cref="BillService"/>），
    /// 账单列表也据此决定要不要给出删除 / 批量归属的入口。
    /// </para>
    /// </summary>
    /// <param name="context">数据库上下文（云端库或客户端本地库）</param>
    /// <param name="userId">当前用户ID</param>
    /// <param name="ledgerIds">要判断的账本ID</param>
    /// <returns>其中用户是只读成员的账本ID（没有则为空集合）</returns>
    public static async Task<HashSet<Guid>> GetReadOnlyLedgerIdsAsync(
        IApplicationDbContext context, Guid userId, IReadOnlyCollection<Guid> ledgerIds)
    {
        if (ledgerIds.Count == 0)
        {
            return [];
        }

        var ids = ledgerIds.ToList();

        var readOnly = await context.LedgerMembers
            .Where(m => ids.Contains(m.LedgerId) && m.UserId == userId &&
                m.Status == LedgerMemberStatus.Active && !m.IsDeleted && m.Role == LedgerMemberRole.Viewer)
            .Where(m => context.Ledgers.Any(l => l.Id == m.LedgerId && !l.IsDeleted && l.UserId != userId))
            .Select(m => m.LedgerId)
            .ToListAsync();

        return readOnly.ToHashSet();
    }
}
