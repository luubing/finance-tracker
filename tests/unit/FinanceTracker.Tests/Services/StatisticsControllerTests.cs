using System.Security.Claims;
using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 同比接口（<c>GET /api/statistics/year-over-year</c>）的 HTTP 契约：
/// <c>month</c> 可省略（省略 = 年度同比）、期间起止日随响应返回、无对比基数时变化率是 JSON <c>null</c> 而不是 0。
/// 统计口径本身由 <see cref="YearOverYearTests"/> 覆盖，这里只验证控制器的分流、校验与序列化。
/// </summary>
public sealed class StatisticsControllerTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private BillService _billService = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _billService = new BillService(_db.Context, NullLogger<BillService>.Instance);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /// <summary>构造一个"此刻是 2026-11-17 12:00（东八区）"的控制器；调用方必须已经进入 UtcPlus8 的作用域</summary>
    private StatisticsController CreateController()
    {
        var utcNow = new DateTimeOffset(AppTimeZone.ToUtc(new DateTime(2026, 11, 17, 12, 0, 0)), TimeSpan.Zero);
        var service = new StatisticsService(_db.Context, new FixedTimeProvider(utcNow));

        return new StatisticsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, _db.User.Id.ToString()) },
                        "test"))
                }
            }
        };
    }

    private Task AddExpenseAsync(DateTime wallTime, decimal amount) =>
        _billService.CreateBillAsync(new Bill
        {
            UserId = _db.User.Id,
            Amount = amount,
            Type = BillType.Expense,
            CategoryId = _db.ExpenseCategory.Id,
            PaymentChannelId = _db.Channel.Id,
            TransactionTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified),
            Source = BillSource.Manual
        });

    private static JsonElement Body(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value);
    }

    [Fact]
    public async Task 省略month_走年度口径_返回整年的起止日和变化率()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        await AddExpenseAsync(new DateTime(2024, 6, 1, 9, 0, 0), 100m);
        await AddExpenseAsync(new DateTime(2025, 6, 1, 9, 0, 0), 150m);
        await AddExpenseAsync(new DateTime(2025, 12, 31, 23, 30, 0), 50m);   // 本地年末最后一刻，仍属 2025
        // 本地 2026-01-01 00:30 = UTC 2025-12-31 16:30：本地已是新年，按 UTC 分年会误算进 2025
        await AddExpenseAsync(new DateTime(2026, 1, 1, 0, 30, 0), 999m);

        var body = Body(await CreateController().GetYearOverYearData(2025, null, null));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("currentMonth").ValueKind);
        Assert.Equal("2025-01-01", body.GetProperty("currentPeriodStart").GetString());
        Assert.Equal("2025-12-31", body.GetProperty("currentPeriodEnd").GetString());
        Assert.Equal("2024-01-01", body.GetProperty("previousPeriodStart").GetString());
        Assert.Equal("2024-12-31", body.GetProperty("previousPeriodEnd").GetString());
        Assert.Equal(200m, body.GetProperty("currentExpense").GetDecimal());
        Assert.Equal(100m, body.GetProperty("previousYearExpense").GetDecimal());
        Assert.Equal(100m, body.GetProperty("expenseChangeRate").GetDecimal());
    }

    [Fact]
    public async Task 当年省略month_期间是年初至今对比去年同月同日()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        var body = Body(await CreateController().GetYearOverYearData(2026, null, null));

        Assert.Equal("2026-01-01", body.GetProperty("currentPeriodStart").GetString());
        Assert.Equal("2026-11-17", body.GetProperty("currentPeriodEnd").GetString());   // 含今天
        Assert.Equal("2025-01-01", body.GetProperty("previousPeriodStart").GetString());
        Assert.Equal("2025-11-17", body.GetProperty("previousPeriodEnd").GetString());
    }

    [Fact]
    public async Task 带month_走月度口径_对比去年同月()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        await AddExpenseAsync(new DateTime(2025, 2, 10, 9, 0, 0), 20m);
        await AddExpenseAsync(new DateTime(2026, 2, 10, 9, 0, 0), 30m);

        var body = Body(await CreateController().GetYearOverYearData(2026, 2, null));

        Assert.Equal(2, body.GetProperty("currentMonth").GetInt32());
        Assert.Equal("2026-02-01", body.GetProperty("currentPeriodStart").GetString());
        Assert.Equal("2026-02-28", body.GetProperty("currentPeriodEnd").GetString());
        Assert.Equal("2025-02-01", body.GetProperty("previousPeriodStart").GetString());
        Assert.Equal("2025-02-28", body.GetProperty("previousPeriodEnd").GetString());
        Assert.Equal(50m, body.GetProperty("expenseChangeRate").GetDecimal());
    }

    [Fact]
    public async Task 去年同期没有账单_变化率序列化为null而不是0()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        await AddExpenseAsync(new DateTime(2025, 6, 1, 9, 0, 0), 150m);   // 2024 年没有任何账单

        var body = Body(await CreateController().GetYearOverYearData(2025, null, null));

        Assert.Equal(150m, body.GetProperty("currentExpense").GetDecimal());
        Assert.Equal(0m, body.GetProperty("previousYearExpense").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("expenseChangeRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("incomeChangeRate").ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public async Task month超出1到12_返回400(int month)
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        var result = await CreateController().GetYearOverYearData(2026, month, null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task year超出范围_返回400(int year)
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        var result = await CreateController().GetYearOverYearData(year, null, null);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
