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
    Task EnsureCanWriteAsync(Guid ledgerId, Guid userId);

    /// <summary>
    /// 获取与当前用户共享的账本列表（已生效且非 Owner 的成员关系，触发云端刷新并合并本地缓存）
    /// </summary>
    Task<List<Ledger>> GetSharedLedgersAsync(Guid userId);
}