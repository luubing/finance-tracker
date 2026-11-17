using System.Net;
using System.Text;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 云端连不上 / 很慢 / 页面中途被销毁时，共享账本的读取路径（账单、账本、记一笔页面加载都会经过它）的行为：
/// 1. 照常返回本地缓存、不抛异常；
/// 2. 不做多余的连接尝试——每次连接失败在部分环境下要等好几秒（Windows 上被拒绝的回环连接约 2 秒，
///    localhost 双栈约 4 秒），页面等待时间 = 失败次数 × 单次等待；
/// 3. 预期内的中断只记调试日志，真正的异常才记警告（否则用户一翻页就在日志里刷警告和堆栈）；
/// 4. 联网刷新只给很短的期限（超时真正取消请求），失败后一小段时间内不再联网（退避）；
///    用户主动发起的云端操作及其后的刷新不受这两条限制（见 <see cref="CloudAvailability"/>）。
///
/// 涉及"期限 / 退避过期"的测试一律用 <see cref="AdjustableTimeProvider"/> 手动拨时钟，不依赖真实时间，
/// 所以不会因为机器慢而偶发失败；<c>WaitAsync</c> 只是兜底（行为不对时报错而不是挂死）。
/// </summary>
public sealed class HttpLedgerMemberServiceOfflineTests
{
    private const string Phone = "13800138000";
    private const string LoginPath = "/api/auth/login";
    private const string PullPath = "/api/sync/ledgermembers/pull";

    private static readonly DateTimeOffset Start = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 这组测试里的读路径期限。故意设得比 <see cref="Guard"/> 长得多：期限只可能由拨动假时钟触发，
    /// 如果实现偷偷用了真实时钟（哪怕真实计时器也会到期），测试会在 <see cref="Guard"/> 内等不到而失败。
    /// 生产默认的 3 秒由 <c>CloudAvailabilityTests</c> 单独锁定。
    /// </summary>
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task 云端连不上且本地没有云端令牌_刷新成员缓存只尝试连接两次()
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var handler = new ThrowingHandler(() => new HttpRequestException("connection refused"));
        var service = CreateService(db, handler, NullLogger<HttpLedgerMemberService>.Instance);

        var invitations = await service.GetPendingInvitationsAsync(db.User.Id);

        Assert.Empty(invitations);
        // 一次"换取云端令牌"（登录）+ 一次"拉取成员关系"。拉取之前不能为了读取云端用户 Id 再单独多换一次令牌
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(LoginPath, handler.Requests[0]);
        Assert.Equal(PullPath, handler.Requests[1]);
    }

    [Theory]
    [InlineData(nameof(HttpRequestException))]       // 连接被拒绝 / 无网络
    [InlineData(nameof(TaskCanceledException))]      // HttpClient 请求超时
    [InlineData(nameof(ObjectDisposedException))]    // 页面（电路）在刷新途中被销毁，HttpClient/DbContext 已释放
    public async Task 云端不可用或请求被中断_只记调试日志不记警告(string exceptionKind)
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var handler = new ThrowingHandler(() => CreateException(exceptionKind));
        var logger = new ListLogger<HttpLedgerMemberService>();
        var service = CreateService(db, handler, logger);

        var shared = await service.GetSharedLedgersAsync(db.User.Id);

        Assert.Empty(shared);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task 云端出现意外错误_记警告并继续使用本地缓存()
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var handler = new ThrowingHandler(() => new InvalidOperationException("boom"));
        var logger = new ListLogger<HttpLedgerMemberService>();
        var service = CreateService(db, handler, logger);

        var shared = await service.GetSharedLedgersAsync(db.User.Id);

        Assert.Empty(shared);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.IsType<InvalidOperationException>(warning.Exception);
    }

    [Fact]
    public async Task 读路径刷新超过期限_真正取消云端请求并改读本地缓存()
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var clock = new AdjustableTimeProvider(Start);
        var availability = NewAvailability(clock);
        var pullStarted = NewSignal();
        var pullCancelled = false;
        var handler = new ScriptedHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == LoginPath)
            {
                return Json("{}", HttpStatusCode.NotFound);     // 换不到令牌，与本测试无关
            }

            pullStarted.TrySetResult();
            // 云端不响应：只有被取消才会结束（30 秒只是兜底，正常不会走到）
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                pullCancelled = true;
                throw;
            }

            throw new InvalidOperationException("请求没有被取消");
        });
        var logger = new ListLogger<HttpLedgerMemberService>();
        var service = CreateService(db, handler, logger, availability);

        var refresh = service.GetSharedLedgersAsync(db.User.Id);
        await pullStarted.Task.WaitAsync(Guard);                 // 拉取请求已经发出，正在等云端应答

        clock.Advance(RefreshTimeout - TimeSpan.FromTicks(1));
        Assert.False(refresh.IsCompleted);                       // 还差一个刻度：仍在等，不能提前放弃
        Assert.False(pullCancelled);

        clock.Advance(TimeSpan.FromTicks(1));                    // 满期限
        var shared = await refresh.WaitAsync(Guard);

        Assert.Empty(shared);
        Assert.True(pullCancelled, "超时必须真正取消云端请求，而不是丢下它继续在后台跑");
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.True(availability.IsInBackoff);
    }

    [Fact]
    public async Task 换取令牌那一步就超过期限_整次刷新立刻结束_不再发出拉取请求()
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var clock = new AdjustableTimeProvider(Start);
        var availability = NewAvailability(clock);
        var loginStarted = NewSignal();
        var handler = new ScriptedHandler(async (request, ct) =>
        {
            loginStarted.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);      // 云端对任何请求都不响应
            throw new InvalidOperationException("请求没有被取消");
        });
        var service = CreateService(db, handler, NullLogger<HttpLedgerMemberService>.Instance, availability);

        var refresh = service.GetPendingInvitationsAsync(db.User.Id);
        await loginStarted.Task.WaitAsync(Guard);

        clock.Advance(RefreshTimeout);
        var invitations = await refresh.WaitAsync(Guard);

        Assert.Empty(invitations);
        // 期限到时正卡在"换取令牌"上：取消必须直接结束整次刷新，不能被当成"换取失败"吞掉、再接着发拉取请求
        Assert.Equal(new[] { LoginPath }, handler.Requests);
        Assert.True(availability.IsInBackoff);
    }

    [Fact]
    public async Task 联网刷新失败后进入退避_期间读路径不再联网_退避结束后恢复()
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var clock = new AdjustableTimeProvider(Start);
        var availability = NewAvailability(clock);
        var handler = new ThrowingHandler(() => new HttpRequestException("connection refused"));
        var logger = new ListLogger<HttpLedgerMemberService>();
        var service = CreateService(db, handler, logger, availability);

        await service.GetSharedLedgersAsync(db.User.Id);
        Assert.Equal(2, handler.Requests.Count);     // 第一次：换取令牌 + 拉取，都失败

        // 退避期内，三条读路径都直接读本地缓存，不再产生任何网络请求
        await service.GetSharedLedgersAsync(db.User.Id);
        await service.GetPendingInvitationsAsync(db.User.Id);
        await service.GetMembersAsync(Guid.NewGuid(), db.User.Id);
        clock.Advance(TimeSpan.FromSeconds(29));
        await service.GetSharedLedgersAsync(db.User.Id);
        Assert.Equal(2, handler.Requests.Count);

        clock.Advance(TimeSpan.FromSeconds(1));      // 退避满 30 秒
        await service.GetSharedLedgersAsync(db.User.Id);
        Assert.Equal(4, handler.Requests.Count);     // 恢复联网（这次依旧连不上，于是重新退避）
        Assert.True(availability.IsInBackoff);

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData(nameof(HttpLedgerMemberService.RespondAsync))]
    [InlineData(nameof(HttpLedgerMemberService.RemoveMemberAsync))]
    [InlineData(nameof(HttpLedgerMemberService.ChangeRoleAsync))]
    [InlineData(nameof(HttpLedgerMemberService.TransferOwnershipAsync))]
    [InlineData(nameof(HttpLedgerMemberService.ExitAsync))]
    public async Task 退避期内用户主动操作成功_其后的刷新照常联网并清除退避(string operation)
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var cloudDown = true;
        var handler = new ScriptedHandler((request, ct) =>
        {
            if (cloudDown)
            {
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));
            }

            return Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                LoginPath => Json("""{"token":"test-token"}"""),
                PullPath => Json("""{"members":[]}"""),
                _ => Json("{}")                                  // 响应邀请
            });
        });
        var availability = NewAvailability(new AdjustableTimeProvider(Start));
        var service = CreateService(db, handler, NullLogger<HttpLedgerMemberService>.Instance, availability);

        await service.GetSharedLedgersAsync(db.User.Id);         // 云端挂着：读路径失败，进入退避
        Assert.True(availability.IsInBackoff);

        cloudDown = false;                                       // 云端恢复了，但退避还没到期
        handler.Requests.Clear();
        await RunOperationAsync(service, operation, db.User.Id);

        Assert.Contains(PullPath, handler.Requests);             // 操作后的刷新没有被退避跳过
        Assert.False(availability.IsInBackoff);                  // 操作成功说明云端可达：退避被清除

        handler.Requests.Clear();
        await service.GetSharedLedgersAsync(db.User.Id);         // 之后的读路径照常联网
        Assert.Contains(PullPath, handler.Requests);
    }

    [Fact]
    public async Task 退避期内用户主动操作成功_即使其后的刷新失败也清除退避()
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var cloudDown = true;
        var handler = new ScriptedHandler((request, ct) =>
        {
            if (cloudDown)
            {
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"));
            }

            return Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                LoginPath => Json("""{"token":"test-token"}"""),
                PullPath => Json("""{"message":"boom"}""", HttpStatusCode.InternalServerError),   // 操作成功了，但随后的刷新失败
                _ => Json("{}")
            });
        });
        var availability = NewAvailability(new AdjustableTimeProvider(Start));
        var logger = new ListLogger<HttpLedgerMemberService>();
        var service = CreateService(db, handler, logger, availability);

        await service.GetSharedLedgersAsync(db.User.Id);
        Assert.True(availability.IsInBackoff);

        cloudDown = false;
        await service.RespondAsync(Guid.NewGuid(), db.User.Id, accept: true);

        // 云端操作成功就说明云端可达，与随后的刷新结果无关：否则退避会一直挂着，之后的读路径持续跳过联网
        Assert.False(availability.IsInBackoff);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(nameof(HttpLedgerMemberService.RespondAsync))]
    [InlineData(nameof(HttpLedgerMemberService.RemoveMemberAsync))]
    [InlineData(nameof(HttpLedgerMemberService.ChangeRoleAsync))]
    [InlineData(nameof(HttpLedgerMemberService.TransferOwnershipAsync))]
    [InlineData(nameof(HttpLedgerMemberService.ExitAsync))]
    public async Task 用户主动操作后的刷新不设读路径那样的期限(string operation)
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var clock = new AdjustableTimeProvider(Start);
        var availability = NewAvailability(clock);
        var pullStarted = NewSignal();
        var releasePull = NewSignal();
        var pullCancelled = false;
        var handler = new ScriptedHandler(async (request, ct) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case LoginPath:
                    return Json("""{"token":"test-token"}""");
                case PullPath:
                    pullStarted.TrySetResult();
                    try
                    {
                        await releasePull.Task.WaitAsync(ct);    // 由测试决定云端什么时候应答；被取消则抛出
                    }
                    catch (OperationCanceledException)
                    {
                        pullCancelled = true;
                        throw;
                    }

                    return Json("""{"members":[]}""");
                default:
                    return Json("{}");                           // 云端操作本身
            }
        });
        var service = CreateService(db, handler, NullLogger<HttpLedgerMemberService>.Instance, availability);

        var action = RunOperationAsync(service, operation, db.User.Id);
        await pullStarted.Task.WaitAsync(Guard);                 // 操作成功，正在等刷新的拉取应答

        clock.Advance(TimeSpan.FromHours(1));                    // 远远超过读路径的期限
        Assert.False(pullCancelled, "操作成功后的刷新关系到这次操作有没有反映到本地，不能套用读路径的短期限");
        Assert.False(action.IsCompleted);

        releasePull.SetResult();
        await action.WaitAsync(Guard);

        Assert.False(pullCancelled);
        Assert.False(availability.IsInBackoff);
    }

    [Theory]
    [InlineData(nameof(HttpRequestException), true)]          // 连接被拒绝 / 无网络：云端确实不可用 → 退避
    [InlineData(nameof(TaskCanceledException), false)]        // 不是我们设的期限到了（HttpClient 自己的超时 / 被销毁）：不能证明云端不可用
    [InlineData(nameof(ObjectDisposedException), false)]      // 页面（电路）被销毁：与云端连通性无关，且不能让一个用户关页面就拖累所有人
    [InlineData(nameof(InvalidOperationException), false)]    // 意外错误（如本地写库失败）：与云端连通性无关
    public async Task 只有云端确实不可用才进入退避(string exceptionKind, bool expectedBackoff)
    {
        await using var db = await SqliteTestDatabase.CreateAsync(Phone);
        var handler = new ThrowingHandler(() => CreateException(exceptionKind));
        var availability = NewAvailability(new AdjustableTimeProvider(Start));
        var service = CreateService(db, handler, NullLogger<HttpLedgerMemberService>.Instance, availability);

        await service.GetSharedLedgersAsync(db.User.Id);

        Assert.Equal(expectedBackoff, availability.IsInBackoff);
    }

    private static CloudAvailability NewAvailability(AdjustableTimeProvider clock) =>
        new(clock, RefreshTimeout, TimeSpan.FromSeconds(30));

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Exception CreateException(string kind) => kind switch
    {
        nameof(HttpRequestException) => new HttpRequestException("connection refused"),
        nameof(TaskCanceledException) => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
        nameof(ObjectDisposedException) => new ObjectDisposedException("System.Net.Http.HttpClient"),
        nameof(InvalidOperationException) => new InvalidOperationException("boom"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>执行一个"先调云端接口、成功后再刷新本地成员缓存"的用户主动操作（云端替身对这些接口一律应答 200 {}）</summary>
    private static Task RunOperationAsync(HttpLedgerMemberService service, string operation, Guid userId)
    {
        var ledgerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        return operation switch
        {
            nameof(HttpLedgerMemberService.RespondAsync) => service.RespondAsync(memberId, userId, accept: true),
            nameof(HttpLedgerMemberService.RemoveMemberAsync) => service.RemoveMemberAsync(ledgerId, memberId, userId),
            nameof(HttpLedgerMemberService.ChangeRoleAsync) => service.ChangeRoleAsync(ledgerId, memberId, LedgerMemberRole.Editor, userId),
            nameof(HttpLedgerMemberService.TransferOwnershipAsync) => service.TransferOwnershipAsync(ledgerId, userId, Guid.NewGuid()),
            nameof(HttpLedgerMemberService.ExitAsync) => service.ExitAsync(ledgerId, userId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
    }

    /// <summary>
    /// 真实的 HttpLedgerMemberService + SyncService + HttpCloudSyncClient + HttpService，云端 HTTP 换成替身。
    /// 已用手机号登录但本地没有云端令牌：每次访问云端前都会尝试换取。
    /// </summary>
    private static HttpLedgerMemberService CreateService(
        SqliteTestDatabase db,
        HttpMessageHandler handler,
        ILogger<HttpLedgerMemberService> logger,
        CloudAvailability? availability = null)
    {
        var js = new FakeLocalStorageJsRuntime();
        js.Storage["phoneNumber"] = Phone;
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://cloud.test/") };
        var http = new HttpService(httpClient, new AuthenticationService(js));
        var sync = new SyncService(
            db.Context,
            new AlwaysOnlineNetworkService(),
            new HttpCloudSyncClient(http, "http://cloud.test"),
            NullLogger<SyncService>.Instance);
        return new HttpLedgerMemberService(http, db.Context, sync, availability ?? new CloudAvailability(), logger);
    }

    /// <summary>记录每个请求的路径，并让它抛出指定异常</summary>
    private sealed class ThrowingHandler(Func<Exception> createException) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromException<HttpResponseMessage>(createException());
        }
    }

    /// <summary>记录每个请求的路径，应答由测试自己按路径 / 状态编排（可以拖延、可以观察取消）</summary>
    private sealed class ScriptedHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            return respond(request, cancellationToken);
        }
    }
}
