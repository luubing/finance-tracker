using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Web.Server;

/// <summary>
/// E2E 专用的「自动识别」假实现——只有开发环境（Development）下配置了 <c>E2E:FakeCapture=true</c>（环境变量 <c>E2E__FakeCapture=true</c>）时才注册，
/// tests/e2e/server.ps1 启动时会打开它；平时 <c>dotnet run</c> 仍是空实现，行为与改造前完全一致。
/// <para>
/// 浏览器里没有「通知使用权」，也读不到短信，空实现永远是「未开启 + 没有短信」，
/// 待确认账单页的「均已开启」状态条、授权流程与短信识别页的读取流程就无从验收。
/// 假实现把权限状态与「读到的短信」放在进程内，由 E2E 经 <c>POST /__e2e/capture</c> 设置、<c>GET /__e2e/capture</c> 观察。
/// 状态是整个进程共享的（E2E 脚本串行执行）：用到它的脚本开始前与结束后各 <c>reset</c> 一次，免得互相影响。
/// </para>
/// </summary>
public static class E2ECapture
{
    /// <summary>开关的配置键（环境变量写作 E2E__FakeCapture）</summary>
    public const string ConfigKey = "E2E:FakeCapture";

    /// <summary>设置 / 查看假实现状态的地址</summary>
    public const string Route = "/__e2e/capture";

    /// <summary>
    /// 是否启用假实现：必须是开发环境（Development）并且配置了开关。
    /// 这些地址没有任何认证、能随意改写进程内状态，所以只在开发环境开放——即便生产环境误带了 <c>E2E__FakeCapture</c> 也不会生效。
    /// （tests/e2e/server.ps1 用 <c>--launch-profile http</c> 启动，环境就是 Development。）
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsDevelopment()
        && string.Equals(configuration[ConfigKey], "true", StringComparison.OrdinalIgnoreCase);

    public static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<E2ECaptureState>();
        services.AddSingleton<ISmsService, E2ESmsService>();
        services.AddSingleton<INotificationListenerPermissionService, E2ENotificationListenerPermissionService>();
    }

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet(Route, (E2ECaptureState state) => Results.Json(state.Snapshot()));
        app.MapPost(Route, (E2ECaptureRequest request, E2ECaptureState state) => Results.Json(state.Apply(request)));
    }
}

/// <summary>E2E 设置假实现的请求：只应用给出的字段；<c>Reset</c> 先把一切恢复成「都没开启、没有短信」。</summary>
public sealed record E2ECaptureRequest(
    bool? Reset = null,
    bool? Notification = null,
    bool? Sms = null,
    bool? SmsGrantable = null,
    List<E2ECaptureSms>? Messages = null);

/// <summary>E2E 设置的一条短信（Date 是设备本地时间的墙上时间）</summary>
public sealed record E2ECaptureSms(long Id, string Address, string Body, DateTime Date);

/// <summary>假实现的当前状态与被调用的次数（E2E 据此断言「点了去开启 / 去授权」真的走到了平台服务）</summary>
public sealed record E2ECaptureSnapshot(
    bool Notification,
    bool Sms,
    bool SmsGrantable,
    int MessageCount,
    int NotificationSettingsOpened,
    int SmsPermissionRequests,
    int SmsReads,
    DateTime? LastSmsReadFrom);

public sealed class E2ECaptureState
{
    private readonly object _gate = new();
    private bool _notification;
    private bool _sms;
    private bool _smsGrantable = true;
    private List<E2ECaptureSms> _messages = [];
    private int _notificationSettingsOpened;
    private int _smsPermissionRequests;
    private int _smsReads;
    private DateTime? _lastSmsReadFrom;

    public bool NotificationEnabled
    {
        get { lock (_gate) { return _notification; } }
    }

    public bool SmsEnabled
    {
        get { lock (_gate) { return _sms; } }
    }

    public void NotificationSettingsOpened()
    {
        lock (_gate)
        {
            _notificationSettingsOpened++;
        }
    }

    /// <summary>用户在系统弹窗里点了「允许」（可授予）或「拒绝」：授予后短信权限变为已开启</summary>
    public bool RequestSmsPermission()
    {
        lock (_gate)
        {
            _smsPermissionRequests++;
            if (_smsGrantable)
            {
                _sms = true;
            }

            return _sms;
        }
    }

    public List<SmsMessage> ReadSms(DateTime fromDate)
    {
        lock (_gate)
        {
            _smsReads++;
            _lastSmsReadFrom = fromDate;

            return _messages
                .Where(m => m.Date >= fromDate)
                .OrderByDescending(m => m.Date)
                .Select(m => new SmsMessage { Id = m.Id, Address = m.Address, Body = m.Body, Date = m.Date })
                .ToList();
        }
    }

    public E2ECaptureSnapshot Apply(E2ECaptureRequest request)
    {
        lock (_gate)
        {
            if (request.Reset == true)
            {
                _notification = false;
                _sms = false;
                _smsGrantable = true;
                _messages = [];
                _notificationSettingsOpened = 0;
                _smsPermissionRequests = 0;
                _smsReads = 0;
                _lastSmsReadFrom = null;
            }

            _notification = request.Notification ?? _notification;
            _sms = request.Sms ?? _sms;
            _smsGrantable = request.SmsGrantable ?? _smsGrantable;
            _messages = request.Messages ?? _messages;

            return SnapshotLocked();
        }
    }

    public E2ECaptureSnapshot Snapshot()
    {
        lock (_gate)
        {
            return SnapshotLocked();
        }
    }

    private E2ECaptureSnapshot SnapshotLocked() =>
        new(_notification, _sms, _smsGrantable, _messages.Count, _notificationSettingsOpened, _smsPermissionRequests, _smsReads, _lastSmsReadFrom);
}

public sealed class E2ESmsService(E2ECaptureState state) : ISmsService
{
    public Task<bool> HasPermissionAsync() => Task.FromResult(state.SmsEnabled);

    public Task<bool> RequestPermissionAsync() => Task.FromResult(state.RequestSmsPermission());

    public Task<List<SmsMessage>> ReadPaymentSmsAsync(DateTime fromDate) => Task.FromResult(state.ReadSms(fromDate));
}

public sealed class E2ENotificationListenerPermissionService(E2ECaptureState state) : INotificationListenerPermissionService
{
    public Task<bool> HasPermissionAsync() => Task.FromResult(state.NotificationEnabled);

    public Task OpenSettingsAsync()
    {
        state.NotificationSettingsOpened();
        return Task.CompletedTask;
    }
}
