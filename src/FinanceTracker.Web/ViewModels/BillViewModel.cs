using FinanceTracker.Core.Enums;

namespace FinanceTracker.Web.ViewModels;

/// <summary>
/// 账单视图模型
/// </summary>
public class BillViewModel
{
    /// <summary>
    /// 账单ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 账单类型
    /// </summary>
    public BillType Type { get; set; }

    /// <summary>
    /// 分类ID
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// 分类名称
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// 分类图标
    /// </summary>
    public string CategoryIcon { get; set; } = string.Empty;

    /// <summary>
    /// 支付渠道ID
    /// </summary>
    public Guid PaymentChannelId { get; set; }

    /// <summary>
    /// 支付渠道名称
    /// </summary>
    public string PaymentChannelName { get; set; } = string.Empty;

    /// <summary>
    /// 支付渠道图标
    /// </summary>
    public string PaymentChannelIcon { get; set; } = string.Empty;

    /// <summary>
    /// 交易时间
    /// </summary>
    public DateTime TransactionTime { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// 数据来源
    /// </summary>
    public BillSource Source { get; set; }

    /// <summary>
    /// 同步状态
    /// </summary>
    public SyncStatus SyncStatus { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
