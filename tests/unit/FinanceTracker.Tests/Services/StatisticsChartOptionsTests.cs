using System.Text.Json;
using FinanceTracker.Core.Models;
using FinanceTracker.Web.Components.Statistics;
using FinanceTracker.Web.Design;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 统计 Tab 里 ECharts 折线图的配置：颜色取自设计令牌、数据与横轴对得上、点多了不画数据点
/// （配置是匿名对象，序列化成 JSON 以后按 ECharts 读它的方式检查）
/// </summary>
public class StatisticsChartOptionsTests
{
    private static JsonElement Json(object option) => JsonSerializer.SerializeToElement(option);

    private static string[] Labels(int count) => Enumerable.Range(1, count).Select(i => $"{i:00}-01").ToArray();

    [Fact]
    public void 三条线的颜色取自设计令牌_支出红_收入绿_结余品牌色()
    {
        Assert.Equal(DesignTokens.ExpenseColor, StatisticsChartOptions.ExpenseColor);
        Assert.Equal(DesignTokens.IncomeColor, StatisticsChartOptions.IncomeColor);
        Assert.Equal(DesignTokens.BrandColor, StatisticsChartOptions.NetColor);
    }

    [Fact]
    public void 单条折线_名称_颜色_数据与横轴一一对应()
    {
        var option = Json(StatisticsChartOptions.Line(
            "支出", ["03-01", "03-02", "03-03"], [10m, 0m, 25.5m], StatisticsChartOptions.ExpenseColor));

        Assert.False(option.GetProperty("animation").GetBoolean());
        Assert.Equal("category", option.GetProperty("xAxis").GetProperty("type").GetString());
        Assert.Equal(
            new[] { "03-01", "03-02", "03-03" },
            option.GetProperty("xAxis").GetProperty("data").EnumerateArray().Select(e => e.GetString()).ToArray());

        var series = option.GetProperty("series");
        Assert.Equal(1, series.GetArrayLength());
        var line = series[0];
        Assert.Equal("支出", line.GetProperty("name").GetString());
        Assert.Equal("line", line.GetProperty("type").GetString());
        Assert.Equal(new[] { 10d, 0d, 25.5d }, line.GetProperty("data").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        Assert.Equal(StatisticsChartOptions.ExpenseColor, line.GetProperty("lineStyle").GetProperty("color").GetString());
        Assert.Equal(StatisticsChartOptions.ExpenseColor, line.GetProperty("itemStyle").GetProperty("color").GetString());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(31, true)]
    [InlineData(32, false)]
    [InlineData(90, false)]
    public void 数据点不多时显示点方便点按看数值_超过31个只画线(int pointCount, bool expectedShowSymbol)
    {
        var values = Enumerable.Repeat(1m, pointCount).ToArray();

        var option = Json(StatisticsChartOptions.Line("收入", Labels(pointCount), values, StatisticsChartOptions.IncomeColor));

        Assert.Equal(expectedShowSymbol, option.GetProperty("series")[0].GetProperty("showSymbol").GetBoolean());
    }

    [Fact]
    public void 纵轴金额带人民币符号_横轴不重叠()
    {
        var option = Json(StatisticsChartOptions.Line("净收支", ["a"], [1m], StatisticsChartOptions.NetColor));

        Assert.Equal("¥{value}", option.GetProperty("yAxis").GetProperty("axisLabel").GetProperty("formatter").GetString());
        Assert.True(option.GetProperty("xAxis").GetProperty("axisLabel").GetProperty("hideOverlap").GetBoolean());
    }

    private static MonthlyStatistics Month(int month, decimal expense, decimal income) =>
        new() { Year = 2026, Month = month, TotalExpense = expense, TotalIncome = income };

    [Fact]
    public void 年度月度趋势_十二个月三条线_净收支是收入减支出()
    {
        var months = Enumerable.Range(1, 12)
            .Select(m => Month(m, expense: m * 10m, income: m * 25m))
            .ToArray();

        var option = Json(StatisticsChartOptions.Monthly(months));

        Assert.Equal(
            Enumerable.Range(1, 12).Select(m => $"{m}月").ToArray(),
            option.GetProperty("xAxis").GetProperty("data").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(
            new[] { "支出", "收入", "净收支" },
            option.GetProperty("legend").GetProperty("data").EnumerateArray().Select(e => e.GetString()).ToArray());

        var series = option.GetProperty("series").EnumerateArray().ToArray();
        Assert.Equal(new[] { "支出", "收入", "净收支" }, series.Select(s => s.GetProperty("name").GetString()).ToArray());
        Assert.Equal(
            new[] { StatisticsChartOptions.ExpenseColor, StatisticsChartOptions.IncomeColor, StatisticsChartOptions.NetColor },
            series.Select(s => s.GetProperty("lineStyle").GetProperty("color").GetString()).ToArray());

        Assert.Equal(
            months.Select(m => (double)m.TotalExpense).ToArray(),
            series[0].GetProperty("data").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        Assert.Equal(
            months.Select(m => (double)m.TotalIncome).ToArray(),
            series[1].GetProperty("data").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        Assert.Equal(
            months.Select(m => (double)(m.TotalIncome - m.TotalExpense)).ToArray(),
            series[2].GetProperty("data").EnumerateArray().Select(e => e.GetDouble()).ToArray());
    }
}
