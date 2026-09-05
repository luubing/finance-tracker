using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Entities;

/// <summary>
/// 账本成员（共享账本的成员关系，含邀请确认状态）
/// </summary>
public class LedgerMember : BaseEntity
{
    /// <summary>
    /// 成员记录ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 账本ID
    /// </summary>
    public Guid LedgerId { get; set; }

    /// <summary>
    /// 成员用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 角色（Owner/Editor/Viewer）
    /// </summary>
    public LedgerMemberRole Role { get; set; }

    /// <summary>
    /// 状态（Pending 待确认 / Active 已生效）
    /// </summary>
    public LedgerMemberStatus Status { get; set; }

    /// <summary>
    /// 是否已删除（软删除：被移出账本/拒绝邀请/退出账本）
    /// </summary>
    public bool IsDeleted { get; set; }

    // 导航属性
    public Ledger Ledger { get; set; } = null!;
    public User User { get; set; } = null!;
}