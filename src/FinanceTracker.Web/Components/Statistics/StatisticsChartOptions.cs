using FinanceTracker.Core.Models;
using FinanceTracker.Web.Design;

namespace FinanceTracker.Web.Components.Statistics;

/// <summary>
/// 统计 Tab 里 ECharts 折线图的配置。颜色取自设计令牌（支出红 / 收入绿 / 结余品牌色，与 KPI 卡的小圆点、月度的累计收支图 / 支出日历同一套），
/// 图表标题由卡片上方的节标题负责，图里只留坐标轴与折线。
/// </summary>
public static class StatisticsChartOptions
{
    /// <summary>支出线的颜色（<c>--ft-expense</c>，白底上约 4.2:1）</summary>
    public const string ExpenseColor = DesignTokens.ExpenseColor;

    /// <summary>收入线的颜色（<c>--ft-income</c>，白底上约 3.2:1；iOS 系统绿 <c>--ft-success</c> 只有约 2.2:1，线条这类图形元素要求 ≥ 3:1）</summary>
    public const string IncomeColor = DesignTokens.IncomeColor;

    /// <summary>净收支（结余）线的颜色（<c>--ft-brand</c>）</summary>
    public const string NetColor = DesignTokens.BrandColor;

    /// <summary>坐标轴网格线（<c>--ft-separator</c>；ECharts 读不了 CSS 变量，取 <see cref="DesignTokens"/> 里与令牌同值的常量）</summary>
    private const string GridLineColor = DesignTokens.ChartGridLineColor;

    /// <summary>坐标轴文字（<c>--ft-label-2</c>；23 号把它从 .62 加深到 .76 以达到 AA 对比度，图表文字跟着同一个值）</summary>
    private const string AxisTextColor = DesignTokens.ChartAxisTextColor;

    /// <summary>
    /// 单条折线（趋势视图的支出 / 收入 / 净收支各一张）。
    /// 点不多（≤ 31 个）时显示数据点，方便手指点按看数值；多了只画线。
    /// </summary>
    public static object Line(string name, IReadOnlyList<string> labels, IReadOnlyList<decimal> values, string color) => new
    {
        animation = false,
        tooltip = new { trigger = "axis", confine = true },
        grid = Grid(),
        xAxis = XAxis(labels),
        yAxis = YAxis(),
        series = new[]
        {
            new
            {
                name,
                type = "line",
                data = values.Select(v => (double)v).ToArray(),
                smooth = true,
                showSymbol = values.Count <= 31,
                symbolSize = 6,
                lineStyle = new { width = 2, color },
                itemStyle = new { color },
                areaStyle = new { opacity = 0.12, color },
            },
        },
    };

    /// <summary>年度的月度趋势：十二个月的支出 / 收入 / 净收支三条线。</summary>
    public static object Monthly(IReadOnlyList<MonthlyStatistics> months) => new
    {
        animation = false,
        tooltip = new { trigger = "axis", confine = true },
        legend = new
        {
            data = new[] { "支出", "收入", "净收支" },
            bottom = 0,
            itemWidth = 14,
            itemHeight = 8,
            textStyle = new { fontSize = 11, color = AxisTextColor },
        },
        grid = new { left = 8, right = 22, top = 16, bottom = 34, containLabel = true },
        xAxis = XAxis(months.Select(m => $"{m.Month}月").ToArray()),
        yAxis = YAxis(),
        series = new object[]
        {
            Series("支出", months.Select(m => (double)m.TotalExpense).ToArray(), ExpenseColor),
            Series("收入", months.Select(m => (double)m.TotalIncome).ToArray(), IncomeColor),
            Series("净收支", months.Select(m => (double)m.NetIncome).ToArray(), NetColor),
        },
    };

    private static object Series(string name, double[] data, string color) => new
    {
        name,
        type = "line",
        data,
        smooth = true,
        showSymbol = true,
        symbolSize = 5,
        lineStyle = new { width = 2, color },
        itemStyle = new { color },
    };

    /// <summary>右边留 22：横轴最后一个标签以折线终点为中心，不留够会被卡片边缘截掉半个字</summary>
    private static object Grid() => new { left = 8, right = 22, top = 16, bottom = 8, containLabel = true };

    private static object XAxis(IReadOnlyList<string> labels) => new
    {
        type = "category",
        data = labels,
        boundaryGap = false,
        axisLine = new { lineStyle = new { color = GridLineColor } },
        axisTick = new { show = false },
        axisLabel = new { fontSize = 10, color = AxisTextColor, hideOverlap = true },
    };

    private static object YAxis() => new
    {
        type = "value",
        axisLabel = new { fontSize = 10, color = AxisTextColor, formatter = "¥{value}" },
        splitLine = new { lineStyle = new { color = GridLineColor } },
    };
}
