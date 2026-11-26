using System.Globalization;
using System.Text;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 分类环比里的一行（展示用：文字、配色、涨跌方向都已算好，页面只管摆）。
/// </summary>
/// <param name="CategoryId">分类 Id</param>
/// <param name="Name">分类名称</param>
/// <param name="Icon">分类图标（MDI，已规整）</param>
/// <param name="IconTone">图标块配色（与账单行、统计图例里同一个分类的颜色一致）</param>
/// <param name="Current">本期金额</param>
/// <param name="Previous">上一等长周期的金额</param>
/// <param name="Subtitle">副标题：「¥500.00（上期 ¥400.00）」</param>
/// <param name="DeltaText">变化文字：「+25.0%」「-10.0%」「0.0%」；上期没有支出时是「新增」</param>
/// <param name="DeltaKind">变化方向：up｜down｜flat｜new（决定箭头；上期没有支出的「新增」按「涨」画向上箭头）</param>
/// <param name="DeltaTone">语义色：bad（支出变多）｜good（支出变少）｜flat（持平）</param>
public sealed record ComparisonRow(
    Guid CategoryId,
    string Name,
    string Icon,
    string IconTone,
    decimal Current,
    decimal Previous,
    string Subtitle,
    string DeltaText,
    string DeltaKind,
    string DeltaTone)
{
    /// <summary>变化文字前的箭头图标（MDI）</summary>
    public string DeltaIcon => DeltaKind switch
    {
        CustomReportPresentation.DeltaDown => "mdi-arrow-down",
        CustomReportPresentation.DeltaFlat => "mdi-minus",
        _ => "mdi-arrow-up",
    };

    /// <summary>本期金额的两位小数写法（E2E 观察属性用，不带千分位与货币符号）</summary>
    public string CurrentPlain => Current.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>上期金额的两位小数写法（E2E 观察属性用）</summary>
    public string PreviousPlain => Previous.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// 自定义报表页的展示规则（纯函数，单元测试覆盖）：快捷区间、起止日期的校验与文案、结果的说明文字、分类环比各行、
/// 导出的 CSV 与分享的文字摘要。页面只负责把这些结果摆出来，文案与判断不散落在 Razor 里。
/// <para>
/// 口径不变（ADR 0005）：这里的日期都是应用时区的自然日（调用方用 <c>AppTimeZone.ToLocal(DateTime.UtcNow).Date</c> 给「今天」），
/// 统计服务把起止日期当作「含当天」的区间、换成 UTC 的左闭右开区间去查库——本类不做任何时区换算。
/// </para>
/// </summary>
public static class CustomReportPresentation
{
    // ------------------------------------------------------------------ 页面文案

    /// <summary>页面标题</summary>
    public const string Title = "自定义报表";

    /// <summary>快捷区间的小标题与读屏名称</summary>
    public const string QuickRangeTitle = "快捷区间";

    /// <summary>起止日期与账本这一组的小标题</summary>
    public const string RangeGroupTitle = "报表范围";

    /// <summary>开始日期一行的标题</summary>
    public const string StartDateTitle = "开始日期";

    /// <summary>结束日期一行的标题</summary>
    public const string EndDateTitle = "结束日期";

    /// <summary>账本一行的标题</summary>
    public const string LedgerTitle = "账本";

    /// <summary>日期被清空时，日期行右侧的占位文字</summary>
    public const string DateMissingText = "请选择";

    /// <summary>底部固定的主按钮</summary>
    public const string GenerateLabel = "生成报表";

    /// <summary>生成中的按钮文字</summary>
    public const string GeneratingLabel = "生成中";

    /// <summary>还没生成过报表时，结果区的提示（沿用原页面的文案）</summary>
    public const string IdleTitle = "选择日期范围后点击「生成报表」";

    /// <summary>第一次生成就失败时，结果区的标题</summary>
    public const string FailedTitle = "报表生成失败";

    /// <summary>第一次生成就失败时，结果区的说明</summary>
    public const string FailedText = "点「生成报表」再试一次";

    /// <summary>节标题：分类支出环比</summary>
    public const string ComparisonTitle = "分类支出环比";

    /// <summary>环比没有数据时的文案（沿用原页面的文案）</summary>
    public const string ComparisonEmptyText = "所选范围内暂无支出数据";

    /// <summary>节标题：分类支出排行（Top 8）</summary>
    public const string RankTitle = "分类支出排行";

    /// <summary>分类排行没有数据时的文案</summary>
    public const string RankEmptyText = "所选范围内暂无支出账单";

    /// <summary>分类排行只显示前几名</summary>
    public const int RankTopN = 8;

    /// <summary>节标题：导出与分享</summary>
    public const string ActionsTitle = "导出与分享";

    /// <summary>导出一行的标题</summary>
    public const string ExportTitle = "导出 CSV";

    /// <summary>导出一行的副标题</summary>
    public const string ExportSubtitle = "支出分类汇总，可用 Excel 打开";

    /// <summary>分享一行的标题</summary>
    public const string ShareRowTitle = "分享摘要";

    /// <summary>分享一行的副标题</summary>
    public const string ShareSubtitle = "文字摘要，发给家人或朋友";

    /// <summary>导出成功的提示</summary>
    public const string ExportedMessage = "报表已导出";

    /// <summary>没有系统分享面板、改为复制到剪贴板之后的提示</summary>
    public const string CopiedMessage = "报表摘要已复制到剪贴板";

    /// <summary>系统分享与剪贴板都用不了的提示</summary>
    public const string ShareUnsupportedMessage = "当前环境不支持分享，可截图保存";

    /// <summary>导出的文件名（沿用原页面）</summary>
    public const string CsvFileName = "custom_report.csv";

    /// <summary>导出文件的 MIME 类型</summary>
    public const string CsvMimeType = "text/csv";

    /// <summary>系统分享面板的标题</summary>
    public const string ShareSheetTitle = "记账报表";

    /// <summary>环比副标题里「上期」的前缀</summary>
    private const string PreviousPrefix = "上期 ";

    /// <summary>加载失败的提示（沿用原页面）</summary>
    public static string LoadFailedMessage(string detail) => $"加载报表失败: {detail}";

    /// <summary>导出失败的提示（沿用原页面）</summary>
    public static string ExportFailedMessage(string detail) => $"导出失败: {detail}";

    /// <summary>构成圆环没有数据时的文案</summary>
    public static string CompositionEmptyText(BillType type) => $"所选范围内暂无{StatisticsPresentation.TypeName(type)}账单";

    // ------------------------------------------------------------------ 快捷区间

    /// <summary>快捷区间：本月（1 日 → 今天）</summary>
    public const string QuickThisMonth = "this-month";

    /// <summary>快捷区间：上月（整月）</summary>
    public const string QuickLastMonth = "last-month";

    /// <summary>快捷区间：近 30 天（今天往前 30 天 → 今天，含两端；沿用原页面的算法，统计 Tab 趋势的「最近30天」同样是往前 30 天）</summary>
    public const string QuickLast30 = "last-30";

    /// <summary>快捷区间：近 90 天</summary>
    public const string QuickLast90 = "last-90";

    /// <summary>四个快捷区间（取值, 文字），顺序即显示顺序（沿用原页面）</summary>
    public static IReadOnlyList<(string Key, string Label)> QuickRanges { get; } =
    [
        (QuickThisMonth, "本月"),
        (QuickLastMonth, "上月"),
        (QuickLast30, "近30天"),
        (QuickLast90, "近90天"),
    ];

    /// <summary>
    /// 快捷区间对应的起止日期（含当天；<paramref name="today"/> 是应用时区的今天，不认识的取值按「本月」处理）。
    /// 本月 = 本月 1 日 → 今天；上月 = 上月 1 日 → 上月最后一天；近 30 / 90 天 = 今天往前 30 / 90 天 → 今天。
    /// </summary>
    public static (DateTime Start, DateTime End) QuickRangeBounds(string? key, DateTime today)
    {
        var day = today.Date;
        switch (key)
        {
            case QuickLastMonth:
                var first = new DateTime(day.Year, day.Month, 1).AddMonths(-1);
                return (first, first.AddMonths(1).AddDays(-1));
            case QuickLast30:
                return (day.AddDays(-30), day);
            case QuickLast90:
                return (day.AddDays(-90), day);
            default:
                return (new DateTime(day.Year, day.Month, 1), day);
        }
    }

    /// <summary>打开页面时的默认区间：本月</summary>
    public static (DateTime Start, DateTime End) DefaultRange(DateTime today) => QuickRangeBounds(QuickThisMonth, today);

    /// <summary>
    /// 当前起止日期正好等于哪个快捷区间（按今天重新算，所以日期被手动改成与某个快捷区间相同时它也亮，改成别的就都不亮）；都不是返回 null。
    /// </summary>
    public static string? QuickRangeOf(DateTime? start, DateTime? end, DateTime today)
    {
        if (start is null || end is null)
        {
            return null;
        }

        foreach (var (key, _) in QuickRanges)
        {
            var bounds = QuickRangeBounds(key, today);
            if (bounds.Start == start.Value.Date && bounds.End == end.Value.Date)
            {
                return key;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ 起止日期

    /// <summary>可选的最早日期（与统计 Tab 的年份范围一致）</summary>
    public static DateTime MinDate { get; } = new(StatisticsPresentation.MinYear, 1, 1);

    /// <summary>可选的最晚日期</summary>
    public static DateTime MaxDate { get; } = new(StatisticsPresentation.MaxYear, 12, 31);

    /// <summary>日期输入控件的 min 属性值</summary>
    public static string MinInputValue => DateInputPresentation.ToInputValue(MinDate);

    /// <summary>日期输入控件的 max 属性值</summary>
    public static string MaxInputValue => DateInputPresentation.ToInputValue(MaxDate);

    /// <summary>没选开始日期</summary>
    public const string StartRequiredMessage = "请选择开始日期";

    /// <summary>没选结束日期</summary>
    public const string EndRequiredMessage = "请选择结束日期";

    /// <summary>开始日期晚于结束日期（沿用原页面的文案）</summary>
    public const string RangeOrderMessage = "开始日期不能晚于结束日期";

    /// <summary>日期超出可选范围</summary>
    public static string OutOfRangeMessage { get; } = $"日期需在 {StatisticsPresentation.MinYear} 年到 {StatisticsPresentation.MaxYear} 年之间";

    /// <summary>
    /// 校验起止日期：合法返回 null，否则返回给用户看的提示。日期输入控件可以被清空（浏览器允许），所以空值要拦下；
    /// 范围上下限保证统计服务回推「上一等长周期」时不会下溢出 <see cref="DateTime.MinValue"/>。
    /// </summary>
    public static string? Validate(DateTime? start, DateTime? end)
    {
        if (start is null)
        {
            return StartRequiredMessage;
        }

        if (end is null)
        {
            return EndRequiredMessage;
        }

        var from = start.Value.Date;
        var to = end.Value.Date;
        if (from < MinDate || from > MaxDate || to < MinDate || to > MaxDate)
        {
            return OutOfRangeMessage;
        }

        return from > to ? RangeOrderMessage : null;
    }

    /// <summary>日期行右侧的文字：「2026年11月1日」；被清空时是「请选择」</summary>
    public static string DateLabel(DateTime? date) => date is { } value ? DateInputPresentation.Label(value) : DateMissingText;

    /// <summary>日期输入控件的值：「2026-11-01」；被清空时是空串</summary>
    public static string DateInputValue(DateTime? date) => date is { } value ? DateInputPresentation.ToInputValue(value) : string.Empty;

    // ------------------------------------------------------------------ 结果说明

    /// <summary>区间文字：「2026-11-01 ~ 2026-11-25」（与导出文件、分享摘要里的写法一致）</summary>
    public static string RangeText(DateTime start, DateTime end) =>
        string.Create(CultureInfo.InvariantCulture, $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}");

    /// <summary>
    /// KPI 卡下面的说明：区间 · 账本范围 · 账单数量，例如「2026-11-01 ~ 2026-11-25 · 全部账本 · 共 12 笔账单」。
    /// 带上账本范围，用户改了参数还没重新生成时，也看得出眼前的结果是哪个范围的。
    /// </summary>
    public static string Caption(DateTime start, DateTime end, string ledgerLabel, int billCount) =>
        $"{RangeText(start, end)} · {ledgerLabel} · 共 {billCount} 笔账单";

    /// <summary>
    /// 环比对比的「上一等长周期」：紧邻本期之前、天数相同（与统计服务按整数天数回推的口径一致）。
    /// </summary>
    public static (DateTime Start, DateTime End) PreviousPeriod(DateTime start, DateTime end)
    {
        var from = start.Date;
        var days = (int)(end.Date - from).TotalDays + 1;
        return (from.AddDays(-days), from.AddDays(-1));
    }

    /// <summary>环比列表下面的说明：对比的是哪一段，例如「对比上一等长周期（2026-10-02 ~ 2026-10-31）」</summary>
    public static string ComparisonFooter(DateTime start, DateTime end)
    {
        var (previousStart, previousEnd) = PreviousPeriod(start, end);
        return $"对比上一等长周期（{RangeText(previousStart, previousEnd)}）";
    }

    // ------------------------------------------------------------------ 分类环比

    /// <summary>变化方向：涨</summary>
    public const string DeltaUp = "up";

    /// <summary>变化方向：跌</summary>
    public const string DeltaDown = "down";

    /// <summary>变化方向：持平（或没有可比的金额）</summary>
    public const string DeltaFlat = "flat";

    /// <summary>变化方向：上期没有支出、本期新出现</summary>
    public const string DeltaNew = "new";

    /// <summary>上期没有支出、本期新出现时的变化文字</summary>
    public const string NewText = "新增";

    /// <summary>环比各行：顺序沿用统计服务给的（变化金额绝对值从大到小）</summary>
    public static IReadOnlyList<ComparisonRow> BuildComparisonRows(IEnumerable<CategoryComparisonData> data) =>
        data.Select(BuildComparisonRow).ToList();

    /// <summary>
    /// 环比里的一行。变化率沿用统计模型给的（上期为 0 时没有变化率）：
    /// 上期没有支出 = 「新增」；否则显示带符号的一位小数变化率，箭头与颜色按「支出涨是坏事」判断（与年度同比一致，按显示的一位小数判断，所以「0.0%」永远是持平）。
    /// </summary>
    public static ComparisonRow BuildComparisonRow(CategoryComparisonData data)
    {
        var name = string.IsNullOrWhiteSpace(data.CategoryName) ? BillPresentation.UnknownCategoryLabel : data.CategoryName.Trim();
        string text;
        string kind;
        string tone;

        if (data.PreviousAmount == 0m)
        {
            // 上期没有这个分类的支出：算不出变化率。本期有支出就是「新增」；两期都是 0（不会出现，兜底）显示持平
            var isNew = data.CurrentAmount > 0m;
            text = isNew ? NewText : StatisticsPresentation.FormatChangeRate(null);
            kind = isNew ? DeltaNew : DeltaFlat;
            tone = isNew ? "bad" : "flat";
        }
        else
        {
            var rate = data.ChangeRate;
            text = StatisticsPresentation.FormatChangeRate(rate);
            tone = StatisticsPresentation.ChangeTone(rate, higherIsBetter: false);
            kind = tone == "flat" ? DeltaFlat : data.ChangeAmount > 0m ? DeltaUp : DeltaDown;
        }

        return new ComparisonRow(
            data.CategoryId,
            name,
            BillPresentation.CategoryIcon(data.CategoryIcon),
            BillPresentation.CategoryTone(data.CategoryName, data.CategoryId),
            data.CurrentAmount,
            data.PreviousAmount,
            $"{MoneyPresentation.Format(data.CurrentAmount)}（{PreviousPrefix}{MoneyPresentation.Format(data.PreviousAmount)}）",
            text,
            kind,
            tone);
    }

    // ------------------------------------------------------------------ 导出 CSV

    /// <summary>
    /// 导出的 CSV 文本（支出分类汇总）：先是区间 / 账本范围 / 总支出 / 总收入 / 净收支 / 账单数量，空一行，再是「分类, 金额, 笔数, 占比(%)」。
    /// <list type="bullet">
    /// <item>金额一律是不带千分位的两位小数（<c>1234.50</c>）：带千分位的金额里有逗号，会把一个金额劈成两列。</item>
    /// <item>含逗号 / 引号 / 换行的文字（用户自建的分类名可以是任何字符）按 RFC 4180 加引号转义。</item>
    /// <item>用户可以自由命名的文字（分类名、账本名）若以 <c>= + - @</c> 或制表 / 回车符开头，前面补一个单引号：Excel / WPS 会把这样的单元格当公式执行
    /// （共享账本里别的成员取个 <c>=HYPERLINK(…)</c> 的分类名，导出的人一打开就中招）。金额不是文字，负数的 <c>-</c> 不能动。</item>
    /// <item>换行固定 CRLF，不随运行平台变化。UTF-8 BOM（Excel 打开中文 CSV 不乱码）由下载脚本加在文件开头，不在这段文本里。</item>
    /// </list>
    /// </summary>
    /// <param name="stats">报表数据</param>
    /// <param name="ledgerName">具体账本的名称；「全部账本」传 null（不写这一行）</param>
    public static string BuildCsv(CustomStatistics stats, string? ledgerName = null)
    {
        var csv = new StringBuilder();
        AppendRow(csv, $"自定义报表 {RangeText(stats.StartDate, stats.EndDate)}");

        if (!string.IsNullOrWhiteSpace(ledgerName))
        {
            AppendRow(csv, "账本", NeutralizeFormula(ledgerName.Trim()));
        }

        AppendRow(csv, "总支出", PlainAmount(stats.TotalExpense));
        AppendRow(csv, "总收入", PlainAmount(stats.TotalIncome));
        AppendRow(csv, "净收支", PlainAmount(stats.NetIncome));
        AppendRow(csv, "账单数量", stats.BillCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(csv);
        AppendRow(csv, "分类", "金额", "笔数", "占比(%)");

        foreach (var category in stats.ExpenseCategoryStats)
        {
            AppendRow(
                csv,
                NeutralizeFormula(category.CategoryName),
                PlainAmount(category.Amount),
                category.Count.ToString(CultureInfo.InvariantCulture),
                category.Percentage.ToString("0.00", CultureInfo.InvariantCulture));
        }

        return csv.ToString();
    }

    /// <summary>
    /// 用户自由命名的文字放进 CSV 之前的处理：以 <c>= + - @</c> 或制表 / 回车符开头时前面补一个单引号（OWASP 的 CSV 注入建议做法），
    /// 表格软件里显示为普通文字（单引号本身不显示）。只用于文字单元格，不要用在金额上（负数就是以 <c>-</c> 开头）。
    /// </summary>
    public static string NeutralizeFormula(string? text)
    {
        var value = text ?? string.Empty;
        return value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    /// <summary>CSV 的一个字段：含逗号 / 引号 / 换行时加引号（里面的引号写两个）</summary>
    public static string CsvField(string? value)
    {
        var text = value ?? string.Empty;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static void AppendRow(StringBuilder csv, params string[] fields)
    {
        csv.Append(string.Join(',', fields.Select(CsvField)));
        csv.Append("\r\n");
    }

    private static string PlainAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ 分享摘要

    /// <summary>
    /// 分享的文字摘要：【记账报表】区间（· 账本）→ 总览 → 支出 Top3 → 环比增长最多 / 减少最多（有才写）。格式沿用原页面，金额统一用 <see cref="MoneyPresentation"/>
    /// （千分位、两位小数、负数是「-¥…」而不是「¥-…」）。
    /// </summary>
    /// <param name="stats">报表数据</param>
    /// <param name="comparisons">分类环比（统计服务给的原始数据）</param>
    /// <param name="ledgerName">具体账本的名称；「全部账本」传 null</param>
    public static string BuildShareText(CustomStatistics stats, IReadOnlyList<CategoryComparisonData> comparisons, string? ledgerName = null)
    {
        var scope = string.IsNullOrWhiteSpace(ledgerName) ? string.Empty : $" · 账本：{ledgerName.Trim()}";
        var text = new StringBuilder();
        text.Append($"【记账报表】{RangeText(stats.StartDate, stats.EndDate)}{scope}");
        text.Append($"\n总支出 {MoneyPresentation.Format(stats.TotalExpense)}，总收入 {MoneyPresentation.Format(stats.TotalIncome)}，净收支 {MoneyPresentation.Format(stats.NetIncome)}（共 {stats.BillCount} 笔账单）");

        var top = stats.ExpenseCategoryStats.Take(3).ToList();
        if (top.Count > 0)
        {
            text.Append("\n支出 Top3：" + string.Join("、", top.Select(c => $"{c.CategoryName} {MoneyPresentation.Format(c.Amount)}")));
        }

        var increase = comparisons.Where(c => c.ChangeAmount > 0m).OrderByDescending(c => c.ChangeAmount).FirstOrDefault();
        if (increase is not null)
        {
            text.Append($"\n环比增长最多：{increase.CategoryName}（+{MoneyPresentation.Format(increase.ChangeAmount)}{RateSuffix(increase)}）");
        }

        var decrease = comparisons.Where(c => c.ChangeAmount < 0m).OrderBy(c => c.ChangeAmount).FirstOrDefault();
        if (decrease is not null)
        {
            text.Append($"\n环比减少最多：{decrease.CategoryName}（-{MoneyPresentation.Format(Math.Abs(decrease.ChangeAmount))}{RateSuffix(decrease)}）");
        }

        return text.ToString();
    }

    /// <summary>摘要里金额后面的变化率：「，+25.0%」；上期没有支出算不出变化率时不写</summary>
    private static string RateSuffix(CategoryComparisonData data) =>
        data.ChangeRate is { } rate ? $"，{StatisticsPresentation.FormatChangeRate(rate)}" : string.Empty;
}
