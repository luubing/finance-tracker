using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 账单语音解析接口：将语音识别出的中文口语解析为账单草稿
/// </summary>
public interface IBillVoiceParser
{
    /// <summary>
    /// 解析语音识别文本为账单草稿
    /// </summary>
    /// <param name="text">语音识别文本（中文口语，如：昨天午饭花了三十五块，微信支付）</param>
    /// <param name="categories">用户的分类列表（含预设分类）</param>
    /// <param name="paymentChannels">用户的支付渠道列表（含预设渠道）</param>
    /// <returns>账单草稿；金额无法识别时 Amount 为 null，由上层提示用户</returns>
    Task<ParsedBillDraft> ParseAsync(
        string text,
        IReadOnlyList<Category> categories,
        IReadOnlyList<PaymentChannel> paymentChannels);
}

/// <summary>
/// 账单语音解析草稿：解析结果均可空，仅用于预填记账表单，由用户确认后再保存，不直接落库
/// </summary>
public class ParsedBillDraft
{
    /// <summary>
    /// 金额（null 表示未能识别）
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// 账单类型（默认支出，出现收入类关键词时为收入）
    /// </summary>
    public BillType Type { get; set; } = BillType.Expense;

    /// <summary>
    /// 分类ID（null 表示未匹配到分类）
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// 支付渠道ID（null 表示未匹配到支付渠道）
    /// </summary>
    public Guid? PaymentChannelId { get; set; }

    /// <summary>
    /// 交易时间（null 表示未提及，由上层使用当前时间）
    /// </summary>
    public DateTime? TransactionTime { get; set; }

    /// <summary>
    /// 识别的原始文本（预填到备注）
    /// </summary>
    public string RawText { get; set; } = string.Empty;
}