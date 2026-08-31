namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// CSV 解析服务接口
/// </summary>
public interface ICsvParserService
{
    /// <summary>
    /// 解析微信账单 CSV
    /// </summary>
    /// <param name="csvContent">CSV 内容</param>
    /// <returns>账单列表</returns>
    Task<List<ImportedBill>> ParseWeChatCsvAsync(string csvContent);

    /// <summary>
    /// 解析支付宝账单 CSV
    /// </summary>
    /// <param name="csvContent">CSV 内容</param>
    /// <returns>账单列表</returns>
    Task<List<ImportedBill>> ParseAlipayCsvAsync(string csvContent);
}

/// <summary>
/// 导入的账单
/// </summary>
public class ImportedBill
{
    /// <summary>
    /// 交易时间
    /// </summary>
    public DateTime TransactionTime { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 是否为收入
    /// </summary>
    public bool IsIncome { get; set; }

    /// <summary>
    /// 交易描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 商户名称
    /// </summary>
    public string MerchantName { get; set; } = string.Empty;

    /// <summary>
    /// 支付渠道
    /// </summary>
    public string PaymentChannel { get; set; } = string.Empty;

    /// <summary>
    /// 原始交易号
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;
}
