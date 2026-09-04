using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Entities;

/// <summary>
/// 账单记录
/// </summary>
public class Bill : BaseEntity
{
    /// <summary>
    /// 账单ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 金额（正数）
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 账单类型（支出/收入）
    /// </summary>
    public BillType Type { get; set; }

    /// <summary>
    /// 分类ID
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// 支付渠道ID
    /// </summary>
    public Guid PaymentChannelId { get; set; }

    /// <summary>
    /// 账本ID（null 表示未归属账本，兼容历史账单）
    /// </summary>
    public Guid? LedgerId { get; set; }

    /// <summary>
    /// 交易时间
    /// </summary>
    public DateTime TransactionTime { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public BillSource Source { get; set; } = BillSource.Manual;

    /// <summary>
    /// 同步状态
    /// </summary>
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; }

    // 导航属性
    public User User { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public PaymentChannel PaymentChannel { get; set; } = null!;
    public Ledger? Ledger { get; set; }
}
