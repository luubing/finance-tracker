namespace FinanceTracker.Core.Enums;

/// <summary>
/// 账本成员角色
/// </summary>
public enum LedgerMemberRole
{
    /// <summary>
    /// 所有者（唯一，拥有账本全部权限）
    /// </summary>
    Owner = 0,

    /// <summary>
    /// 可编辑成员（可记账，查看全部账单）
    /// </summary>
    Editor = 1,

    /// <summary>
    /// 只读成员（仅查看账本内账单）
    /// </summary>
    Viewer = 2
}