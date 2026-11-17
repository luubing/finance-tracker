namespace FinanceTracker.Web.Services;

/// <summary>
/// 云端可用性的"短期记忆"：避免云端连不上时，每次打开页面都白等一次连接超时。
/// <para>
/// 页面加载（账单 / 账本 / 记一笔 / 成员管理）都会顺带联网刷新账本成员缓存，云端不可用时每次刷新都要等
/// 连接失败（部分环境下好几秒）甚至 HttpClient 默认的 100 秒超时，页面就被一次次拖住。这里做两件事：
/// </para>
/// <list type="bullet">
/// <item><see cref="RefreshTimeout"/>：读路径的联网刷新只给很短的期限，超时就真正取消请求、改读本地缓存</item>
/// <item>退避：刷新失败后的 <see cref="DefaultBackoff"/> 之内不再联网，页面直接读本地缓存</item>
/// </list>
/// 用户主动发起的云端操作（邀请 / 移除成员 / 响应邀请…）及其成功后的刷新不受这两条限制，且成功会清除退避。
/// <para>
/// 作为单例共享：它描述的是"这台设备 / 这个服务进程到云端的连通性"，与具体用户无关；多线程 / 多个 Blazor 电路并发读写安全。
/// </para>
/// </summary>
public sealed class CloudAvailability
{
    /// <summary>读路径联网刷新的期限：超过就放弃，直接使用本地缓存（换取令牌 + 拉取成员关系合计）</summary>
    public static readonly TimeSpan DefaultRefreshTimeout = TimeSpan.FromSeconds(3);

    /// <summary>刷新失败后，在这段时间内不再联网重试</summary>
    public static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _backoff;

    // 退避截止时间（UTC ticks）；0 表示当前没有退避。单例被多线程读写，用 Volatile 保证可见性
    private long _backoffUntilUtcTicks;

    /// <summary>使用默认期限（3 秒）与默认退避（30 秒）</summary>
    public CloudAvailability()
        : this(TimeProvider.System, DefaultRefreshTimeout, DefaultBackoff)
    {
    }

    public CloudAvailability(TimeProvider timeProvider, TimeSpan refreshTimeout, TimeSpan backoff)
    {
        _timeProvider = timeProvider;
        RefreshTimeout = refreshTimeout;
        _backoff = backoff;
    }

    /// <summary>读路径联网刷新的期限</summary>
    public TimeSpan RefreshTimeout { get; }

    /// <summary>
    /// 新建一个读路径联网刷新用的期限令牌源：到 <see cref="RefreshTimeout"/> 时自动取消（计时用本实例的 <see cref="TimeProvider"/>）。
    /// 调用方负责释放。
    /// </summary>
    public CancellationTokenSource CreateRefreshTimeout() => new(RefreshTimeout, _timeProvider);

    /// <summary>当前是否处于退避期（刚刚失败过，暂时不要联网）</summary>
    public bool IsInBackoff =>
        _timeProvider.GetUtcNow().UtcTicks < Volatile.Read(ref _backoffUntilUtcTicks);

    /// <summary>云端不可用 / 超时：开始（或重新计时）退避</summary>
    public void ReportFailure() =>
        Volatile.Write(ref _backoffUntilUtcTicks, (_timeProvider.GetUtcNow() + _backoff).UtcTicks);

    /// <summary>云端可达：清除退避</summary>
    public void ReportSuccess() =>
        Volatile.Write(ref _backoffUntilUtcTicks, 0);
}
