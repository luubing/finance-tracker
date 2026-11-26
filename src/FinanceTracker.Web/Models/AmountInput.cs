using System.Globalization;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 记一笔的金额输入规则：自定义数字键盘每按一个键，由这里算出新的金额文本（界面只负责渲染与转发按键）。
/// <para>
/// 金额文本就是用户「已经敲出来的样子」：<c>""</c>（还没敲）、<c>"35"</c>、<c>"35."</c>（刚敲了小数点）、<c>"35.5"</c>、<c>"35.50"</c>。
/// 文本只含数字和至多一个小数点，没有千分位、没有符号；显示用 <see cref="Format"/> 加千分位，保存用 <see cref="Parse"/> 取数值。
/// </para>
/// <para>
/// 规则（spec 记一笔 Sheet · 金额规则）：最多两位小数；上限 99,999,999.99（整数部分最多 8 位）；不允许多个小数点；
/// 不出现前导零（<c>"05"</c> 会变成 <c>"5"</c>）；<c>00</c> 键等于连按两次 <c>0</c>；退格删一个字符；长按退格清空。
/// 违反规则的按键被忽略（返回原文本），不报错、不弹提示。
/// </para>
/// </summary>
public static class AmountInput
{
    /// <summary>金额上限（含）</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    /// <summary>最多保留的小数位数</summary>
    public const int MaxDecimals = 2;

    /// <summary>整数部分最多几位（<see cref="MaxAmount"/> 的整数位数）</summary>
    public const int MaxIntegerDigits = 8;

    /// <summary>小数点键</summary>
    public const string KeyDot = ".";

    /// <summary>「00」键</summary>
    public const string KeyDoubleZero = "00";

    /// <summary>退格键</summary>
    public const string KeyBackspace = "backspace";

    /// <summary>清空（长按退格）</summary>
    public const string KeyClear = "clear";

    /// <summary>
    /// 按一个键后的金额文本。<paramref name="key"/> 取 <c>"0"</c>–<c>"9"</c>、<see cref="KeyDot"/>、<see cref="KeyDoubleZero"/>、
    /// <see cref="KeyBackspace"/>、<see cref="KeyClear"/>；不认识的键什么也不做。
    /// </summary>
    public static string Apply(string? current, string? key)
    {
        var text = Normalize(current);

        return key switch
        {
            KeyClear => string.Empty,
            KeyBackspace => text.Length == 0 ? text : text[..^1],
            KeyDot => AppendDot(text),
            KeyDoubleZero => AppendDigit(AppendDigit(text, '0'), '0'),
            { Length: 1 } when key[0] is >= '0' and <= '9' => AppendDigit(text, key[0]),
            _ => text,
        };
    }

    /// <summary>
    /// 金额文本对应的数值；没敲（<c>""</c>）或只有小数点（<c>"."</c>）时为 <c>null</c>。<c>"35."</c> 是 35。
    /// </summary>
    public static decimal? Parse(string? text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0 || normalized == ".")
        {
            return null;
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// 数值转成金额文本（语音预填、编辑回填用）：保留两位小数以内、限制在 [0, 上限]；整数不带小数点，有小数时补满两位（<c>12.3</c> → <c>"12.30"</c>）。
    /// 零或负数视为「没有金额」，返回 <c>""</c>。
    /// </summary>
    public static string FromDecimal(decimal amount)
    {
        var rounded = decimal.Round(Math.Min(amount, MaxAmount), MaxDecimals, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
        {
            return string.Empty;
        }

        return rounded == decimal.Truncate(rounded)
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 展示用：整数部分加千分位，小数部分原样（<c>"12000.5"</c> → <c>"12,000.5"</c>；<c>"35."</c> → <c>"35."</c>，保留刚敲的小数点）；
    /// 还没敲时显示 <c>"0"</c>。
    /// </summary>
    public static string Format(string? text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return "0";
        }

        var dot = normalized.IndexOf('.');
        var integer = dot < 0 ? normalized : normalized[..dot];
        var rest = dot < 0 ? string.Empty : normalized[dot..];

        if (integer.Length == 0)
        {
            integer = "0";
        }

        return Group(integer) + rest;
    }

    /// <summary>展示用：「还没敲」（显示成浅色的 0）</summary>
    public static bool IsEmpty(string? text) => Normalize(text).Length == 0;

    /// <summary>
    /// 金额字号档位（金额越长字越小，免得超出一行）：按 <see cref="Format"/> 后的字符数分档
    /// （≤ 9 个字符 <c>xl</c>，≤ 12 个 <c>lg</c>，再长 <c>md</c>；上限 <c>99,999,999.99</c> 恰好 13 个字符）。
    /// </summary>
    public static string SizeOf(string? text)
    {
        var length = Format(text).Length;
        return length switch
        {
            <= 9 => "xl",
            <= 12 => "lg",
            _ => "md",
        };
    }

    /// <summary>
    /// 无障碍 / E2E 用的读法：<c>"¥35.00"</c>（有效金额，两位小数）；没敲或为零时是 <c>"¥0.00"</c>。
    /// </summary>
    public static string Spoken(string? text) =>
        "¥" + (Parse(text) ?? 0m).ToString("N2", CultureInfo.InvariantCulture);

    private static string AppendDot(string text)
    {
        if (text.Contains('.'))
        {
            return text;
        }

        return text.Length == 0 ? "0." : text + ".";
    }

    private static string AppendDigit(string text, char digit)
    {
        // 整数部分只有一个 0：再按数字是「替换」不是「追加」，不出现 05、00
        if (text == "0")
        {
            return digit.ToString();
        }

        var dot = text.IndexOf('.');
        if (dot >= 0)
        {
            if (text.Length - dot - 1 >= MaxDecimals)
            {
                return text;
            }
        }
        else if (text.Length >= MaxIntegerDigits)
        {
            return text;
        }

        var next = text + digit;
        return Parse(next) is { } value && value > MaxAmount ? text : next;
    }

    /// <summary>只保留数字和第一个小数点（防御：外部传入的文本不一定干净）</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var seenDot = false;
        foreach (var c in text)
        {
            if (c is >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (c == '.' && !seenDot)
            {
                seenDot = true;
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string Group(string digits)
    {
        if (digits.Length <= 3)
        {
            return digits;
        }

        var builder = new System.Text.StringBuilder(digits.Length + digits.Length / 3);
        var head = digits.Length % 3;
        if (head > 0)
        {
            builder.Append(digits, 0, head);
        }

        for (var i = head; i < digits.Length; i += 3)
        {
            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(digits, i, 3);
        }

        return builder.ToString();
    }
}
