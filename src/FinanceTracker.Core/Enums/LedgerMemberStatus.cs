namespace FinanceTracker.Core.Enums;

/// <summary>
/// 账本成员状态（邀请确认机制）
/// </summary>
public enum LedgerMemberStatus
{
    /// <summary>
    /// 待确认（已被邀请，尚未接受）
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已生效（成员邀请已被接受）
    /// </summary>
    Active = 1
}