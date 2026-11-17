namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 测试用时区：全部用 <see cref="TimeZoneInfo.CreateCustomTimeZone(string, TimeSpan, string?, string?)"/> 自建，
/// 不依赖运行机器的时区数据库（Windows 与 Linux 的时区 ID 不同），保证结果可复现。
/// </summary>
public static class TestZones
{
    /// <summary>固定 UTC+8（无夏令时），模拟中国用户：本地 3 月 1 日 00:30 = UTC 2 月 28 日 16:30</summary>
    public static readonly TimeZoneInfo UtcPlus8 =
        TimeZoneInfo.CreateCustomTimeZone("Test-UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

    /// <summary>
    /// 固定 UTC-5（无夏令时）：专门用来区分"应用时区"与"运行机器的系统时区"——
    /// 它和常见的开发机/CI 时区（UTC+8、UTC）都不同，按系统时区换算的实现在这里会得到不同的结果。
    /// </summary>
    public static readonly TimeZoneInfo UtcMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test-UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    /// <summary>
    /// 带夏令时的欧洲式区域：标准时间 UTC+1，夏令时 UTC+2。
    /// 3 月最后一个周日 02:00 → 03:00（本地 02:00~03:00 不存在，当天只有 23 小时），
    /// 10 月最后一个周日 03:00 → 02:00（本地 02:00~03:00 出现两次，当天有 25 小时）。
    /// 2026 年的切换日分别是 3 月 29 日和 10 月 25 日。
    /// </summary>
    public static readonly TimeZoneInfo EuropeLike = CreateEuropeLike();

    private static TimeZoneInfo CreateEuropeLike()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test-EuropeLike",
            TimeSpan.FromHours(1),
            "EuropeLike",
            "EuropeLike Standard",
            "EuropeLike Daylight",
            new[] { rule });
    }
}
