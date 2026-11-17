namespace FinanceTracker.Web.Models;

/// <summary>
/// 账本视图模型：页面中的账本下拉筛选、列表展示与编辑对话框共用。
/// <see cref="Id"/> / <see cref="Name"/> / <see cref="Icon"/> 为通用字段，
/// <see cref="IsShared"/> / <see cref="IsDefault"/> / <see cref="SortOrder"/> 按页面需要选用。
/// </summary>
public class LedgerModel
{
    /// <summary>
    /// 账本ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 账本名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 是否为他人共享给我的账本
    /// </summary>
    public bool IsShared { get; set; }

    /// <summary>
    /// 是否为用户默认账本
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 下拉显示文本：共享账本追加（共享）标记
    /// </summary>
    public string DisplayText => IsShared ? $"{Name}（共享）" : Name;
}
