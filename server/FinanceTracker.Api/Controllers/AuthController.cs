using FinanceTracker.Api.Services;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
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
    private readonly PhoneAllowList _allowList;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ITokenService tokenService,
        PhoneAllowList allowList,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _tokenService = tokenService;
        _allowList = allowList;
        _logger = logger;
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

        // 手机号白名单（可选，默认关闭）：必须先于 RegisterOrLoginAsync——被拒绝的号码不能在库里留下一个新用户。
        // 403 而不是 401（架构决策 7）：401 会让客户端当成"登录过期"而登出本地账号；
        // 注意不要用 Forbid(string)：参数是认证方案名而不是消息，误用会导致 500
        if (!_allowList.IsAllowed(request.PhoneNumber))
        {
            _logger.LogWarning(
                "手机号不在白名单内，已拒绝登录 / 注册：{PhoneNumber}",
                PhoneNumberMasker.Mask(request.PhoneNumber));
            return StatusCode(StatusCodes.Status403Forbidden, new { message = PhoneAllowList.DeniedMessage });
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
