using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FinanceTracker.Api.Middleware;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using FinanceTracker.Core.Entities;
using FinanceTracker.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 白名单的令牌侧检查：登录入口只能拦住新的登录 / 注册，名单启用前签发的令牌还能一直用到过期（默认 7 天）。
/// 中间件让名单一启用就对所有已认证请求生效——名单写错时第一次请求就暴露，而不是等令牌过期后才集体登录失败。
/// 被拒绝必须是 403（已认证但无权），不能是 401：401 会让客户端当成"登录过期"而登出本地账号（架构决策 7）。
/// </summary>
public sealed class PhoneAllowListMiddlewareTests
{
    private const string Alice = "13800138000";
    private const string Bob = "13900139000";

    private static readonly JwtSettings Jwt = new()
    {
        SecretKey = new string('k', 48),
        Issuer = "finance-tracker-tests",
        Audience = "finance-tracker-tests",
        ExpiryInMinutes = 60
    };

    private sealed record Outcome(bool NextCalled, int StatusCode, string Body);

    private static async Task<Outcome> InvokeAsync(PhoneAllowList allowList, ClaimsPrincipal? user)
    {
        var nextCalled = false;
        var middleware = new PhoneAllowListMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            allowList,
            NullLogger<PhoneAllowListMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (user != null)
        {
            context.User = user;
        }

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return new Outcome(nextCalled, context.Response.StatusCode, body);
    }

    private static ClaimsPrincipal Authenticated(string? phoneNumber)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (phoneNumber != null)
        {
            claims.Add(new Claim(ClaimTypes.MobilePhone, phoneNumber));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string MessageOf(Outcome outcome) =>
        JsonDocument.Parse(outcome.Body).RootElement.GetProperty("message").GetString()!;

    // ---------- 放行的情形 ----------

    [Fact]
    public async Task 名单未启用_什么都不做()
    {
        var outcome = await InvokeAsync(PhoneAllowList.Disabled, Authenticated("13700137000"));

        Assert.True(outcome.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
    }

    [Fact]
    public async Task 名单启用_未认证的请求直接放行_由各自的认证逻辑处理()
    {
        // 登录接口本身（还没有令牌）也属于这一类：它由 AuthController 自己按白名单判断
        var outcome = await InvokeAsync(new PhoneAllowList(new[] { Alice }), user: null);

        Assert.True(outcome.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
    }

    [Fact]
    public async Task 名单启用_令牌手机号在名单内_放行()
    {
        var outcome = await InvokeAsync(new PhoneAllowList(new[] { $"{Alice},{Bob}" }), Authenticated(Bob));

        Assert.True(outcome.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
    }

    // ---------- 拒绝的情形 ----------

    [Fact]
    public async Task 名单启用_令牌手机号不在名单内_返回403和提示_不调用后续处理()
    {
        var outcome = await InvokeAsync(new PhoneAllowList(new[] { Alice }), Authenticated(Bob));

        Assert.False(outcome.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, outcome.StatusCode);   // 绝不能是 401（客户端会据此登出）
        Assert.Equal(PhoneAllowList.DeniedMessage, MessageOf(outcome));
    }

    [Fact]
    public async Task 名单启用_令牌里读不到手机号_按不在名单内处理()
    {
        // 白名单语义：不放行未知。旧版本签发的令牌若没有手机号声明，同样挡住
        var outcome = await InvokeAsync(new PhoneAllowList(new[] { Alice }), Authenticated(phoneNumber: null));

        Assert.False(outcome.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, outcome.StatusCode);
    }

    [Fact]
    public async Task 拒绝响应的正文不回显手机号()
    {
        var outcome = await InvokeAsync(new PhoneAllowList(new[] { Alice }), Authenticated(Bob));

        Assert.DoesNotContain(Bob, outcome.Body);
    }

    // ---------- 与 Program.cs 的接线绑定 ----------
    // Program.cs 启动时要连 PostgreSQL 做迁移，单元测试里起不了真实宿主；
    // 这里按本仓库的惯例扫描源码，把「顺序」和「注册」这两个一旦错了就悄悄失效的点钉住。

    [Fact]
    public void Program里中间件排在认证之后_授权之前()
    {
        var source = File.ReadAllText(RepoRoot.Combine("server/FinanceTracker.Api/Program.cs"));

        var authentication = source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var allowList = source.IndexOf("app.UseMiddleware<PhoneAllowListMiddleware>();", StringComparison.Ordinal);
        var authorization = source.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        Assert.True(authentication >= 0, "Program.cs 里找不到 app.UseAuthentication();");
        Assert.True(allowList > authentication,
            "PhoneAllowListMiddleware 必须在 UseAuthentication 之后：之前的话读不到令牌身份，名单对已签发令牌就悄悄失效");
        Assert.True(authorization > allowList,
            "PhoneAllowListMiddleware 必须在 UseAuthorization 之前：否则名单外的请求会先走到授权 / 控制器");
    }

    [Fact]
    public void Program里从配置读取名单并注册为单例()
    {
        var source = File.ReadAllText(RepoRoot.Combine("server/FinanceTracker.Api/Program.cs"));

        Assert.Contains("PhoneAllowList.FromConfiguration(builder.Configuration)", source);
        Assert.Contains("builder.Services.AddSingleton(phoneAllowList);", source);
    }

    // ---------- 与真实签发 / 验证的令牌绑定 ----------

    /// <summary>
    /// 用服务端真实的 TokenService 签发令牌，再按 Program.cs 里 JwtBearer 同样的参数验证，
    /// 把验证得到的身份交给中间件：TokenService 改了手机号声明名时，这里先红，
    /// 而不是线上白名单悄悄对所有人（含名单内的人）返回 403。
    /// </summary>
    private static async Task<ClaimsPrincipal> ValidatedPrincipalOfAsync(string phoneNumber)
    {
        var token = new TokenService(Options.Create(Jwt))
            .GenerateToken(new User { Id = Guid.NewGuid(), PhoneNumber = phoneNumber });

        var result = await new JsonWebTokenHandler { MapInboundClaims = true }.ValidateTokenAsync(
            token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Jwt.Issuer,
                ValidAudience = Jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.SecretKey))
            });

        Assert.True(result.IsValid, "测试里签发的令牌应当能通过验证");
        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    [Fact]
    public async Task 真实令牌_手机号在名单内_放行()
    {
        var outcome = await InvokeAsync(new PhoneAllowList(new[] { Alice }), await ValidatedPrincipalOfAsync(Alice));

        Assert.True(outcome.NextCalled);
    }

    [Fact]
    public async Task 真实令牌_手机号不在名单内_403()
    {
        var outcome = await InvokeAsync(new PhoneAllowList(new[] { Alice }), await ValidatedPrincipalOfAsync(Bob));

        Assert.False(outcome.NextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, outcome.StatusCode);
    }
}
