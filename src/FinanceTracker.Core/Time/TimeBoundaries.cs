namespace FinanceTracker.Core.Time;

/// <summary>
/// 应用时区日历上"含端点的结束时刻"的统一算法（详见 docs/adr/0005-time-zone-handling.md）。
/// <para>
/// <see cref="Services.BillService"/> 的账单列表/计数筛选与 <see cref="Interfaces.IStatisticsService.GetTrendDataAsync"/>
/// 的 <c>endDate</c> 都是"含端点的结束时刻"（比较用 <c>&lt;=</c>），而页面手里只有"某一天/某个月"。
/// 调用方必须传"次日 00:00 减 1 个 tick"，不能减 1 秒：减 1 秒会留下 [23:59:59, 24:00:00) 这一整秒的缺口，
/// 落在这一秒里的账单（短信/通知捕获用 <c>DateTime.Now</c>，带小数秒）会从当天/当月的列表与趋势里消失，
/// 却仍被按左闭右开统计的月度/分类汇总计入，两处口径对不上。
/// </para>
/// <para>
/// 这里只做日历运算，不做时区换算：入参是应用时区的墙上时间，换算由 <see cref="AppTimeZone.ToUtc(DateTime)"/> 在服务内完成。
/// </para>
/// </summary>
public static class TimeBoundaries
{
    /// <summary>
    /// 某一天的含端点结束时刻（次日 00:00 减 1 个 tick）。入参只取日期部分。
    /// </summary>
    public static DateTime EndOfDay(DateTime date) => date.Date.AddDays(1).AddTicks(-1);

    /// <summary>
    /// 某个月的含端点结束时刻（下月 1 日 00:00 减 1 个 tick）。
    /// </summary>
    public static DateTime EndOfMonth(int year, int month) => new DateTime(year, month, 1).AddMonths(1).AddTicks(-1);

    /// <summary>
    /// 某一天所在那一周的第一天（周一 00:00）。全应用统一以周一为一周之始（统计趋势、语音记账的「本周 / 上周」也是如此）。入参只取日期部分。
    /// </summary>
    public static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
