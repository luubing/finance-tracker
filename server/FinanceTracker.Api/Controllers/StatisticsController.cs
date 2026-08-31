using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 统计控制器
/// </summary>
public class StatisticsController : BaseApiController
{
    private readonly IStatisticsService _statisticsService;

    public StatisticsController(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    /// <summary>
    /// 获取月度统计数据
    /// </summary>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <returns>月度统计数据</returns>
    [HttpGet("monthly")]
    public async Task<IActionResult> GetMonthlyStatistics(
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var userId = GetUserId();

        if (year < 2000 || year > 2100)
        {
            return BadRequest(new { message = "年份不正确" });
        }

        if (month < 1 || month > 12)
        {
            return BadRequest(new { message = "月份不正确" });
        }

        var statistics = await _statisticsService.GetMonthlyStatisticsAsync(userId, year, month);

        return Ok(new
        {
            year = statistics.Year,
            month = statistics.Month,
            totalExpense = statistics.TotalExpense,
            totalIncome = statistics.TotalIncome,
            netIncome = statistics.NetIncome,
            billCount = statistics.BillCount
        });
    }

    /// <summary>
    /// 获取分类统计数据
    /// </summary>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="type">账单类型</param>
    /// <returns>分类统计数据</returns>
    [HttpGet("category")]
    public async Task<IActionResult> GetCategoryStatistics(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] BillType type)
    {
        var userId = GetUserId();

        if (year < 2000 || year > 2100)
        {
            return BadRequest(new { message = "年份不正确" });
        }

        if (month < 1 || month > 12)
        {
            return BadRequest(new { message = "月份不正确" });
        }

        var statistics = await _statisticsService.GetCategoryStatisticsAsync(userId, year, month, type);

        return Ok(statistics.Select(s => new
        {
            categoryId = s.CategoryId,
            categoryName = s.CategoryName,
            categoryIcon = s.CategoryIcon,
            amount = s.Amount,
            percentage = s.Percentage,
            count = s.Count
        }));
    }

    /// <summary>
    /// 获取趋势数据
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="dimension">维度（day/week/month）</param>
    /// <returns>趋势数据</returns>
    [HttpGet("trend")]
    public async Task<IActionResult> GetTrendData(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] string dimension = "day")
    {
        var userId = GetUserId();

        if (startDate >= endDate)
        {
            return BadRequest(new { message = "开始日期必须早于结束日期" });
        }

        if (!new[] { "day", "week", "month" }.Contains(dimension.ToLower()))
        {
            return BadRequest(new { message = "维度必须是 day、week 或 month" });
        }

        var trendData = await _statisticsService.GetTrendDataAsync(userId, startDate, endDate, dimension);

        return Ok(trendData.Select(t => new
        {
            date = t.Date,
            expense = t.Expense,
            income = t.Income,
            netIncome = t.NetIncome
        }));
    }

    /// <summary>
    /// 获取年度统计数据
    /// </summary>
    /// <param name="year">年份</param>
    /// <returns>年度统计数据</returns>
    [HttpGet("annual")]
    public async Task<IActionResult> GetAnnualStatistics([FromQuery] int year)
    {
        var userId = GetUserId();

        if (year < 2000 || year > 2100)
        {
            return BadRequest(new { message = "年份不正确" });
        }

        var statistics = await _statisticsService.GetAnnualStatisticsAsync(userId, year);

        return Ok(new
        {
            year = statistics.Year,
            totalExpense = statistics.TotalExpense,
            totalIncome = statistics.TotalIncome,
            netIncome = statistics.NetIncome,
            billCount = statistics.BillCount,
            monthlyData = statistics.MonthlyData.Select(m => new
            {
                month = m.Month,
                totalExpense = m.TotalExpense,
                totalIncome = m.TotalIncome,
                netIncome = m.NetIncome,
                billCount = m.BillCount
            }),
            categoryStats = statistics.CategoryStats.Select(c => new
            {
                categoryId = c.CategoryId,
                categoryName = c.CategoryName,
                categoryIcon = c.CategoryIcon,
                amount = c.Amount,
                percentage = c.Percentage,
                count = c.Count
            })
        });
    }

    /// <summary>
    /// 获取同比数据
    /// </summary>
    /// <param name="year">当前年份</param>
    /// <param name="month">当前月份</param>
    /// <returns>同比数据</returns>
    [HttpGet("year-over-year")]
    public async Task<IActionResult> GetYearOverYearData([FromQuery] int year, [FromQuery] int month)
    {
        var userId = GetUserId();

        if (year < 2000 || year > 2100)
        {
            return BadRequest(new { message = "年份不正确" });
        }

        if (month < 1 || month > 12)
        {
            return BadRequest(new { message = "月份不正确" });
        }

        var data = await _statisticsService.GetYearOverYearDataAsync(userId, year, month);

        return Ok(new
        {
            currentYear = data.CurrentYear,
            currentMonth = data.CurrentMonth,
            currentExpense = data.CurrentExpense,
            currentIncome = data.CurrentIncome,
            previousYearExpense = data.PreviousYearExpense,
            previousYearIncome = data.PreviousYearIncome,
            expenseChangeRate = data.ExpenseChangeRate,
            incomeChangeRate = data.IncomeChangeRate
        });
    }
}
