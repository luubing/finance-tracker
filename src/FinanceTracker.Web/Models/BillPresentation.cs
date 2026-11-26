using System.Globalization;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Time;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 账单行（首页「最近账单」、账单 Tab 的列表共用）渲染所需的全部文字与配色，取数时一次性算好，渲染时不再查库。
/// </summary>
/// <param name="Id">账单 Id</param>
/// <param name="Title">标题：分类 · 备注（没有备注时只有分类）</param>
/// <param name="Subtitle">副标题：支付渠道 · 账本（历史无账本的账单标注「未归属账本」）</param>
/// <param name="AmountText">金额：支出 -¥35.00，收入 +¥12,000.00</param>
/// <param name="IsIncome">是否收入（决定金额颜色）</param>
/// <param name="TimeText">时间文案（应用时区）</param>
/// <param name="Icon">分类图标（已校验）</param>
/// <param name="Tone">图标块配色（.ft-tile--soft.ft-tile--{色}）</param>
/// <param name="AriaLabel">读屏名称：把一行里的信息连成一句话</param>
/// <param name="CategoryName">分类名（标题的前半段）；给了就让账单行把分类名和备注分成两段排版（设计稿 02：分类加粗、「 · 备注」灰色常规），不给就按整句 Title 显示</param>
/// <param name="NoteText">备注（已去首尾空白）；没有备注为空</param>
public sealed record BillRowModel(
    Guid Id,
    string Title,
    string Subtitle,
    string AmountText,
    bool IsIncome,
    string TimeText,
    string Icon,
    string Tone,
    string AriaLabel,
    string? CategoryName = null,
    string? NoteText = null);

/// <summary>
/// 账单行的展示规则（纯函数，单元测试覆盖）：标题 / 副标题 / 金额 / 时间文案、分类图标与配色。
/// 页面只负责取数与交互，规则集中在这里，页面里不散落字符串拼接。
/// </summary>
public static class BillPresentation
{
    /// <summary>历史上没有账本的账单在副标题里的标注（与统计里的「未归属账本」同名）</summary>
    public const string UnassignedLedgerLabel = "未归属账本";

    /// <summary>账单的分类被删除 / 缺失时的名称</summary>
    public const string UnknownCategoryLabel = "未知分类";

    /// <summary>账单的支付渠道被删除 / 缺失时的名称</summary>
    public const string UnknownChannelLabel = "未知渠道";

    /// <summary>分类图标缺失 / 非法时的回退</summary>
    public const string DefaultCategoryIcon = "mdi-tag";

    /// <summary>用户自定义分类按分类 Id 稳定取色时用的色板（不含灰色：灰色留给「其他」与无分类）</summary>
    public static IReadOnlyList<string> Tones { get; } = ["purple", "teal", "pink", "orange", "blue", "green", "red"];

    // 预设分类的固定配色：餐饮橙、交通蓝、工资绿……与设计稿一致；其余（用户自定义分类）按 Id 取色
    private static readonly IReadOnlyDictionary<string, string> PresetTones = new Dictionary<string, string>
    {
        ["餐饮美食"] = "orange",
        ["交通出行"] = "blue",
        ["日用百货"] = "teal",
        ["购物消费"] = "pink",
        ["娱乐休闲"] = "purple",
        ["医疗健康"] = "red",
        ["教育培训"] = "blue",
        ["居住生活"] = "green",
        ["通讯物流"] = "teal",
        ["其他支出"] = "gray",
        ["工资薪酬"] = "green",
        ["奖金补贴"] = "orange",
        ["投资理财"] = "purple",
        ["兼职副业"] = "blue",
        ["其他收入"] = "gray",
    };

    /// <summary>
    /// 把账单（需带上 Category / PaymentChannel / Ledger 导航属性）转成列表行。
    /// </summary>
    /// <param name="bill">账单；TransactionTime 是 UTC</param>
    /// <param name="nowLocal">应用时区的「现在」（用于判断今天 / 昨天；由调用方传入以便测试）</param>
    /// <param name="includeDate">是否带日期：按天分组的列表只显示时分，首页的混合列表要带日期</param>
    public static BillRowModel ToRow(Bill bill, DateTime nowLocal, bool includeDate)
    {
        var isIncome = bill.Type == BillType.Income;
        var categoryName = string.IsNullOrWhiteSpace(bill.Category?.Name) ? UnknownCategoryLabel : bill.Category!.Name;

        var title = Title(categoryName, bill.Note);
        var subtitle = Subtitle(bill.PaymentChannel?.Name, bill.Ledger?.Name);
        var amountText = MoneyPresentation.Signed(bill.Amount, isIncome);
        var timeText = TimeText(AppTimeZone.ToLocal(bill.TransactionTime), nowLocal, includeDate);

        return new BillRowModel(
            bill.Id,
            title,
            subtitle,
            amountText,
            isIncome,
            timeText,
            CategoryIcon(bill.Category?.Icon),
            CategoryTone(bill.Category?.Name, bill.CategoryId),
            $"{title}，{subtitle}，{(isIncome ? "收入" : "支出")} {MoneyPresentation.Format(Math.Abs(bill.Amount))}，{timeText}",
            categoryName,
            NoteText(bill.Note));
    }

    /// <summary>
    /// 备注文字：去首尾空白，中间的换行 / 连续空白折成一个空格（与 HTML 单行显示的空白折叠一致；
    /// 账单行的备注框用 <c>white-space: pre</c> 保住「 · 」前面的空格，不折叠的换行会把一行撑成两行）；
    /// 没有备注（空或全是空白）为空，账单行据此决定要不要画「 · 备注」那一段
    /// </summary>
    public static string? NoteText(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return null;
        return string.Join(' ', note.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// 行标题：分类 · 备注；备注为空（或全是空白）时只显示分类
    /// </summary>
    public static string Title(string categoryName, string? note)
    {
        var trimmed = NoteText(note);
        return trimmed is null ? categoryName : $"{categoryName} · {trimmed}";
    }

    /// <summary>
    /// 行副标题：支付渠道 · 账本。没有账本的历史账单标注「未归属账本」，不显示成空
    /// </summary>
    public static string Subtitle(string? channelName, string? ledgerName)
    {
        var channel = string.IsNullOrWhiteSpace(channelName) ? UnknownChannelLabel : channelName.Trim();
        var ledger = string.IsNullOrWhiteSpace(ledgerName) ? UnassignedLedgerLabel : ledgerName.Trim();
        return $"{channel} · {ledger}";
    }

    /// <summary>
    /// 时间文案（参数都是应用时区的墙上时间）。
    /// 不带日期：HH:mm。带日期：今天只显示 HH:mm，昨天「昨天 HH:mm」，同年「MM-dd HH:mm」，更早「yyyy-MM-dd HH:mm」。
    /// </summary>
    public static string TimeText(DateTime local, DateTime nowLocal, bool includeDate)
    {
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (!includeDate)
        {
            return clock;
        }

        var day = local.Date;
        var today = nowLocal.Date;

        if (day == today)
        {
            return clock;
        }

        if (day == today.AddDays(-1))
        {
            return $"昨天 {clock}";
        }

        return day.Year == today.Year
            ? $"{local.ToString("MM-dd", CultureInfo.InvariantCulture)} {clock}"
            : $"{local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {clock}";
    }

    /// <summary>
    /// 分类图标：合法就用，否则回退通用标签图标（历史 / 用户自建分类的图标可能为空或手填错误）
    /// </summary>
    public static string CategoryIcon(string? icon) => MdiIcons.Normalize(icon, DefaultCategoryIcon);

    /// <summary>
    /// 分类图标块的配色：预设分类固定配色（餐饮橙、交通蓝、工资绿…），其余按分类 Id 稳定取色
    /// （同一个分类在各设备、各次进入时颜色一致）；没有分类信息时用灰色。
    /// </summary>
    public static string CategoryTone(string? categoryName, Guid? categoryId)
    {
        if (categoryName is not null && PresetTones.TryGetValue(categoryName, out var preset))
        {
            return preset;
        }

        if (categoryId is not { } id || id == Guid.Empty)
        {
            return "gray";
        }

        // Guid.GetHashCode 由 16 个字节决定、不随进程变化（不像 string.GetHashCode 每次启动都不同）
        return Tones[(int)((uint)id.GetHashCode() % (uint)Tones.Count)];
    }
}
