namespace FinanceTracker.Core.Entities;

/// <summary>
/// 基础实体（用于审计字段）
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
