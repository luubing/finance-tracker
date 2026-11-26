using System.Globalization;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Time;
using FinanceTracker.Web.Shell;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 构成图例里的一行（圆环的一段对应一行）。
/// </summary>
/// <param name="Name">分类名或账本名</param>
/// <param name="Amount">金额</param>
/// <param name="Percentage">占比（0–100，沿用统计服务算出的两位小数）</param>
/// <param name="Count">笔数</param>
/// <param name="ColorIndex">配色序号（0 起，按金额排名；超出色板长度循环）</param>
public sealed record LegendRow(string Name, decimal Amount, decimal Percentage, int Count, int ColorIndex)
{
    /// <summary>图例上显示的占比，例如「36.7%」</summary>
    public string PercentText => StatisticsPresentation.PercentText(Percentage);
}

/// <summary>
/// 统计 Tab 的展示规则（纯函数，单元测试覆盖）：分段、月份 / 年份切换、账本胶囊、构成图例与文案。
/// 页面与组件只负责把这些结果摆出来，文案与判断不散落在 Razor 里。
/// </summary>
public static class StatisticsPresentation
{
    // ------------------------------------------------------------------ 分段

    /// <summary>分段：月度</summary>
    public const string SegmentMonthly = "monthly";

    /// <summary>分段：趋势</summary>
    public const string SegmentTrend = "trend";

    /// <summary>分段：年度</summary>
    public const string SegmentAnnual = "annual";

    /// <summary>分段控件的读屏名称</summary>
    public const string SegmentAriaLabel = "统计维度";

    /// <summary>三个分段（取值, 文字），顺序即显示顺序</summary>
    public static IReadOnlyList<(string Value, string Label)> Segments { get; } =
    [
        (SegmentMonthly, "月度"),
        (SegmentTrend, "趋势"),
        (SegmentAnnual, "年度"),
    ];

    /// <summary>把任意字符串规整成合法分段；不认识的取值（包括空）回到月度</summary>
    public static string NormalizeSegment(string? value) => value switch
    {
        SegmentTrend => SegmentTrend,
        SegmentAnnual => SegmentAnnual,
        _ => SegmentMonthly,
    };

    /// <summary>统计 Tab 根页面的路径</summary>
    public const string RootPath = "/statistics";

    /// <summary>
    /// 旧地址 → 统计页的分段：趋势分析、年度报告并入统计 Tab 之后，这两个地址只负责跳过去
    /// （直接访问由宿主在 HTTP 层跳转，应用内导航到它们由 <c>StatisticsRedirect</c> 页处理）。
    /// </summary>
    public static IReadOnlyList<(string Path, string Segment)> LegacyRoutes { get; } =
    [
        ("/trend", SegmentTrend),
        ("/annual-report", SegmentAnnual),
    ];

    /// <summary>地址（绝对 URI 或应用内路径）是不是统计页（不看查询串）</summary>
    public static bool IsStatisticsUri(string? uriOrPath) => TabNavigation.NormalizePath(uriOrPath) == RootPath;

    /// <summary>
    /// 统计页的地址（带分段）：旧地址 <c>/trend</c>、<c>/annual-report</c> 重定向到这里。
    /// 月度是默认分段，不带参数。
    /// </summary>
    public static string UriForSegment(string? segment)
    {
        var normalized = NormalizeSegment(segment);
        return normalized == SegmentMonthly ? RootPath : $"{RootPath}?segment={normalized}";
    }

    /// <summary>
    /// 从地址（绝对 URI 或应用内路径）的查询串里取出 <c>segment</c> 参数。
    /// 只认统计页自己的地址（其它页面的 <c>segment</c> 参数与统计无关）；没有这个参数或取值不认识时返回 null——
    /// 此时页面保持当前分段不变（点 Tab 栏回到统计页的地址不带参数，不应该把用户重置回月度）。
    /// </summary>
    public static string? SegmentFromUri(string? uriOrPath)
    {
        if (uriOrPath is null || !IsStatisticsUri(uriOrPath))
        {
            return null;
        }

        var queryStart = uriOrPath.IndexOf('?');
        if (queryStart < 0)
        {
            return null;
        }

        var query = uriOrPath[(queryStart + 1)..];
        var fragmentStart = query.IndexOf('#');
        if (fragmentStart >= 0)
        {
            query = query[..fragmentStart];
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            if (!key.Equals("segment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
            return Segments.FirstOrDefault(s => s.Value.Equals(value, StringComparison.OrdinalIgnoreCase)).Value;
        }

        return null;
    }

    /// <summary>右上角「自定义报表」按钮的读屏名称</summary>
    public const string ReportButtonLabel = "自定义报表";

    // ------------------------------------------------------------------ 月份 / 年份

    /// <summary>能切换到的最早年份（再往前没有账单，图表组件也不处理）</summary>
    public const int MinYear = 2000;

    /// <summary>能切换到的最晚年份</summary>
    public const int MaxYear = 2100;

    /// <summary>月份胶囊上的文字，例如「2026年11月」</summary>
    public static string MonthLabel(int year, int month) => $"{year}年{month}月";

    /// <summary>年份胶囊上的文字，例如「2026年」</summary>
    public static string YearLabel(int year) => $"{year}年";

    /// <summary>
    /// 月份平移（跨年进位 / 退位）；结果限制在 <see cref="MinYear"/> 年 1 月到 <see cref="MaxYear"/> 年 12 月之间。
    /// </summary>
    public static (int Year, int Month) ShiftMonth(int year, int month, int delta)
    {
        var index = Math.Clamp(year * 12 + (month - 1) + delta, MinYear * 12, MaxYear * 12 + 11);
        return (index / 12, index % 12 + 1);
    }

    /// <summary>能否再平移 <paramref name="delta"/> 个月（到边界后切换按钮置灰）</summary>
    public static bool CanShiftMonth(int year, int month, int delta) => ShiftMonth(year, month, delta) != (year, month);

    /// <summary>
    /// 月份平移，并且往后不超过 <paramref name="latest"/>——统计页用：未来的月份没有账单，「下一月」走到当月为止
    /// （预算页要给未来的月份设预算，仍用不设上限的 <see cref="ShiftMonth"/>）。往前的边界规则同 <see cref="ShiftMonth"/>。
    /// </summary>
    /// <param name="latest">能到的最晚月份（应用时区的当月）</param>
    public static (int Year, int Month) ShiftMonthUpTo(int year, int month, int delta, (int Year, int Month) latest)
    {
        if (delta > 0)
        {
            // 已经在上限之外（例如页面常驻跨了月份之后又被设成未来的月份）也只是原地不动，不会被「拉回」当月
            var room = Math.Max(0, latest.Year * 12 + (latest.Month - 1) - (year * 12 + (month - 1)));
            delta = Math.Min(delta, room);
        }

        return ShiftMonth(year, month, delta);
    }

    /// <summary>统计页：能否再平移 <paramref name="delta"/> 个月（往后到当月为止，当月时「下一月」置灰）</summary>
    public static bool CanShiftMonthUpTo(int year, int month, int delta, (int Year, int Month) latest) =>
        ShiftMonthUpTo(year, month, delta, latest) != (year, month);

    /// <summary>年份平移，限制在 <see cref="MinYear"/>–<see cref="MaxYear"/> 之间</summary>
    public static int ShiftYear(int year, int delta) => Math.Clamp(year + delta, MinYear, MaxYear);

    /// <summary>「上一月」按钮的读屏名称：带上目标月份，读屏用户知道会去哪一月</summary>
    public static string PreviousMonthLabel(int year, int month)
    {
        var (y, m) = ShiftMonth(year, month, -1);
        return $"上一月，{MonthLabel(y, m)}";
    }

    /// <summary>「下一月」按钮的读屏名称</summary>
    public static string NextMonthLabel(int year, int month)
    {
        var (y, m) = ShiftMonth(year, month, 1);
        return $"下一月，{MonthLabel(y, m)}";
    }

    // ------------------------------------------------------------------ 账本

    /// <summary>不筛选账本时胶囊上的文字</summary>
    public const string AllLedgersText = "全部账本";

    /// <summary>账本胶囊上的文字：选中账本的名称；没选或选中的账本已经不在列表里时是「全部账本」</summary>
    public static string LedgerPillText(Guid? ledgerId, IEnumerable<LedgerModel> ledgers)
    {
        if (ledgerId is { } id && ledgers.FirstOrDefault(l => l.Id == id) is { } ledger)
        {
            return ledger.Name;
        }

        return AllLedgersText;
    }

    /// <summary>账本胶囊的读屏名称</summary>
    public static string LedgerPillAriaLabel(string pillText) => $"账本：{pillText}，点按切换";

    /// <summary>
    /// 校验账本筛选：账本被删除 / 换了账号以后，原来选中的账本不在列表里了，回到「全部账本」（null）。
    /// </summary>
    public static Guid? ValidLedgerId(Guid? ledgerId, IEnumerable<LedgerModel> ledgers) =>
        ledgerId is { } id && ledgers.Any(l => l.Id == id) ? id : null;

    // ------------------------------------------------------------------ 构成（圆环 + 图例）

    /// <summary>色板长度：图例 / 圆环按金额排名依次取色，超出后循环</summary>
    public const int PaletteSize = 10;

    /// <summary>金额排名（0 起）对应的色板序号（CSS 类 <c>ft-hue--0</c> … <c>ft-hue--9</c>）</summary>
    public static int ColorIndex(int rank) => Math.Abs(rank) % PaletteSize;

    /// <summary>另一种账单类型（支出 ↔ 收入）</summary>
    public static BillType Other(BillType type) => type == BillType.Income ? BillType.Expense : BillType.Income;

    /// <summary>「支出」/「收入」</summary>
    public static string TypeName(BillType type) => type == BillType.Income ? "收入" : "支出";

    /// <summary>节标题：「支出构成」/「收入构成」</summary>
    public static string CompositionTitle(BillType type) => $"{TypeName(type)}构成";

    /// <summary>圆环中心的小字：「总支出」/「总收入」</summary>
    public static string TotalLabel(BillType type) => $"总{TypeName(type)}";

    /// <summary>标题旁切换按钮上的字：显示「点了会切到哪一类」</summary>
    public static string SwitchTypeText(BillType current) => TypeName(Other(current));

    /// <summary>标题旁切换按钮的读屏名称</summary>
    public static string SwitchTypeAriaLabel(BillType current) => $"切换为{CompositionTitle(Other(current))}";

    /// <summary>节标题右侧操作的文字：当前按分类看就显示「按账本」，反之亦然</summary>
    public static string DimensionActionText(bool byLedger) => byLedger ? "按分类" : "按账本";

    /// <summary>节标题右侧操作的读屏名称</summary>
    public static string DimensionActionAriaLabel(bool byLedger) => byLedger ? "按分类查看构成" : "按账本查看构成";

    /// <summary>图例的读屏名称</summary>
    public static string LegendAriaLabel(BillType type, bool byLedger) =>
        $"{CompositionTitle(type)}明细（{(byLedger ? "按账本" : "按分类")}）";

    /// <summary>当月没有这一类账单时的文案</summary>
    public static string CompositionEmptyText(BillType type) => $"当月暂无{TypeName(type)}账单";

    /// <summary>
    /// 能否按账本汇总：只在「全部账本」且用户至少有一个账本时才有意义——
    /// 选了具体账本，汇总只剩那一个账本；一个账本都没有（只剩历史账单），汇总只会出现一条「未归属账本」，都没有信息量。
    /// </summary>
    public static bool CanGroupByLedger(Guid? ledgerId, int ledgerCount) => ledgerId is null && ledgerCount > 0;

    /// <summary>按分类汇总的图例：金额降序（同额按名称），金额 ≤ 0 的不出现</summary>
    public static IReadOnlyList<LegendRow> LegendOfCategories(IEnumerable<CategoryStatistics> stats) =>
        Rank(stats.Select(s => (s.CategoryName, s.Amount, s.Percentage, s.Count)));

    /// <summary>按账本汇总的图例（无账本的历史账单归「未归属账本」，由统计服务给出）</summary>
    public static IReadOnlyList<LegendRow> LegendOfLedgers(IEnumerable<LedgerStatistics> stats) =>
        Rank(stats.Select(s => (s.LedgerName, s.Amount, s.Percentage, s.Count)));

    private static IReadOnlyList<LegendRow> Rank(IEnumerable<(string Name, decimal Amount, decimal Percentage, int Count)> items) =>
        items
            .Where(i => i.Amount > 0m)
            .OrderByDescending(i => i.Amount)
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .Select((item, index) => new LegendRow(item.Name, item.Amount, item.Percentage, item.Count, ColorIndex(index)))
            .ToList();

    /// <summary>
    /// 占比文字：保留一位小数（「36.7%」）；大于 0 但不足 0.1% 的显示「&lt;0.1%」，避免一个真实存在的分类显示成「0.0%」。
    /// </summary>
    public static string PercentText(decimal percentage)
    {
        if (percentage > 0m && percentage < 0.05m)
        {
            return "<0.1%";
        }

        return Math.Round(percentage, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    // ------------------------------------------------------------------ 月度各节

    /// <summary>节标题：预算执行</summary>
    public const string BudgetTitle = "预算执行";

    /// <summary>节标题：分类排行</summary>
    public const string RankTitle = "分类排行";

    /// <summary>节标题：支出日历</summary>
    public const string CalendarTitle = "支出日历";

    /// <summary>节标题：累计收支</summary>
    public const string CumulativeTitle = "累计收支";

    /// <summary>分类排行没有数据时的文案</summary>
    public const string RankEmptyText = "当月暂无支出账单";

    /// <summary>支出日历没有数据时的文案（沿用原文案）</summary>
    public const string CalendarEmptyText = "当月暂无支出账单";

    /// <summary>累计收支没有数据时的文案（沿用原文案）</summary>
    public const string CumulativeEmptyText = "当月暂无收支账单";

    /// <summary>
    /// 汇总里金额太长（≥ 12 个字符，例如 ¥1,234,567.89）时用缩小的字号，不挤出单元格
    /// </summary>
    public static bool IsLongAmount(decimal amount) => MoneyPresentation.Format(amount).Length >= 12;

    // ------------------------------------------------------------------ 趋势：时间范围 / 时间维度 / 明细表

    /// <summary>时间范围：最近 7 天</summary>
    public const string TrendRange7Days = "7days";

    /// <summary>时间范围：最近 30 天</summary>
    public const string TrendRange30Days = "30days";

    /// <summary>时间范围：最近 90 天</summary>
    public const string TrendRange90Days = "90days";

    /// <summary>时间范围：最近 12 个月</summary>
    public const string TrendRange12Months = "12months";

    /// <summary>默认时间范围（与原趋势分析页一致）</summary>
    public const string DefaultTrendRange = TrendRange7Days;

    /// <summary>时间范围菜单的标题</summary>
    public const string TrendRangeSheetTitle = "选择时间范围";

    /// <summary>四个时间范围（取值, 文字），顺序即菜单顺序</summary>
    public static IReadOnlyList<(string Value, string Label)> TrendRanges { get; } =
    [
        (TrendRange7Days, "最近7天"),
        (TrendRange30Days, "最近30天"),
        (TrendRange90Days, "最近90天"),
        (TrendRange12Months, "最近12个月"),
    ];

    /// <summary>规整时间范围；不认识的取值（包括空）回到默认的最近 7 天</summary>
    public static string NormalizeTrendRange(string? value) =>
        TrendRanges.Any(r => r.Value == value) ? value! : DefaultTrendRange;

    /// <summary>时间范围胶囊上的文字，例如「最近7天」</summary>
    public static string TrendRangeText(string? range)
    {
        var normalized = NormalizeTrendRange(range);
        return TrendRanges.First(r => r.Value == normalized).Label;
    }

    /// <summary>时间范围胶囊的读屏名称</summary>
    public static string TrendRangeAriaLabel(string pillText) => $"时间范围：{pillText}，点按切换";

    /// <summary>
    /// 时间范围对应的起止时刻（应用时区）：终点是「现在」，起点往前推 7 / 30 / 90 天或 12 个月。
    /// 口径与原趋势分析页一致；统计服务拿到后按自己的规则归整到自然日。
    /// </summary>
    /// <param name="range">时间范围取值（不认识的按默认处理）</param>
    /// <param name="localNow">应用时区的当前时刻（<c>AppTimeZone.ToLocal(DateTime.UtcNow)</c> 的结果）</param>
    public static (DateTime Start, DateTime End) TrendRangeBounds(string? range, DateTime localNow) =>
        NormalizeTrendRange(range) switch
        {
            TrendRange30Days => (localNow.AddDays(-30), localNow),
            TrendRange90Days => (localNow.AddDays(-90), localNow),
            TrendRange12Months => (localNow.AddMonths(-12), localNow),
            _ => (localNow.AddDays(-7), localNow),
        };

    /// <summary>默认时间维度：按日</summary>
    public const string DefaultTrendDimension = "day";

    /// <summary>时间维度分段控件的读屏名称</summary>
    public const string TrendDimensionAriaLabel = "时间维度";

    /// <summary>三个时间维度（取值, 文字），取值与 <see cref="TrendDimensionParser"/> 认识的名称一致</summary>
    public static IReadOnlyList<(string Value, string Label)> TrendDimensions { get; } =
    [
        ("day", "按日"),
        ("week", "按周"),
        ("month", "按月"),
    ];

    /// <summary>规整时间维度；不认识的取值（包括空）回到按日</summary>
    public static string NormalizeTrendDimension(string? value) =>
        TrendDimensions.Any(d => d.Value == value) ? value! : DefaultTrendDimension;

    /// <summary>把时间维度取值转成统计服务的枚举；不认识的取值按「按日」</summary>
    public static TrendDimension ParseTrendDimension(string? value) =>
        TrendDimensionParser.TryParse(value, out var dimension) ? dimension : TrendDimension.Day;

    /// <summary>明细表里的日期：按日 / 按周（周一）写完整日期，按月只写年月</summary>
    public static string TrendDateLabel(DateTime date, TrendDimension dimension) =>
        date.ToString(dimension == TrendDimension.Month ? "yyyy-MM" : "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>图表横轴上的日期：按日 / 按周写月-日，按月写年-月</summary>
    public static string TrendAxisLabel(DateTime date, TrendDimension dimension) =>
        date.ToString(dimension == TrendDimension.Month ? "yy-MM" : "MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// 把趋势数据补成连续的横轴（折线图用；第 2 轮评审后用户拍板「趋势折线图横轴补零」）。
    /// 统计服务只返回「有账单的时间桶」，直接画线会让没有账单的日 / 周 / 月凭空消失、相邻两点的时间跨度被拉平；
    /// 这里按 <paramref name="dimension"/> 从起点所在的时间桶一路走到终点所在的时间桶（按日 = 自然日，按周 = 周一，按月 = 月初，
    /// 与 <see cref="Core.Interfaces.IStatisticsService.GetTrendDataAsync"/> 的分桶一致），每个桶一个点，没有账单的桶补 0。
    /// <para>
    /// <b>只给趋势视图的三张折线图用</b>：明细表仍只列有账单的桶、KPI 合计不变（补的全是 0）；
    /// 月度视图的累计收支图与支出日历取数不走这里（用户确认只补趋势视图）。
    /// 服务返回的桶一定落在 [起点所在桶, 终点所在桶] 内（分桶函数单调）；万一有桶落在范围外，也原样保留，保证补零前后合计不变。
    /// </para>
    /// </summary>
    /// <param name="data">统计服务返回的趋势数据（按桶聚合、日期升序）</param>
    /// <param name="start">窗口起点（应用时区，<see cref="TrendRangeBounds"/> 的结果）</param>
    /// <param name="end">窗口终点（应用时区）</param>
    /// <param name="dimension">时间维度</param>
    /// <returns>按日期升序、桶连续的趋势数据；窗口为空（终点早于起点）且没有数据时返回空列表</returns>
    public static IReadOnlyList<TrendData> FillTrendBuckets(IReadOnlyList<TrendData> data, DateTime start, DateTime end, TrendDimension dimension)
    {
        var byBucket = data
            .GroupBy(t => t.Date)
            .ToDictionary(
                g => g.Key,
                g => new TrendData { Date = g.Key, Expense = g.Sum(t => t.Expense), Income = g.Sum(t => t.Income) });

        var filled = new List<TrendData>();
        var last = TrendBucketStart(end, dimension);
        for (var bucket = TrendBucketStart(start, dimension); bucket <= last; bucket = NextTrendBucket(bucket, dimension))
        {
            filled.Add(byBucket.Remove(bucket, out var existing) ? existing : new TrendData { Date = bucket });
        }

        // 范围外的桶（理论上不会有）原样并回去，再按日期排好
        if (byBucket.Count > 0)
        {
            filled.AddRange(byBucket.Values);
            filled.Sort((a, b) => a.Date.CompareTo(b.Date));
        }

        return filled;
    }

    /// <summary>某一时刻所在的时间桶的起点：按日 = 当天 0 点，按周 = 周一 0 点，按月 = 当月 1 日 0 点</summary>
    private static DateTime TrendBucketStart(DateTime moment, TrendDimension dimension) => dimension switch
    {
        TrendDimension.Week => TimeBoundaries.StartOfWeek(moment),
        TrendDimension.Month => new DateTime(moment.Year, moment.Month, 1),
        _ => moment.Date,
    };

    private static DateTime NextTrendBucket(DateTime bucket, TrendDimension dimension) => dimension switch
    {
        TrendDimension.Week => bucket.AddDays(7),
        TrendDimension.Month => bucket.AddMonths(1),
        _ => bucket.AddDays(1),
    };

    /// <summary>节标题：支出趋势</summary>
    public const string TrendExpenseTitle = "支出趋势";

    /// <summary>节标题：收入趋势</summary>
    public const string TrendIncomeTitle = "收入趋势";

    /// <summary>节标题：净收支趋势</summary>
    public const string TrendNetTitle = "净收支趋势";

    /// <summary>节标题：趋势数据明细</summary>
    public const string TrendTableTitle = "趋势数据明细";

    /// <summary>支出趋势没有数据时的文案（沿用原文案）</summary>
    public const string TrendExpenseEmptyText = "暂无支出数据";

    /// <summary>收入趋势没有数据时的文案（沿用原文案）</summary>
    public const string TrendIncomeEmptyText = "暂无收入数据";

    /// <summary>净收支趋势没有数据时的文案（沿用原文案）</summary>
    public const string TrendNetEmptyText = "暂无数据";

    /// <summary>明细表一次显示的行数（原表格每页 10 行；超出的由「显示更多」逐批展开）</summary>
    public const int TrendTablePageSize = 10;

    /// <summary>明细表四列的取值（排序用）：日期 / 支出 / 收入 / 净收支</summary>
    public const string TrendColumnDate = "date";

    /// <summary>明细表列：支出</summary>
    public const string TrendColumnExpense = "expense";

    /// <summary>明细表列：收入</summary>
    public const string TrendColumnIncome = "income";

    /// <summary>明细表列：净收支</summary>
    public const string TrendColumnNet = "net";

    /// <summary>明细表的列（取值, 表头文字），顺序即列顺序</summary>
    public static IReadOnlyList<(string Value, string Label)> TrendColumns { get; } =
    [
        (TrendColumnDate, "日期"),
        (TrendColumnExpense, "支出"),
        (TrendColumnIncome, "收入"),
        (TrendColumnNet, "净收支"),
    ];

    /// <summary>
    /// 点表头后的排序状态：换一列 → 该列升序；同一列再点 → 降序；再点 → 取消排序（回到数据原顺序，即日期升序）。
    /// </summary>
    public static (string? Column, bool Descending) NextTrendSort((string? Column, bool Descending) current, string clicked)
    {
        if (current.Column != clicked)
        {
            return (clicked, false);
        }

        return current.Descending ? (null, false) : (clicked, true);
    }

    /// <summary>
    /// 按列排序明细（稳定排序：同值保持原顺序）；<paramref name="column"/> 为空或不认识时保持原顺序。
    /// </summary>
    public static IReadOnlyList<TrendData> SortTrend(IReadOnlyList<TrendData> rows, string? column, bool descending)
    {
        Func<TrendData, IComparable>? key = column switch
        {
            TrendColumnDate => t => t.Date,
            TrendColumnExpense => t => t.Expense,
            TrendColumnIncome => t => t.Income,
            TrendColumnNet => t => t.NetIncome,
            _ => null,
        };

        if (key is null)
        {
            return rows;
        }

        return (descending ? rows.OrderByDescending(key) : rows.OrderBy(key)).ToList();
    }

    /// <summary>表头 <c>aria-sort</c> 取值：ascending / descending / none</summary>
    public static string AriaSort((string? Column, bool Descending) sort, string column) =>
        sort.Column != column ? "none" : sort.Descending ? "descending" : "ascending";

    /// <summary>「显示更多」按钮的文字，例如「显示更多（还有 25 条）」</summary>
    public static string ShowMoreText(int remaining) => $"显示更多（还有 {remaining} 条）";

    // ------------------------------------------------------------------ 年度：年份 / 同比 / 各节

    /// <summary>年份胶囊的读屏名称</summary>
    public const string YearAriaLabel = "年份";

    /// <summary>能否再平移 <paramref name="delta"/> 年（到边界后切换按钮置灰）</summary>
    public static bool CanShiftYear(int year, int delta) => ShiftYear(year, delta) != year;

    /// <summary>「上一年」按钮的读屏名称：带上目标年份</summary>
    public static string PreviousYearLabel(int year) => $"上一年，{YearLabel(ShiftYear(year, -1))}";

    /// <summary>「下一年」按钮的读屏名称</summary>
    public static string NextYearLabel(int year) => $"下一年，{YearLabel(ShiftYear(year, 1))}";

    /// <summary>节标题：同比分析</summary>
    public const string YoyTitle = "同比分析";

    /// <summary>节标题：月度趋势</summary>
    public const string AnnualMonthlyTitle = "月度趋势";

    /// <summary>年度没有账单时的文案</summary>
    public const string AnnualEmptyText = "当年暂无账单";

    // 年度的「分类统计」在第 2 轮评审后拆成「支出构成 / 收入构成」两个圆环（用户拍板）：节标题沿用 CompositionTitle(type)，
    // 占比各自占本类合计（统计服务的 AnnualStatistics.ExpenseCategoryStats / IncomeCategoryStats），不再有「含支出与收入分类，占比按两者合计计算」那行说明。

    /// <summary>年度构成圆环中心的小字：「全年支出」/「全年收入」</summary>
    public static string AnnualTotalLabel(BillType type) => $"全年{TypeName(type)}";

    /// <summary>年度构成没有这一类账单时的文案：「当年暂无支出账单」/「当年暂无收入账单」</summary>
    public static string AnnualCompositionEmptyText(BillType type) => $"当年暂无{TypeName(type)}账单";

    /// <summary>年度构成图例的读屏名称：「全年支出分类明细」/「全年收入分类明细」</summary>
    public static string AnnualLegendAriaLabel(BillType type) => $"全年{TypeName(type)}分类明细";

    /// <summary>
    /// 同比期间没有覆盖整年（当年只比较到今天为止）
    /// </summary>
    public static bool IsPartialYear(YearOverYearData data) =>
        data.CurrentPeriodEnd < new DateTime(data.CurrentYear, 12, 31);

    /// <summary>同比对比的口径：当年「年初至今 vs 去年同期」，已结束的年份「2025年 vs 2024年」</summary>
    public static string YoyScopeText(YearOverYearData data) =>
        IsPartialYear(data) ? "年初至今 vs 去年同期" : $"{data.CurrentYear}年 vs {data.CurrentYear - 1}年";

    /// <summary>对比的两个期间，例如「2026-01-01 ~ 2026-11-25 对比 2025-01-01 ~ 2025-11-25」</summary>
    public static string YoyPeriodText(YearOverYearData data) =>
        $"{PeriodText(data.CurrentPeriodStart, data.CurrentPeriodEnd)} 对比 {PeriodText(data.PreviousPeriodStart, data.PreviousPeriodEnd)}";

    private static string PeriodText(DateTime start, DateTime end) =>
        string.Create(CultureInfo.InvariantCulture, $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd}");

    /// <summary>
    /// 同比变化率文字：保留一位小数，正数带 +；去年同期为 0（<paramref name="rate"/> 为 null）时显示「—」，
    /// 而不是会被误读成「没有变化」的 0%。四舍五入后是 0.0 的不带符号。
    /// </summary>
    public static string FormatChangeRate(decimal? rate)
    {
        if (rate is not { } value)
        {
            return "—";
        }

        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        if (rounded == 0m)
        {
            // -0.04 四舍五入后是带符号的零，直接写死，免得格式化出「-0.0%」
            return "0.0%";
        }

        var text = rounded.ToString("0.0", CultureInfo.InvariantCulture);
        return rounded > 0m ? $"+{text}%" : $"{text}%";
    }

    /// <summary>
    /// 变化率文字的字号档位：空 = 正常；<c>long</c>（≥ 9 个字符，例如 +12345.6%）与 <c>xlong</c>（≥ 12 个字符）逐级缩小，
    /// 免得去年同期基数很小时变化率长得挤出半张卡的宽度。
    /// </summary>
    public static string RateSize(string rateText) => rateText.Length switch
    {
        >= 12 => "xlong",
        >= 9 => "long",
        _ => string.Empty,
    };

    /// <summary>
    /// 变化率的语义色：good（好事）/ bad（坏事）/ flat（没有变化或没有对比基数）。
    /// 收入涨是好事（<paramref name="higherIsBetter"/> 为 true），支出涨是坏事（false）。按显示的一位小数判断，所以「0.0%」永远是 flat。
    /// </summary>
    public static string ChangeTone(decimal? rate, bool higherIsBetter)
    {
        if (rate is not { } value || Math.Round(value, 1, MidpointRounding.AwayFromZero) == 0m)
        {
            return "flat";
        }

        return (value > 0m) == higherIsBetter ? "good" : "bad";
    }
}
