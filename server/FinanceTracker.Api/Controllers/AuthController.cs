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

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// 注册或登录
    /// </summary>
    /// <param name="request">手机号请求</param>
    /// <returns>用户信息</returns>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var validationResult = PhoneValidator.Validate(request.PhoneNumber);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { message = validationResult.ErrorMessage });
        }

        var user = await _authService.RegisterOrLoginAsync(request.PhoneNumber);

        return Ok(new
        {
            userId = user.Id,
            phoneNumber = user.PhoneNumber,
            createdAt = user.CreatedAt
        });
    }

    /// <summary>
    /// 获取用户信息
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>用户信息</returns>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUser(Guid userId)
    {
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
