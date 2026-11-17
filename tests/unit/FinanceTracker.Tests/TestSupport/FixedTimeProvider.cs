namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 固定"此刻"的 <see cref="TimeProvider"/>：测试里需要稳定的"今天"（例如年度同比的"年初至今"）时注入。
/// 只覆盖 <see cref="GetUtcNow"/>——服务端取"今天"只用到它；本地时区的换算仍然走 <c>AppTimeZone</c>。
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
