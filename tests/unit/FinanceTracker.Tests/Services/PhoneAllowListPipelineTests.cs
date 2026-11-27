using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinanceTracker.Api.Middleware;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using FinanceTracker.Core.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 白名单在「真实认证管线」里的端到端行为：真 Kestrel（只绑本机回环、端口由系统分配）+ 真 JwtBearer + 服务端真实的
/// <see cref="TokenService"/> 签发的令牌，管线顺序与 Program.cs 一致（UseAuthentication → 白名单中间件 → UseAuthorization）。
/// <para>
/// <see cref="PhoneAllowListMiddlewareTests"/> 已经分别验证了中间件本身、令牌声明、Program.cs 里的接线顺序；
/// 这里补上"拼在一起之后"的三态语义——它们是架构决策 7（401 与 403 严格区分）在整条链路上的体现：
/// 没有令牌 / 令牌无效 → 401（客户端据此重新登录）；令牌有效但手机号不在名单内 → 403（客户端不能因此登出）。
/// JwtBearer 的参数与 Program.cs 里的 AddJwtBearer 保持同一组（见 <see cref="StartAsync"/>）。
/// </para>
/// </summary>
public sealed class PhoneAllowListPipelineTests
{
    private const string Alice = "13800138000";
    private const string Bob = "13900139000";

    private static readonly JwtSettings Jwt = new()
    {
        SecretKey = new string('k', 48),
        Issuer = "finance-tracker-pipeline-tests",
        Audience = "finance-tracker-pipeline-tests",
        ExpiryInMinutes = 60
    };

    /// <summary>跑起来的临时宿主：释放时停掉服务器</summary>
    private sealed class PipelineHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        public PipelineHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static async Task<PipelineHost> StartAsync(PhoneAllowList allowList)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(allowList);
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Jwt.Issuer,
                ValidAudience = Jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.SecretKey))
            };
        });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseMiddleware<PhoneAllowListMiddleware>();
        app.UseAuthorization();
        app.MapGet("/public", () => "public");                        // 不需要登录（像登录接口本身）
        app.MapGet("/private", () => "private").RequireAuthorization(); // 需要登录（像账单接口）

        await app.StartAsync();

        var address = app.Urls.Single();
        return new PipelineHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private static string TokenFor(string phoneNumber, JwtSettings? settings = null) =>
        new TokenService(Options.Create(settings ?? Jwt))
            .GenerateToken(new User { Id = Guid.NewGuid(), PhoneNumber = phoneNumber });

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    // ---------- 名单启用 ----------

    [Fact]
    public async Task 名单启用_没有令牌_公开接口照常访问_受保护接口是401()
    {
        await using var host = await StartAsync(new PhoneAllowList(new[] { Alice }));

        using var publicResponse = await GetAsync(host.Client, "/public");
        using var privateResponse = await GetAsync(host.Client, "/private");

        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);                  // 登录入口属于这一类：白名单不拦未认证请求
        Assert.Equal(HttpStatusCode.Unauthorized, privateResponse.StatusCode);       // 没登录就是 401，与白名单无关
    }

    [Fact]
    public async Task 名单启用_令牌手机号在名单内_受保护接口放行()
    {
        await using var host = await StartAsync(new PhoneAllowList(new[] { $"{Alice},{Bob}" }));

        using var response = await GetAsync(host.Client, "/private", TokenFor(Bob));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("private", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 名单启用_令牌有效但手机号不在名单内_是403而不是401()
    {
        await using var host = await StartAsync(new PhoneAllowList(new[] { Alice }));

        using var response = await GetAsync(host.Client, "/private", TokenFor(Bob));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);                 // 401 会让客户端当成"登录过期"而登出本地账号
        Assert.False(response.Headers.WwwAuthenticate.Any(), "403 不应带 401 的认证质询头");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(PhoneAllowList.DeniedMessage, JsonDocument.Parse(body).RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain(Bob, body);
    }

    [Fact]
    public async Task 名单启用_名单外手机号的令牌访问公开接口_同样是403()
    {
        // 已认证的请求一律过白名单：名单改了之后，名单外手机号手里的旧令牌立刻失效，不用等令牌过期
        await using var host = await StartAsync(new PhoneAllowList(new[] { Alice }));

        using var response = await GetAsync(host.Client, "/public", TokenFor(Bob));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 名单启用_令牌签名不对_是401_白名单不替它背书()
    {
        // 名单内手机号 + 别的密钥签出来的令牌：认证先失败 → 401。白名单只管"已认证的人是否被许可"，不降低认证门槛
        await using var host = await StartAsync(new PhoneAllowList(new[] { Alice }));
        var forged = TokenFor(Alice, new JwtSettings
        {
            SecretKey = new string('x', 48),
            Issuer = Jwt.Issuer,
            Audience = Jwt.Audience,
            ExpiryInMinutes = 60
        });

        using var response = await GetAsync(host.Client, "/private", forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- 名单关闭 ----------

    [Fact]
    public async Task 名单关闭_任何手机号的有效令牌都照常放行()
    {
        await using var host = await StartAsync(PhoneAllowList.Disabled);

        using var response = await GetAsync(host.Client, "/private", TokenFor("13700137000"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
