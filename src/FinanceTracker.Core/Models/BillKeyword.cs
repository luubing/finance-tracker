using System.Globalization;

namespace FinanceTracker.Core.Models;

/// <summary>
/// 账单搜索关键字解析：把用户在搜索框里输入的文字拆成两部分，供 <see cref="Services.BillService"/> 组装查询——
/// <list type="bullet">
/// <item><see cref="Text"/>：去掉首尾空白并转成小写，用来匹配备注与分类名称（不区分大小写的包含匹配）；</item>
/// <item><see cref="Amount"/>：关键字<b>整体</b>能解析为数字时的数值，用来匹配「金额等于该数值」的账单（数值相等，不是文本包含）。</item>
/// </list>
/// 纯函数、与系统区域设置无关：数字一律按固定区域（小数点 <c>.</c>）解析，不接受千分位逗号——
/// 「1,5」在德语区是一点五、在中文区是一千五百，把它当数字就会因设备而异，所以只做文本匹配。
/// 含非数字字符的关键字（「35元」「¥35」「1e3」「12,5」）同样只做文本匹配。
/// </summary>
/// <param name="Text">小写化后的关键字（不为空）</param>
/// <param name="Amount">关键字能解析为数字时的数值；否则为 null</param>
public sealed record BillKeyword(string Text, decimal? Amount)
{
    private const NumberStyles AmountStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// 解析搜索关键字；null / 空 / 全空白表示「没有关键字条件」，返回 null。
    /// 文本取首尾去空白后转小写（<see cref="string.ToLowerInvariant"/>，与区域设置无关）。
    /// </summary>
    public static BillKeyword? Parse(string? keyword)
    {
        var trimmed = keyword?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        decimal? amount = decimal.TryParse(trimmed, AmountStyles, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

        return new BillKeyword(trimmed.ToLowerInvariant(), amount);
    }
}
