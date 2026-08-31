namespace FinanceTracker.Core.Entities;

/// <summary>
/// 用户实体
/// </summary>
public class User
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 手机号（唯一标识）
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; }

    // 导航属性
    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<PaymentChannel> PaymentChannels { get; set; } = new List<PaymentChannel>();
}
