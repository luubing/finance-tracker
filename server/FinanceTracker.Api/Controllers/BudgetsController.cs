using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 预算控制器
/// </summary>
public class BudgetsController : BaseApiController
{
    private readonly IBudgetService _budgetService;

    public BudgetsController(IBudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    /// <summary>
    /// 获取预算列表
    /// </summary>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="ledgerId">账本ID（可选，null 表示不按账本筛选）</param>
    /// <returns>预算列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetBudgets([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? ledgerId)
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

        var budgets = await _budgetService.GetBudgetsAsync(userId, year, month, ledgerId);

        return Ok(budgets.Select(MapToResponse));
    }

    /// <summary>
    /// 获取预算执行情况（预算额、已用额、剩余额、使用百分比）
    /// </summary>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="ledgerId">账本ID（可选，null 表示全部账本）</param>
    /// <returns>预算执行情况列表</returns>
    [HttpGet("status")]
    public async Task<IActionResult> GetBudgetStatus([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? ledgerId)
    {
        var userId = GetUserId();
        var statuses = await _budgetService.GetBudgetStatusAsync(userId, year, month, ledgerId);

        return Ok(statuses);
    }

    /// <summary>
    /// 获取预算详情
    /// </summary>
    /// <param name="id">预算ID</param>
    /// <returns>预算信息</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBudget(Guid id)
    {
        var userId = GetUserId();
        var budget = await _budgetService.GetBudgetByIdAsync(id);

        if (budget == null || budget.UserId != userId)
        {
            return NotFound(new { message = "预算不存在" });
        }

        return Ok(MapToResponse(budget));
    }

    /// <summary>
    /// 创建预算
    /// </summary>
    /// <param name="request">预算请求</param>
    /// <returns>创建的预算</returns>
    [HttpPost]
    public async Task<IActionResult> CreateBudget([FromBody] BudgetRequest request)
    {
        var userId = GetUserId();

        var budget = new Budget
        {
            UserId = userId,
            LedgerId = request.LedgerId,
            Year = request.Year,
            Month = request.Month,
            Amount = request.Amount,
            CategoryId = request.CategoryId
        };

        try
        {
            var createdBudget = await _budgetService.CreateBudgetAsync(budget);

            return CreatedAtAction(nameof(GetBudget), new { id = createdBudget.Id }, MapToResponse(createdBudget));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新预算
    /// </summary>
    /// <param name="id">预算ID</param>
    /// <param name="request">预算请求</param>
    /// <returns>更新的预算</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBudget(Guid id, [FromBody] BudgetRequest request)
    {
        var userId = GetUserId();

        var existingBudget = await _budgetService.GetBudgetByIdAsync(id);
        if (existingBudget == null || existingBudget.UserId != userId)
        {
            return NotFound(new { message = "预算不存在" });
        }

        var budget = new Budget
        {
            Id = id,
            UserId = userId,
            LedgerId = request.LedgerId,
            Year = request.Year,
            Month = request.Month,
            Amount = request.Amount,
            CategoryId = request.CategoryId
        };

        try
        {
            var updatedBudget = await _budgetService.UpdateBudgetAsync(budget);
            return Ok(MapToResponse(updatedBudget));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除预算
    /// </summary>
    /// <param name="id">预算ID</param>
    /// <returns>删除结果</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBudget(Guid id)
    {
        var userId = GetUserId();
        var result = await _budgetService.DeleteBudgetAsync(id, userId);

        if (!result)
        {
            return NotFound(new { message = "预算不存在" });
        }

        return NoContent();
    }

    private static object MapToResponse(Budget budget) => new
    {
        id = budget.Id,
        ledgerId = budget.LedgerId,
        year = budget.Year,
        month = budget.Month,
        amount = budget.Amount,
        categoryId = budget.CategoryId
    };
}

/// <summary>
/// 预算请求
/// </summary>
public class BudgetRequest
{
    /// <summary>
    /// 账本ID（null 表示全部账本）
    /// </summary>
    public Guid? LedgerId { get; set; }

    /// <summary>
    /// 预算年份
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// 预算月份 (1-12)
    /// </summary>
    public int Month { get; set; }

    /// <summary>
    /// 预算金额（正数）
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 分类ID（null 表示总预算）
    /// </summary>
    public Guid? CategoryId { get; set; }
}