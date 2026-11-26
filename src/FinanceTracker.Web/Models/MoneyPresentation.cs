using System.Globalization;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 金额拆成三段，供「整数大字 + 小数小字」的展示：符号、整数部分（含千分位）、小数部分（含小数点）。
/// </summary>
/// <param name="Sign">负数为 "-"；要求带正号且金额为正时为 "+"；其余为空</param>
/// <param name="Integer">整数部分，含千分位，例如 "8,713"</param>
/// <param name="Fraction">小数部分，含小数点，恒为两位，例如 ".50"</param>
public readonly record struct AmountParts(string Sign, string Integer, string Fraction);

/// <summary>
/// 金额的展示规则（纯函数，单元测试覆盖）：币种符号、千分位、两位小数、正负号。
/// 与现有页面的 <c>ToString("N2")</c> 口径一致，但不依赖运行环境的区域设置（千分位与小数点固定为 , 与 .），
/// 这样 MAUI 设备、服务器、测试机上同一个金额总是同一个字符串。
/// </summary>
public static class MoneyPresentation
{
    /// <summary>人民币符号</summary>
    public const string Symbol = "¥";

    /// <summary>
    /// 把金额拆成符号 / 整数 / 小数三段。金额先按「四舍五入到分」处理，
    /// 舍入后为 0 的金额（例如 -0.001）不带符号。
    /// </summary>
    /// <param name="amount">金额</param>
    /// <param name="plusForPositive">正数是否带 "+"（收入类展示用）</param>
    public static AmountParts Split(decimal amount, bool plusForPositive = false)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        var sign = rounded < 0 ? "-" : plusForPositive && rounded > 0 ? "+" : string.Empty;
        var text = Math.Abs(rounded).ToString("N2", CultureInfo.InvariantCulture);
        var dot = text.LastIndexOf('.');

        return new AmountParts(sign, text[..dot], text[dot..]);
    }

    /// <summary>
    /// 完整金额文本：¥1,234.50；负数为 -¥1,234.50
    /// </summary>
    public static string Format(decimal amount)
    {
        var parts = Split(amount);
        return $"{parts.Sign}{Symbol}{parts.Integer}{parts.Fraction}";
    }

    /// <summary>
    /// 账单金额文本：支出带负号（-¥35.00），收入带正号（+¥12,000.00）；金额为 0 不带符号。
    /// 账单金额本身恒为正，收支方向由账单类型决定，所以这里取绝对值再按方向加符号。
    /// </summary>
    /// <param name="amount">账单金额（取绝对值）</param>
    /// <param name="isIncome">是否收入</param>
    public static string Signed(decimal amount, bool isIncome)
    {
        var parts = Split(Math.Abs(amount));
        var isZero = parts is { Integer: "0", Fraction: ".00" };
        var sign = isZero ? string.Empty : isIncome ? "+" : "-";

        return $"{sign}{Symbol}{parts.Integer}{parts.Fraction}";
    }
}
