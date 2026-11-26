namespace FinanceTracker.Web.Models;

/// <summary>
/// 支付渠道管理页的展示规则（纯函数，单元测试覆盖）：图标候选与配色、默认菜单文案。
/// 页面只负责取数与交互，规则集中在这里，页面里不散落字符串拼接。
/// </summary>
public static class PaymentChannelPresentation
{
    /// <summary>
    /// 新建支付渠道的默认图标，也是历史数据里图标缺失 / 非法时的回退（与旧页面一致）
    /// </summary>
    public const string DefaultIcon = "mdi-credit-card";

    /// <summary>
    /// 图标选择器里的候选图标（4 行 × 6 列）。名称均已对照应用引用的 Material Design Icons 7.2.96 的字体 CSS 逐个核对存在
    /// （预设渠道「支付宝」用的 mdi-alipay 已从字体里移除，所以不在候选里，历史数据由 <see cref="MdiIcons"/> 兜底替换）。
    /// </summary>
    public static IReadOnlyList<IconChoice> IconChoices { get; } =
    [
        new("mdi-wechat", "微信"),
        new("mdi-credit-card", "银行卡"),
        new("mdi-credit-card-outline", "信用卡"),
        new("mdi-cash", "现金"),
        new("mdi-qrcode-scan", "扫码支付"),
        new("mdi-cellphone-nfc", "手机支付"),

        new("mdi-bank", "银行"),
        new("mdi-wallet", "钱包"),
        new("mdi-apple", "苹果"),
        new("mdi-contactless-payment", "闪付"),
        new("mdi-hand-coin", "收款"),
        new("mdi-cash-multiple", "钞票"),

        new("mdi-shopping", "购物"),
        new("mdi-food", "餐饮"),
        new("mdi-cart", "购物车"),
        new("mdi-gift", "礼品卡"),
        new("mdi-ticket-percent", "优惠券"),
        new("mdi-piggy-bank", "储蓄"),

        new("mdi-swap-horizontal", "转账"),
        new("mdi-account-cash", "账户"),
        new("mdi-receipt", "账单"),
        new("mdi-gold", "黄金"),
        new("mdi-star", "星标"),
        new("mdi-dots-horizontal", "其他"),
    ];

    /// <summary>
    /// 支付渠道图标：合法就用，否则回退默认图标；字体里已不存在的图标换成替代图标
    /// </summary>
    public static string NormalizeIcon(string? icon) => MdiIcons.Normalize(icon, DefaultIcon);

    /// <summary>
    /// 图标块的配色：按支付渠道 Id 稳定取色（预设渠道的 Id 是固定常量，所以各设备上颜色一致；调整排序也不会换色）
    /// </summary>
    public static string ToneFor(Guid channelId)
    {
        // Guid.GetHashCode 由 16 个字节决定、不随进程变化（不像 string.GetHashCode 每次启动都不同）
        var tones = BillPresentation.Tones;
        return tones[(int)((uint)channelId.GetHashCode() % (uint)tones.Count)];
    }

    /// <summary>
    /// 预设项点按后菜单里「设为默认 / 取消默认」那一项的文案
    /// </summary>
    public static string DefaultMenuLabel(bool isDefault) => isDefault ? "取消默认" : "设为默认支付渠道";

    /// <summary>
    /// 设为 / 取消默认之后的提示
    /// </summary>
    public static string DefaultChangedMessage(bool nowDefault) => nowDefault ? "已设置为默认支付渠道" : "已取消默认支付渠道";
}
