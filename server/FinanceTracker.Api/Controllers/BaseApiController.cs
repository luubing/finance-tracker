using System.Security.Claims;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// API 控制器基类
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Microsoft.AspNetCore.Authorization.Authorize]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// 从 JWT Token 获取当前用户 ID
    /// </summary>
    /// <returns>用户ID</returns>
    /// <exception cref="UnauthorizedAccessException">未授权时抛出</exception>
    protected Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("未授权");
        }
        return userId;
    }

    /// <summary>
    /// 账本写权限校验（与 ADR 0004 权限模型一致）：自有账本，或拥有写权限的共享账本成员，返回 null 表示放行。
    /// 无权记账返回 403（已认证但无权操作，不能是 401，否则客户端会误判为登录过期而登出），账本不存在返回 400。
    /// </summary>
    /// <param name="ledgerMemberService">账本成员服务</param>
    /// <param name="ledgerId">要写入账单的账本ID</param>
    /// <param name="userId">当前用户ID</param>
    /// <returns>放行返回 null；否则返回应直接作为响应的错误结果</returns>
    protected async Task<IActionResult?> CheckLedgerWritableAsync(
        ILedgerMemberService ledgerMemberService, Guid ledgerId, Guid userId)
    {
        try
        {
            await ledgerMemberService.EnsureCanWriteAsync(ledgerId, userId);
            return null;
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
    }
}
