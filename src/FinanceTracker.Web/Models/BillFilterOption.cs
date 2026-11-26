using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 筛选条件里的一个可选项（分类 / 支付渠道 / 账本）：筛选 Sheet 画标签、账单 Tab 的条件条取名字都用它。
/// 展示用的图标与配色在构造时一次算好（图标经过合法性校验，配色与账单行里同一个分类的图标块一致）。
/// </summary>
/// <param name="Id">主键</param>
/// <param name="Name">名称</param>
/// <param name="Icon">MDI 图标类名（已校验）</param>
/// <param name="Tone">图标块配色（.ft-tile--{色}）</param>
/// <param name="Type">分类才有：支出 / 收入；其它为空</param>
public sealed record BillFilterOption(Guid Id, string Name, string Icon, string Tone, BillType? Type = null)
{
    /// <summary>支付渠道的回退图标</summary>
    public const string DefaultChannelIcon = "mdi-credit-card-outline";

    /// <summary>由分类构造（图标与配色同账单行里的分类图标块）</summary>
    public static BillFilterOption FromCategory(Category category) => new(
        category.Id,
        category.Name,
        BillPresentation.CategoryIcon(category.Icon),
        BillPresentation.CategoryTone(category.Name, category.Id),
        category.Type);

    /// <summary>由支付渠道构造（渠道标签只用普通小图标，配色是中性灰）</summary>
    public static BillFilterOption FromChannel(PaymentChannel channel) => new(
        channel.Id,
        channel.Name,
        MdiIcons.Normalize(channel.Icon, DefaultChannelIcon),
        "gray");
}
