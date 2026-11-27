using System.Net;
using System.Text;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 云端启用手机号白名单后，名单外的手机号换取云端令牌会被 403 拒绝。
///
/// 事故场景（修复前）：HttpService 把"换取令牌失败"一律静默放过，业务请求于是不带令牌发出去，云端回 401，
/// 而 HttpService 对 401 的处理是登出本地账号——用户只会看到误导性的"登录已过期，请重新登录"，
/// 登录后一同步又被登出，永远不知道真正的原因是"该手机号未获云端使用许可"。
/// 权限拒绝（403）必须与登录过期（401）严格区分（见 ForbiddenAccessException 与架构决策 7）：
/// 抛出带提示的 ForbiddenAccessException，不发业务请求，本地登录状态原样保留。
/// 其他失败（离线、云端 5xx……）保持原有的静默放行。
/// </summary>
public sealed class CloudLoginForbiddenTests
{
    private const string Phone = "13800138000";
    private const string ServerMessage = "该手机号未获当前云端服务的使用许可，请联系服务器管理员";

    private sealed class StubCloud : HttpMessageHandler
    {
        public HttpStatusCode LoginStatus { get; set; } = HttpStatusCode.Forbidden;
        public string LoginBody { get; set; } = $$"""{"message":"{{ServerMessage}}"}""";

        /// <summary>业务请求的响应码：默认 401，模拟真实云端对"没带令牌的请求"的回应</summary>
        public HttpStatusCode BusinessStatus { get; set; } = HttpStatusCode.Unauthorized;

        public int LoginCalls { get; private set; }
        public int BusinessCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 与真实的 HttpMessageHandler 一致：令牌已取消就不再发出请求
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Method == HttpMethod.Post &&
                request.RequestUri!.AbsolutePath.EndsWith("api/auth/login", StringComparison.Ordinal))
            {
                LoginCalls++;
                return Task.FromResult(Respond(LoginStatus, LoginBody));
            }

            BusinessCalls++;
            return Task.FromResult(Respond(BusinessStatus, "{}"));
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (HttpService Http, FakeLocalStorageJsRuntime Js, StubCloud Cloud) Create()
    {
        var js = new FakeLocalStorageJsRuntime();
        js.Storage["phoneNumber"] = Phone;
        js.Storage["userId"] = Guid.NewGuid().ToString();

        var cloud = new StubCloud();
        var httpClient = new HttpClient(cloud) { BaseAddress = new Uri("http://cloud.test/") };
        return (new HttpService(httpClient, new AuthenticationService(js)), js, cloud);
    }

    [Fact]
    public async Task 登录被403拒绝_业务请求抛出权限拒绝并带上云端的提示_且不发出业务请求()
    {
        var (http, _, cloud) = Create();

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => http.GetAsync<object>("api/bills"));

        Assert.Equal(ServerMessage, ex.Message);
        Assert.Equal(1, cloud.LoginCalls);
        Assert.Equal(0, cloud.BusinessCalls);
    }

    [Fact]
    public async Task 登录被403拒绝_本地登录状态原样保留_不会被登出()
    {
        var (http, js, cloud) = Create();
        var userId = js.Storage["userId"];

        // 业务请求即使真的发出去也会 401——修复前正是这条 401 把用户登出的
        cloud.BusinessStatus = HttpStatusCode.Unauthorized;

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => http.PostAsync<object>("api/sync/push", new { }));

        Assert.Equal(Phone, js.Storage["phoneNumber"]);
        Assert.Equal(userId, js.Storage["userId"]);
        Assert.False(js.Storage.ContainsKey("authToken"));
    }

    [Fact]
    public async Task 登录被403拒绝_读取云端用户Id同样抛出权限拒绝()
    {
        var (http, _, _) = Create();

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => http.GetCloudUserIdAsync());

        Assert.Equal(ServerMessage, ex.Message);
    }

    [Fact]
    public async Task 登录被403拒绝_响应体用error字段时也能取到提示()
    {
        var (http, _, cloud) = Create();
        cloud.LoginBody = """{"error":"来自全局异常中间件的提示","requestId":"x"}""";

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => http.GetAsync<object>("api/bills"));

        Assert.Equal("来自全局异常中间件的提示", ex.Message);
    }

    [Theory]
    [InlineData("<html><body>403 Forbidden</body></html>")]   // 反向代理 / WAF 的拦截页
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"message":""}""")]
    public async Task 登录被403拒绝但响应体没有可用提示_使用默认提示(string body)
    {
        var (http, _, cloud) = Create();
        cloud.LoginBody = body;

        var ex = await Assert.ThrowsAsync<ForbiddenAccessException>(() => http.GetAsync<object>("api/bills"));

        Assert.Contains("403", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task 其他登录失败保持原有行为_静默放行由后续请求报错(HttpStatusCode loginStatus)
    {
        var (http, _, cloud) = Create();
        cloud.LoginStatus = loginStatus;
        cloud.BusinessStatus = HttpStatusCode.OK;

        Assert.Null(await http.GetCloudUserIdAsync());
        await http.GetAsync<object>("api/bills");

        Assert.Equal(1, cloud.BusinessCalls);
    }

    [Fact]
    public async Task 调用方已取消_按取消向上抛而不是被当成权限拒绝()
    {
        var (http, _, _) = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // 已取消的令牌：必须按取消向上抛（TaskCanceledException 派生自 OperationCanceledException），而不是被当成权限拒绝
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.GetAsync<object>("api/bills", cts.Token));
    }
}
