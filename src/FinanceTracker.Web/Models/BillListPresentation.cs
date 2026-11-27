using System.Globalization;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Time;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 账单列表里的一行（取数时一次性算好）：行渲染所需的文字配色，加上分组、权限判断要用的几个原始值。
/// </summary>
/// <param name="Row">行渲染模型（标题 / 副标题 / 金额 / 时间 / 图标，见 <see cref="BillPresentation"/>）</param>
/// <param name="LocalTime">应用时区的墙上时间：按天分组与同日内排序用</param>
/// <param name="Amount">金额（恒为正，收支方向看 <paramref name="IsIncome"/>）</param>
/// <param name="IsIncome">是否收入</param>
/// <param name="CanModify">当前用户能否编辑：只有自己记的账单可以；共享账本里其他成员记的账单只读</param>
/// <param name="InReadOnlyLedger">
/// 账单是自己记的，但所在的共享账本里自己现在只是只读成员（被改成了 Viewer）：
/// 删除、批量归属账本都会被服务层以 403 拒绝（<c>BillService</c>，ADR 0004），所以列表不给这两个入口；
/// 编辑照常能打开，保存时才被拒绝
/// </param>
public sealed record BillListItem(BillRowModel Row, DateTime LocalTime, decimal Amount, bool IsIncome, bool CanModify, bool InReadOnlyLedger = false)
{
    /// <summary>账单 Id</summary>
    public Guid Id => Row.Id;

    /// <summary>能不能删除：自己记的，并且不在自己只读的共享账本里</summary>
    public bool CanDelete => CanModify && !InReadOnlyLedger;

    /// <summary>能不能进批量归属账本的选择：与删除同一条规则（改归属就是在修改只读账本里的账单）</summary>
    public bool Selectable => CanDelete;
}

/// <summary>
/// 账单列表里的一天：组头（标题 / 日期 / 当日收支合计）+ 当天已加载的账单。
/// 合计基于<b>已加载</b>的账单（加载更多后才会包含更早的账单），与汇总卡的全量口径不同——这是刻意保留的差异。
/// </summary>
/// <param name="Date">应用时区的当天（零点）</param>
/// <param name="Title">组头标题：今天 / 昨天 / 11月23日（跨年带年份）</param>
/// <param name="DateText">组头日期：今天 / 昨天是「11月25日 周三」，更早的只有「周日」（标题里已经有日期）</param>
/// <param name="Expense">当日已加载账单的支出合计</param>
/// <param name="Income">当日已加载账单的收入合计</param>
/// <param name="TotalsText">收支合计文案：「支出 ¥69.00」「支出 ¥485.00 · 收入 ¥12,000.00」</param>
/// <param name="Items">当天的账单，时间晚的在前</param>
public sealed record BillDayGroup(
    DateTime Date,
    string Title,
    string DateText,
    decimal Expense,
    decimal Income,
    string TotalsText,
    IReadOnlyList<BillListItem> Items)
{
    /// <summary>E2E 与列表 key：yyyy-MM-dd</summary>
    public string Key => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// 账单 Tab 列表的展示规则（纯函数，单元测试覆盖）：按天分组、组头文案、空状态文案、分段控件取值、批量选择与删除的文案。
/// 页面只负责取数与交互，规则集中在这里。
/// </summary>
public static class BillListPresentation
{
    /// <summary>分段控件取值：全部</summary>
    public const string SegmentAll = "All";

    /// <summary>分段控件取值：支出</summary>
    public const string SegmentExpense = "Expense";

    /// <summary>分段控件取值：收入</summary>
    public const string SegmentIncome = "Income";

    /// <summary>
    /// 只读账单被点按时的提示：不是错误（更不是登录失效），只说明为什么不能改
    /// </summary>
    public const string ReadOnlyHint = "这笔账单由账本里的其他成员记录，只有记录它的人可以修改或删除";

    private static readonly string[] Weekdays = ["日", "一", "二", "三", "四", "五", "六"];

    /// <summary>账单类型 → 分段控件取值（没有类型条件 = 全部）</summary>
    public static string SegmentOf(BillType? type) => type switch
    {
        BillType.Expense => SegmentExpense,
        BillType.Income => SegmentIncome,
        _ => SegmentAll,
    };

    /// <summary>分段控件取值 → 账单类型（全部 / 未知取值 = 没有类型条件）</summary>
    public static BillType? TypeOf(string? segment) => segment switch
    {
        SegmentExpense => BillType.Expense,
        SegmentIncome => BillType.Income,
        _ => null,
    };

    /// <summary>
    /// 把账单转成列表行。
    /// </summary>
    /// <param name="bill">账单（需带上 Category / PaymentChannel / Ledger 导航属性）；TransactionTime 是 UTC</param>
    /// <param name="nowLocal">应用时区的「现在」</param>
    /// <param name="currentUserId">当前用户：只有自己记的账单可以编辑 / 删除；也是「不计入统计」标记的查看者（只标自己的分类）</param>
    /// <param name="readOnlyLedgerIds">当前用户是只读成员的共享账本（没读到 / 没有共享账本时传 null）：自己记在里面的账单不能删除 / 批量归属</param>
    public static BillListItem ToItem(Bill bill, DateTime nowLocal, Guid currentUserId, IReadOnlySet<Guid>? readOnlyLedgerIds = null)
    {
        var row = BillPresentation.ToRow(bill, nowLocal, includeDate: false, viewerId: currentUserId);
        var local = AppTimeZone.ToLocal(bill.TransactionTime);
        var inReadOnlyLedger = bill.LedgerId is { } ledgerId && readOnlyLedgerIds?.Contains(ledgerId) == true;

        return new BillListItem(row, local, bill.Amount, bill.Type == BillType.Income, bill.UserId == currentUserId, inReadOnlyLedger);
    }

    /// <summary>
    /// 按应用时区的日期分组：日期晚的在前；同一天内时间晚的在前，时间相同时保持传入顺序
    /// （服务层已经按交易时间、创建时间、Id 排好，这里的排序是稳定的，不会打乱它）。
    /// </summary>
    public static IReadOnlyList<BillDayGroup> Group(IEnumerable<BillListItem> items, DateTime nowLocal)
    {
        var today = nowLocal.Date;

        return items
            .GroupBy(item => item.LocalTime.Date)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(item => item.LocalTime).ToList();
                var expense = ordered.Where(item => !item.IsIncome).Sum(item => item.Amount);
                var income = ordered.Where(item => item.IsIncome).Sum(item => item.Amount);

                return new BillDayGroup(
                    group.Key,
                    DayTitle(group.Key, today),
                    DayDateText(group.Key, today),
                    expense,
                    income,
                    TotalsText(expense, income),
                    ordered);
            })
            .ToList();
    }

    /// <summary>周几：周一 … 周日（不依赖运行环境的区域设置）</summary>
    public static string Weekday(DateTime day) => $"周{Weekdays[(int)day.DayOfWeek]}";

    /// <summary>
    /// 组头标题：今天 / 昨天；更早的是「11月23日」，不在今年的带年份「2025年11月23日」
    /// </summary>
    public static string DayTitle(DateTime day, DateTime today)
    {
        if (day.Date == today.Date)
        {
            return "今天";
        }

        if (day.Date == today.Date.AddDays(-1))
        {
            return "昨天";
        }

        return DateWithoutWeekday(day, today);
    }

    /// <summary>
    /// 组头日期：今天 / 昨天需要补上具体日期与星期（「11月25日 周三」）；更早的标题里已经是日期，这里只补星期（「周日」）
    /// </summary>
    public static string DayDateText(DateTime day, DateTime today) =>
        day.Date == today.Date || day.Date == today.Date.AddDays(-1)
            ? $"{DateWithoutWeekday(day, today)} {Weekday(day)}"
            : Weekday(day);

    /// <summary>
    /// 当日收支合计：只列出有金额的一边（「支出 ¥69.00」），两边都有时用「 · 」连接；两边都为 0 时给「支出 ¥0.00」，不留空
    /// </summary>
    public static string TotalsText(decimal expense, decimal income)
    {
        var parts = new List<string>(2);

        if (expense > 0m || income <= 0m)
        {
            parts.Add($"支出 {MoneyPresentation.Format(expense)}");
        }

        if (income > 0m)
        {
            parts.Add($"收入 {MoneyPresentation.Format(income)}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>空状态里「查看全部账单」按钮的文案：只受默认的「本月」限制时，取消时间范围看所有账单</summary>
    public const string ShowAllTimeText = "查看全部账单";

    /// <summary>
    /// 空状态文案：搜了关键字的说「没有找到相关账单」并提示换关键字；有筛选条件的说「没有符合条件的账单」并提示换条件；
    /// 只受默认的「本月」限制（没有别的条件）的说「本月还没有账单」——不能说「还没有账单」，更早的月份里可能有；
    /// 什么限制都没有才是新用户的空列表。
    /// </summary>
    /// <param name="hasConditions">有筛选条件（四个维度里偏离默认的、或账单类型，不含搜索关键字）</param>
    /// <param name="hasKeyword">有搜索关键字</param>
    /// <param name="onlyDefaultMonth">除了默认的「本月」之外没有任何别的限制（即时间是默认的本月，且没有筛选条件、没有搜索）</param>
    public static (string Title, string Text) EmptyState(bool hasConditions, bool hasKeyword = false, bool onlyDefaultMonth = false)
    {
        if (hasKeyword)
        {
            return ("没有找到相关账单", hasConditions ? "换个关键字试试，或清除搜索与筛选" : "换个关键字试试，或清除搜索");
        }

        if (hasConditions)
        {
            return ("没有符合条件的账单", "换个筛选条件试试，或清除筛选");
        }

        return onlyDefaultMonth
            ? ("本月还没有账单", "记下这个月的第一笔，或查看全部账单")
            : ("还没有账单", "记下第一笔，这里会显示你的账单");
    }

    /// <summary>空状态里「清除」按钮的文案：按钮清掉的是全部条件（含搜索关键字），文案与当前生效的条件对应</summary>
    public static string ClearConditionsText(bool hasConditions, bool hasKeyword) => (hasConditions, hasKeyword) switch
    {
        (true, true) => "清除搜索与筛选",
        (false, true) => "清除搜索",
        _ => "清除筛选",
    };

    // ───────────────────────── 搜索（账单 Tab 顶部的搜索框） ─────────────────────────

    /// <summary>搜索框的占位文字：直接说清楚能搜什么</summary>
    public const string SearchPlaceholder = "搜索备注、分类或金额";

    /// <summary>搜索框的读屏名称</summary>
    public const string SearchLabel = "搜索账单";

    /// <summary>搜索框里「清除」按钮的读屏名称</summary>
    public const string SearchClearLabel = "清除搜索";

    /// <summary>搜索框允许输入的最大长度：备注 / 分类名 / 金额都远短于它，防止粘贴超长文本拖慢查询</summary>
    public const int SearchMaxLength = 50;

    /// <summary>搜索结果的读屏播报（搜索框旁的 role=status 区域）：找到几笔账单，或没找到</summary>
    public static string SearchResultText(int count) =>
        count > 0 ? $"找到 {count.ToString("N0", CultureInfo.InvariantCulture)} 笔账单" : "没有找到相关账单";

    /// <summary>批量选择栏的计数文案</summary>
    public static string SelectionText(int selected) => selected > 0 ? $"已选 {selected} 笔" : "未选择";

    /// <summary>筛选按钮的读屏名称（角标是图形，读屏要读出生效的条件数）</summary>
    public static string FilterButtonLabel(int activeCount) => activeCount > 0 ? $"筛选，已设置 {activeCount} 项" : "筛选";

    // ───────────────────────── 筛选（条件条与筛选 Sheet） ─────────────────────────

    /// <summary>账本维度里的「未归属账本」：账本必填之前创建、没有账本的历史账单（可批量「归属账本」补上）</summary>
    public const string UnassignedLedgerText = "未归属账本";

    /// <summary>筛选 Sheet 里「全部」账本标签的文案（不限账本）</summary>
    public const string AnyLedgerText = "全部";

    /// <summary>维度名称：条件条上没设置条件时的标签文字，也是筛选 Sheet 里每一组的小标题</summary>
    public static string DimensionName(BillFilterDimension dimension) => dimension switch
    {
        BillFilterDimension.Time => "时间",
        BillFilterDimension.Ledger => "账本",
        BillFilterDimension.Category => "分类",
        BillFilterDimension.Channel => "支付渠道",
        _ => string.Empty,
    };

    /// <summary>
    /// 时间条件的显示文案：预设是「今天 / 本周 / 本月 / 本年」；自定义是「11月1日至11月25日」（和今年同年的省略年份），
    /// 只填了一端时是「11月1日起」「至11月25日」，起止同一天时只写这一天。
    /// </summary>
    public static string TimeText(BillListFilter filter, DateTime nowLocal)
    {
        if (filter.TimePreset != BillTimePreset.None)
        {
            return BillTimePresets.Label(filter.TimePreset);
        }

        var (start, end) = filter.CustomRange;
        return CustomRangeText(start, end, nowLocal);
    }

    /// <summary>自定义起止日期的显示文案（两端都没填时是空串）</summary>
    public static string CustomRangeText(DateTime? start, DateTime? end, DateTime nowLocal) => (start, end) switch
    {
        ({ } s, { } e) when s.Date == e.Date => DateWithoutWeekday(s, nowLocal),
        ({ } s, { } e) => $"{DateWithoutWeekday(s, nowLocal)}至{DateWithoutWeekday(e, nowLocal)}",
        ({ } s, null) => $"{DateWithoutWeekday(s, nowLocal)}起",
        (null, { } e) => $"至{DateWithoutWeekday(e, nowLocal)}",
        _ => string.Empty,
    };

    /// <summary>
    /// 账本条件的显示文案：「未归属账本」，或所选账本的名字（共享账本带「（共享）」）；
    /// 账本列表里找不到所选账本（例如共享账本还没读到）时退回通用的「指定账本」。没有账本条件时是空串。
    /// </summary>
    public static string LedgerText(BillListFilter filter, IEnumerable<LedgerModel> ledgers)
    {
        if (filter.UnassignedLedger)
        {
            return UnassignedLedgerText;
        }

        if (filter.LedgerId is not { } ledgerId)
        {
            return string.Empty;
        }

        return ledgers.FirstOrDefault(ledger => ledger.Id == ledgerId)?.DisplayText
               ?? UnknownConditionText(BillFilterDimension.Ledger);
    }

    /// <summary>
    /// 分类条件的显示文案：选了一个时是它的名字，选了多个时是「第一个的名字等 N 项」（条件条上放不下全部名字）。
    /// 所选分类里有的查不到名字（选项列表还没读到）时，名字取能查到的第一个；一个都查不到退回通用的「指定分类」。没有分类条件时是空串。
    /// </summary>
    /// <param name="categoryIds">所选分类 Id（按选中的先后）</param>
    /// <param name="options">可选的分类</param>
    public static string CategoryText(IReadOnlyList<Guid> categoryIds, IEnumerable<BillFilterOption> options)
    {
        if (categoryIds.Count == 0)
        {
            return string.Empty;
        }

        var names = new Dictionary<Guid, string>();
        foreach (var option in options)
        {
            names.TryAdd(option.Id, option.Name);
        }

        var first = categoryIds.Select(id => names.GetValueOrDefault(id)).FirstOrDefault(name => name is not null)
                    ?? UnknownConditionText(BillFilterDimension.Category);

        return categoryIds.Count == 1 ? first : $"{first}等 {categoryIds.Count} 项";
    }

    /// <summary>条件已设置、但查不到对应名称时的兜底文案（选项列表还没读到），例如「指定分类」</summary>
    public static string UnknownConditionText(BillFilterDimension dimension) => $"指定{DimensionName(dimension)}";

    /// <summary>
    /// 条件条标签的读屏名称：设置了条件的读出「时间：本月，点按调整」，没设置的读出「分类，点按设置」。
    /// 标签上的文字是图形化的（带箭头 / 叉），所以读屏文案要说清楚「这是哪一项、现在是什么」。
    /// </summary>
    /// <param name="dimension">维度</param>
    /// <param name="valueText">当前条件的文案；没设置条件时传 null</param>
    public static string ChipLabel(BillFilterDimension dimension, string? valueText) =>
        string.IsNullOrEmpty(valueText)
            ? $"{DimensionName(dimension)}，点按设置"
            : $"{DimensionName(dimension)}：{valueText}，点按调整";

    /// <summary>条件标签上「移除」小按钮的读屏名称</summary>
    public static string ChipRemoveLabel(BillFilterDimension dimension, string valueText) =>
        $"移除{DimensionName(dimension)}条件：{valueText}";

    /// <summary>
    /// 筛选 Sheet 底部主按钮的文案（设计稿 13 里它始终是「查看 N 笔账单」）：显示当前草稿条件下符合的账单笔数（与账单页汇总同一口径），
    /// 还没查到时是「查看账单」。笔数为 0 也照常写「查看 0 笔账单」且按钮可点——点了之后账单页会给出「没有符合条件的账单」的空状态和清除入口。
    /// </summary>
    /// <param name="count">笔数；null = 还在查询中 / 查询失败</param>
    public static string ConfirmText(int? count) => count switch
    {
        null => "查看账单",
        int n => $"查看 {n.ToString("N0", CultureInfo.InvariantCulture)} 笔账单",
    };

    /// <summary>批量归属账本 Sheet 的标题</summary>
    public const string AssignTitle = "归属账本";

    /// <summary>批量归属账本 Sheet 里账本列表下方的说明：已有账本的账单也会被改到所选账本（只补不覆盖不是这个功能的语义）</summary>
    public const string AssignNote = "已有账本的账单也会改到所选账本";

    /// <summary>批量归属账本时一个自己的账本都没有：账本必填，没有目标可选，引导去新建</summary>
    public const string AssignNoLedgerText = "还没有自己的账本，请先到「我的 - 账本管理」新建一个";

    /// <summary>批量归属账本 Sheet 的说明：点明要处理几笔账单</summary>
    public static string AssignHint(int selected) => $"将已选的 {selected} 笔账单归属到：";

    /// <summary>批量归属账本 Sheet 底部主按钮的文案</summary>
    public static string AssignConfirmText(int selected) => $"归属 {selected} 笔账单";

    /// <summary>批量归属成功的提示：点明归属到了哪个账本、实际处理了几笔</summary>
    public static string AssignedText(int count, string ledgerName) => $"已将 {count} 笔账单归属到「{ledgerName}」";

    /// <summary>删除确认 Alert 的标题</summary>
    public const string DeleteTitle = "删除这笔账单？";

    /// <summary>
    /// 删除确认 Alert 的说明：点名是哪一笔（分类 · 备注，与金额），并告诉用户删除会随同步传播。
    /// 点名用备注而不是行标题里的账本——备注比账本更能认出是哪一笔
    /// </summary>
    public static string DeleteMessage(BillListItem item) =>
        DeleteMessage(BillPresentation.Describe(item.Row.CategoryName ?? item.Row.Title, item.Row.NoteText), item.Row.AmountText);

    /// <summary>同一句说明，由调用方给出标题与金额（记一笔 Sheet 的编辑模式里的「删除」用，与账单页左滑删除的措辞一致）</summary>
    public static string DeleteMessage(string title, string amountText) =>
        $"{title}，{amountText}。删除后不再显示，并会随同步传播到其他设备。";

    private static string DateWithoutWeekday(DateTime day, DateTime today) =>
        day.Year == today.Year
            ? $"{day.Month}月{day.Day}日"
            : $"{day.Year}年{day.Month}月{day.Day}日";
}
