namespace FinanceTracker.Core.Entities;

/// <summary>
/// 用户默认值偏好（每个用户独立，不影响其他用户）
/// </summary>
public class UserDefault : BaseEntity
{
    /// <summary>
    /// 默认值ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 默认支出分类ID
    /// </summary>
    public Guid? DefaultExpenseCategoryId { get; set; }

    /// <summary>
    /// 默认收入分类ID
    /// </summary>
    public Guid? DefaultIncomeCategoryId { get; set; }

    /// <summary>
    /// 默认支付渠道ID
    /// </summary>
    public Guid? DefaultPaymentChannelId { get; set; }

    /// <summary>
    /// 默认账本ID
    /// </summary>
    public Guid? DefaultLedgerId { get; set; }

    // 导航属性
    public User User { get; set; } = null!;
    public Category? DefaultExpenseCategory { get; set; }
    public Category? DefaultIncomeCategory { get; set; }
    public PaymentChannel? DefaultPaymentChannel { get; set; }
    public Ledger? DefaultLedger { get; set; }
}
