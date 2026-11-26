using FinanceTracker.Core.Enums;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 分类管理页的展示规则（纯函数，单元测试覆盖）：支出 / 收入分段、图标候选与配色、默认菜单文案。
/// 页面只负责取数与交互，规则集中在这里，页面里不散落字符串拼接。
/// </summary>
public static class CategoryPresentation
{
    /// <summary>「支出」分段的取值（与 <see cref="BillType.Expense"/> 同名）</summary>
    public const string SegmentExpense = "Expense";

    /// <summary>「收入」分段的取值（与 <see cref="BillType.Income"/> 同名）</summary>
    public const string SegmentIncome = "Income";

    /// <summary>
    /// 新建分类的默认图标，也是历史数据里图标缺失 / 非法时的回退（与账单行、筛选面板里的分类回退图标一致）
    /// </summary>
    public const string DefaultIcon = BillPresentation.DefaultCategoryIcon;

    /// <summary>
    /// 分类页顶部的分段：只有支出 / 收入（账单页的「全部」在这里没有意义——每个分类只属于一种账单类型）
    /// </summary>
    public static IReadOnlyList<(string Value, string Label)> Segments { get; } =
    [
        (SegmentExpense, "支出"),
        (SegmentIncome, "收入"),
    ];

    /// <summary>
    /// 分段取值对应的账单类型；不认识的取值按支出处理
    /// </summary>
    public static BillType TypeOf(string? segment) =>
        string.Equals(segment, SegmentIncome, StringComparison.Ordinal) ? BillType.Income : BillType.Expense;

    /// <summary>
    /// 账单类型对应的分段取值
    /// </summary>
    public static string SegmentOf(BillType type) => type == BillType.Income ? SegmentIncome : SegmentExpense;

    /// <summary>
    /// 图标选择器里的候选图标（7 行 × 6 列）。名称均已对照应用引用的 Material Design Icons 7.2.96 的字体 CSS 逐个核对存在
    /// （数据库里保存的是 mdi 图标名，选了字体里没有的图标会渲染成空白）。
    /// 覆盖预设分类用到的图标之外，再补常见的自建分类场景：吃喝、购物、出行、居住、健康学习娱乐、收入理财、宠物育儿等。
    /// </summary>
    public static IReadOnlyList<IconChoice> IconChoices { get; } =
    [
        new("mdi-food", "餐饮"),
        new("mdi-coffee", "咖啡"),
        new("mdi-cake-variant", "甜点"),
        new("mdi-glass-mug-variant", "饮品"),
        new("mdi-fruit-cherries", "水果"),
        new("mdi-silverware-fork-knife", "正餐"),

        new("mdi-cart", "购物"),
        new("mdi-shopping", "日用"),
        new("mdi-tshirt-crew", "服饰"),
        new("mdi-gift", "礼物"),
        new("mdi-cellphone", "数码"),
        new("mdi-flower", "鲜花"),

        new("mdi-car", "汽车"),
        new("mdi-bus", "公交"),
        new("mdi-train", "火车"),
        new("mdi-airplane", "飞机"),
        new("mdi-bike", "骑行"),
        new("mdi-gas-station", "加油"),

        new("mdi-home", "居住"),
        new("mdi-lightbulb", "水电"),
        new("mdi-sofa", "家具"),
        new("mdi-wifi", "网络"),
        new("mdi-phone", "通讯"),
        new("mdi-washing-machine", "家电"),

        new("mdi-hospital-box", "医疗"),
        new("mdi-pill", "药品"),
        new("mdi-dumbbell", "健身"),
        new("mdi-school", "教育"),
        new("mdi-gamepad-variant", "游戏"),
        new("mdi-movie-open", "电影"),

        new("mdi-cash", "现金"),
        new("mdi-chart-line", "理财"),
        new("mdi-briefcase", "工作"),
        new("mdi-piggy-bank", "储蓄"),
        new("mdi-wallet", "钱包"),
        new("mdi-bank", "银行"),

        new("mdi-paw", "宠物"),
        new("mdi-baby-carriage", "育儿"),
        new("mdi-music", "音乐"),
        new("mdi-tag", "通用"), // 新建分类的默认图标：必须在候选里，用户换了别的图标之后还能再点回来
        new("mdi-heart", "爱心"),
        new("mdi-dots-horizontal", "其他"),
    ];

    /// <summary>
    /// 分类图标：合法就用，否则回退通用标签图标
    /// </summary>
    public static string NormalizeIcon(string? icon) => BillPresentation.CategoryIcon(icon);

    /// <summary>
    /// 分类图标块的配色：预设分类固定配色，自定义分类按 Id 稳定取色。
    /// 与账单行、统计图例里同一个分类的颜色一致（同一套规则，见 <see cref="BillPresentation.CategoryTone"/>）。
    /// 数据库里的分类没有颜色字段，所以新建 / 编辑分类时不能自选颜色。
    /// </summary>
    public static string ToneFor(string? name, Guid? id) => BillPresentation.CategoryTone(name, id);

    /// <summary>
    /// 预设项点按后菜单里「设为默认 / 取消默认」那一项的文案（默认值按账单类型分开：默认支出分类、默认收入分类各有一个）
    /// </summary>
    public static string DefaultMenuLabel(BillType type, bool isDefault)
    {
        if (isDefault)
        {
            return "取消默认";
        }

        return type == BillType.Income ? "设为默认收入分类" : "设为默认支出分类";
    }

    /// <summary>
    /// 设为 / 取消默认之后的提示
    /// </summary>
    public static string DefaultChangedMessage(bool nowDefault) => nowDefault ? "已设置为默认分类" : "已取消默认分类";

    /// <summary>
    /// 新建分类时输入框的占位提示（按账单类型举例）
    /// </summary>
    public static string NamePlaceholder(BillType type) => type == BillType.Income ? "例如：稿费" : "例如：宠物";
}
