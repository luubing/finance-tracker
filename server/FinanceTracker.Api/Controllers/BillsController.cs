using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 账单控制器
/// </summary>
public class BillsController : BaseApiController
{
    private readonly IBillService _billService;
    private readonly ILedgerMemberService _ledgerMemberService;

    public BillsController(IBillService billService, ILedgerMemberService ledgerMemberService)
    {
        _billService = billService;
        _ledgerMemberService = ledgerMemberService;
    }

    /// <summary>
    /// 账本写权限校验：账单归属共享账本时，Viewer/非成员不能记账（与 ADR 0004 权限模型一致）。
    /// 账本为空（未归属账本）或属于本人时直接放行（EnsureCanWrite 内部已覆盖自有账本场景）。
    /// </summary>
    private async Task<IActionResult?> ValidateLedgerWritePermissionAsync(Guid userId, Guid? ledgerId)
    {
        if (ledgerId.HasValue && ledgerId.Value != Guid.Empty)
        {
            try
            {
                await _ledgerMemberService.EnsureCanWriteAsync(ledgerId.Value, userId);
            }
            catch (ForbiddenAccessException ex)
            {
                // 注意：Forbid(string) 的参数是 authenticationScheme 而非消息，误用会导致 500。
                // 这里显式返回 403 + 错误信息
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        return null;
    }

    /// <summary>
    /// 获取账单列表
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="categoryId">分类ID</param>
    /// <param name="paymentChannelId">支付渠道ID</param>
    /// <param name="ledgerId">账本ID</param>
    /// <param name="type">账单类型</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>账单列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetBills(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] Guid? paymentChannelId = null,
        [FromQuery] Guid? ledgerId = null,
        [FromQuery] BillType? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();

        var bills = await _billService.GetBillsAsync(
            userId, startDate, endDate, categoryId, paymentChannelId, ledgerId, type, page, pageSize);

        var totalCount = await _billService.GetBillCountAsync(
            userId, startDate, endDate, categoryId, paymentChannelId, ledgerId, type);

        return Ok(new
        {
            items = bills.Select(MapToResponse),
            totalCount,
            page,
            pageSize
        });
    }

    /// <summary>
    /// 获取账单详情
    /// </summary>
    /// <param name="id">账单ID</param>
    /// <returns>账单信息</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBill(Guid id)
    {
        var userId = GetUserId();
        var bill = await _billService.GetBillByIdAsync(id);

        if (bill == null || bill.UserId != userId)
        {
            return NotFound(new { message = "账单不存在" });
        }

        return Ok(MapToResponse(bill));
    }

    /// <summary>
    /// 创建账单
    /// </summary>
    /// <param name="request">账单请求</param>
    /// <returns>创建的账单</returns>
    [HttpPost]
    public async Task<IActionResult> CreateBill([FromBody] BillRequest request)
    {
        var userId = GetUserId();

        var validationError = ValidateBillRequest(request);
        if (validationError != null)
        {
            return validationError;
        }

        var ledgerError = await ValidateLedgerWritePermissionAsync(userId, request.LedgerId);
        if (ledgerError != null)
        {
            return ledgerError;
        }

        var bill = new Core.Entities.Bill
        {
            UserId = userId,
            Amount = request.Amount,
            Type = request.Type,
            CategoryId = request.CategoryId,
            PaymentChannelId = request.PaymentChannelId,
            LedgerId = request.LedgerId,
            TransactionTime = request.TransactionTime ?? DateTime.UtcNow,
            Note = request.Note
        };

        var createdBill = await _billService.CreateBillAsync(bill);

        return CreatedAtAction(nameof(GetBill), new { id = createdBill.Id }, MapToResponse(createdBill));
    }

    /// <summary>
    /// 更新账单
    /// </summary>
    /// <param name="id">账单ID</param>
    /// <param name="request">账单请求</param>
    /// <returns>更新的账单</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBill(Guid id, [FromBody] BillRequest request)
    {
        var userId = GetUserId();

        var existingBill = await _billService.GetBillByIdAsync(id);
        if (existingBill == null || existingBill.UserId != userId)
        {
            return NotFound(new { message = "账单不存在" });
        }

        var validationError = ValidateBillRequest(request);
        if (validationError != null)
        {
            return validationError;
        }

        var ledgerError = await ValidateLedgerWritePermissionAsync(userId, request.LedgerId);
        if (ledgerError != null)
        {
            return ledgerError;
        }

        var bill = new Core.Entities.Bill
        {
            Id = id,
            UserId = userId,
            Amount = request.Amount,
            Type = request.Type,
            CategoryId = request.CategoryId,
            PaymentChannelId = request.PaymentChannelId,
            LedgerId = request.LedgerId,
            TransactionTime = request.TransactionTime ?? DateTime.UtcNow,
            Note = request.Note
        };

        try
        {
            var updatedBill = await _billService.UpdateBillAsync(bill);
            return Ok(MapToResponse(updatedBill));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除账单
    /// </summary>
    /// <param name="id">账单ID</param>
    /// <returns>删除结果</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBill(Guid id)
    {
        var userId = GetUserId();
        var result = await _billService.DeleteBillAsync(id, userId);

        if (!result)
        {
            return NotFound(new { message = "账单不存在" });
        }

        return NoContent();
    }

    private IActionResult? ValidateBillRequest(BillRequest request)
    {
        if (request.Amount <= 0)
        {
            return BadRequest(new { message = "金额必须大于0" });
        }

        if (request.CategoryId == Guid.Empty)
        {
            return BadRequest(new { message = "请选择分类" });
        }

        if (request.PaymentChannelId == Guid.Empty)
        {
            return BadRequest(new { message = "请选择支付渠道" });
        }

        return null;
    }

    private static object MapToResponse(Core.Entities.Bill bill) => new
    {
        id = bill.Id,
        amount = bill.Amount,
        type = bill.Type.ToString(),
        categoryId = bill.CategoryId,
        categoryName = bill.Category?.Name,
        categoryIcon = bill.Category?.Icon,
        paymentChannelId = bill.PaymentChannelId,
        paymentChannelName = bill.PaymentChannel?.Name,
        paymentChannelIcon = bill.PaymentChannel?.Icon,
        ledgerId = bill.LedgerId,
        ledgerName = bill.Ledger?.Name,
        ledgerIcon = bill.Ledger?.Icon,
        transactionTime = bill.TransactionTime,
        note = bill.Note,
        source = bill.Source.ToString(),
        syncStatus = bill.SyncStatus.ToString(),
        createdAt = bill.CreatedAt
    };
}

/// <summary>
/// 账单请求
/// </summary>
public class BillRequest
{
    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 账单类型
    /// </summary>
    public BillType Type { get; set; }

    /// <summary>
    /// 分类ID
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// 支付渠道ID
    /// </summary>
    public Guid PaymentChannelId { get; set; }

    /// <summary>
    /// 账本ID（null 表示未归属账本）
    /// </summary>
    public Guid? LedgerId { get; set; }

    /// <summary>
    /// 交易时间
    /// </summary>
    public DateTime? TransactionTime { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Note { get; set; }
}
