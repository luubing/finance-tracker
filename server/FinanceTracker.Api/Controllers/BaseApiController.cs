using System.Security.Claims;
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
}
