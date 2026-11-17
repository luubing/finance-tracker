using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 云端可用性的"短期记忆"：失败后一段时间内标记为退避，到期或成功后恢复。
/// 它决定了读路径联网刷新的期限与退避时长，数值是和用户确认过的策略（3 秒 / 30 秒），测试里把它们钉住。
/// </summary>
public sealed class CloudAvailabilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 默认策略_联网刷新期限3秒_失败后退避30秒()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), CloudAvailability.DefaultRefreshTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), CloudAvailability.DefaultBackoff);
        Assert.Equal(CloudAvailability.DefaultRefreshTimeout, new CloudAvailability().RefreshTimeout);
    }

    [Fact]
    public void 初始状态不在退避期()
    {
        var availability = new CloudAvailability(new AdjustableTimeProvider(Start), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));

        Assert.False(availability.IsInBackoff);
    }

    [Fact]
    public void 失败后进入退避_恰好到期时恢复()
    {
        var clock = new AdjustableTimeProvider(Start);
        var availability = new CloudAvailability(clock, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));

        availability.ReportFailure();
        Assert.True(availability.IsInBackoff);

        clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
        Assert.True(availability.IsInBackoff);      // 还差一个刻度，仍在退避

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.False(availability.IsInBackoff);     // 恰好满 30 秒，退避结束
    }

    [Fact]
    public void 再次失败_从最近一次失败重新计时()
    {
        var clock = new AdjustableTimeProvider(Start);
        var availability = new CloudAvailability(clock, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));

        availability.ReportFailure();
        clock.Advance(TimeSpan.FromSeconds(20));
        availability.ReportFailure();               // 第 20 秒再次失败，退避顺延到第 50 秒

        clock.Advance(TimeSpan.FromSeconds(29));    // 第 49 秒
        Assert.True(availability.IsInBackoff);

        clock.Advance(TimeSpan.FromSeconds(1));     // 第 50 秒
        Assert.False(availability.IsInBackoff);
    }

    [Fact]
    public void 成功后立刻清除退避()
    {
        var availability = new CloudAvailability(new AdjustableTimeProvider(Start), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));
        availability.ReportFailure();

        availability.ReportSuccess();

        Assert.False(availability.IsInBackoff);
    }

    [Fact]
    public void 刷新期限由注入的时钟驱动_恰好满期限时取消()
    {
        // 期限取 5 分钟：只有拨动假时钟能让它到期，真实时间走不到，所以能区分"用了注入的时钟"和"偷偷用了真实计时器"
        var clock = new AdjustableTimeProvider(Start);
        var availability = new CloudAvailability(clock, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));

        using var timeout = availability.CreateRefreshTimeout();
        Assert.False(timeout.IsCancellationRequested);

        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        Assert.False(timeout.IsCancellationRequested);      // 还差一个刻度

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(timeout.IsCancellationRequested);       // 恰好满期限
    }

    [Fact]
    public void 每次刷新的期限从各自开始的时刻计时()
    {
        var clock = new AdjustableTimeProvider(Start);
        var availability = new CloudAvailability(clock, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));

        using var first = availability.CreateRefreshTimeout();
        clock.Advance(TimeSpan.FromMinutes(4));
        using var second = availability.CreateRefreshTimeout();     // 第 4 分钟才开始的另一次刷新

        clock.Advance(TimeSpan.FromMinutes(1));                      // 第 5 分钟：第一次满期限，第二次才过 1 分钟
        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }
}
