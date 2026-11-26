using System.Text.RegularExpressions;

namespace FinanceTracker.Web.Models;

/// <summary>
/// Material Design Icons 图标类名的校验。图标类名来自数据库（用户自建的账本 / 分类可能手填错误，历史数据也可能为空），
/// 直接拼进 <c>class="mdi ..."</c> 会渲染出空白色块甚至注入额外的类名，所以统一经这里校验，非法就回退。
/// </summary>
public static partial class MdiIcons
{
    [GeneratedRegex(@"^mdi-[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IconPattern();

    /// <summary>
    /// 字体里已经不存在的图标 → 替代图标。类名合法、但应用引用的 MDI 版本（7.2.96）里没有对应字形，渲染出来是空白；
    /// 预设支付渠道「支付宝」的 <c>mdi-alipay</c> 就是这样（品牌图标已从 MDI 移除）。
    /// 数据库里的老数据不改，展示时统一替换，之后修正预设数据时这里仍然兜得住用户设备上的历史数据。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RetiredIcons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mdi-alipay"] = "mdi-qrcode-scan",
    };

    /// <summary>
    /// 图标类名合法（形如 mdi-book-open-variant）就原样使用（去掉首尾空白），否则返回回退图标；
    /// 字体里已不存在的图标换成替代图标（<see cref="RetiredIcons"/>）。
    /// </summary>
    /// <param name="icon">数据库里的图标类名</param>
    /// <param name="fallback">非法 / 为空时使用的图标</param>
    public static string Normalize(string? icon, string fallback)
    {
        var trimmed = icon?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !IconPattern().IsMatch(trimmed))
        {
            return fallback;
        }

        return RetiredIcons.TryGetValue(trimmed, out var replacement) ? replacement : trimmed;
    }
}
