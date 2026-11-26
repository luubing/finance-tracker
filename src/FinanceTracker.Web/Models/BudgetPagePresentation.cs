using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 「日均可用」提示的取值
/// </summary>
/// <param name="DaysLeft">本月剩余天数（含今天）</param>
/// <param name="PerDay">日均可用额度（总预算剩余额度 ÷ 剩余天数，四舍五入到分）</param>
public sealed record DailyAvailable(int DaysLeft, decimal PerDay);

/// <summary>
/// 环形总览卡片所需的全部文字与状态
/// </summary>
/// <param name="BudgetId">总预算的 Id</param>
/// <param name="Title">卡片标题：全部账本的总预算叫「总预算」，账本级的叫「旅行 · 总预算」</param>
/// <param name="State">状态（正常 / 接近上限 / 超支）</param>
/// <param name="ShownPercent">显示的使用百分比（向下取整）</param>
/// <param name="PercentText">环中心的百分比文字（位数太多时封顶为「999%+」，免得挤出环的孔）</param>
/// <param name="RingPercent">圆环弧长占整圈的百分比（0–100，超支时撑满）</param>
/// <param name="Caption">环中心百分比下面的小字：已使用 / 已用完 / 已超支</param>
/// <param name="Amount">预算额度</param>
/// <param name="Used">已用金额</param>
/// <param name="RemainLabel">剩余一行的标签：剩余 / 超支</param>
/// <param name="RemainAmount">剩余（或超支）的金额，恒为非负，正负由 <paramref name="RemainLabel"/> 表达</param>
/// <param name="AriaValueText">圆环的读屏文案：已用百分比 + 状态</param>
public sealed record BudgetOverviewModel(
    Guid BudgetId,
    string Title,
    BudgetState State,
    int ShownPercent,
    string PercentText,
    decimal RingPercent,
    string Caption,
    decimal Amount,
    decimal Used,
    string RemainLabel,
    decimal RemainAmount,
    string AriaValueText);

/// <summary>
/// 预算页某个月份 / 账本筛选下的全部内容（取数后一次算好，渲染时不再计算）
/// </summary>
/// <param name="Overview">环形总览（本筛选下没有总预算时为空）</param>
/// <param name="Daily">「日均可用」（不满足显示条件时为空）</param>
/// <param name="OtherTotals">总览之外的其它总预算（指定账本时，该账本与全部账本各有一个总预算的情形）</param>
/// <param name="Categories">分类预算行</param>
public sealed record BudgetPageModel(
    BudgetOverviewModel? Overview,
    DailyAvailable? Daily,
    IReadOnlyList<BudgetRowModel> OtherTotals,
    IReadOnlyList<BudgetRowModel> Categories)
{
    /// <summary>这个筛选下一条预算也没有</summary>
    public bool IsEmpty => Overview is null && OtherTotals.Count == 0 && Categories.Count == 0;
}

/// <summary>
/// 预算页的展示规则（纯函数，单元测试覆盖）：总览取哪一个总预算、「日均可用」、行文字、排序。
/// 状态判定与行文字的基础规则在 <see cref="BudgetPresentation"/>，这里只做预算页自己的组合。
/// </summary>
public static class BudgetPagePresentation
{
    /// <summary>「分类预算」分组的标题</summary>
    public const string CategoryGroupTitle = "分类预算";

    /// <summary>「其它总预算」分组的标题</summary>
    public const string OtherTotalsGroupTitle = "其他总预算";

    /// <summary>分类预算下面的脚注（设计稿 screens 09-budgets）</summary>
    public const string Footnote = "预算按账本独立设置，也可设置覆盖全部账本的总预算。超支提醒每天最多通知一次。";

    /// <summary>环中心百分比文字的封顶：超过 999% 写「999%+」</summary>
    private const int PercentTextCap = 999;

    /// <summary>
    /// 总览用哪一个总预算：指定账本时优先用该账本自己的总预算，没有再用「全部账本」的总预算；
    /// 没指定账本时只有「全部账本」的总预算。都没有返回空。
    /// </summary>
    public static BudgetStatus? PrimaryTotal(IEnumerable<BudgetStatus> statuses, Guid? ledgerId)
    {
        var totals = statuses.Where(s => s.CategoryId is null).ToList();

        if (ledgerId is { } id && totals.FirstOrDefault(t => t.LedgerId == id) is { } scoped)
        {
            return scoped;
        }

        return totals.FirstOrDefault(t => t.LedgerId is null);
    }

    /// <summary>
    /// 日均可用 = 总预算剩余额度 ÷ 本月剩余天数（含今天），四舍五入到分。
    /// 只在查看「当前月份」时显示（过去、未来的月份没有意义）；没有总预算、已用完或已超支（剩余额度 ≤ 0）时不显示。
    /// </summary>
    /// <param name="year">正在查看的年份</param>
    /// <param name="month">正在查看的月份</param>
    /// <param name="nowLocal">现在的应用时区时间（调用方用 <c>AppTimeZone.ToLocal(DateTime.UtcNow)</c> 取得；"今天是几号"按应用时区，ADR 0005）</param>
    /// <param name="total">总览用的总预算</param>
    public static DailyAvailable? DailyAvailableOf(int year, int month, DateTime nowLocal, BudgetStatus? total)
    {
        if (total is null || year != nowLocal.Year || month != nowLocal.Month)
        {
            return null;
        }

        var remaining = total.RemainingAmount;
        if (remaining <= 0m)
        {
            return null;
        }

        var daysLeft = DateTime.DaysInMonth(year, month) - nowLocal.Day + 1;
        return new DailyAvailable(daysLeft, Math.Round(remaining / daysLeft, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>「日均可用」提示的文案，例如「还剩 6 天（含今天），日均可用 ¥214.42」</summary>
    public static string DailyAvailableText(DailyAvailable daily) =>
        $"还剩 {daily.DaysLeft} 天（含今天），日均可用 {MoneyPresentation.Format(daily.PerDay)}";

    /// <summary>
    /// 环中心的小字：超支「已超支」、刚好用完「已用完」，其余「已使用」。
    /// </summary>
    public static string OverviewCaption(decimal remainingAmount) => remainingAmount switch
    {
        < 0m => "已超支",
        0m => "已用完",
        _ => "已使用",
    };

    /// <summary>环中心的百分比文字</summary>
    public static string PercentText(int shownPercent) =>
        shownPercent > PercentTextCap ? $"{PercentTextCap}%+" : $"{shownPercent}%";

    /// <summary>
    /// 把总预算转成环形总览
    /// </summary>
    public static BudgetOverviewModel ToOverview(BudgetStatus status)
    {
        var state = BudgetPresentation.StateOf(status.UsagePercentage);
        var shown = BudgetPresentation.ShownPercent(status.UsagePercentage);
        var remaining = status.RemainingAmount;

        return new BudgetOverviewModel(
            status.BudgetId,
            string.IsNullOrWhiteSpace(status.LedgerName) ? "总预算" : $"{status.LedgerName} · 总预算",
            state,
            shown,
            PercentText(shown),
            BudgetPresentation.BarPercent(status.UsagePercentage),
            OverviewCaption(remaining),
            status.Amount,
            status.UsedAmount,
            remaining < 0m ? "超支" : "剩余",
            Math.Abs(remaining),
            BudgetPresentation.AriaValueText(state, shown));
    }

    /// <summary>
    /// 预算页的一行：名称带账本前缀（账本级预算「旅行 · 餐饮美食」，全部账本的总预算「全部账本 · 总预算」，全部账本的分类预算就是分类名），
    /// 名称后面的灰色小字是「已用 / 额度」。其余（图标、状态、右上角文字、进度条）与首页的预算行一致。
    /// </summary>
    public static BudgetRowModel ToPageRow(BudgetStatus status) =>
        BudgetPresentation.ToRow(status) with
        {
            Name = PageRowName(status),
            Tag = UsageSummary(status),
        };

    /// <summary>预算页一行的名称</summary>
    public static string PageRowName(BudgetStatus status)
    {
        var baseName = string.IsNullOrWhiteSpace(status.CategoryName) ? "总预算" : status.CategoryName;

        if (!string.IsNullOrWhiteSpace(status.LedgerName))
        {
            return $"{status.LedgerName} · {baseName}";
        }

        return status.CategoryId is null ? $"全部账本 · {baseName}" : baseName;
    }

    /// <summary>「已用 / 额度」，例如「¥2,360 / ¥2,000」</summary>
    public static string UsageSummary(BudgetStatus status) =>
        $"{CompactMoney(status.UsedAmount)} / {CompactMoney(status.Amount)}";

    /// <summary>
    /// 紧凑金额：整数金额不带小数（¥2,360），否则两位小数（¥2,360.50）——行里空间紧，整数更清爽。
    /// </summary>
    public static string CompactMoney(decimal amount)
    {
        var parts = MoneyPresentation.Split(amount);
        return parts.Fraction == ".00"
            ? $"{parts.Sign}{MoneyPresentation.Symbol}{parts.Integer}"
            : $"{parts.Sign}{MoneyPresentation.Symbol}{parts.Integer}{parts.Fraction}";
    }

    /// <summary>
    /// 预算行的读屏名称（点按进入编辑）：名称 + 当前状态文字
    /// </summary>
    public static string OpenAriaLabel(BudgetRowModel row) => $"编辑预算：{row.Name}，{row.RemainText}";

    /// <summary>
    /// 一次算好预算页的全部内容。
    /// 分类预算按已用金额从多到少排（花得多的在前），已用相同按分类名、账本名排，顺序稳定；
    /// 其它总预算「全部账本」的在前，账本级的按账本名。
    /// </summary>
    /// <param name="statuses">预算执行情况（<c>IBudgetService.GetBudgetStatusAsync</c> 的结果）</param>
    /// <param name="ledgerId">当前账本筛选（空 = 全部账本）</param>
    /// <param name="year">正在查看的年份</param>
    /// <param name="month">正在查看的月份</param>
    /// <param name="nowLocal">现在的应用时区时间</param>
    public static BudgetPageModel Build(IReadOnlyList<BudgetStatus> statuses, Guid? ledgerId, int year, int month, DateTime nowLocal)
    {
        var primary = PrimaryTotal(statuses, ledgerId);

        var otherTotals = statuses
            .Where(s => s.CategoryId is null && !ReferenceEquals(s, primary))
            .OrderBy(s => s.LedgerId is null ? 0 : 1)
            .ThenBy(s => s.LedgerName, StringComparer.Ordinal)
            .Select(ToPageRow)
            .ToList();

        var categories = statuses
            .Where(s => s.CategoryId is not null)
            .OrderByDescending(s => s.UsedAmount)
            .ThenBy(s => s.CategoryName, StringComparer.Ordinal)
            .ThenBy(s => s.LedgerName, StringComparer.Ordinal)
            .Select(ToPageRow)
            .ToList();

        return new BudgetPageModel(
            primary is null ? null : ToOverview(primary),
            DailyAvailableOf(year, month, nowLocal, primary),
            otherTotals,
            categories);
    }
}
