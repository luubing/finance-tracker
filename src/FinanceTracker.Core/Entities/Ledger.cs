namespace FinanceTracker.Core.Entities;

/// <summary>
/// 账本（账单的归属分组，账本名称由用户维护）
/// </summary>
public class Ledger : BaseEntity
{
    /// <summary>
    /// 账本ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 用户ID（账本均由用户创建，无预设账本）
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 账本名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; }

    // 导航属性
    public User User { get; set; } = null!;
    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
}
