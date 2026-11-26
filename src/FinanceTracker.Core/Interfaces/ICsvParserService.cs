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
    /// 解析微信账单 CSV（含跳过行统计：中性交易、已撤销等无效交易）
    /// </summary>
    /// <param name="csvContent">CSV 内容</param>
    /// <returns>解析结果（账单列表 + 跳过行数）</returns>
    Task<CsvParseResult> ParseWeChatCsvWithStatsAsync(string csvContent);

    /// <summary>
    /// 解析支付宝账单 CSV：各列按<b>表头名称</b>定位（新旧版导出的列数与顺序不同，不能按固定位置取），
    /// 只收「收/支」是收入 / 支出、交易状态有效（不是交易关闭 / 失败等）、金额大于 0 的行；
    /// 「不计收支」这类资金内部转移、解析失败的行静默丢弃
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

/// <summary>
/// CSV 解析结果（账单列表 + 跳过行统计）
/// </summary>
public class CsvParseResult
{
    /// <summary>
    /// 解析出的账单列表
    /// </summary>
    public List<ImportedBill> Bills { get; set; } = new();

    /// <summary>
    /// 被跳过的行数（中性交易、已撤销/已失效等无效交易、无法解析的行）
    /// </summary>
    public int SkippedCount { get; set; }
}
