using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Entities;

/// <summary>
/// 待确认账单：由通知栏监听或短信实时监听自动捕获的支付记录，
/// 用户在待确认账单页面确认后才转为正式账单（Bill）。
/// </summary>
public class PendingBill
{
    /// <summary>
    /// 待确认账单ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 金额（正数）
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 账单类型（支出/收入）
    /// </summary>
    public BillType Type { get; set; }

    /// <summary>
    /// 支付渠道名称（如"微信支付"、"支付宝"），确认时用于匹配 PaymentChannel
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// 备注（通知/短信的识别摘要）
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// 交易时间
    /// </summary>
    public DateTime TransactionTime { get; set; }

    /// <summary>
    /// 数据来源（通知栏识别 / 短信识别）
    /// </summary>
    public BillSource Source { get; set; }

    /// <summary>
    /// 捕获时间
    /// </summary>
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}
