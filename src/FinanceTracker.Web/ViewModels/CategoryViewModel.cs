using FinanceTracker.Core.Enums;

namespace FinanceTracker.Web.ViewModels;

/// <summary>
/// 分类视图模型
/// </summary>
public class CategoryViewModel
{
    /// <summary>
    /// 分类ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 分类名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 分类类型
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
}
