using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 账本控制器
/// </summary>
public class LedgersController : BaseApiController
{
    private readonly ILedgerService _ledgerService;

    public LedgersController(ILedgerService ledgerService)
    {
        _ledgerService = ledgerService;
    }

    /// <summary>
    /// 获取账本列表
    /// </summary>
    /// <returns>账本列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetLedgers()
    {
        var userId = GetUserId();
        var ledgers = await _ledgerService.GetLedgersAsync(userId);

        return Ok(ledgers.Select(MapToResponse));
    }

    /// <summary>
    /// 获取账本详情
    /// </summary>
    /// <param name="id">账本ID</param>
    /// <returns>账本信息</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetLedger(Guid id)
    {
        var userId = GetUserId();
        var ledger = await _ledgerService.GetLedgerByIdAsync(id);

        if (ledger == null || ledger.UserId != userId)
        {
            return NotFound(new { message = "账本不存在" });
        }

        return Ok(MapToResponse(ledger));
    }

    /// <summary>
    /// 创建账本
    /// </summary>
    /// <param name="request">账本请求</param>
    /// <returns>创建的账本</returns>
    [HttpPost]
    public async Task<IActionResult> CreateLedger([FromBody] LedgerRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "账本名称不能为空" });
        }

        var ledger = new Core.Entities.Ledger
        {
            UserId = userId,
            Name = request.Name,
            Icon = request.Icon ?? "mdi-book",
            SortOrder = request.SortOrder
        };

        var createdLedger = await _ledgerService.CreateLedgerAsync(ledger);

        return CreatedAtAction(nameof(GetLedger), new { id = createdLedger.Id }, MapToResponse(createdLedger));
    }

    /// <summary>
    /// 更新账本
    /// </summary>
    /// <param name="id">账本ID</param>
    /// <param name="request">账本请求</param>
    /// <returns>更新的账本</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateLedger(Guid id, [FromBody] LedgerRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "账本名称不能为空" });
        }

        // 检查账本是否存在且属于当前用户
        var existingLedger = await _ledgerService.GetLedgerByIdAsync(id);
        if (existingLedger == null || existingLedger.UserId != userId)
        {
            return NotFound(new { message = "账本不存在" });
        }

        var ledger = new Core.Entities.Ledger
        {
            Id = id,
            UserId = userId,
            Name = request.Name,
            Icon = request.Icon ?? "mdi-book",
            SortOrder = request.SortOrder
        };

        try
        {
            var updatedLedger = await _ledgerService.UpdateLedgerAsync(ledger);
            return Ok(MapToResponse(updatedLedger));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除账本
    /// </summary>
    /// <param name="id">账本ID</param>
    /// <returns>删除结果</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteLedger(Guid id)
    {
        var userId = GetUserId();

        try
        {
            var result = await _ledgerService.DeleteLedgerAsync(id, userId);

            if (!result)
            {
                return NotFound(new { message = "账本不存在" });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static object MapToResponse(Core.Entities.Ledger ledger) => new
    {
        id = ledger.Id,
        name = ledger.Name,
        icon = ledger.Icon,
        sortOrder = ledger.SortOrder
    };
}

/// <summary>
/// 账本请求
/// </summary>
public class LedgerRequest
{
    /// <summary>
    /// 账本名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int SortOrder { get; set; }
}
