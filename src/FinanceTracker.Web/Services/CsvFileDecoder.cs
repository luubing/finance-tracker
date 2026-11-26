using System.Text;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 把导出的账单文件（字节）解码成文本：微信 / 支付宝的 CSV 常常是 GB18030 编码，也可能是带 BOM 的 UTF-8 / UTF-16。
/// 从导入页拆出来是为了能用单元测试覆盖各种编码；判断顺序与旧导入页完全一致。
/// </summary>
public static class CsvFileDecoder
{
    static CsvFileDecoder()
    {
        // 注册编码提供程序，才能使用 GB18030 等代码页编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 检测编码并解码：
    /// BOM（UTF-8 / UTF-16 LE / UTF-16 BE）优先；没有 BOM 时先试 UTF-8，且文本里要有账单表头里的「交易时间」或「交易类型」才算数
    /// （GB18030 的字节常常也能被 UTF-8 当成乱码解出来，不能只看「能不能解」）；否则按 GB18030 解码，GB18030 不可用时退回 UTF-8。
    /// </summary>
    /// <param name="bytes">文件内容</param>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // 检测 BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        // 尝试 UTF-8（无 BOM）：解码结果里要有账单表头里的常见字样才算成功
        var utf8Text = Encoding.UTF8.GetString(bytes);
        if (utf8Text.Contains("交易时间") || utf8Text.Contains("交易类型"))
        {
            return utf8Text;
        }

        // 默认使用 GB18030（微信 / 支付宝账单标准编码）
        try
        {
            return Encoding.GetEncoding("gb18030").GetString(bytes);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // 平台不支持 GB18030：最后回退到 UTF-8
            return utf8Text;
        }
    }
}
