using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 账单控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BillsController : ControllerBase
{
    private readonly IBillService _billService;

    public BillsController(IBillService billService)
    {
        _billService = billService;
    }

    /// <summary>
    /// 获取账单列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="categoryId">分类ID</param>
    /// <param name="paymentChannelId">支付渠道ID</param>
    /// <param name="type">账单类型</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>账单列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetBills(
        [FromQuery] Guid userId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] Guid? paymentChannelId = null,
        [FromQuery] BillType? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var bills = await _billService.GetBillsAsync(
            userId, startDate, endDate, categoryId, paymentChannelId, type, page, pageSize);

        var totalCount = await _billService.GetBillCountAsync(
            userId, startDate, endDate, categoryId, paymentChannelId, type);

        return Ok(new
        {
            items = bills.Select(b => new
            {
                id = b.Id,
                amount = b.Amount,
                type = b.Type.ToString(),
                categoryId = b.CategoryId,
                categoryName = b.Category?.Name,
                categoryIcon = b.Category?.Icon,
                paymentChannelId = b.PaymentChannelId,
                paymentChannelName = b.PaymentChannel?.Name,
                paymentChannelIcon = b.PaymentChannel?.Icon,
                transactionTime = b.TransactionTime,
                note = b.Note,
                source = b.Source.ToString(),
                syncStatus = b.SyncStatus.ToString(),
                createdAt = b.CreatedAt
            }),
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
        var bill = await _billService.GetBillByIdAsync(id);

        if (bill == null)
        {
            return NotFound(new { message = "账单不存在" });
        }

        return Ok(new
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
            transactionTime = bill.TransactionTime,
            note = bill.Note,
            source = bill.Source.ToString(),
            syncStatus = bill.SyncStatus.ToString(),
            createdAt = bill.CreatedAt
        });
    }

    /// <summary>
    /// 创建账单
    /// </summary>
    /// <param name="request">账单请求</param>
    /// <returns>创建的账单</returns>
    [HttpPost]
    public async Task<IActionResult> CreateBill([FromBody] CreateBillRequest request)
    {
        if (request.UserId == Guid.Empty)
        {
            return BadRequest(new { message = "用户ID不能为空" });
        }

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

        var bill = new Core.Entities.Bill
        {
            UserId = request.UserId,
            Amount = request.Amount,
            Type = request.Type,
            CategoryId = request.CategoryId,
            PaymentChannelId = request.PaymentChannelId,
            TransactionTime = request.TransactionTime ?? DateTime.UtcNow,
            Note = request.Note
        };

        var createdBill = await _billService.CreateBillAsync(bill);

        return CreatedAtAction(nameof(GetBill), new { id = createdBill.Id }, new
        {
            id = createdBill.Id,
            amount = createdBill.Amount,
            type = createdBill.Type.ToString(),
            categoryId = createdBill.CategoryId,
            categoryName = createdBill.Category?.Name,
            categoryIcon = createdBill.Category?.Icon,
            paymentChannelId = createdBill.PaymentChannelId,
            paymentChannelName = createdBill.PaymentChannel?.Name,
            paymentChannelIcon = createdBill.PaymentChannel?.Icon,
            transactionTime = createdBill.TransactionTime,
            note = createdBill.Note,
            source = createdBill.Source.ToString(),
            syncStatus = createdBill.SyncStatus.ToString(),
            createdAt = createdBill.CreatedAt
        });
    }

    /// <summary>
    /// 更新账单
    /// </summary>
    /// <param name="id">账单ID</param>
    /// <param name="request">账单请求</param>
    /// <returns>更新的账单</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBill(Guid id, [FromBody] UpdateBillRequest request)
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

        var bill = new Core.Entities.Bill
        {
            Id = id,
            UserId = request.UserId,
            Amount = request.Amount,
            Type = request.Type,
            CategoryId = request.CategoryId,
            PaymentChannelId = request.PaymentChannelId,
            TransactionTime = request.TransactionTime ?? DateTime.UtcNow,
            Note = request.Note
        };

        try
        {
            var updatedBill = await _billService.UpdateBillAsync(bill);

            return Ok(new
            {
                id = updatedBill.Id,
                amount = updatedBill.Amount,
                type = updatedBill.Type.ToString(),
                categoryId = updatedBill.CategoryId,
                categoryName = updatedBill.Category?.Name,
                categoryIcon = updatedBill.Category?.Icon,
                paymentChannelId = updatedBill.PaymentChannelId,
                paymentChannelName = updatedBill.PaymentChannel?.Name,
                paymentChannelIcon = updatedBill.PaymentChannel?.Icon,
                transactionTime = updatedBill.TransactionTime,
                note = updatedBill.Note,
                source = updatedBill.Source.ToString(),
                syncStatus = updatedBill.SyncStatus.ToString(),
                createdAt = updatedBill.CreatedAt
            });
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
    /// <param name="userId">用户ID</param>
    /// <returns>删除结果</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBill(Guid id, [FromQuery] Guid userId)
    {
        var result = await _billService.DeleteBillAsync(id, userId);

        if (!result)
        {
            return NotFound(new { message = "账单不存在" });
        }

        return Ok(new { message = "删除成功" });
    }
}

/// <summary>
/// 创建账单请求
/// </summary>
public class CreateBillRequest
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

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
    /// 交易时间
    /// </summary>
    public DateTime? TransactionTime { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Note { get; set; }
}

/// <summary>
/// 更新账单请求
/// </summary>
public class UpdateBillRequest
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

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
    /// 交易时间
    /// </summary>
    public DateTime? TransactionTime { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Note { get; set; }
}
