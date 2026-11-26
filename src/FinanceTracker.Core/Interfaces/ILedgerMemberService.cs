using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 账本成员服务接口（共享账本的成员与权限管理）
/// </summary>
public interface ILedgerMemberService
{
    /// <summary>
    /// 获取账本成员列表（已生效成员，含用户手机号）
    /// </summary>
    /// <param name="ledgerId">账本ID</param>
    /// <param name="operatorUserId">操作者用户ID（须为账本成员）</param>
    /// <returns>成员列表</returns>
    Task<List<LedgerMember>> GetMembersAsync(Guid ledgerId, Guid operatorUserId);

    /// <summary>
    /// 获取当前用户的待处理邀请（含账本与邀请人信息）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>待处理邀请列表</returns>
    Task<List<LedgerMember>> GetPendingInvitationsAsync(Guid userId);

    /// <summary>
    /// 邀请成员（按手机号，Owner 专属；生成 Pending 状态邀请）
    /// </summary>
    /// <param name="ledgerId">账本ID</param>
    /// <param name="operatorUserId">操作者用户ID（须为 Owner）</param>
    /// <param name="phoneNumber">被邀请人手机号（须已注册）</param>
    /// <param name="role">角色（Editor/Viewer）</param>
    /// <returns>创建的成员记录</returns>
    Task<LedgerMember> InviteAsync(Guid ledgerId, Guid operatorUserId, string phoneNumber, LedgerMemberRole role);

    /// <summary>
    /// 响应邀请（被邀请人确认或拒绝）
    /// </summary>
    /// <param name="memberId">成员记录ID</param>
    /// <param name="userId">被邀请人用户ID</param>
    /// <param name="accept">true 接受 / false 拒绝</param>
    Task RespondAsync(Guid memberId, Guid userId, bool accept);

    /// <summary>
    /// 移除成员（Owner 专属，不能移除 Owner）
    /// </summary>
    Task RemoveMemberAsync(Guid ledgerId, Guid memberId, Guid operatorUserId);

    /// <summary>
    /// 修改成员角色（Owner 专属，不能修改 Owner 的角色）
    /// </summary>
    Task ChangeRoleAsync(Guid ledgerId, Guid memberId, LedgerMemberRole newRole, Guid operatorUserId);

    /// <summary>
    /// 转让所有权（Owner 专属；原 Owner 降为 Editor）
    /// </summary>
    Task TransferOwnershipAsync(Guid ledgerId, Guid operatorUserId, Guid toUserId);

    /// <summary>
    /// 退出共享账本（非 Owner 成员；Owner 须先转让所有权）
    /// </summary>
    Task ExitAsync(Guid ledgerId, Guid userId);

    /// <summary>
    /// 获取用户在账本中的角色（非成员或未生效返回 null）
    /// </summary>
    Task<LedgerMemberRole?> GetMyRoleAsync(Guid ledgerId, Guid userId);

    /// <summary>
    /// 历史账本懒补 Owner 成员行（幂等）：为共享功能上线前创建、尚无成员行的账本补建 Owner 记录，
    /// 使其 Owner 判定、成员列表与邀请功能可用（云端/本地缓存各自维护）
    /// </summary>
    Task EnsureOwnerMemberRowAsync(Guid ledgerId);

    /// <summary>
    /// 用户是否可查看该账本（账本所有者或已生效成员）
    /// </summary>
    Task<bool> CanViewLedgerAsync(Guid ledgerId, Guid userId);

    /// <summary>
    /// 校验用户可向账本写入账单（自有账本 / Owner / Editor），否则抛出异常
    /// </summary>
    /// <exception cref="ArgumentException">账本不存在</exception>
    /// <exception cref="Exceptions.ForbiddenAccessException">非成员或只读成员（Viewer）无权记账</exception>
    Task EnsureCanWriteAsync(Guid ledgerId, Guid userId);

    /// <summary>
    /// 获取与当前用户共享的账本列表（已生效且非 Owner 的成员关系，触发云端刷新并合并本地缓存）。
    /// 返回的账本已带上所有者（<see cref="Ledger.User"/>），列表页据此展示「所有者 138****0099」。
    /// </summary>
    Task<List<Ledger>> GetSharedLedgersAsync(Guid userId);

    /// <summary>
    /// 统计账本的已生效成员数（含所有者本人），只读本库 / 本地缓存、不联网：
    /// 账本列表据此给「我的账本」标注「共享」与成员数，不必为每个账本各刷新一次成员关系。
    /// 只统计 <paramref name="userId"/> 自己是已生效成员（含所有者）的账本；没有成员行的账本
    /// （共享功能上线前的历史账本，尚未补建 Owner 行）不出现在结果里。
    /// </summary>
    /// <param name="userId">当前用户ID</param>
    /// <param name="ledgerIds">要统计的账本ID</param>
    /// <returns>账本ID → 已生效成员数</returns>
    Task<Dictionary<Guid, int>> GetActiveMemberCountsAsync(Guid userId, IReadOnlyCollection<Guid> ledgerIds);

    /// <summary>
    /// 在给定的账本里，<paramref name="userId"/> 是已生效的<b>只读成员</b>（Viewer）的有哪些，只读本库 / 本地缓存、不联网。
    /// 与 <c>BillService</c> 拒绝只读成员修改 / 删除 / 改归属账单用的是同一条规则（<see cref="Services.LedgerMemberQueries.GetReadOnlyLedgerIdsAsync"/>）：
    /// 界面据此决定要不要给出删除 / 批量归属的入口，服务层才是最终的把关点。
    /// 所有者 / 可编辑成员、已被移除或还没接受邀请的成员、已删除的账本都不算。
    /// </summary>
    /// <param name="userId">当前用户ID</param>
    /// <param name="ledgerIds">要判断的账本ID</param>
    /// <returns>其中用户是只读成员的账本ID（没有则为空集合）</returns>
    Task<HashSet<Guid>> GetReadOnlyLedgerIdsAsync(Guid userId, IReadOnlyCollection<Guid> ledgerIds);
}