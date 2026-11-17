using FinanceTracker.Core.Time;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// <see cref="TimeBoundaries"/> 是页面构造"含端点结束时刻"的唯一入口：
/// 必须是"次日 00:00 减 1 个 tick"，不能留下最后一秒的缺口。
/// </summary>
public sealed class TimeBoundariesTests
{
    [Fact]
    public void 某天的结束时刻_是次日零点减一个tick()
    {
        var end = TimeBoundaries.EndOfDay(new DateTime(2026, 3, 1));

        Assert.Equal(new DateTime(2026, 3, 2).AddTicks(-1), end);
        Assert.Equal(new DateTime(2026, 3, 1, 23, 59, 59, 999).AddTicks(9999), end);
    }

    [Fact]
    public void 某天的结束时刻_忽略传入的时分秒()
    {
        var end = TimeBoundaries.EndOfDay(new DateTime(2026, 3, 1, 15, 42, 7));

        Assert.Equal(new DateTime(2026, 3, 2).AddTicks(-1), end);
    }

    [Fact]
    public void 某天的结束时刻_跨年与闰日也正确()
    {
        Assert.Equal(new DateTime(2027, 1, 1).AddTicks(-1), TimeBoundaries.EndOfDay(new DateTime(2026, 12, 31)));
        Assert.Equal(new DateTime(2028, 3, 1).AddTicks(-1), TimeBoundaries.EndOfDay(new DateTime(2028, 2, 29)));
    }

    [Theory]
    [InlineData(2026, 2, 2026, 3)]   // 平年 2 月
    [InlineData(2028, 2, 2028, 3)]   // 闰年 2 月
    [InlineData(2026, 12, 2027, 1)]  // 12 月跨年
    [InlineData(2026, 4, 2026, 5)]   // 30 天的月份
    public void 某月的结束时刻_是下月一日零点减一个tick(int year, int month, int nextYear, int nextMonth)
    {
        var end = TimeBoundaries.EndOfMonth(year, month);

        Assert.Equal(new DateTime(nextYear, nextMonth, 1).AddTicks(-1), end);
    }

    [Fact]
    public void 结束时刻之前最后一个tick属于本期_之后第一个tick不属于()
    {
        var end = TimeBoundaries.EndOfDay(new DateTime(2026, 3, 1));

        // 含端点语义（<=）：结束时刻本身属于当天，再加一个 tick 就是次日零点
        Assert.Equal(new DateTime(2026, 3, 1), end.Date);
        Assert.Equal(new DateTime(2026, 3, 2), end.AddTicks(1));
    }
}
