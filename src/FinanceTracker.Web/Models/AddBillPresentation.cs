using System.Globalization;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Web.Models;

/// <summary>「记一笔」里的一个分类候选（只含展示与选择需要的字段）</summary>
public sealed record CategoryOption(Guid Id, string Name, string Icon, BillType Type);

/// <summary>「记一笔」里的一个支付渠道候选</summary>
public sealed record ChannelOption(Guid Id, string Name, string Icon);

/// <summary>
/// 一张账单草稿的内容快照：编辑模式用它判断「有没有改过」（打开时记一份，之后与当前内容比较）。
/// 比较的是「用户能看见、能改的内容」，所以先规整再比：金额取两位小数、时间只到分钟（时间输入控件的精度，
/// 库里账单的秒不算用户改动）、备注去掉首尾空白且空白等同于没有备注。用 <see cref="Of"/> 构造才会规整。
/// </summary>
/// <param name="Amount">金额（两位小数）；没有金额记 0</param>
/// <param name="Type">账单类型</param>
/// <param name="CategoryId">分类</param>
/// <param name="ChannelId">支付渠道</param>
/// <param name="LedgerId">账本；历史无账本账单打开时是 null</param>
/// <param name="Time">应用时区的墙上时间（到分钟）</param>
/// <param name="Note">备注（已去空白；没有备注是 null）</param>
public sealed record BillDraft(decimal Amount, BillType Type, Guid CategoryId, Guid ChannelId, Guid? LedgerId, DateTime Time, string? Note)
{
    /// <summary>按「用户看得见的内容」规整后构造一份快照</summary>
    public static BillDraft Of(decimal? amount, BillType type, Guid categoryId, Guid channelId, Guid? ledgerId, DateTime time, string? note)
    {
        var trimmed = note?.Trim();

        return new BillDraft(
            decimal.Round(amount ?? 0m, AmountInput.MaxDecimals, MidpointRounding.AwayFromZero),
            type,
            categoryId,
            channelId,
            ledgerId is { } id && id != Guid.Empty ? id : null,
            new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified),
            string.IsNullOrEmpty(trimmed) ? null : trimmed);
    }
}

/// <summary>保存前的校验结果：缺了哪一项（按界面上要补的先后顺序）</summary>
public enum AddBillBlock
{
    /// <summary>可以保存</summary>
    None = 0,

    /// <summary>没有金额（空或为零）</summary>
    Amount,

    /// <summary>没选分类</summary>
    Category,

    /// <summary>没选支付渠道</summary>
    Channel,

    /// <summary>没选账本（账本必填）</summary>
    Ledger,
}

/// <summary>
/// 「记一笔」Sheet 的展示与选择规则（金额键盘规则见 <see cref="AmountInput"/>）：默认值挑选、保存前校验、时间文案、分类网格分页、关闭确认。
/// 这些规则与界面无关，集中在这里便于单元测试；Sheet 只负责取数、渲染与转发交互。
/// </summary>
public static class AddBillPresentation
{
    /// <summary>分类网格的列数</summary>
    public const int CategoryColumns = 5;

    /// <summary>分类网格每页的行数（超过两行左右翻页）</summary>
    public const int CategoryRowsPerPage = 2;

    /// <summary>分类网格每页放几个格子（含末尾的「新建分类」）</summary>
    public const int CategoryPageSize = CategoryColumns * CategoryRowsPerPage;

    /// <summary>时间输入控件（<c>datetime-local</c>）使用的格式（精确到分钟）</summary>
    public const string InputTimeFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>解析时间输入控件的值时接受的格式：浏览器在秒不为零时会带上秒，一并接受</summary>
    private static readonly string[] InputTimeFormats = [InputTimeFormat, "yyyy-MM-ddTHH:mm:ss"];

    // ------------------------------------------------------------------ 标题与文案（新建与编辑共用同一个 Sheet）

    /// <summary>新建模式的标题</summary>
    public const string CreateTitle = "记一笔";

    /// <summary>编辑模式的标题</summary>
    public const string EditTitle = "编辑账单";

    /// <summary>关闭前确认的标题：新建是「放弃本次记账？」，编辑是「放弃修改？」</summary>
    public static string DiscardTitle(bool editing) => editing ? "放弃修改？" : "放弃本次记账？";

    /// <summary>关闭前确认的说明</summary>
    public static string DiscardMessage(bool editing) => editing ? "本次的修改不会保存。" : "已输入的内容不会保存。";

    /// <summary>编辑保存成功的 Toast</summary>
    public const string UpdatedText = "账单已更新";

    /// <summary>删除成功的 Toast（与账单页左滑删除一致）</summary>
    public const string DeletedText = "账单已删除";

    /// <summary>要编辑 / 删除的账单已经不存在（别处删了，或地址里的 Id 不对）的 Toast</summary>
    public const string BillGoneText = "账单不存在或已删除";

    /// <summary>
    /// 保存编辑时被拒绝（没有写权限，例如被改成只读成员的共享账本里的账单）的提示：点明是「无权限」，并带上服务层给的原因。
    /// 编辑 Sheet 打开时不拦，保存时才拒绝；这是「已认证但无权操作」（403 语义），不是登录失效——只提示、不登出。
    /// </summary>
    /// <param name="reason">服务层的原因文案（<c>ForbiddenAccessException.Message</c>）；空则只说「无权限」</param>
    public static string NoPermissionText(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "无权限编辑这笔账单" : $"无权限编辑这笔账单：{reason.Trim()}";

    // ------------------------------------------------------------------ 默认值

    /// <summary>
    /// 挑选当前账单类型下默认选中的分类：用户设置的默认分类优先（必须是这个类型下存在的），否则取该类型的第一项；
    /// 该类型一个分类都没有时返回 <see cref="Guid.Empty"/>。不引入「最近使用」。
    /// </summary>
    public static Guid PickCategory(IEnumerable<CategoryOption> categories, BillType type, Guid? preferredId)
    {
        var ofType = categories.Where(c => c.Type == type).ToList();

        return ofType.FirstOrDefault(c => c.Id == preferredId)?.Id
               ?? ofType.FirstOrDefault()?.Id
               ?? Guid.Empty;
    }

    /// <summary>挑选默认支付渠道：用户设置的默认渠道优先，否则取第一项；没有渠道时返回 <see cref="Guid.Empty"/></summary>
    public static Guid PickChannel(IEnumerable<ChannelOption> channels, Guid? preferredId)
    {
        var list = channels.ToList();

        return list.FirstOrDefault(c => c.Id == preferredId)?.Id
               ?? list.FirstOrDefault()?.Id
               ?? Guid.Empty;
    }

    // ------------------------------------------------------------------ 保存前校验

    /// <summary>
    /// 保存前还缺哪一项。先后顺序与用户操作顺序一致：金额 → 分类 → 支付渠道 → 账本。
    /// 账本必填由服务层统一把关，这里只是让界面提前拦下、给出明确提示。
    /// </summary>
    public static AddBillBlock CheckSave(decimal? amount, Guid categoryId, Guid channelId, Guid? ledgerId)
    {
        if (amount is not > 0m)
        {
            return AddBillBlock.Amount;
        }

        if (categoryId == Guid.Empty)
        {
            return AddBillBlock.Category;
        }

        if (channelId == Guid.Empty)
        {
            return AddBillBlock.Channel;
        }

        return ledgerId.HasValue && ledgerId.Value != Guid.Empty ? AddBillBlock.None : AddBillBlock.Ledger;
    }

    /// <summary>缺项对应的提示文案（沿用原记账页的措辞）；可以保存时返回空串</summary>
    public static string BlockMessage(AddBillBlock block) => block switch
    {
        AddBillBlock.Amount => "请输入金额",
        AddBillBlock.Category => "请选择分类",
        AddBillBlock.Channel => "请选择支付渠道",
        AddBillBlock.Ledger => "请选择账本",
        _ => string.Empty,
    };

    // ------------------------------------------------------------------ 关闭确认

    /// <summary>
    /// 是否已经输入了内容（金额或备注非空）：有的话关闭 Sheet 要二次确认，避免误丢数据。
    /// 只选了分类 / 账本这类默认值可改的选项不算输入。
    /// </summary>
    public static bool HasInput(string? amountText, string? note) =>
        !AmountInput.IsEmpty(amountText) || !string.IsNullOrWhiteSpace(note);

    // ------------------------------------------------------------------ 时间

    /// <summary>
    /// 「时间」行右侧的文案（参数都是应用时区的墙上时间）：今天 / 昨天 + HH:mm；同年「M月d日 HH:mm」；跨年带年份。
    /// </summary>
    public static string TimeLabel(DateTime local, DateTime nowLocal)
    {
        var clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var day = local.Date;
        var today = nowLocal.Date;

        if (day == today)
        {
            return $"今天 {clock}";
        }

        if (day == today.AddDays(-1))
        {
            return $"昨天 {clock}";
        }

        return day.Year == today.Year
            ? $"{day.Month}月{day.Day}日 {clock}"
            : $"{day.Year}年{day.Month}月{day.Day}日 {clock}";
    }

    /// <summary>应用时区的墙上时间 → 时间输入控件的值（精确到分钟）</summary>
    public static string ToInputValue(DateTime local) => local.ToString(InputTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// 时间输入控件的值 → 应用时区的墙上时间（Kind 为 Unspecified，由 <c>BillService</c> 经 <c>AppTimeZone</c> 换成 UTC 入库）。
    /// 空或格式不对返回 false。
    /// </summary>
    public static bool TryParseInputValue(string? value, out DateTime local) =>
        DateTime.TryParseExact(value, InputTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out local);

    // ------------------------------------------------------------------ 分类网格

    /// <summary>
    /// 分类网格的页数：格子总数（分类 + 末尾的「新建分类」）每页放 <see cref="CategoryPageSize"/> 个，至少 1 页。
    /// 支出 10 个预设 + 「新建分类」= 11 个 → 2 页；收入 5 + 1 = 6 个 → 1 页（没有分页点）。
    /// </summary>
    public static int CategoryPageCount(int cellCount) =>
        Math.Max(1, (Math.Max(0, cellCount) + CategoryPageSize - 1) / CategoryPageSize);

    /// <summary>第 <paramref name="cellIndex"/>（从 0 起）个格子落在第几页（从 0 起）</summary>
    public static int CategoryPageOf(int cellIndex) => Math.Max(0, cellIndex) / CategoryPageSize;
}
