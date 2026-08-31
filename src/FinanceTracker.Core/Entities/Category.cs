using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Entities;

/// <summary>
/// 分类
/// </summary>
public class Category : BaseEntity
{
    /// <summary>
    /// 分类ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 用户ID（null表示预设分类）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 分类名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 账单类型（支出/收入）
    /// </summary>
    public BillType Type { get; set; }

    /// <summary>
    /// 是否为预设分类
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
