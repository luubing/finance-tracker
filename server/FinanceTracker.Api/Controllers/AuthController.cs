using FinanceTracker.Api.Services;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Shared.Validators;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;

    public AuthController(IAuthService authService, ITokenService tokenService)
    {
        _authService = authService;
        _tokenService = tokenService;
    }

    /// <summary>
    /// 注册或登录
    /// </summary>
    /// <param name="request">手机号请求</param>
    /// <returns>用户信息和 JWT Token</returns>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var validationResult = PhoneValidator.Validate(request.PhoneNumber);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { message = validationResult.ErrorMessage });
        }

        var user = await _authService.RegisterOrLoginAsync(request.PhoneNumber);

        // 生成 JWT Token
        var token = _tokenService.GenerateToken(user);

        return Ok(new
        {
            userId = user.Id,
            phoneNumber = user.PhoneNumber,
            createdAt = user.CreatedAt,
            token = token
        });
    }

    /// <summary>
    /// 获取用户信息（需要认证）
    /// </summary>
    /// <returns>用户信息</returns>
    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(new { message = "未授权" });
        }

        var user = await _authService.GetUserByIdAsync(userId);

        if (user == null)
        {
            return NotFound(new { message = "用户不存在" });
        }

        return Ok(new
        {
            userId = user.Id,
            phoneNumber = user.PhoneNumber,
            createdAt = user.CreatedAt
        });
    }
}

/// <summary>
/// 登录请求
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// 手机号
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
