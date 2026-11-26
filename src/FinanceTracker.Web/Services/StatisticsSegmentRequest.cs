using FinanceTracker.Web.Models;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 「请统计 Tab 落在某个分段」的一次性请求。
/// <para>
/// 旧地址 <c>/trend</c>、<c>/annual-report</c> 在应用内被压栈打开时，由 <c>StatisticsRedirect</c> 页把自己换成统计 Tab：
/// 统计页是常驻的 Tab 根页面（可能早已创建，也可能就是这次才首次创建），需要一种不依赖「地址里带参数」的方式告诉它落在哪一段。
/// </para>
/// <para>
/// 不用地址（<c>/statistics?segment=trend</c>）传递的原因：带参数的地址会留在历史里——用户随后切到别的分段、进二级页面、再返回时，
/// 地址又回到带参数的形态，统计页若再按地址切分段，就会把用户切换后的分段冲掉。所以地址里的 <c>segment</c> 只在统计页创建时读一次
/// （宿主 302 过来的直接访问），应用内的跳转一律走这个请求。
/// </para>
/// <para>
/// 请求只生效一次（统计页取走后清空），并且只在发出后的 <see cref="Lifetime"/> 内有效——跳转没能完成时，
/// 它不会在以后某次无关的进入统计页时突然生效。Scoped：MAUI 里等同应用级单例，Blazor Server 里每条电路一份。
/// </para>
/// </summary>
public sealed class StatisticsSegmentRequest
{
    /// <summary>请求的有效期（跳转页发出请求到统计页取走，通常不到一秒）</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time;
    private readonly object _sync = new();
    private string? _segment;
    private DateTimeOffset _requestedAt;

    /// <param name="time">时间来源；不传（依赖注入里没有注册）就用系统时间，测试里传假时钟</param>
    public StatisticsSegmentRequest(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>登记一次请求；后一次覆盖前一次，不认识的分段按月度处理</summary>
    public void Request(string? segment)
    {
        lock (_sync)
        {
            _segment = StatisticsPresentation.NormalizeSegment(segment);
            _requestedAt = _time.GetUtcNow();
        }
    }

    /// <summary>取走并清空待处理的请求；没有请求、或请求已经过期时返回 null</summary>
    public string? Take()
    {
        lock (_sync)
        {
            var segment = _segment;
            _segment = null;
            return segment is not null && _time.GetUtcNow() - _requestedAt <= Lifetime ? segment : null;
        }
    }
}
