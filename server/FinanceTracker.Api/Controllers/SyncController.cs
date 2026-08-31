using System.Security.Claims;
using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 同步控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;

    public SyncController(ISyncService syncService)
    {
        _syncService = syncService;
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
    /// 同步账单到云端
    /// </summary>
    /// <returns>同步结果</returns>
    [HttpPost("bills")]
    public async Task<IActionResult> SyncBills()
    {
        var userId = GetUserId();

        var canSync = await _syncService.CanSyncAsync();
        if (!canSync)
        {
            return BadRequest(new { message = "当前无法同步，请检查网络连接" });
        }

        var result = await _syncService.SyncBillsAsync(userId);

        return Ok(new
        {
            success = result.Success,
            syncedCount = result.SyncedCount,
            failedCount = result.FailedCount,
            message = result.Success ? "同步完成" : $"同步完成，{result.FailedCount}条失败"
        });
    }

    /// <summary>
    /// 获取待同步的账单数量
    /// </summary>
    /// <returns>待同步数量</returns>
    [HttpGet("pending-count")]
    public async Task<IActionResult> GetPendingCount()
    {
        var userId = GetUserId();
        var count = await _syncService.GetOfflineCacheCountAsync(userId);

        return Ok(new { count });
    }
}
