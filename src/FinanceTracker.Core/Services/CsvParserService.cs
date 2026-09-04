using System.Globalization;
using System.Text.RegularExpressions;
using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Core.Services;

/// <summary>
/// CSV 解析服务实现
/// </summary>
public class CsvParserService : ICsvParserService
{
    public async Task<List<ImportedBill>> ParseWeChatCsvAsync(string csvContent)
    {
        var result = await ParseWeChatCsvWithStatsAsync(csvContent);
        return result.Bills;
    }

    public async Task<CsvParseResult> ParseWeChatCsvWithStatsAsync(string csvContent)
    {
        var result = new CsvParseResult();

        await Task.Run(() =>
        {
            var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 定位表头行（同时包含“交易时间”和“交易单号”，避免误命中说明文字）
            var startIndex = Array.FindIndex(lines, line => line.Contains("交易时间") && line.Contains("交易单号"));
            if (startIndex < 0) return;

            for (int i = startIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var fields = ParseCsvLine(line);
                    if (fields.Length < 6) continue;

                    // 收/支列：仅接受“收入”/“支出”；“/”、“不计收支”等为中性交易
                    // （充值/提现/零钱通存取/信用卡还款等资金内部转移），不应计入账单
                    var direction = fields[4].Trim();
                    if (direction != "收入" && direction != "支出")
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // 当前状态：跳过已撤销/已失效/已退返/交易关闭/失败/全额退款等无效交易
                    var status = fields.Length > 7 ? fields[7] : string.Empty;
                    if (IsInvalidWeChatStatus(status))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    var bill = new ImportedBill
                    {
                        TransactionTime = ParseWeChatDateTime(fields[0]),
                        Description = BuildWeChatDescription(fields),
                        MerchantName = BuildWeChatMerchant(fields),
                        Amount = ParseAmount(fields[5]),
                        IsIncome = direction == "收入",
                        PaymentChannel = NormalizeWeChatChannel(fields.Length > 6 ? fields[6] : string.Empty),
                        TransactionId = fields.Length > 8 ? fields[8] : ""
                    };

                    if (bill.Amount <= 0)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    result.Bills.Add(bill);
                }
                catch
                {
                    // 跳过解析失败的行
                }
            }
        });

        return result;
    }

    public async Task<List<ImportedBill>> ParseAlipayCsvAsync(string csvContent)
    {
        var bills = new List<ImportedBill>();

        await Task.Run(() =>
        {
            var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 跳过标题行（支付宝账单前几行是说明信息）
            var startIndex = Array.FindIndex(lines, line => line.Contains("交易时间") || line.Contains("交易创建时间"));
            if (startIndex < 0) return;

            for (int i = startIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var fields = ParseCsvLine(line);
                    if (fields.Length < 7) continue;

                    var bill = new ImportedBill
                    {
                        TransactionTime = DateTime.ParseExact(fields[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Description = fields[4],
                        MerchantName = fields[5],
                        // 金额列可能带 +/- 符号（支付宝格式）：先取绝对值，再依据符号/收支柱判断收支方向
                        Amount = Math.Abs(decimal.Parse(fields[6].Replace("¥", "").Trim().TrimStart('+', '-'), CultureInfo.InvariantCulture)),
                        IsIncome = fields[6].Contains("+") || fields[3].Contains("收入"),
                        PaymentChannel = "支付宝",
                        TransactionId = fields.Length > 8 ? fields[8] : ""
                    };

                    bills.Add(bill);
                }
                catch
                {
                    // 跳过解析失败的行
                }
            }
        });

        return bills;
    }

    private string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var currentField = "";
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(currentField.Trim());
                currentField = "";
            }
            else
            {
                currentField += c;
            }
        }

        fields.Add(currentField.Trim());
        return fields.ToArray();
    }

    /// <summary>
    /// 判断微信账单“当前状态”是否为无效交易（撤销/失效/退返/关闭/失败/全额退款）
    /// </summary>
    private static bool IsInvalidWeChatStatus(string status)
    {
        if (IsWeChatPlaceholder(status)) return false;

        return status.Contains("已撤销") ||
               status.Contains("已失效") ||
               status.Contains("已退返") ||
               status.Contains("交易关闭") ||
               status.Contains("已全额退款") ||
               status.Contains("失败");
    }

    /// <summary>
    /// 组装交易描述：优先“商品”，为空占位时依次回退“备注”、“交易类型”
    /// </summary>
    private static string BuildWeChatDescription(string[] fields)
    {
        var product = fields.Length > 3 ? fields[3] : string.Empty;
        if (!IsWeChatPlaceholder(product)) return product;

        var remark = fields.Length > 10 ? fields[10] : string.Empty;
        if (!IsWeChatPlaceholder(remark)) return remark;

        var type = fields.Length > 1 ? fields[1] : string.Empty;
        if (!IsWeChatPlaceholder(type)) return type;

        var counterparty = fields.Length > 2 ? fields[2] : string.Empty;
        return IsWeChatPlaceholder(counterparty) ? "微信交易" : counterparty;
    }

    /// <summary>
    /// 组装商户名称：优先“交易对方”，为空占位时回退“交易类型”
    /// </summary>
    private static string BuildWeChatMerchant(string[] fields)
    {
        var counterparty = fields.Length > 2 ? fields[2] : string.Empty;
        if (!IsWeChatPlaceholder(counterparty)) return counterparty;

        var type = fields.Length > 1 ? fields[1] : string.Empty;
        return IsWeChatPlaceholder(type) ? "微信交易" : type;
    }

    /// <summary>
    /// 微信账单中空值占位符（"/"、空白）
    /// </summary>
    private static bool IsWeChatPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) || value!.Trim() == "/";

    /// <summary>
    /// 将微信“支付方式”归一化为应用内支付渠道名称：
    /// 零钱/零钱通仍属微信支付；信用卡归入“信用卡”；储蓄卡/借记卡/银行卡归入“银行卡”
    /// </summary>
    private static string NormalizeWeChatChannel(string paymentMethod)
    {
        if (IsWeChatPlaceholder(paymentMethod)) return "微信支付";

        var method = paymentMethod.Trim();

        if (method.Contains("信用卡")) return "信用卡";
        if (method.Contains("储蓄卡") || method.Contains("借记卡") || method.Contains("银行卡")) return "银行卡";

        return "微信支付";
    }

    /// <summary>
    /// 解析微信账单日期格式（支持多种格式）
    /// </summary>
    private static DateTime ParseWeChatDateTime(string dateStr)
    {
        dateStr = dateStr.Trim();

        // 尝试 yyyy/M/d H:mm 格式（微信账单常见格式）
        if (DateTime.TryParseExact(dateStr, "yyyy/M/d H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            return result;

        // 尝试 yyyy-MM-dd HH:mm:ss 格式
        if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return result;

        // 尝试通用解析
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return result;

        throw new FormatException($"无法解析日期: {dateStr}");
    }

    /// <summary>
    /// 解析金额（处理全角/半角符号和千分位逗号）
    /// </summary>
    private static decimal ParseAmount(string amountStr)
    {
        // 移除各种货币符号和空白
        amountStr = amountStr
            .Replace("¥", "")
            .Replace("￥", "")
            .Replace(" ", "")
            .Trim();

        // 移除千分位逗号（但保留小数点）
        if (amountStr.Contains(","))
            amountStr = amountStr.Replace(",", "");

        if (decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            return Math.Abs(amount);

        throw new FormatException($"无法解析金额: {amountStr}");
    }
}
