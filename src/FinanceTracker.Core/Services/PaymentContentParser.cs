using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 支付内容解析器：从短信/通知栏文本中提取金额、收入/支出类型和支付渠道。
/// <para>
/// 短信识别页（SmsRecognition.razor）、短信广播接收器（SmsBroadcastReceiver）与通知栏监听
/// （NotificationCaptureService）三个入口必须共用这一份解析规则——各自带一份实现会让同一条短信在不同入口得到不同结果。
/// </para>
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

    /// <summary>
    /// 金额数字（捕获组）：千分位写法（1,234.50）优先，其次是普通数字（1234.5、35）。
    /// 只认普通数字的话，"1,234.50元" 会被截成 "234.50"，金额静默少掉一个千位。
    /// </summary>
    private const string AmountNumber = @"(\d{1,3}(?:,\d{3})+(?:\.\d*)?|\d+\.?\d*)";

    /// <summary>
    /// 货币符号：半角 ¥（U+00A5）与全角 ￥（U+FFE5）。银行/支付类短信里的"￥"多半是全角，两者不是同一个字符
    /// </summary>
    private const string YenSign = @"[\u00A5\uFFE5]";

    [GeneratedRegex(AmountNumber + @"\s*元")]
    private static partial Regex YuanPattern();

    [GeneratedRegex(@"人民币\s*" + AmountNumber)]
    private static partial Regex RmbPattern();

    [GeneratedRegex(YenSign + @"\s*" + AmountNumber)]
    private static partial Regex YenPattern();

    [GeneratedRegex(@"金额\s*" + AmountNumber)]
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

            // 数字大到超出 decimal 范围时 TryParse 失败：按"这种写法没提取到"处理，继续试下一种，
            // 不能抛异常（短信广播/通知监听的处理流程不应被一条异常短信打断）
            if (match.Success && TryParseAmount(match.Groups[1].Value, out var parsed))
            {
                amount = parsed;
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

    /// <summary>
    /// 把匹配到的金额文本转成 decimal：去掉千分位逗号，并固定用 Invariant 区域——
    /// 区域相关的解析会让德语等区域把 "35.5" 当成 355。
    /// </summary>
    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(
            text.Replace(",", string.Empty),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
}