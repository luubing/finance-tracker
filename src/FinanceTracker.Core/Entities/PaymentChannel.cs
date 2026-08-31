namespace FinanceTracker.Core.Entities;

/// <summary>
/// 支付渠道
/// </summary>
public class PaymentChannel
{
    /// <summary>
    /// 渠道ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 用户ID（null表示预设渠道）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 渠道名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 是否为预设渠道
    /// </summary>
    public bool IsPreset { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; }

    // 导航属性
    public User? User { get; set; }
    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
}
