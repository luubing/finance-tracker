using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 支付渠道控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PaymentChannelsController : ControllerBase
{
    private readonly IPresetDataService _presetDataService;

    public PaymentChannelsController(IPresetDataService presetDataService)
    {
        _presetDataService = presetDataService;
    }

    /// <summary>
    /// 获取支付渠道列表
    /// </summary>
    /// <param name="userId">用户ID（可选）</param>
    /// <returns>支付渠道列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetPaymentChannels([FromQuery] Guid? userId = null)
    {
        var channels = await _presetDataService.GetPaymentChannelsAsync(userId);

        return Ok(channels.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            icon = c.Icon,
            isPreset = c.IsPreset,
            sortOrder = c.SortOrder
        }));
    }
}
