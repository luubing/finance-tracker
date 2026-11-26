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
                        TransactionTime = ParseBillDateTime(fields[0]),
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

            // 定位表头行：同时有「交易时间」（旧版导出叫「交易创建时间」）和「金额」——支付宝账单前面有一大段导出说明，
            // 单看一个词容易命中说明文字
            var headerIndex = Array.FindIndex(lines, line =>
                (line.Contains("交易时间") || line.Contains("交易创建时间")) && line.Contains("金额"));
            if (headerIndex < 0) return;

            // 各列按表头名称定位，不按固定位置：新旧版导出的列数与顺序不同，位置一变就整列错位
            // （例如把「收/支」当成商家、把「交易状态」当成订单号）
            var columns = AlipayColumns.From(ParseCsvLine(lines[headerIndex].Trim()));
            if (!columns.IsUsable) return;

            for (int i = headerIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var fields = ParseCsvLine(line);
                    if (fields.Length <= columns.RequiredMaxIndex) continue;

                    // 收/支列：仅接受“收入”/“支出”；“不计收支”（信用卡还款、转入余额宝、退款等资金内部转移）不是真正的收支，
                    // 不应计入账单（与微信一致）
                    var direction = columns.Get(fields, columns.Direction);
                    if (direction != "收入" && direction != "支出") continue;

                    // 交易状态：交易关闭 / 失败 / 已撤销等钱没有真正动过的行不计入
                    if (IsInvalidAlipayStatus(columns.Get(fields, columns.Status))) continue;

                    var amount = ParseAmount(columns.Get(fields, columns.Amount));
                    if (amount <= 0) continue;

                    var bill = new ImportedBill
                    {
                        TransactionTime = ParseBillDateTime(columns.Get(fields, columns.Time)),
                        Description = BuildAlipayDescription(columns, fields),
                        MerchantName = BuildAlipayMerchant(columns, fields),
                        Amount = amount,
                        IsIncome = direction == "收入",
                        PaymentChannel = "支付宝",
                        TransactionId = columns.Get(fields, columns.TransactionId),
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
    /// 支付宝账单各列在表头里的位置（按列名定位，找不到的列是 -1）。
    /// 新版导出的列：交易时间、交易分类、交易对方、对方账号、商品说明、收/支、金额、收/付款方式、交易状态、交易订单号、商家订单号、备注；
    /// 旧版导出叫法不同（交易创建时间、商品名称、金额（元）、交易号……），列数与顺序也不一样——所以不能按固定位置取。
    /// </summary>
    private sealed class AlipayColumns
    {
        public int Time { get; private init; } = -1;
        public int Category { get; private init; } = -1;
        public int Counterparty { get; private init; } = -1;
        public int Product { get; private init; } = -1;
        public int Direction { get; private init; } = -1;
        public int Amount { get; private init; } = -1;
        public int Status { get; private init; } = -1;
        public int TransactionId { get; private init; } = -1;

        /// <summary>
        /// 表头里有微信才有的「当前状态」列：这是微信账单（用户选错了来源）。微信与支付宝有好几列同名（交易时间 / 交易对方 / 收/支 / 金额(元)），
        /// 按列名定位会把它们对上号、悄悄导成「支付宝」渠道的账单——不如明确解析失败，让用户换成微信来源。
        /// </summary>
        public bool LooksLikeWeChat { get; private init; }

        /// <summary>解析一行必需的三列（时间 / 收支 / 金额）都在表头里找到了，且不是微信账单</summary>
        public bool IsUsable => Time >= 0 && Direction >= 0 && Amount >= 0 && !LooksLikeWeChat;

        /// <summary>必需的三列里位置最靠后的那个：数据行的字段数不够就没法解析</summary>
        public int RequiredMaxIndex => Math.Max(Time, Math.Max(Direction, Amount));

        public static AlipayColumns From(string[] header)
        {
            var names = header.Select(NormalizeHeaderName).ToArray();

            // 同一种含义新旧版叫法不同：按优先级依次找
            int Find(params string[] candidates)
            {
                foreach (var candidate in candidates)
                {
                    var index = Array.IndexOf(names, candidate);
                    if (index >= 0) return index;
                }

                return -1;
            }

            return new AlipayColumns
            {
                Time = Find("交易时间", "交易创建时间", "付款时间"),
                Category = Find("交易分类"),
                Counterparty = Find("交易对方"),
                Product = Find("商品说明", "商品名称"),
                Direction = Find("收/支"),
                Amount = Find("金额", "金额(元)"),
                Status = Find("交易状态"),
                TransactionId = Find("交易订单号", "交易号"),
                LooksLikeWeChat = Array.IndexOf(names, "当前状态") >= 0,
            };
        }

        /// <summary>取某一列的值：这一列不存在、或这一行字段不够时是空串</summary>
        public string Get(string[] fields, int index) =>
            index >= 0 && index < fields.Length ? fields[index] : string.Empty;

        // 去掉 BOM / 空白，全角括号换成半角：「金额（元）」与「金额(元)」是同一列
        private static string NormalizeHeaderName(string name) =>
            name.Trim('\uFEFF', ' ', '\t', '\r').Replace('（', '(').Replace('）', ')');
    }

    /// <summary>
    /// 判断支付宝账单“交易状态”是否为无效交易（交易关闭/失败/已撤销/已失效/已全额退款）：钱没有真正动过，不应计入账单
    /// </summary>
    private static bool IsInvalidAlipayStatus(string status)
    {
        if (IsPlaceholder(status)) return false;

        return status.Contains("交易关闭") ||
               status.Contains("已关闭") ||
               status.Contains("已撤销") ||
               status.Contains("已失效") ||
               status.Contains("已全额退款") ||
               status.Contains("失败");
    }

    /// <summary>
    /// 组装支付宝交易描述：优先“商品说明”，为空占位时依次回退“交易分类”、“交易对方”
    /// </summary>
    private static string BuildAlipayDescription(AlipayColumns columns, string[] fields)
    {
        var product = columns.Get(fields, columns.Product);
        if (!IsPlaceholder(product)) return product;

        var category = columns.Get(fields, columns.Category);
        if (!IsPlaceholder(category)) return category;

        var counterparty = columns.Get(fields, columns.Counterparty);
        return IsPlaceholder(counterparty) ? "支付宝交易" : counterparty;
    }

    /// <summary>
    /// 组装支付宝商户名称：优先“交易对方”，为空占位时回退“交易分类”
    /// </summary>
    private static string BuildAlipayMerchant(AlipayColumns columns, string[] fields)
    {
        var counterparty = columns.Get(fields, columns.Counterparty);
        if (!IsPlaceholder(counterparty)) return counterparty;

        var category = columns.Get(fields, columns.Category);
        return IsPlaceholder(category) ? "支付宝交易" : category;
    }

    /// <summary>
    /// 判断微信账单“当前状态”是否为无效交易（撤销/失效/退返/关闭/失败/全额退款）
    /// </summary>
    private static bool IsInvalidWeChatStatus(string status)
    {
        if (IsPlaceholder(status)) return false;

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
        if (!IsPlaceholder(product)) return product;

        var remark = fields.Length > 10 ? fields[10] : string.Empty;
        if (!IsPlaceholder(remark)) return remark;

        var type = fields.Length > 1 ? fields[1] : string.Empty;
        if (!IsPlaceholder(type)) return type;

        var counterparty = fields.Length > 2 ? fields[2] : string.Empty;
        return IsPlaceholder(counterparty) ? "微信交易" : counterparty;
    }

    /// <summary>
    /// 组装商户名称：优先“交易对方”，为空占位时回退“交易类型”
    /// </summary>
    private static string BuildWeChatMerchant(string[] fields)
    {
        var counterparty = fields.Length > 2 ? fields[2] : string.Empty;
        if (!IsPlaceholder(counterparty)) return counterparty;

        var type = fields.Length > 1 ? fields[1] : string.Empty;
        return IsPlaceholder(type) ? "微信交易" : type;
    }

    /// <summary>
    /// 账单文件中空值占位符（"/"、空白）：微信与支付宝导出文件都这样表示「没有」
    /// </summary>
    private static bool IsPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) || value!.Trim() == "/";

    /// <summary>
    /// 将微信“支付方式”归一化为应用内支付渠道名称：
    /// 零钱/零钱通仍属微信支付；信用卡归入“信用卡”；储蓄卡/借记卡/银行卡归入“银行卡”
    /// </summary>
    private static string NormalizeWeChatChannel(string paymentMethod)
    {
        if (IsPlaceholder(paymentMethod)) return "微信支付";

        var method = paymentMethod.Trim();

        if (method.Contains("信用卡")) return "信用卡";
        if (method.Contains("储蓄卡") || method.Contains("借记卡") || method.Contains("银行卡")) return "银行卡";

        return "微信支付";
    }

    /// <summary>
    /// 解析账单日期格式（微信 / 支付宝，支持多种格式）
    /// </summary>
    private static DateTime ParseBillDateTime(string dateStr)
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
