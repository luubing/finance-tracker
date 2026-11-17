using System.Net;
using System.Text;
using System.Text.Json;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using FinanceTracker.Core.Entities;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 云端 JWT 必须始终属于"当前登录的手机号"。
///
/// 事故场景：同一设备先用手机号 A 登录并换到云端令牌，再换手机号 B 登录。
/// 本地登录不会产生云端令牌，而 HttpService 只要本地已有令牌就直接沿用，于是 B 的数据会带着 A 的令牌
/// 推进 A 的云端账号；加上"云端用户 Id 映射回本地用户"之后，A 的云端数据还会被当成 B 自己的拉下来。
/// 这里用服务端真实的 TokenService 签发令牌，覆盖"换号时作废"和"残留旧令牌时丢弃重换"两道防线。
/// </summary>
public class CloudTokenOwnershipTests
{
    private const string PhoneA = "13800000001";
    private const string PhoneB = "13800000002";

    private static readonly TokenService ServerTokenService = new(Options.Create(new JwtSettings
    {
        SecretKey = new string('k', 48),
        Issuer = "finance-tracker-tests",
        Audience = "finance-tracker-tests",
        ExpiryInMinutes = 60
    }));

    /// <summary>每个手机号在"云端"对应固定的用户 Id（与本地用户 Id 不同，和线上一致）</summary>
    private static Guid CloudUserIdFor(string phone) =>
        phone == PhoneA
            ? Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a")
            : Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static string TokenFor(string phone) =>
        ServerTokenService.GenerateToken(new User { Id = CloudUserIdFor(phone), PhoneNumber = phone });

    // ---------- AuthenticationService：换号登录时作废旧令牌 ----------

    [Fact]
    public async Task 换号登录时_上一个手机号的令牌被作废()
    {
        var js = new FakeLocalStorageJsRuntime();
        var auth = new AuthenticationService(js);
        await auth.SaveUserInfoAsync(Guid.NewGuid(), PhoneA);
        await auth.SaveTokenAsync(TokenFor(PhoneA));

        await auth.SaveUserInfoAsync(Guid.NewGuid(), PhoneB);

        Assert.Null(await auth.GetTokenAsync());
        Assert.False(js.Storage.ContainsKey("authToken"));
        Assert.Equal(PhoneB, js.Storage["phoneNumber"]);
    }

    [Fact]
    public async Task 同一手机号再次登录时_令牌保留()
    {
        var js = new FakeLocalStorageJsRuntime();
        var auth = new AuthenticationService(js);
        var token = TokenFor(PhoneA);
        await auth.SaveUserInfoAsync(Guid.NewGuid(), PhoneA);
        await auth.SaveTokenAsync(token);

        await auth.SaveUserInfoAsync(Guid.NewGuid(), PhoneA);

        Assert.Equal(token, await auth.GetTokenAsync());
    }

    [Fact]
    public async Task 退出登录后换号登录_不残留令牌()
    {
        var js = new FakeLocalStorageJsRuntime();
        var auth = new AuthenticationService(js);
        await auth.SaveUserInfoAsync(Guid.NewGuid(), PhoneA);
        await auth.SaveTokenAsync(TokenFor(PhoneA));
        await auth.LogoutAsync();

        await auth.SaveUserInfoAsync(Guid.NewGuid(), PhoneB);

        Assert.Null(await auth.GetTokenAsync());
    }

    // ---------- HttpService：访问云端前核对令牌归属 ----------

    [Fact]
    public async Task 本地残留了别人的令牌_访问云端时丢弃并按当前手机号重新换取()
    {
        var (http, js, cloud) = CreateHttpService();
        js.Storage["phoneNumber"] = PhoneB;
        js.Storage["authToken"] = TokenFor(PhoneA);     // 旧版本换号后残留 / 存储被改动

        var cloudUserId = await http.GetCloudUserIdAsync();

        Assert.Equal(CloudUserIdFor(PhoneB), cloudUserId);
        Assert.Equal(new[] { PhoneB }, cloud.LoginPhones);
        Assert.Equal(PhoneB, JwtClaimReader.TryReadPhoneNumber(js.Storage["authToken"]));
    }

    [Fact]
    public async Task 本地残留了别人的令牌_业务请求携带的是当前手机号的令牌()
    {
        var (http, js, cloud) = CreateHttpService();
        js.Storage["phoneNumber"] = PhoneB;
        js.Storage["authToken"] = TokenFor(PhoneA);

        await http.GetAsync<JsonElement>("api/bills");

        var bearer = Assert.Single(cloud.BearerTokensOfBusinessRequests);
        Assert.Equal(PhoneB, JwtClaimReader.TryReadPhoneNumber(bearer));
        Assert.Equal(CloudUserIdFor(PhoneB), JwtClaimReader.TryReadUserId(bearer));
    }

    [Fact]
    public async Task 令牌属于当前手机号_不会重复登录()
    {
        var (http, js, cloud) = CreateHttpService();
        js.Storage["phoneNumber"] = PhoneA;
        js.Storage["authToken"] = TokenFor(PhoneA);

        var cloudUserId = await http.GetCloudUserIdAsync();

        Assert.Equal(CloudUserIdFor(PhoneA), cloudUserId);
        Assert.Empty(cloud.LoginPhones);
    }

    [Fact]
    public async Task 令牌里读不到手机号_保持原有行为直接使用()
    {
        var (http, js, cloud) = CreateHttpService();
        var userId = Guid.NewGuid();
        js.Storage["phoneNumber"] = PhoneB;
        js.Storage["authToken"] = JwtClaimReaderTests.BuildToken($$"""{"nameid":"{{userId}}"}""");

        var cloudUserId = await http.GetCloudUserIdAsync();

        Assert.Equal(userId, cloudUserId);
        Assert.Empty(cloud.LoginPhones);
    }

    [Fact]
    public async Task 本地没有手机号时_不判断归属也不发起登录()
    {
        var (http, js, cloud) = CreateHttpService();
        js.Storage["authToken"] = TokenFor(PhoneA);

        var cloudUserId = await http.GetCloudUserIdAsync();

        Assert.Equal(CloudUserIdFor(PhoneA), cloudUserId);
        Assert.Empty(cloud.LoginPhones);
    }

    [Fact]
    public async Task 旧令牌作废后重新换取失败_宁可没有用户Id也不沿用旧令牌()
    {
        var (http, js, cloud) = CreateHttpService();
        cloud.LoginSucceeds = false;
        js.Storage["phoneNumber"] = PhoneB;
        js.Storage["authToken"] = TokenFor(PhoneA);

        var cloudUserId = await http.GetCloudUserIdAsync();

        Assert.Null(cloudUserId);
        Assert.False(js.Storage.ContainsKey("authToken"));
    }

    private static (HttpService Http, FakeLocalStorageJsRuntime Js, StubCloudHandler Cloud) CreateHttpService()
    {
        var js = new FakeLocalStorageJsRuntime();
        var cloud = new StubCloudHandler();
        var httpClient = new HttpClient(cloud) { BaseAddress = new Uri("http://cloud.test/") };
        return (new HttpService(httpClient, new AuthenticationService(js)), js, cloud);
    }

    /// <summary>
    /// 假云端：/api/auth/login 按请求里的手机号签发真实的服务端令牌；其他请求回 200 {} 并记录携带的 Bearer
    /// </summary>
    private sealed class StubCloudHandler : HttpMessageHandler
    {
        public bool LoginSucceeds { get; set; } = true;
        public List<string> LoginPhones { get; } = new();
        public List<string> BearerTokensOfBusinessRequests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri!.AbsolutePath.EndsWith("api/auth/login", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                var phone = doc.RootElement.GetProperty("phoneNumber").GetString()!;
                LoginPhones.Add(phone);

                if (!LoginSucceeds)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return Json(new { token = TokenFor(phone), userId = CloudUserIdFor(phone), phoneNumber = phone });
            }

            BearerTokensOfBusinessRequests.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            return Json(new { });
        }

        private static HttpResponseMessage Json(object payload) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
    }
}
