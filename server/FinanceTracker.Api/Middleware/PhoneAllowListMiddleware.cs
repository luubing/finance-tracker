using System.Security.Claims;
using FinanceTracker.Api.Services;
using FinanceTracker.Core.Services;

namespace FinanceTracker.Api.Middleware;

/// <summary>
/// 手机号白名单的「令牌侧」检查：已通过 JWT 认证的请求，令牌里的手机号不在名单内就回 403。
/// 登录入口（<c>AuthController.Login</c>）只能拦住新的登录 / 注册，名单启用之前签发的令牌还能一直用到过期（默认 7 天，
/// 见 Jwt:ExpiryInMinutes）；这里让名单一启用就对所有人生效——名单写错时第一次请求就暴露，
/// 而不是等令牌过期后才集体登录失败。
/// <para>
/// 必须放在 <c>UseAuthentication()</c> 之后。未认证的请求（含登录接口本身）直接放行，交给各自的认证 / 授权逻辑；
/// 名单未启用时什么都不做。
/// </para>
/// <para>
/// 返回 403 而不是 401（见架构决策 7）：401 会让客户端把它当成"登录过期"而登出本地账号，
/// 这里的语义是"已认证，但无权使用这个云端"。令牌里读不到手机号时按不在名单内处理（启用名单就是白名单语义，不放行未知）。
/// </para>
/// </summary>
public class PhoneAllowListMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PhoneAllowList _allowList;
    private readonly ILogger<PhoneAllowListMiddleware> _logger;

    public PhoneAllowListMiddleware(
        RequestDelegate next,
        PhoneAllowList allowList,
        ILogger<PhoneAllowListMiddleware> logger)
    {
        _next = next;
        _allowList = allowList;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_allowList.IsEnabled && context.User.Identity?.IsAuthenticated == true)
        {
            // TokenService 把手机号写在 ClaimTypes.MobilePhone 声明里；JwtBearer 解析后仍是这个声明名
            // （PhoneAllowListMiddlewareTests 用真实签发的令牌守着这一点）
            var phoneNumber = context.User.FindFirst(ClaimTypes.MobilePhone)?.Value;

            if (!_allowList.IsAllowed(phoneNumber))
            {
                // 手机号等同于登录凭据：日志里只记脱敏后的号码
                _logger.LogWarning(
                    "令牌对应的手机号不在白名单内，已拒绝请求：{PhoneNumber}",
                    PhoneNumberMasker.Mask(phoneNumber));

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = PhoneAllowList.DeniedMessage });
                return;
            }
        }

        await _next(context);
    }
}
