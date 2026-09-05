namespace FinanceTracker.Core.Entities;

/// <summary>
/// 预算（用户按月份设置的支出预算，支持总预算与分类预算，可按账本独立设置）
/// </summary>
public class Budget : BaseEntity
{
    /// <summary>
    /// 预算ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 账本ID（null 表示全部账本）
    /// </summary>
    public Guid? LedgerId { get; set; }

    /// <summary>
    /// 预算年份
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// 预算月份 (1-12)
    /// </summary>
    public int Month { get; set; }

    /// <summary>
    /// 分类ID（null 表示总预算）
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// 预算金额（正数）
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; }

    // 导航属性
    public User User { get; set; } = null!;
    public Ledger? Ledger { get; set; }
    public Category? Category { get; set; }
}