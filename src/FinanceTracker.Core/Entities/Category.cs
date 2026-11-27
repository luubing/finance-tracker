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
    /// 不计入统计：该分类下的账单不参与统计（统计 Tab、首页本月概览）与预算已用金额，账单列表里仍然照常显示。
    /// 例如「存款」记在支出里，但不想算进花销。
    /// <para>
    /// 只对自建分类有效——预设分类是全局共享数据，不放个人开关（<see cref="IsPreset"/> 为 true 时恒为 false）。
    /// 按分类<b>当前</b>的开关在查询时判定，不是账单入库时的快照：改开关会让该分类的历史账单一并重新计入 / 不再计入。
    /// 判定口径只写在 <c>StatisticsScope</c> 一处，见 docs/adr/0008-category-exclude-from-statistics.md。
    /// </para>
    /// </summary>
    public bool ExcludeFromStatistics { get; set; }

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; }

    // 导航属性
    public User? User { get; set; }
    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
}
