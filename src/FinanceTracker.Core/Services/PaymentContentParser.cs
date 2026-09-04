using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 支付内容解析器：从短信/通知栏文本中提取金额、收入/支出类型和支付渠道。
/// 供短信识别（SmsRecognition）与通知栏识别（Notification）复用，
/// 解析规则与 SmsRecognition.razor 中的 ParseSmsContent 保持一致。
/// </summary>
public static partial class PaymentContentParser
{
    /// <summary>
    /// 微信 / 支付宝支付成功通知的包名，通知栏监听只处理这两个应用的支付类通知
    /// </summary>
    public static readonly HashSet<string> PaymentAppPackageNames = new()
    {
        "com.tencent.mm",               // 微信
        "com.eg.android.AlipayGphone",  // 支付宝
    };

    /// <summary>
    /// 解析结果
    /// </summary>
    public record PaymentParseResult(decimal Amount, bool IsIncome, string Channel, string Description);

    [GeneratedRegex(@"(\d+\.?\d*)\s*元")]
    private static partial Regex YuanPattern();

    [GeneratedRegex(@"人民币\s*(\d+\.?\d*)")]
    private static partial Regex RmbPattern();

    [GeneratedRegex(@"¥\s*(\d+\.?\d*)")]
    private static partial Regex YenPattern();

    [GeneratedRegex(@"金额\s*(\d+\.?\d*)")]
    private static partial Regex AmountPattern();

    /// <summary>
    /// 解析支付类文本（短信内容或通知文本）
    /// </summary>
    /// <param name="content">短信内容或通知文本</param>
    /// <returns>解析结果，无法提取金额时 Amount 为 0</returns>
    public static PaymentParseResult Parse(string content)
    {
        var amount = 0m;
        var isIncome = false;
        var channel = string.Empty;

        // 提取金额 - 支持多种格式
        var patterns = new[] { YuanPattern(), RmbPattern(), YenPattern(), AmountPattern() };

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(content);
            if (match.Success)
            {
                amount = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                break;
            }
        }

        // 判断收入/支出
        isIncome = content.Contains("收入") || content.Contains("到账") || content.Contains("转入") || content.Contains("退款");

        // 识别渠道
        if (content.Contains("微信"))
            channel = "微信支付";
        else if (content.Contains("支付宝") || content.Contains("花呗") || content.Contains("余额宝"))
            channel = "支付宝";
        else if (content.Contains("京东") || content.Contains("白条"))
            channel = "京东支付";
        else if (content.Contains("美团"))
            channel = "美团支付";
        else if (content.Contains("云闪付") || content.Contains("银联"))
            channel = "云闪付";
        else
            channel = "银行卡";

        return new PaymentParseResult(amount, isIncome, channel, content);
    }
}