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
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>月度统计数据</returns>
    [HttpGet("monthly")]
    public async Task<IActionResult> GetMonthlyStatistics(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] Guid? ledgerId)
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

        var statistics = await _statisticsService.GetMonthlyStatisticsAsync(userId, year, month, ledgerId);

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
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>分类统计数据</returns>
    [HttpGet("category")]
    public async Task<IActionResult> GetCategoryStatistics(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] BillType type,
        [FromQuery] Guid? ledgerId)
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

        var statistics = await _statisticsService.GetCategoryStatisticsAsync(userId, year, month, type, ledgerId);

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
    /// 获取账本统计数据（某月按账本汇总，用于"全部账本"视图）
    /// </summary>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="type">账单类型</param>
    /// <returns>账本统计数据；ledgerId 为 null 的一项表示未归属账本的历史账单</returns>
    [HttpGet("ledger")]
    public async Task<IActionResult> GetLedgerStatistics(
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

        var statistics = await _statisticsService.GetLedgerStatisticsAsync(userId, year, month, type);

        return Ok(statistics.Select(s => new
        {
            ledgerId = s.LedgerId,
            ledgerName = s.LedgerName,
            ledgerIcon = s.LedgerIcon,
            amount = s.Amount,
            percentage = s.Percentage,
            count = s.Count
        }));
    }

    /// <summary>
    /// 获取趋势数据
    /// </summary>
    /// <param name="startDate">开始时刻（应用时区，含）</param>
    /// <param name="endDate">结束时刻（应用时区，<b>含端点</b>）；要覆盖整天/整月请传最后一个 tick（<c>TimeBoundaries.EndOfDay/EndOfMonth</c>），不要传"减 1 秒"</param>
    /// <param name="dimension">维度（day/week/month）</param>
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>趋势数据</returns>
    [HttpGet("trend")]
    public async Task<IActionResult> GetTrendData(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] string dimension = "day",
        [FromQuery] Guid? ledgerId = null)
    {
        var userId = GetUserId();

        if (startDate >= endDate)
        {
            return BadRequest(new { message = "开始日期必须早于结束日期" });
        }

        if (!TrendDimensionParser.TryParse(dimension, out var trendDimension))
        {
            return BadRequest(new { message = "维度必须是 day、week 或 month" });
        }

        var trendData = await _statisticsService.GetTrendDataAsync(userId, startDate, endDate, trendDimension, ledgerId);

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
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>年度统计数据</returns>
    [HttpGet("annual")]
    public async Task<IActionResult> GetAnnualStatistics([FromQuery] int year, [FromQuery] Guid? ledgerId)
    {
        var userId = GetUserId();

        if (year < 2000 || year > 2100)
        {
            return BadRequest(new { message = "年份不正确" });
        }

        var statistics = await _statisticsService.GetAnnualStatisticsAsync(userId, year, ledgerId);

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
    /// <param name="month">
    /// 当前月份（可选）。传入时比较该月与去年同月；不传时比较整年：
    /// 已结束的年份为整年 vs 上一整年，当年为年初至今 vs 去年同期（截至去年的同月同日）
    /// </param>
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>
    /// 同比数据。去年同期的支出/收入为 0 时没有对比基数，对应的 expenseChangeRate / incomeChangeRate 为 null
    /// （不是 0）；currentPeriod* / previousPeriod* 为对比期间的起止日（含首尾，格式 yyyy-MM-dd）
    /// </returns>
    [HttpGet("year-over-year")]
    public async Task<IActionResult> GetYearOverYearData([FromQuery] int year, [FromQuery] int? month, [FromQuery] Guid? ledgerId)
    {
        var userId = GetUserId();

        if (year < 2000 || year > 2100)
        {
            return BadRequest(new { message = "年份不正确" });
        }

        if (month.HasValue && (month.Value < 1 || month.Value > 12))
        {
            return BadRequest(new { message = "月份不正确" });
        }

        var data = month.HasValue
            ? await _statisticsService.GetYearOverYearDataAsync(userId, year, month.Value, ledgerId)
            : await _statisticsService.GetAnnualYearOverYearDataAsync(userId, year, ledgerId);

        return Ok(new
        {
            currentYear = data.CurrentYear,
            currentMonth = data.CurrentMonth,
            currentPeriodStart = data.CurrentPeriodStart.ToString("yyyy-MM-dd"),
            currentPeriodEnd = data.CurrentPeriodEnd.ToString("yyyy-MM-dd"),
            previousPeriodStart = data.PreviousPeriodStart.ToString("yyyy-MM-dd"),
            previousPeriodEnd = data.PreviousPeriodEnd.ToString("yyyy-MM-dd"),
            currentExpense = data.CurrentExpense,
            currentIncome = data.CurrentIncome,
            previousYearExpense = data.PreviousYearExpense,
            previousYearIncome = data.PreviousYearIncome,
            expenseChangeRate = data.ExpenseChangeRate,
            incomeChangeRate = data.IncomeChangeRate
        });
    }

    /// <summary>
    /// 获取自定义时间范围统计数据
    /// </summary>
    /// <param name="startDate">开始日期（含当天）</param>
    /// <param name="endDate">结束日期（含当天）</param>
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>自定义范围统计数据</returns>
    [HttpGet("custom")]
    public async Task<IActionResult> GetCustomStatistics(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] Guid? ledgerId)
    {
        var userId = GetUserId();

        if (startDate.Date > endDate.Date)
        {
            return BadRequest(new { message = "开始日期必须早于或等于结束日期" });
        }

        if (startDate.Date < new DateTime(2000, 1, 1) || endDate.Date > new DateTime(2100, 1, 1))
        {
            return BadRequest(new { message = "日期范围无效" });
        }

        var statistics = await _statisticsService.GetCustomStatisticsAsync(userId, startDate, endDate, ledgerId);

        return Ok(new
        {
            startDate = statistics.StartDate,
            endDate = statistics.EndDate,
            totalExpense = statistics.TotalExpense,
            totalIncome = statistics.TotalIncome,
            netIncome = statistics.NetIncome,
            billCount = statistics.BillCount,
            expenseCategoryStats = statistics.ExpenseCategoryStats.Select(s => new
            {
                categoryId = s.CategoryId,
                categoryName = s.CategoryName,
                categoryIcon = s.CategoryIcon,
                amount = s.Amount,
                percentage = s.Percentage,
                count = s.Count
            }),
            incomeCategoryStats = statistics.IncomeCategoryStats.Select(s => new
            {
                categoryId = s.CategoryId,
                categoryName = s.CategoryName,
                categoryIcon = s.CategoryIcon,
                amount = s.Amount,
                percentage = s.Percentage,
                count = s.Count
            }),
            dailyTrend = statistics.DailyTrend.Select(t => new
            {
                date = t.Date,
                expense = t.Expense,
                income = t.Income,
                netIncome = t.NetIncome
            })
        });
    }

    /// <summary>
    /// 获取分类环比对比（当前周期 vs 上一等长周期）
    /// </summary>
    /// <param name="startDate">开始日期（含当天）</param>
    /// <param name="endDate">结束日期（含当天）</param>
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>分类环比对比列表</returns>
    [HttpGet("category-comparison")]
    public async Task<IActionResult> GetCategoryComparison(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] Guid? ledgerId)
    {
        var userId = GetUserId();

        if (startDate.Date > endDate.Date)
        {
            return BadRequest(new { message = "开始日期必须早于或等于结束日期" });
        }

        var comparison = await _statisticsService.GetCategoryComparisonAsync(userId, startDate, endDate, ledgerId);

        return Ok(comparison.Select(c => new
        {
            categoryId = c.CategoryId,
            categoryName = c.CategoryName,
            categoryIcon = c.CategoryIcon,
            currentAmount = c.CurrentAmount,
            previousAmount = c.PreviousAmount,
            changeAmount = c.ChangeAmount,
            changeRate = c.ChangeRate
        }));
    }

    /// <summary>
    /// 获取区间内被「不计入统计」的分类排除掉的账单汇总。
    /// 本控制器的其他统计接口都不含这些账单（分类的 excludeFromStatistics 开着时），这个接口告诉调用方被排除了什么。
    /// 没有被排除的账单时返回空数组。
    /// </summary>
    /// <param name="startDate">开始日期（含当天）</param>
    /// <param name="endDate">结束日期（含当天）</param>
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>被排除的分类汇总列表（金额降序）</returns>
    [HttpGet("excluded")]
    public async Task<IActionResult> GetExcludedStatistics(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] Guid? ledgerId)
    {
        var userId = GetUserId();

        if (startDate.Date > endDate.Date)
        {
            return BadRequest(new { message = "开始日期必须早于或等于结束日期" });
        }

        if (startDate.Date < new DateTime(2000, 1, 1) || endDate.Date > new DateTime(2100, 1, 1))
        {
            return BadRequest(new { message = "日期范围无效" });
        }

        // 接口的日期含当天，服务的区间是左闭右开：终点取结束日的次日零点（与自定义统计的归一化一致）
        var excluded = await _statisticsService.GetExcludedStatisticsAsync(
            userId, startDate.Date, endDate.Date.AddDays(1), ledgerId);

        return Ok(excluded.Select(e => new
        {
            categoryId = e.CategoryId,
            categoryName = e.CategoryName,
            categoryIcon = e.CategoryIcon,
            type = e.Type.ToString(),
            amount = e.Amount,
            count = e.Count
        }));
    }
}
