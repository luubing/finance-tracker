using System.Security.Claims;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 统计控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StatisticsController : ControllerBase
{
    private readonly IStatisticsService _statisticsService;

    public StatisticsController(IStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("未授权");
        }
        return userId;
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
}
