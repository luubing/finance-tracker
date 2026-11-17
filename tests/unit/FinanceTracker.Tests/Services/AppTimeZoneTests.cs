using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 应用时区换算（P6）：库里是 UTC、用户看到的是应用时区的墙上时间，两者之间只经 <see cref="AppTimeZone"/> 换算。
/// 所有用例都显式指定时区（UTC+8 / 带夏令时的自建区域），不依赖运行机器的时区。
/// </summary>
public sealed class AppTimeZoneTests
{
    private static IDisposable UseUtcPlus8() => AppTimeZone.UseZone(TestZones.UtcPlus8);

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) =>
        new(y, m, d, h, min, s, DateTimeKind.Utc);

    private static DateTime Wall(int y, int m, int d, int h = 0, int min = 0, int s = 0) =>
        new(y, m, d, h, min, s, DateTimeKind.Unspecified);

    // ───────────────────────── ToUtc ─────────────────────────

    [Fact]
    public void 墙上时间转UTC_东八区减8小时且Kind为Utc()
    {
        using var _ = UseUtcPlus8();

        var utc = AppTimeZone.ToUtc(Wall(2026, 3, 1, 0, 30));

        Assert.Equal(Utc(2026, 2, 28, 16, 30), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void Kind为Local的值_按应用时区而不是系统时区解释()
    {
        using var _ = UseUtcPlus8();

        // 表单/DateTime.Now 产生的就是 Local。若被系统时区解释，结果会随运行机器而变
        var local = DateTime.SpecifyKind(Wall(2026, 3, 1, 0, 30), DateTimeKind.Local);

        Assert.Equal(Utc(2026, 2, 28, 16, 30), AppTimeZone.ToUtc(local));
    }

    [Fact]
    public void UTC输入_原样返回()
    {
        using var _ = UseUtcPlus8();

        var input = Utc(2026, 2, 28, 16, 30);

        Assert.Equal(input, AppTimeZone.ToUtc(input));
    }

    [Fact]
    public void 显式偏移转UTC_正负偏移都正确_UTC输入不再换算()
    {
        // 东八区 +480 分钟
        Assert.Equal(Utc(2026, 2, 28, 16, 30), AppTimeZone.ToUtc(Wall(2026, 3, 1, 0, 30), TimeSpan.FromMinutes(480)));
        // 西五区 -300 分钟
        Assert.Equal(Utc(2026, 3, 1, 5, 30), AppTimeZone.ToUtc(Wall(2026, 3, 1, 0, 30), TimeSpan.FromMinutes(-300)));
        // 已是 UTC 的值不受偏移影响
        Assert.Equal(Utc(2026, 3, 1, 0, 30), AppTimeZone.ToUtc(Utc(2026, 3, 1, 0, 30), TimeSpan.FromMinutes(480)));
    }

    // ───────────────────────── ToLocal ─────────────────────────

    [Fact]
    public void UTC转本地_东八区加8小时_Kind为Unspecified()
    {
        using var _ = UseUtcPlus8();

        var local = AppTimeZone.ToLocal(Utc(2026, 2, 28, 16, 30));

        Assert.Equal(Wall(2026, 3, 1, 0, 30), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
    }

    [Fact]
    public void 库里读出的Unspecified值_视为UTC()
    {
        using var _ = UseUtcPlus8();

        // EF Core 从 SQLite 读出的 DateTime 没有 Kind
        Assert.Equal(Wall(2026, 3, 1, 0, 30), AppTimeZone.ToLocal(Wall(2026, 2, 28, 16, 30)));
    }

    [Fact]
    public void Kind为Local的值_视为已是本地时间_不再加偏移()
    {
        using var _ = UseUtcPlus8();

        var alreadyLocal = DateTime.SpecifyKind(Wall(2026, 3, 1, 0, 30), DateTimeKind.Local);

        Assert.Equal(Wall(2026, 3, 1, 0, 30), AppTimeZone.ToLocal(alreadyLocal));
    }

    [Fact]
    public void 本地与UTC互转_往返一致()
    {
        using var _ = UseUtcPlus8();

        var start = Utc(2026, 1, 1);
        for (var i = 0; i < 24 * 60; i++)
        {
            var instant = start.AddMinutes(i * 30);
            Assert.Equal(instant, AppTimeZone.ToUtc(AppTimeZone.ToLocal(instant)));
        }
    }

    // ───────────────────────── 边界与夏令时 ─────────────────────────

    [Fact]
    public void 极值不会抛异常_而是钳制()
    {
        using var _ = UseUtcPlus8();

        // default(DateTime) 在正偏移区域减去偏移会下溢；MaxValue 加偏移会上溢
        Assert.Equal(DateTime.MinValue, AppTimeZone.ToUtc(DateTime.MinValue));
        Assert.Equal(DateTime.MaxValue, AppTimeZone.ToLocal(DateTime.MaxValue));
        Assert.Equal(DateTime.MaxValue, AppTimeZone.ToUtc(DateTime.MaxValue, TimeSpan.FromHours(-5)));
    }

    [Fact]
    public void 夏令时期间与标准时间期间_偏移不同()
    {
        using var _ = AppTimeZone.UseZone(TestZones.EuropeLike);

        // 夏令时 UTC+2
        Assert.Equal(Utc(2026, 7, 1, 10), AppTimeZone.ToUtc(Wall(2026, 7, 1, 12)));
        Assert.Equal(Wall(2026, 7, 1, 12), AppTimeZone.ToLocal(Utc(2026, 7, 1, 10)));

        // 标准时间 UTC+1
        Assert.Equal(Utc(2026, 1, 15, 11), AppTimeZone.ToUtc(Wall(2026, 1, 15, 12)));
        Assert.Equal(Wall(2026, 1, 15, 12), AppTimeZone.ToLocal(Utc(2026, 1, 15, 11)));
    }

    [Fact]
    public void 夏令时跳变丢失的那一小时_不抛异常()
    {
        using var _ = AppTimeZone.UseZone(TestZones.EuropeLike);

        // 2026-03-29 02:30 在该区域不存在（02:00 直接跳到 03:00）。
        // TimeZoneInfo.ConvertTimeToUtc 会对此抛 ArgumentException，业务（如导入、手填时间）不能因此崩溃
        Assert.True(TestZones.EuropeLike.IsInvalidTime(Wall(2026, 3, 29, 2, 30)));

        var utc = AppTimeZone.ToUtc(Wall(2026, 3, 29, 2, 30));

        // 取标准偏移或夏令时偏移都可接受，但必须落在跳变点附近（UTC 00:30 ~ 01:30）
        Assert.InRange(utc, Utc(2026, 3, 29, 0, 30), Utc(2026, 3, 29, 1, 30));
    }

    [Fact]
    public void 夏令时重复的那一小时_不抛异常()
    {
        using var _ = AppTimeZone.UseZone(TestZones.EuropeLike);

        // 2026-10-25 02:30 出现两次（夏令时 UTC 00:30 与标准时间 UTC 01:30）
        Assert.True(TestZones.EuropeLike.IsAmbiguousTime(Wall(2026, 10, 25, 2, 30)));

        var utc = AppTimeZone.ToUtc(Wall(2026, 10, 25, 2, 30));

        Assert.InRange(utc, Utc(2026, 10, 25, 0, 30), Utc(2026, 10, 25, 1, 30));
    }

    [Fact]
    public void 夏令时区域全年往返一致_仅重复小时除外()
    {
        using var _ = AppTimeZone.UseZone(TestZones.EuropeLike);

        var start = Utc(2026, 1, 1);
        for (var i = 0; i < 365 * 24; i++)
        {
            var instant = start.AddHours(i);
            var local = AppTimeZone.ToLocal(instant);

            // 重复的那一小时本地时间对应两个 UTC 时刻，无法反推，跳过
            if (TestZones.EuropeLike.IsAmbiguousTime(local))
            {
                continue;
            }

            Assert.Equal(instant, AppTimeZone.ToUtc(local));
        }
    }

    // ───────────────────────── 偏移分钟数 / 时区覆盖作用域 ─────────────────────────

    [Fact]
    public void 偏移分钟数_固定时区与夏令时区域()
    {
        using (AppTimeZone.UseZone(TestZones.UtcPlus8))
        {
            Assert.Equal(480, AppTimeZone.GetUtcOffsetMinutes());
        }

        using (AppTimeZone.UseZone(TestZones.EuropeLike))
        {
            Assert.Equal(120, AppTimeZone.GetUtcOffsetMinutes(Utc(2026, 7, 1, 12)));
            Assert.Equal(60, AppTimeZone.GetUtcOffsetMinutes(Utc(2026, 1, 15, 12)));
        }
    }

    [Fact]
    public void 时区覆盖_可嵌套_释放后恢复()
    {
        var before = AppTimeZone.Zone;

        using (AppTimeZone.UseZone(TestZones.UtcPlus8))
        {
            Assert.Equal(TestZones.UtcPlus8, AppTimeZone.Zone);

            using (AppTimeZone.UseZone(TestZones.EuropeLike))
            {
                Assert.Equal(TestZones.EuropeLike, AppTimeZone.Zone);
            }

            Assert.Equal(TestZones.UtcPlus8, AppTimeZone.Zone);
        }

        Assert.Equal(before, AppTimeZone.Zone);
    }

    [Fact]
    public void 时区覆盖_重复释放是安全的()
    {
        var before = AppTimeZone.Zone;
        var scope = AppTimeZone.UseZone(TestZones.UtcPlus8);

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(before, AppTimeZone.Zone);
    }

    [Fact]
    public async Task 时区覆盖_随异步流程传递到await之后和子任务()
    {
        using var _ = UseUtcPlus8();

        await Task.Yield();
        Assert.Equal(TestZones.UtcPlus8, AppTimeZone.Zone);

        var insideTask = await Task.Run(() => AppTimeZone.Zone);
        Assert.Equal(TestZones.UtcPlus8, insideTask);
    }
}
