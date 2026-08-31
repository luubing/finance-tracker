namespace FinanceTracker.Web.ViewModels;

/// <summary>
/// 支付渠道视图模型
/// </summary>
public class PaymentChannelViewModel
{
    /// <summary>
    /// 渠道ID
    /// </summary>
    public Guid Id { get; set; }

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
}
