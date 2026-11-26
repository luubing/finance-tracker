using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 统计 Tab 的展示规则：分段、月份 / 年份切换与边界、账本胶囊、构成图例（排序、配色、占比文字）与文案
/// </summary>
public class StatisticsPresentationTests
{
    private static LedgerModel Ledger(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static CategoryStatistics Category(string name, decimal amount, decimal percentage = 0m, int count = 1) =>
        new() { CategoryId = Guid.NewGuid(), CategoryName = name, Amount = amount, Percentage = percentage, Count = count };

    private static LedgerStatistics LedgerStat(string name, decimal amount, decimal percentage = 0m, int count = 1) =>
        new() { LedgerId = Guid.NewGuid(), LedgerName = name, Amount = amount, Percentage = percentage, Count = count };

    // ───────────────────────── 分段 ─────────────────────────

    [Fact]
    public void 三个分段按月度_趋势_年度的顺序给出()
    {
        Assert.Equal(
            new[] { ("monthly", "月度"), ("trend", "趋势"), ("annual", "年度") },
            StatisticsPresentation.Segments.ToArray());
    }

    [Theory]
    [InlineData("monthly", "monthly")]
    [InlineData("trend", "trend")]
    [InlineData("annual", "annual")]
    [InlineData("", "monthly")]
    [InlineData(null, "monthly")]
    [InlineData("unknown", "monthly")]
    [InlineData("TREND", "monthly")]
    public void 规整分段_不认识的取值回到月度(string? value, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.NormalizeSegment(value));
    }

    // ───────────────────────── 月份 / 年份 ─────────────────────────

    [Theory]
    [InlineData(2026, 11, -1, 2026, 10)]
    [InlineData(2026, 11, 1, 2026, 12)]
    [InlineData(2026, 12, 1, 2027, 1)]
    [InlineData(2026, 1, -1, 2025, 12)]
    [InlineData(2026, 3, 12, 2027, 3)]
    [InlineData(2026, 3, -15, 2024, 12)]
    [InlineData(2026, 11, 0, 2026, 11)]
    public void 月份平移_跨年进位与退位(int year, int month, int delta, int expectedYear, int expectedMonth)
    {
        Assert.Equal((expectedYear, expectedMonth), StatisticsPresentation.ShiftMonth(year, month, delta));
    }

    [Fact]
    public void 月份平移到边界后停住_不越过最早与最晚年份()
    {
        Assert.Equal((2000, 1), StatisticsPresentation.ShiftMonth(2000, 1, -1));
        Assert.Equal((2000, 1), StatisticsPresentation.ShiftMonth(2000, 3, -100));
        Assert.Equal((2100, 12), StatisticsPresentation.ShiftMonth(2100, 12, 1));
        Assert.Equal((2100, 12), StatisticsPresentation.ShiftMonth(2100, 10, 100));
    }

    [Fact]
    public void 到边界后不能再往前或往后()
    {
        Assert.False(StatisticsPresentation.CanShiftMonth(2000, 1, -1));
        Assert.True(StatisticsPresentation.CanShiftMonth(2000, 1, 1));
        Assert.False(StatisticsPresentation.CanShiftMonth(2100, 12, 1));
        Assert.True(StatisticsPresentation.CanShiftMonth(2100, 12, -1));
        Assert.True(StatisticsPresentation.CanShiftMonth(2026, 11, -1));
        Assert.True(StatisticsPresentation.CanShiftMonth(2026, 11, 1));
    }

    [Theory]
    [InlineData(2026, 10, 1, 2026, 11)] // 上月 → 当月：可以
    [InlineData(2026, 11, 1, 2026, 11)] // 当月：不能再往后（「下一月」置灰）
    [InlineData(2026, 11, 5, 2026, 11)] // 一次平移很多个月也只到当月为止
    [InlineData(2026, 12, 1, 2026, 12)] // 已经在未来的月份（不该发生）：原地不动，不会被拉回当月
    [InlineData(2025, 12, 1, 2026, 1)] // 跨年往后
    [InlineData(2026, 11, -1, 2026, 10)] // 往前不受上限影响
    [InlineData(2026, 1, -1, 2025, 12)]
    public void 统计页的月份平移往后最晚到当月(int year, int month, int delta, int expectedYear, int expectedMonth)
    {
        Assert.Equal((expectedYear, expectedMonth), StatisticsPresentation.ShiftMonthUpTo(year, month, delta, (2026, 11)));
    }

    [Fact]
    public void 统计页当月时下一月不可用_上一月可用_预算页的月份平移不受影响()
    {
        Assert.False(StatisticsPresentation.CanShiftMonthUpTo(2026, 11, 1, (2026, 11)));
        Assert.True(StatisticsPresentation.CanShiftMonthUpTo(2026, 11, -1, (2026, 11)));
        Assert.True(StatisticsPresentation.CanShiftMonthUpTo(2026, 10, 1, (2026, 11)));

        // 最早年份的边界规则同 ShiftMonth
        Assert.False(StatisticsPresentation.CanShiftMonthUpTo(2000, 1, -1, (2026, 11)));

        // 预算页仍然可以给未来的月份设预算
        Assert.True(StatisticsPresentation.CanShiftMonth(2026, 11, 1));
        Assert.Equal((2026, 12), StatisticsPresentation.ShiftMonth(2026, 11, 1));
    }

    [Theory]
    [InlineData(2026, 1, 2027)]
    [InlineData(2026, -1, 2025)]
    [InlineData(2000, -1, 2000)]
    [InlineData(2100, 1, 2100)]
    [InlineData(2099, 5, 2100)]
    public void 年份平移_限制在最早与最晚年份之间(int year, int delta, int expected)
    {
        Assert.Equal(expected, StatisticsPresentation.ShiftYear(year, delta));
    }

    [Fact]
    public void 月份与年份胶囊的文字()
    {
        Assert.Equal("2026年11月", StatisticsPresentation.MonthLabel(2026, 11));
        Assert.Equal("2026年1月", StatisticsPresentation.MonthLabel(2026, 1));
        Assert.Equal("2026年", StatisticsPresentation.YearLabel(2026));
    }

    [Fact]
    public void 上一月下一月按钮的读屏名称带上目标月份_跨年也对()
    {
        Assert.Equal("上一月，2026年10月", StatisticsPresentation.PreviousMonthLabel(2026, 11));
        Assert.Equal("下一月，2026年12月", StatisticsPresentation.NextMonthLabel(2026, 11));
        Assert.Equal("上一月，2025年12月", StatisticsPresentation.PreviousMonthLabel(2026, 1));
        Assert.Equal("下一月，2027年1月", StatisticsPresentation.NextMonthLabel(2026, 12));
    }

    // ───────────────────────── 账本 ─────────────────────────

    [Fact]
    public void 账本胶囊显示选中账本的名称_没选或选中的账本不在列表里时是全部账本()
    {
        var daily = Ledger("日常开销");
        var travel = Ledger("旅行基金");
        var ledgers = new[] { daily, travel };

        Assert.Equal("旅行基金", StatisticsPresentation.LedgerPillText(travel.Id, ledgers));
        Assert.Equal("全部账本", StatisticsPresentation.LedgerPillText(null, ledgers));
        Assert.Equal("全部账本", StatisticsPresentation.LedgerPillText(Guid.NewGuid(), ledgers));
        Assert.Equal("全部账本", StatisticsPresentation.LedgerPillText(daily.Id, []));
    }

    [Fact]
    public void 账本胶囊的读屏名称带上当前选择()
    {
        Assert.Equal("账本：全部账本，点按切换", StatisticsPresentation.LedgerPillAriaLabel("全部账本"));
        Assert.Equal("账本：旅行基金，点按切换", StatisticsPresentation.LedgerPillAriaLabel("旅行基金"));
    }

    [Fact]
    public void 校验账本筛选_账本被删除或换了账号后回到全部账本()
    {
        var daily = Ledger("日常开销");

        Assert.Equal(daily.Id, StatisticsPresentation.ValidLedgerId(daily.Id, [daily]));
        Assert.Null(StatisticsPresentation.ValidLedgerId(daily.Id, []));
        Assert.Null(StatisticsPresentation.ValidLedgerId(Guid.NewGuid(), [daily]));
        Assert.Null(StatisticsPresentation.ValidLedgerId(null, [daily]));
    }

    [Theory]
    [InlineData(false, 3, true)]
    [InlineData(false, 1, true)]
    [InlineData(false, 0, false)]
    [InlineData(true, 3, false)]
    [InlineData(true, 0, false)]
    public void 只有全部账本且至少有一个账本时才能按账本汇总(bool ledgerSelected, int ledgerCount, bool expected)
    {
        Guid? ledgerId = ledgerSelected ? Guid.NewGuid() : null;

        Assert.Equal(expected, StatisticsPresentation.CanGroupByLedger(ledgerId, ledgerCount));
    }

    // ───────────────────────── 构成：图例 ─────────────────────────

    [Fact]
    public void 图例按金额降序_金额相同按名称排_金额不大于零的不出现()
    {
        var rows = StatisticsPresentation.LegendOfCategories(
        [
            Category("交通出行", 120m),
            Category("餐饮美食", 580m),
            Category("零钱", 0m),
            Category("购物消费", 120m),
            Category("退款冲销", -10m),
        ]);

        Assert.Equal(new[] { "餐饮美食", "交通出行", "购物消费" }, rows.Select(r => r.Name));
    }

    [Fact]
    public void 图例配色按金额排名依次取色_超出色板长度后循环()
    {
        var stats = Enumerable.Range(0, 12).Select(i => Category($"分类{i:00}", 1000m - i)).ToList();

        var rows = StatisticsPresentation.LegendOfCategories(stats);

        Assert.Equal(12, rows.Count);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => i % StatisticsPresentation.PaletteSize), rows.Select(r => r.ColorIndex));
        Assert.Equal(new[] { 0, 1, 2 }, rows.Take(3).Select(r => r.ColorIndex));
        Assert.Equal(0, rows[10].ColorIndex);
    }

    [Fact]
    public void 过滤掉零金额后配色序号仍然连续_不留空档()
    {
        var rows = StatisticsPresentation.LegendOfCategories(
        [
            Category("A", 300m),
            Category("B", 0m),
            Category("C", 200m),
        ]);

        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.ColorIndex));
    }

    [Fact]
    public void 图例保留统计服务给出的占比与笔数()
    {
        var rows = StatisticsPresentation.LegendOfCategories([Category("餐饮美食", 580m, percentage: 36.74m, count: 12)]);

        var row = Assert.Single(rows);
        Assert.Equal(580m, row.Amount);
        Assert.Equal(36.74m, row.Percentage);
        Assert.Equal(12, row.Count);
        Assert.Equal("36.7%", row.PercentText);
    }

    [Fact]
    public void 按账本汇总的图例沿用同样的排序与配色_未归属账本也是一行()
    {
        var rows = StatisticsPresentation.LegendOfLedgers(
        [
            LedgerStat("日常开销", 300m),
            new LedgerStatistics { LedgerId = null, LedgerName = LedgerStatistics.UnassignedLedgerName, Amount = 450m, Count = 3 },
            LedgerStat("旅行基金", 0m),
        ]);

        Assert.Equal(new[] { "未归属账本", "日常开销" }, rows.Select(r => r.Name));
        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.ColorIndex));
    }

    [Fact]
    public void 没有任何账单时图例为空()
    {
        Assert.Empty(StatisticsPresentation.LegendOfCategories([]));
        Assert.Empty(StatisticsPresentation.LegendOfLedgers([]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 9)]
    [InlineData(10, 0)]
    [InlineData(23, 3)]
    [InlineData(-1, 1)]
    public void 色板序号随排名循环(int rank, int expected)
    {
        Assert.Equal(expected, StatisticsPresentation.ColorIndex(rank));
    }

    [Theory]
    [InlineData(36.74, "36.7%")]
    [InlineData(0, "0.0%")]
    [InlineData(0.04, "<0.1%")]
    [InlineData(0.05, "0.1%")]
    [InlineData(12.25, "12.3%")]
    [InlineData(99.95, "100.0%")]
    [InlineData(100, "100.0%")]
    public void 占比文字保留一位小数_不足千分之一的显示小于号(double percentage, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.PercentText((decimal)percentage));
    }

    // ───────────────────────── 构成：文案 ─────────────────────────

    [Fact]
    public void 支出与收入的构成文案()
    {
        Assert.Equal("支出构成", StatisticsPresentation.CompositionTitle(BillType.Expense));
        Assert.Equal("收入构成", StatisticsPresentation.CompositionTitle(BillType.Income));
        Assert.Equal("总支出", StatisticsPresentation.TotalLabel(BillType.Expense));
        Assert.Equal("总收入", StatisticsPresentation.TotalLabel(BillType.Income));
        Assert.Equal("当月暂无支出账单", StatisticsPresentation.CompositionEmptyText(BillType.Expense));
        Assert.Equal("当月暂无收入账单", StatisticsPresentation.CompositionEmptyText(BillType.Income));
    }

    [Fact]
    public void 切换按钮显示点了会切到哪一类()
    {
        Assert.Equal(BillType.Income, StatisticsPresentation.Other(BillType.Expense));
        Assert.Equal(BillType.Expense, StatisticsPresentation.Other(BillType.Income));
        Assert.Equal("收入", StatisticsPresentation.SwitchTypeText(BillType.Expense));
        Assert.Equal("支出", StatisticsPresentation.SwitchTypeText(BillType.Income));
        Assert.Equal("切换为收入构成", StatisticsPresentation.SwitchTypeAriaLabel(BillType.Expense));
        Assert.Equal("切换为支出构成", StatisticsPresentation.SwitchTypeAriaLabel(BillType.Income));
    }

    [Fact]
    public void 汇总口径按钮显示点了会切到哪种口径()
    {
        Assert.Equal("按账本", StatisticsPresentation.DimensionActionText(byLedger: false));
        Assert.Equal("按分类", StatisticsPresentation.DimensionActionText(byLedger: true));
        Assert.Equal("按账本查看构成", StatisticsPresentation.DimensionActionAriaLabel(byLedger: false));
        Assert.Equal("按分类查看构成", StatisticsPresentation.DimensionActionAriaLabel(byLedger: true));
        Assert.Equal("支出构成明细（按分类）", StatisticsPresentation.LegendAriaLabel(BillType.Expense, byLedger: false));
        Assert.Equal("收入构成明细（按账本）", StatisticsPresentation.LegendAriaLabel(BillType.Income, byLedger: true));
    }

    // ───────────────────────── 金额位数 ─────────────────────────

    [Theory]
    [InlineData(0, false)]
    [InlineData(8713.5, false)]
    [InlineData(99999.99, false)]
    [InlineData(123456.78, false)]
    [InlineData(1234567.89, true)]
    [InlineData(1000000, true)]
    [InlineData(-123456.78, true)]
    [InlineData(-8713.5, false)]
    public void 金额太长时汇总用缩小的字号_十二个字符起算(double amount, bool expected)
    {
        Assert.Equal(expected, StatisticsPresentation.IsLongAmount((decimal)amount));
    }

    // ───────────────────────── 地址：旧地址跳转与分段参数 ─────────────────────────

    [Fact]
    public void 旧地址只有趋势分析与年度报告_各自落在对应分段()
    {
        Assert.Equal(
            new[] { ("/trend", "trend"), ("/annual-report", "annual") },
            StatisticsPresentation.LegacyRoutes.ToArray());
    }

    [Theory]
    [InlineData("monthly", "/statistics")]
    [InlineData("trend", "/statistics?segment=trend")]
    [InlineData("annual", "/statistics?segment=annual")]
    [InlineData("", "/statistics")]
    [InlineData(null, "/statistics")]
    [InlineData("nonsense", "/statistics")]
    public void 统计页的地址_月度是默认分段不带参数(string? segment, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.UriForSegment(segment));
    }

    [Fact]
    public void 旧地址对应的统计页地址_与宿主的跳转目标一致()
    {
        // Web.Server 对旧地址回 302 时用的就是这里的结果：直接访问 /trend 落在 /statistics?segment=trend
        Assert.Equal(
            new[] { "/statistics?segment=trend", "/statistics?segment=annual" },
            StatisticsPresentation.LegacyRoutes.Select(r => StatisticsPresentation.UriForSegment(r.Segment)).ToArray());
    }

    [Theory]
    [InlineData("/statistics", true)]
    [InlineData("/statistics/", true)]
    [InlineData("/Statistics?segment=trend", true)]
    [InlineData("http://localhost:5999/statistics?segment=annual#top", true)]
    // MAUI Blazor 宿主的根地址：Android https://0.0.0.0/，iOS / Mac Catalyst app://0.0.0.0/（协议不是 http(s) 也要认）
    [InlineData("https://0.0.0.0/statistics", true)]
    [InlineData("app://0.0.0.0/statistics", true)]
    [InlineData("app://0.0.0.0/Statistics/?segment=trend#top", true)]
    [InlineData("app://0.0.0.0/", false)]
    [InlineData("app://0.0.0.0/bills", false)]
    [InlineData("/statistics-other", false)]
    [InlineData("/trend", false)]
    [InlineData("/", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 判断地址是不是统计页_不看查询串(string? uri, bool expected)
    {
        Assert.Equal(expected, StatisticsPresentation.IsStatisticsUri(uri));
    }

    [Theory]
    [InlineData("/statistics?segment=trend", "trend")]
    [InlineData("/statistics?segment=annual", "annual")]
    [InlineData("/statistics?segment=monthly", "monthly")]
    [InlineData("/statistics?segment=TREND", "trend")]
    [InlineData("/statistics?SEGMENT=annual", "annual")]
    [InlineData("http://localhost:5999/statistics?segment=annual", "annual")]
    [InlineData("http://localhost:5999/statistics/?x=1&segment=trend&y=2", "trend")]
    [InlineData("https://0.0.0.0/statistics?segment=annual", "annual")]
    [InlineData("app://0.0.0.0/statistics?segment=annual", "annual")]
    [InlineData("app://0.0.0.0/statistics/?x=1&segment=trend&y=2", "trend")]
    [InlineData("app://0.0.0.0/statistics", null)]
    [InlineData("app://0.0.0.0/bills?segment=trend", null)]
    [InlineData("/statistics?segment=trend#top", "trend")]
    [InlineData("/statistics?segment=%74rend", "trend")]
    [InlineData("/statistics?segment=unknown", null)]
    [InlineData("/statistics?segment=", null)]
    [InlineData("/statistics?segment", null)]
    [InlineData("/statistics?other=trend", null)]
    [InlineData("/statistics", null)]
    [InlineData("/statistics?", null)]
    [InlineData("/bills?segment=trend", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void 从地址取分段_只认统计页自己的参数_缺省或不认识时返回空让页面保持当前分段(string? uri, string? expected)
    {
        Assert.Equal(expected, StatisticsPresentation.SegmentFromUri(uri));
    }

    // ───────────────────────── 趋势：时间范围 ─────────────────────────

    [Fact]
    public void 四个时间范围按菜单顺序给出_默认最近7天()
    {
        Assert.Equal(
            new[] { ("7days", "最近7天"), ("30days", "最近30天"), ("90days", "最近90天"), ("12months", "最近12个月") },
            StatisticsPresentation.TrendRanges.ToArray());
        Assert.Equal("7days", StatisticsPresentation.DefaultTrendRange);
    }

    [Theory]
    [InlineData("7days", "7days")]
    [InlineData("30days", "30days")]
    [InlineData("90days", "90days")]
    [InlineData("12months", "12months")]
    [InlineData("", "7days")]
    [InlineData(null, "7days")]
    [InlineData("1year", "7days")]
    [InlineData("30DAYS", "7days")]
    public void 规整时间范围_不认识的取值回到最近7天(string? value, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.NormalizeTrendRange(value));
    }

    [Theory]
    [InlineData("7days", "最近7天")]
    [InlineData("30days", "最近30天")]
    [InlineData("90days", "最近90天")]
    [InlineData("12months", "最近12个月")]
    [InlineData("nonsense", "最近7天")]
    [InlineData(null, "最近7天")]
    public void 时间范围胶囊的文字(string? range, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.TrendRangeText(range));
    }

    [Fact]
    public void 时间范围胶囊的读屏名称带上当前选择()
    {
        Assert.Equal("时间范围：最近30天，点按切换", StatisticsPresentation.TrendRangeAriaLabel("最近30天"));
    }

    [Fact]
    public void 时间范围的起止_终点是现在_起点往前推7_30_90天或12个月()
    {
        var now = new DateTime(2026, 11, 25, 15, 30, 0);

        Assert.Equal((now.AddDays(-7), now), StatisticsPresentation.TrendRangeBounds("7days", now));
        Assert.Equal((now.AddDays(-30), now), StatisticsPresentation.TrendRangeBounds("30days", now));
        Assert.Equal((now.AddDays(-90), now), StatisticsPresentation.TrendRangeBounds("90days", now));
        Assert.Equal((new DateTime(2025, 11, 25, 15, 30, 0), now), StatisticsPresentation.TrendRangeBounds("12months", now));
        Assert.Equal((now.AddDays(-7), now), StatisticsPresentation.TrendRangeBounds("nonsense", now));
        Assert.Equal((now.AddDays(-7), now), StatisticsPresentation.TrendRangeBounds(null, now));
    }

    [Fact]
    public void 十二个月的起点遇到闰日按日历回退到去年2月28日()
    {
        var leapDay = new DateTime(2028, 2, 29);

        Assert.Equal(new DateTime(2027, 2, 28), StatisticsPresentation.TrendRangeBounds("12months", leapDay).Start);
    }

    // ───────────────────────── 趋势：时间维度 ─────────────────────────

    [Fact]
    public void 三个时间维度按日_周_月的顺序给出_默认按日()
    {
        Assert.Equal(
            new[] { ("day", "按日"), ("week", "按周"), ("month", "按月") },
            StatisticsPresentation.TrendDimensions.ToArray());
        Assert.Equal("day", StatisticsPresentation.DefaultTrendDimension);
    }

    [Theory]
    [InlineData("day", "day")]
    [InlineData("week", "week")]
    [InlineData("month", "month")]
    [InlineData("", "day")]
    [InlineData(null, "day")]
    [InlineData("year", "day")]
    [InlineData("WEEK", "day")]
    public void 规整时间维度_不认识的取值回到按日(string? value, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.NormalizeTrendDimension(value));
    }

    [Theory]
    [InlineData("day", TrendDimension.Day)]
    [InlineData("week", TrendDimension.Week)]
    [InlineData("month", TrendDimension.Month)]
    [InlineData("Week", TrendDimension.Week)]
    [InlineData(null, TrendDimension.Day)]
    [InlineData("1", TrendDimension.Day)]
    [InlineData("hour", TrendDimension.Day)]
    public void 时间维度转统计服务的枚举_不认识的按日(string? value, TrendDimension expected)
    {
        Assert.Equal(expected, StatisticsPresentation.ParseTrendDimension(value));
    }

    [Fact]
    public void 趋势里的日期文字_明细表写完整日期_按月只写年月_横轴写月日或年月()
    {
        var date = new DateTime(2026, 3, 9);

        Assert.Equal("2026-03-09", StatisticsPresentation.TrendDateLabel(date, TrendDimension.Day));
        Assert.Equal("2026-03-09", StatisticsPresentation.TrendDateLabel(date, TrendDimension.Week));
        Assert.Equal("2026-03", StatisticsPresentation.TrendDateLabel(date, TrendDimension.Month));
        Assert.Equal("03-09", StatisticsPresentation.TrendAxisLabel(date, TrendDimension.Day));
        Assert.Equal("03-09", StatisticsPresentation.TrendAxisLabel(date, TrendDimension.Week));
        Assert.Equal("26-03", StatisticsPresentation.TrendAxisLabel(date, TrendDimension.Month));
    }

    // ───────────────────────── 趋势：折线图横轴补零 ─────────────────────────

    private static TrendData Bucket(DateTime date, decimal expense, decimal income) =>
        new() { Date = date, Expense = expense, Income = income };

    [Fact]
    public void 横轴补零_按日_起点那天到终点那天每天一个点_没有账单的天是0()
    {
        // 窗口 11-19 15:30 ~ 11-26 15:30（最近 7 天）：11-19 与 11-26 两头也各占一个点，共 8 个点
        var start = new DateTime(2026, 11, 19, 15, 30, 0);
        var end = new DateTime(2026, 11, 26, 15, 30, 0);
        var data = new[]
        {
            Bucket(new DateTime(2026, 11, 21), 30m, 0m),
            Bucket(new DateTime(2026, 11, 24), 12.5m, 100m),
        };

        var filled = StatisticsPresentation.FillTrendBuckets(data, start, end, TrendDimension.Day);

        Assert.Equal(Enumerable.Range(19, 8).Select(d => new DateTime(2026, 11, d)), filled.Select(t => t.Date));
        Assert.Equal(new[] { 0m, 0m, 30m, 0m, 0m, 12.5m, 0m, 0m }, filled.Select(t => t.Expense));
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 0m, 100m, 0m, 0m }, filled.Select(t => t.Income));
        Assert.Equal(new[] { 0m, 0m, -30m, 0m, 0m, 87.5m, 0m, 0m }, filled.Select(t => t.NetIncome));
    }

    [Fact]
    public void 横轴补零_按周_从起点所在周的周一走到终点所在周的周一()
    {
        // 2026-11-26 是周四（所在周周一 11-23）；起点 2026-10-28 周三（所在周周一 10-26）
        var start = new DateTime(2026, 10, 28, 9, 0, 0);
        var end = new DateTime(2026, 11, 26, 9, 0, 0);
        var data = new[] { Bucket(new DateTime(2026, 11, 2), 50m, 0m), Bucket(new DateTime(2026, 11, 23), 8m, 20m) };

        var filled = StatisticsPresentation.FillTrendBuckets(data, start, end, TrendDimension.Week);

        Assert.Equal(
            new[] { "2026-10-26", "2026-11-02", "2026-11-09", "2026-11-16", "2026-11-23" },
            filled.Select(t => t.Date.ToString("yyyy-MM-dd")));
        Assert.All(filled, t => Assert.Equal(DayOfWeek.Monday, t.Date.DayOfWeek));
        Assert.Equal(new[] { 0m, 50m, 0m, 0m, 8m }, filled.Select(t => t.Expense));
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 20m }, filled.Select(t => t.Income));
    }

    [Fact]
    public void 横轴补零_按月_从起点所在月到终点所在月每月一个点_跨年也连续()
    {
        // 最近 12 个月：2025-11-26 ~ 2026-11-26 → 2025-11 … 2026-11 共 13 个月
        var start = new DateTime(2025, 11, 26, 8, 0, 0);
        var end = new DateTime(2026, 11, 26, 8, 0, 0);
        var data = new[] { Bucket(new DateTime(2025, 12, 1), 5m, 0m), Bucket(new DateTime(2026, 11, 1), 9m, 1m) };

        var filled = StatisticsPresentation.FillTrendBuckets(data, start, end, TrendDimension.Month);

        Assert.Equal(13, filled.Count);
        Assert.Equal(new DateTime(2025, 11, 1), filled[0].Date);
        Assert.Equal(new DateTime(2025, 12, 1), filled[1].Date);
        Assert.Equal(new DateTime(2026, 1, 1), filled[2].Date);
        Assert.Equal(new DateTime(2026, 11, 1), filled[^1].Date);
        Assert.Equal(5m, filled[1].Expense);
        Assert.Equal(9m, filled[^1].Expense);
        Assert.Equal(1m, filled[^1].Income);
        Assert.Equal(11, filled.Count(t => t.Expense == 0m && t.Income == 0m));
    }

    [Theory]
    [InlineData(TrendDimension.Day)]
    [InlineData(TrendDimension.Week)]
    [InlineData(TrendDimension.Month)]
    public void 横轴补零_合计不变_日期升序且不重复_原数据一个不丢(TrendDimension dimension)
    {
        var start = new DateTime(2026, 2, 27, 13, 0, 0);
        var end = new DateTime(2026, 5, 28, 13, 0, 0);
        var data = new[]
        {
            Bucket(new DateTime(2026, 3, 2), 10m, 1m),
            Bucket(new DateTime(2026, 3, 9), 20m, 2m),
            Bucket(new DateTime(2026, 4, 1), 30m, 3m),
            Bucket(new DateTime(2026, 5, 4), 40m, 4m),
        };

        var filled = StatisticsPresentation.FillTrendBuckets(data, start, end, dimension);

        Assert.Equal(data.Sum(t => t.Expense), filled.Sum(t => t.Expense));
        Assert.Equal(data.Sum(t => t.Income), filled.Sum(t => t.Income));
        Assert.Equal(filled.Select(t => t.Date).OrderBy(d => d), filled.Select(t => t.Date));
        Assert.Equal(filled.Count, filled.Select(t => t.Date).Distinct().Count());
        Assert.All(data, row => Assert.Contains(filled, t => t.Date == row.Date && t.Expense == row.Expense && t.Income == row.Income));
    }

    [Theory]
    [InlineData(TrendDimension.Day, 8)]
    [InlineData(TrendDimension.Week, 2)]
    [InlineData(TrendDimension.Month, 1)]
    public void 横轴补零_没有任何账单时也按窗口给出全0的横轴_由页面决定是否显示空状态(TrendDimension dimension, int expectedCount)
    {
        var filled = StatisticsPresentation.FillTrendBuckets(
            [], new DateTime(2026, 11, 19, 15, 30, 0), new DateTime(2026, 11, 26, 15, 30, 0), dimension);

        Assert.Equal(expectedCount, filled.Count);
        Assert.All(filled, t => Assert.Equal(0m, t.Expense + t.Income));
    }

    [Fact]
    public void 横轴补零_窗口之外的桶原样保留_不会丢账单也不会重复()
    {
        // 服务返回的桶理论上都在窗口内；万一有范围外的桶（例如调用方传了更窄的窗口），合计也不能少
        var start = new DateTime(2026, 11, 24);
        var end = new DateTime(2026, 11, 26);
        var data = new[] { Bucket(new DateTime(2026, 11, 20), 7m, 0m), Bucket(new DateTime(2026, 11, 25), 3m, 0m) };

        var filled = StatisticsPresentation.FillTrendBuckets(data, start, end, TrendDimension.Day);

        Assert.Equal(new[] { 20, 24, 25, 26 }, filled.Select(t => t.Date.Day));
        Assert.Equal(10m, filled.Sum(t => t.Expense));
    }

    [Fact]
    public void 横轴补零_同一个桶出现多行时合并求和_不改动传入的数据()
    {
        var day = new DateTime(2026, 11, 25);
        var first = Bucket(day, 3m, 1m);
        var second = Bucket(day, 4m, 2m);

        var filled = StatisticsPresentation.FillTrendBuckets([first, second], day, day, TrendDimension.Day);

        var only = Assert.Single(filled);
        Assert.Equal((7m, 3m), (only.Expense, only.Income));
        Assert.Equal((3m, 1m), (first.Expense, first.Income));
        Assert.Equal((4m, 2m), (second.Expense, second.Income));
    }

    [Fact]
    public void 横轴补零_终点早于起点且没有数据时是空列表()
    {
        var filled = StatisticsPresentation.FillTrendBuckets(
            [], new DateTime(2026, 11, 26), new DateTime(2026, 11, 20), TrendDimension.Day);

        Assert.Empty(filled);
    }

    // ───────────────────────── 趋势：明细表排序 ─────────────────────────

    private static TrendData Row(int day, decimal expense, decimal income) =>
        new() { Date = new DateTime(2026, 11, day), Expense = expense, Income = income };

    [Fact]
    public void 明细表四列按日期_支出_收入_净收支的顺序给出()
    {
        Assert.Equal(
            new[] { ("date", "日期"), ("expense", "支出"), ("income", "收入"), ("net", "净收支") },
            StatisticsPresentation.TrendColumns.ToArray());
    }

    [Fact]
    public void 点表头的排序循环_升序_降序_取消排序()
    {
        (string? Column, bool Descending) state = (null, false);

        state = StatisticsPresentation.NextTrendSort(state, "expense");
        Assert.Equal(("expense", false), state);

        state = StatisticsPresentation.NextTrendSort(state, "expense");
        Assert.Equal(("expense", true), state);

        state = StatisticsPresentation.NextTrendSort(state, "expense");
        Assert.Equal((null, false), state);
    }

    [Fact]
    public void 点另一列的表头_从头开始升序_不管上一列排到了哪一步()
    {
        Assert.Equal(("income", false), StatisticsPresentation.NextTrendSort(("expense", true), "income"));
        Assert.Equal(("income", false), StatisticsPresentation.NextTrendSort(("expense", false), "income"));
        Assert.Equal(("date", false), StatisticsPresentation.NextTrendSort((null, false), "date"));
    }

    [Fact]
    public void 明细排序_按列升降序_同值保持原顺序()
    {
        // 日期 1~4：支出 30 / 10 / 30 / 20，收入 0 / 50 / 5 / 0，净收支 -30 / 40 / -25 / -20
        var rows = new[] { Row(1, 30m, 0m), Row(2, 10m, 50m), Row(3, 30m, 5m), Row(4, 20m, 0m) };

        Assert.Equal(new[] { 2, 4, 1, 3 }, Days(StatisticsPresentation.SortTrend(rows, "expense", descending: false)));
        Assert.Equal(new[] { 1, 3, 4, 2 }, Days(StatisticsPresentation.SortTrend(rows, "expense", descending: true)));
        Assert.Equal(new[] { 1, 4, 3, 2 }, Days(StatisticsPresentation.SortTrend(rows, "income", descending: false)));
        Assert.Equal(new[] { 2, 3, 1, 4 }, Days(StatisticsPresentation.SortTrend(rows, "income", descending: true)));
        Assert.Equal(new[] { 1, 3, 4, 2 }, Days(StatisticsPresentation.SortTrend(rows, "net", descending: false)));
        Assert.Equal(new[] { 2, 4, 3, 1 }, Days(StatisticsPresentation.SortTrend(rows, "net", descending: true)));
        Assert.Equal(new[] { 1, 2, 3, 4 }, Days(StatisticsPresentation.SortTrend(rows, "date", descending: false)));
        Assert.Equal(new[] { 4, 3, 2, 1 }, Days(StatisticsPresentation.SortTrend(rows, "date", descending: true)));
    }

    [Fact]
    public void 明细排序_没有排序列或列名不认识时保持数据原顺序_也不改动原列表()
    {
        var rows = new[] { Row(3, 1m, 0m), Row(1, 2m, 0m), Row(2, 3m, 0m) };

        Assert.Same(rows, StatisticsPresentation.SortTrend(rows, null, descending: false));
        Assert.Same(rows, StatisticsPresentation.SortTrend(rows, "unknown", descending: true));

        _ = StatisticsPresentation.SortTrend(rows, "date", descending: false);
        Assert.Equal(new[] { 3, 1, 2 }, rows.Select(r => r.Date.Day));
    }

    private static int[] Days(IEnumerable<TrendData> rows) => rows.Select(r => r.Date.Day).ToArray();

    [Fact]
    public void 表头的aria_sort取值()
    {
        Assert.Equal("none", StatisticsPresentation.AriaSort((null, false), "date"));
        Assert.Equal("ascending", StatisticsPresentation.AriaSort(("date", false), "date"));
        Assert.Equal("descending", StatisticsPresentation.AriaSort(("date", true), "date"));
        Assert.Equal("none", StatisticsPresentation.AriaSort(("date", true), "expense"));
    }

    [Fact]
    public void 显示更多按钮的文字带上剩余条数()
    {
        Assert.Equal("显示更多（还有 25 条）", StatisticsPresentation.ShowMoreText(25));
        Assert.Equal("显示更多（还有 1 条）", StatisticsPresentation.ShowMoreText(1));
    }

    // ───────────────────────── 年度：年份 / 同比 ─────────────────────────

    [Fact]
    public void 年份胶囊的上一年下一年读屏名称带上目标年份_边界停住()
    {
        Assert.Equal("上一年，2025年", StatisticsPresentation.PreviousYearLabel(2026));
        Assert.Equal("下一年，2027年", StatisticsPresentation.NextYearLabel(2026));
        Assert.Equal("上一年，2000年", StatisticsPresentation.PreviousYearLabel(2000));
        Assert.Equal("下一年，2100年", StatisticsPresentation.NextYearLabel(2100));
    }

    [Fact]
    public void 年份到边界后不能再往前或往后()
    {
        Assert.False(StatisticsPresentation.CanShiftYear(2000, -1));
        Assert.True(StatisticsPresentation.CanShiftYear(2000, 1));
        Assert.False(StatisticsPresentation.CanShiftYear(2100, 1));
        Assert.True(StatisticsPresentation.CanShiftYear(2100, -1));
        Assert.True(StatisticsPresentation.CanShiftYear(2026, -1));
        Assert.True(StatisticsPresentation.CanShiftYear(2026, 1));
    }

    [Fact]
    public void 年度构成拆成支出构成与收入构成_标题沿用月度_圆环小字与空文案带上全年当年()
    {
        Assert.Equal("支出构成", StatisticsPresentation.CompositionTitle(BillType.Expense));
        Assert.Equal("收入构成", StatisticsPresentation.CompositionTitle(BillType.Income));
        Assert.Equal("全年支出", StatisticsPresentation.AnnualTotalLabel(BillType.Expense));
        Assert.Equal("全年收入", StatisticsPresentation.AnnualTotalLabel(BillType.Income));
        Assert.Equal("当年暂无支出账单", StatisticsPresentation.AnnualCompositionEmptyText(BillType.Expense));
        Assert.Equal("当年暂无收入账单", StatisticsPresentation.AnnualCompositionEmptyText(BillType.Income));
        Assert.Equal("全年支出分类明细", StatisticsPresentation.AnnualLegendAriaLabel(BillType.Expense));
        Assert.Equal("全年收入分类明细", StatisticsPresentation.AnnualLegendAriaLabel(BillType.Income));
    }

    [Fact]
    public void 年度构成的图例各自占本类合计_支出与收入互不混淆()
    {
        // 统计服务分别给出的两份分类统计（占比已经是各自占本类合计）：图例按金额降序、金额 ≤ 0 的不出现
        var expense = new[] { Category("餐饮", 300m, 75m, 5), Category("交通", 100m, 25m, 2), Category("零额", 0m, 0m, 0) };
        var income = new[] { Category("工资", 5000m, 100m, 1) };

        var expenseRows = StatisticsPresentation.LegendOfCategories(expense);
        var incomeRows = StatisticsPresentation.LegendOfCategories(income);

        Assert.Equal(new[] { "餐饮", "交通" }, expenseRows.Select(r => r.Name));
        Assert.Equal(new[] { "75.0%", "25.0%" }, expenseRows.Select(r => r.PercentText));
        Assert.Equal(new[] { "工资" }, incomeRows.Select(r => r.Name));
        Assert.Equal("100.0%", incomeRows[0].PercentText);
    }

    private static YearOverYearData Yoy(int year, DateTime end) => new()
    {
        CurrentYear = year,
        CurrentPeriodStart = new DateTime(year, 1, 1),
        CurrentPeriodEnd = end,
        PreviousPeriodStart = new DateTime(year - 1, 1, 1),
        PreviousPeriodEnd = new DateTime(year - 1, end.Month, Math.Min(end.Day, DateTime.DaysInMonth(year - 1, end.Month))),
    };

    [Fact]
    public void 同比口径文字_当年是年初至今_已结束的年份是两个年份对比()
    {
        var partial = Yoy(2026, new DateTime(2026, 11, 25));
        Assert.True(StatisticsPresentation.IsPartialYear(partial));
        Assert.Equal("年初至今 vs 去年同期", StatisticsPresentation.YoyScopeText(partial));
        Assert.Equal("2026-01-01 ~ 2026-11-25 对比 2025-01-01 ~ 2025-11-25", StatisticsPresentation.YoyPeriodText(partial));

        var full = Yoy(2025, new DateTime(2025, 12, 31));
        Assert.False(StatisticsPresentation.IsPartialYear(full));
        Assert.Equal("2025年 vs 2024年", StatisticsPresentation.YoyScopeText(full));
        Assert.Equal("2025-01-01 ~ 2025-12-31 对比 2024-01-01 ~ 2024-12-31", StatisticsPresentation.YoyPeriodText(full));
    }

    [Fact]
    public void 当年12月30日仍是年初至今_12月31日才算整年()
    {
        Assert.True(StatisticsPresentation.IsPartialYear(Yoy(2026, new DateTime(2026, 12, 30))));
        Assert.False(StatisticsPresentation.IsPartialYear(Yoy(2026, new DateTime(2026, 12, 31))));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0.0, "0.0%")]
    [InlineData(12.34, "+12.3%")]
    [InlineData(12.35, "+12.4%")]
    [InlineData(-12.35, "-12.4%")]
    [InlineData(-50.0, "-50.0%")]
    [InlineData(100.0, "+100.0%")]
    [InlineData(0.04, "0.0%")]
    [InlineData(-0.04, "0.0%")]
    [InlineData(0.05, "+0.1%")]
    [InlineData(-0.05, "-0.1%")]
    [InlineData(1234.56, "+1234.6%")]
    public void 同比变化率文字_一位小数_正数带加号_没有基数显示破折号(double? rate, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.FormatChangeRate(rate is null ? null : (decimal)rate.Value));
    }

    [Theory]
    [InlineData("—", "")]
    [InlineData("0.0%", "")]
    [InlineData("+425.0%", "")]
    [InlineData("-100.0%", "")]
    [InlineData("+1000.0%", "")]
    [InlineData("+10000.0%", "long")]
    [InlineData("-99999.9%", "long")]
    [InlineData("+9999999.9%", "long")]
    [InlineData("+99999999.9%", "xlong")]
    [InlineData("+9999999900.0%", "xlong")]
    public void 变化率文字太长时逐级缩小字号_九个字符起一档_十二个字符起二档(string rateText, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.RateSize(rateText));
    }

    [Theory]
    [InlineData(null, true, "flat")]
    [InlineData(null, false, "flat")]
    [InlineData(0.0, true, "flat")]
    [InlineData(0.04, true, "flat")]
    [InlineData(-0.04, false, "flat")]
    [InlineData(12.5, true, "good")]
    [InlineData(-12.5, true, "bad")]
    [InlineData(12.5, false, "bad")]
    [InlineData(-12.5, false, "good")]
    [InlineData(0.05, true, "good")]
    [InlineData(-0.05, false, "good")]
    public void 同比变化率的语义色_收入涨与支出降是好事_按显示的一位小数判断(double? rate, bool higherIsBetter, string expected)
    {
        Assert.Equal(expected, StatisticsPresentation.ChangeTone(rate is null ? null : (decimal)rate.Value, higherIsBetter));
    }
}
