namespace FinanceTracker.Core.Time;

/// <summary>
/// 应用时区约定（详见 docs/adr/0005-time-zone-handling.md）。
/// <para>
/// 1. <b>存储与同步一律用 UTC</b>：<c>Bill.TransactionTime</c> 在数据库（SQLite/PostgreSQL）和同步 DTO 中都是 UTC。<br/>
/// 2. <b>用户看到和输入的一律是应用时区的墙上时间</b>：账单列表时间、编辑回填、按天/月分组、
///    "本月/本年"边界、预算月度边界都以应用时区为准。<br/>
/// 3. 应用时区默认 = 运行环境的本地时区：MAUI App 为设备时区；API / Blazor Server 宿主为服务器时区。
/// </para>
/// <para>
/// 本类是两种口径之间唯一的换算入口，业务代码不要再直接调用 <c>ToUniversalTime()</c> /
/// <c>ToLocalTime()</c>（它们依赖 <see cref="DateTime.Kind"/>，而 EF Core 从 SQLite 读出的值 Kind 为
/// <see cref="DateTimeKind.Unspecified"/>，极易二次换算）。
/// </para>
/// <para>
/// Kind 解释规则：<see cref="DateTimeKind.Utc"/> 视为 UTC；<see cref="DateTimeKind.Local"/> 与
/// <see cref="DateTimeKind.Unspecified"/> 都视为"应用时区的墙上时间"（表单输入、CSV 解析、短信/通知时间均属此类）。
/// 唯一例外是 <see cref="ToLocal(DateTime)"/> 的输入：它面向"库里读出来的值"，因此把 Unspecified 当作 UTC。
/// </para>
/// </summary>
public static class AppTimeZone
{
    private static readonly AsyncLocal<TimeZoneInfo?> ZoneOverride = new();

    /// <summary>
    /// 当前生效的应用时区：默认是 <see cref="TimeZoneInfo.Local"/>，
    /// 测试可通过 <see cref="UseZone"/> 在当前异步上下文内临时覆盖，以获得与运行机器无关的确定性结果。
    /// </summary>
    public static TimeZoneInfo Zone => ZoneOverride.Value ?? TimeZoneInfo.Local;

    /// <summary>
    /// 在当前异步上下文内临时覆盖应用时区（主要给测试用）。释放返回值即恢复之前的时区。
    /// </summary>
    public static IDisposable UseZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var previous = ZoneOverride.Value;
        ZoneOverride.Value = zone;
        return new ZoneScope(previous);
    }

    /// <summary>
    /// 应用时区的墙上时间 → UTC。UTC 输入原样返回；Local/Unspecified 按应用时区解释。
    /// 夏令时"不存在的时间"和"重复的时间"不会抛异常，按时区标准偏移处理；越界时钳制到 <see cref="DateTime"/> 的取值范围。
    /// </summary>
    public static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        // 必须先去掉 Kind：对 Kind=Local 的值，TimeZoneInfo 会按"系统本地时区"而不是 Zone 去解释
        var wallTime = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return ToUtc(wallTime, Zone.GetUtcOffset(wallTime));
    }

    /// <summary>
    /// 按显式的 UTC 偏移把墙上时间转为 UTC（导入接口使用：服务器不知道用户时区，由客户端上报偏移）。
    /// UTC 输入原样返回。
    /// </summary>
    public static DateTime ToUtc(DateTime value, TimeSpan utcOffset)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        return FromTicks(value.Ticks - utcOffset.Ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// 库里/DTO 里的 UTC 时间 → 应用时区的墙上时间（返回值 Kind=Unspecified，可直接格式化、取 <c>.Date</c>、分组）。
    /// Utc 与 Unspecified（EF Core 从 SQLite 读出时丢失了 Kind）都视为 UTC；
    /// Local 视为已经是本地时间，原样返回（仅把 Kind 置为 Unspecified）。
    /// </summary>
    public static DateTime ToLocal(DateTime value)
    {
        if (value.Kind == DateTimeKind.Local)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        }

        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return FromTicks(utc.Ticks + Zone.GetUtcOffset(utc).Ticks, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// 应用时区在指定 UTC 时刻的 UTC 偏移（分钟）。导入页用它告诉服务器：CSV 里的本地时间属于哪个时区。
    /// </summary>
    public static int GetUtcOffsetMinutes(DateTime? utcNow = null)
    {
        var instant = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
        return (int)Zone.GetUtcOffset(instant).TotalMinutes;
    }

    private static DateTime FromTicks(long ticks, DateTimeKind kind)
    {
        if (ticks < DateTime.MinValue.Ticks)
        {
            ticks = DateTime.MinValue.Ticks;
        }
        else if (ticks > DateTime.MaxValue.Ticks)
        {
            ticks = DateTime.MaxValue.Ticks;
        }

        return new DateTime(ticks, kind);
    }

    private sealed class ZoneScope : IDisposable
    {
        private readonly TimeZoneInfo? _previous;
        private bool _disposed;

        public ZoneScope(TimeZoneInfo? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ZoneOverride.Value = _previous;
        }
    }
}
