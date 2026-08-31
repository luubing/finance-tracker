using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 支付渠道控制器
/// </summary>
public class PaymentChannelsController : BaseApiController
{
    private readonly IPaymentChannelService _paymentChannelService;

    public PaymentChannelsController(IPaymentChannelService paymentChannelService)
    {
        _paymentChannelService = paymentChannelService;
    }

    /// <summary>
    /// 获取支付渠道列表
    /// </summary>
    /// <returns>支付渠道列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetPaymentChannels()
    {
        var userId = GetUserId();
        var channels = await _paymentChannelService.GetPaymentChannelsAsync(userId);

        return Ok(channels.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            icon = c.Icon,
            isPreset = c.IsPreset,
            sortOrder = c.SortOrder
        }));
    }

    /// <summary>
    /// 获取支付渠道详情
    /// </summary>
    /// <param name="id">渠道ID</param>
    /// <returns>支付渠道信息</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPaymentChannel(Guid id)
    {
        var userId = GetUserId();
        var channel = await _paymentChannelService.GetPaymentChannelByIdAsync(id);

        if (channel == null || (!channel.IsPreset && channel.UserId != userId))
        {
            return NotFound(new { message = "支付渠道不存在" });
        }

        return Ok(new
        {
            id = channel.Id,
            name = channel.Name,
            icon = channel.Icon,
            isPreset = channel.IsPreset,
            sortOrder = channel.SortOrder
        });
    }

    /// <summary>
    /// 创建自定义支付渠道
    /// </summary>
    /// <param name="request">支付渠道请求</param>
    /// <returns>创建的支付渠道</returns>
    [HttpPost]
    public async Task<IActionResult> CreatePaymentChannel([FromBody] PaymentChannelRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "渠道名称不能为空" });
        }

        var channel = new Core.Entities.PaymentChannel
        {
            UserId = userId,
            Name = request.Name,
            Icon = request.Icon ?? "mdi-credit-card",
            SortOrder = request.SortOrder
        };

        var createdChannel = await _paymentChannelService.CreatePaymentChannelAsync(channel);

        return CreatedAtAction(nameof(GetPaymentChannel), new { id = createdChannel.Id }, new
        {
            id = createdChannel.Id,
            name = createdChannel.Name,
            icon = createdChannel.Icon,
            isPreset = createdChannel.IsPreset,
            sortOrder = createdChannel.SortOrder
        });
    }

    /// <summary>
    /// 更新自定义支付渠道
    /// </summary>
    /// <param name="id">渠道ID</param>
    /// <param name="request">支付渠道请求</param>
    /// <returns>更新的支付渠道</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePaymentChannel(Guid id, [FromBody] PaymentChannelRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "渠道名称不能为空" });
        }

        var channel = new Core.Entities.PaymentChannel
        {
            Id = id,
            UserId = userId,
            Name = request.Name,
            Icon = request.Icon ?? "mdi-credit-card",
            SortOrder = request.SortOrder
        };

        try
        {
            var updatedChannel = await _paymentChannelService.UpdatePaymentChannelAsync(channel);

            return Ok(new
            {
                id = updatedChannel.Id,
                name = updatedChannel.Name,
                icon = updatedChannel.Icon,
                isPreset = updatedChannel.IsPreset,
                sortOrder = updatedChannel.SortOrder
            });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    /// <summary>
    /// 删除自定义支付渠道
    /// </summary>
    /// <param name="id">渠道ID</param>
    /// <returns>删除结果</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePaymentChannel(Guid id)
    {
        var userId = GetUserId();

        try
        {
            var result = await _paymentChannelService.DeletePaymentChannelAsync(id, userId);

            if (!result)
            {
                return NotFound(new { message = "支付渠道不存在" });
            }

            return Ok(new { message = "删除成功" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

/// <summary>
/// 支付渠道请求
/// </summary>
public class PaymentChannelRequest
{
    /// <summary>
    /// 渠道名称
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
