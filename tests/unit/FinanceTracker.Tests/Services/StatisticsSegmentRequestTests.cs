using FinanceTracker.Web.Models;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「请统计 Tab 落在某个分段」的一次性请求：旧地址的跳转页登记，统计页取走；只生效一次、过期作废
/// </summary>
public class StatisticsSegmentRequestTests
{
    /// <summary>手动拨动的时钟</summary>
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void 没有登记过请求_取走得到空()
    {
        Assert.Null(new StatisticsSegmentRequest().Take());
    }

    [Theory]
    [InlineData(StatisticsPresentation.SegmentTrend)]
    [InlineData(StatisticsPresentation.SegmentAnnual)]
    [InlineData(StatisticsPresentation.SegmentMonthly)]
    public void 登记的分段被取走一次_再取就没有了(string segment)
    {
        var request = new StatisticsSegmentRequest();

        request.Request(segment);

        Assert.Equal(segment, request.Take());
        Assert.Null(request.Take());
    }

    [Fact]
    public void 后一次请求覆盖前一次()
    {
        var request = new StatisticsSegmentRequest();

        request.Request(StatisticsPresentation.SegmentTrend);
        request.Request(StatisticsPresentation.SegmentAnnual);

        Assert.Equal(StatisticsPresentation.SegmentAnnual, request.Take());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("weekly")]
    public void 不认识的分段按月度处理(string? segment)
    {
        var request = new StatisticsSegmentRequest();

        request.Request(segment);

        Assert.Equal(StatisticsPresentation.SegmentMonthly, request.Take());
    }

    [Fact]
    public void 有效期内取得到_刚好到期也算有效()
    {
        var clock = new FakeClock();
        var request = new StatisticsSegmentRequest(clock);

        request.Request(StatisticsPresentation.SegmentTrend);
        clock.Now += StatisticsSegmentRequest.Lifetime;

        Assert.Equal(StatisticsPresentation.SegmentTrend, request.Take());
    }

    [Fact]
    public void 过期的请求取不到_也不会留到以后()
    {
        var clock = new FakeClock();
        var request = new StatisticsSegmentRequest(clock);

        request.Request(StatisticsPresentation.SegmentTrend);
        clock.Now += StatisticsSegmentRequest.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Null(request.Take());

        // 过期那一次已经把它清掉：时间再往回拨（或者别的请求）也不会让它复活
        clock.Now -= TimeSpan.FromSeconds(30);
        Assert.Null(request.Take());
    }

    [Fact]
    public void 过期之后重新登记_按新的时间算()
    {
        var clock = new FakeClock();
        var request = new StatisticsSegmentRequest(clock);

        request.Request(StatisticsPresentation.SegmentTrend);
        clock.Now += TimeSpan.FromMinutes(5);
        request.Request(StatisticsPresentation.SegmentAnnual);
        clock.Now += TimeSpan.FromSeconds(1);

        Assert.Equal(StatisticsPresentation.SegmentAnnual, request.Take());
    }
}
