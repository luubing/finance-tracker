using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Models;

/// <summary>
/// 账本成员同步传输对象（客户端缓存成员关系，用于本地账单可见性判定）
/// </summary>
public class LedgerMemberSyncDto
{
    public Guid Id { get; set; }
    public Guid LedgerId { get; set; }
    public Guid UserId { get; set; }
    public LedgerMemberRole Role { get; set; }
    public LedgerMemberStatus Status { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ---- 附带信息（客户端补建用户/账本存根，避免外键违例） ----
    public string? UserPhoneNumber { get; set; }
    public string? LedgerName { get; set; }
    public string? LedgerIcon { get; set; }
    public Guid? LedgerOwnerId { get; set; }

    public static LedgerMemberSyncDto FromEntity(LedgerMember member) => new()
    {
        Id = member.Id,
        LedgerId = member.LedgerId,
        UserId = member.UserId,
        Role = member.Role,
        Status = member.Status,
        IsDeleted = member.IsDeleted,
        CreatedAt = member.CreatedAt,
        UpdatedAt = member.UpdatedAt,
        UserPhoneNumber = member.User?.PhoneNumber,
        LedgerName = member.Ledger?.Name,
        LedgerIcon = member.Ledger?.Icon,
        LedgerOwnerId = member.Ledger?.UserId
    };

    /// <summary>
    /// 内容是否与实体一致（跳过无变化写入，避免 UpdatedAt 空转）
    /// </summary>
    public bool ContentEquals(LedgerMember member) =>
        member.Role == Role &&
        member.Status == Status &&
        member.IsDeleted == IsDeleted;
}

/// <summary>
/// 账本成员同步 - 拉取请求体
/// </summary>
public class LedgerMemberSyncPullRequest
{
    public DateTime? Since { get; set; }
}

/// <summary>
/// 账本成员同步 - 拉取响应体
/// </summary>
public class LedgerMemberSyncPullResponse
{
    public List<LedgerMemberSyncDto> Members { get; set; } = new();
}

/// <summary>
/// 成员操作请求（邀请/修改角色）
/// </summary>
public class LedgerMemberRequest
{
    /// <summary>
    /// 被邀请人手机号（邀请时必填）
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 角色（Editor/Viewer）
    /// </summary>
    public LedgerMemberRole Role { get; set; }
}

/// <summary>
/// 邀请响应请求
/// </summary>
public class LedgerInvitationRespondRequest
{
    public bool Accept { get; set; }
}

/// <summary>
/// 转让所有权请求
/// </summary>
public class LedgerTransferOwnershipRequest
{
    public Guid ToUserId { get; set; }
}