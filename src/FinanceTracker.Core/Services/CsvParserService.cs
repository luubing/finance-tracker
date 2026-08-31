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
        var bills = new List<ImportedBill>();

        await Task.Run(() =>
        {
            var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // 跳过标题行（微信账单前16行是说明信息）
            var startIndex = Array.FindIndex(lines, line => line.Contains("交易时间"));
            if (startIndex < 0) return;

            for (int i = startIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var fields = ParseCsvLine(line);
                    if (fields.Length < 6) continue;

                    var bill = new ImportedBill
                    {
                        TransactionTime = DateTime.ParseExact(fields[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        Description = fields[2],
                        MerchantName = fields[3],
                        Amount = decimal.Parse(fields[4].Replace("¥", "").Trim()),
                        IsIncome = fields[5].Contains("收入"),
                        PaymentChannel = "微信支付",
                        TransactionId = fields.Length > 6 ? fields[6] : ""
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
                        Amount = Math.Abs(decimal.Parse(fields[6].Replace("¥", "").Trim())),
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
}
