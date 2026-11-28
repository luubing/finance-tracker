namespace FinanceTracker.Core.Services;

/// <summary>
/// 日志清洗：把用户输入（分类名、账本名、支付渠道名、手机号等）写进日志前先过一遍，
/// 防止有人在里面塞换行来伪造日志行（CWE-117）。
/// 日志既写进服务器日志文件，也能在"我的 → 错误日志"里查看和导出。
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// 去掉换行和其他控制字符（制表符、终端转义 ESC 等，以及 Unicode 的行分隔符 U+2028、段分隔符 U+2029）。
    /// null 当作空串；正常的中英文、数字和标点原样保留。
    /// </summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // 先用 Replace 去掉换行：最直接的防护，也是 CodeQL（cs/log-forging）认可的清洗写法
        var text = value.Replace("\r", string.Empty).Replace("\n", string.Empty);

        return string.Concat(text.Where(c => !IsUnsafe(c)));
    }

    private static bool IsUnsafe(char c) => char.IsControl(c) || c is '\u2028' or '\u2029';
}
