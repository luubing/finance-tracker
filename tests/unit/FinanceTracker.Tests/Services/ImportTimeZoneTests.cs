using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 导入接口的时区（P6）：微信/支付宝 CSV 里的交易时间是<b>不带时区的用户本地时间</b>，
/// 而导入发生在云端服务器上。服务器多半是 UTC，若按服务器时区解释，东八区用户导入的账单会差 8 小时。
/// 因此客户端上报自己的 UTC 偏移（<see cref="ImportRequest.UtcOffsetMinutes"/>），服务器按该偏移换算。
/// </summary>
public sealed class ImportTimeZoneTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private Guid _ledgerId;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();

        // 导入必须指定账本：这些用例关心的是时区，统一导入到默认账本
        _ledgerId = (await _db.GetDefaultLedgerAsync()).Id;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private ImportController CreateController(DateTime csvWallTime)
    {
        var parser = new StubCsvParser
        {
            Bills =
            {
                new ImportedBill
                {
                    TransactionTime = csvWallTime,
                    Amount = 25m,
                    IsIncome = false,
                    Description = "测试商户",
                    MerchantName = "测试商户"
                }
            }
        };

        var controller = new ImportController(
            parser,
            new BillService(_db.Context, NullLogger<BillService>.Instance),
            new CategoryService(_db.Context, NullLogger<CategoryService>.Instance),
            new PaymentChannelService(_db.Context, NullLogger<PaymentChannelService>.Instance),
            new LedgerMemberService(_db.Context))
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

        return controller;
    }

    private async Task<DateTime> ReadRawStoredTimeAsync()
    {
        await using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT TransactionTime FROM Bills LIMIT 1;";
        var raw = (string)(await command.ExecuteScalarAsync())!;
        return DateTime.Parse(raw, CultureInfo.InvariantCulture);
    }

    private async Task<long> CountBillsAsync()
    {
        await using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Bills;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static int GetInt(IActionResult result, string property)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value).GetProperty(property).GetInt32();
    }

    [Fact]
    public async Task 上报东八区偏移_服务器在UTC_CSV本地时间按偏移入库()
    {
        // 服务器是 UTC；用户在东八区导入 CSV 里的 "2026-03-01 08:30:00"
        using (AppTimeZone.UseZone(TimeZoneInfo.Utc))
        {
            var result = await CreateController(new DateTime(2026, 3, 1, 8, 30, 0))
                .ImportWeChatBill(new ImportRequest { CsvContent = "x", UtcOffsetMinutes = 480, LedgerId = _ledgerId });

            Assert.Equal(1, GetInt(result, "successCount"));
            Assert.Equal(0, GetInt(result, "failCount"));
        }

        // 08:30（UTC+8）= 00:30 UTC，而不是被服务器当成 08:30 UTC
        Assert.Equal(new DateTime(2026, 3, 1, 0, 30, 0), await ReadRawStoredTimeAsync());

        // 用户的设备再把它换算回本地，应该仍是 CSV 里的 08:30
        using (AppTimeZone.UseZone(TestZones.UtcPlus8))
        {
            Assert.Equal(
                new DateTime(2026, 3, 1, 8, 30, 0),
                AppTimeZone.ToLocal(await ReadRawStoredTimeAsync()));
        }
    }

    [Fact]
    public async Task 支付宝导入同样按上报偏移换算()
    {
        using var _ = AppTimeZone.UseZone(TimeZoneInfo.Utc);

        var result = await CreateController(new DateTime(2026, 3, 1, 8, 30, 0))
            .ImportAlipayBill(new ImportRequest { CsvContent = "x", UtcOffsetMinutes = 480, LedgerId = _ledgerId });

        Assert.Equal(1, GetInt(result, "successCount"));
        Assert.Equal(new DateTime(2026, 3, 1, 0, 30, 0), await ReadRawStoredTimeAsync());
    }

    [Fact]
    public async Task 上报负偏移_西五区_也正确()
    {
        using var _ = AppTimeZone.UseZone(TimeZoneInfo.Utc);

        await CreateController(new DateTime(2026, 3, 1, 8, 30, 0))
            .ImportWeChatBill(new ImportRequest { CsvContent = "x", UtcOffsetMinutes = -300, LedgerId = _ledgerId });

        Assert.Equal(new DateTime(2026, 3, 1, 13, 30, 0), await ReadRawStoredTimeAsync());
    }

    [Fact]
    public async Task 旧版客户端未上报偏移_仍按服务器应用时区解释()
    {
        // 服务器时区 = UTC+8：保持原有行为
        using (AppTimeZone.UseZone(TestZones.UtcPlus8))
        {
            await CreateController(new DateTime(2026, 3, 1, 8, 30, 0))
                .ImportWeChatBill(new ImportRequest { CsvContent = "x", LedgerId = _ledgerId });
        }

        Assert.Equal(new DateTime(2026, 3, 1, 0, 30, 0), await ReadRawStoredTimeAsync());
    }

    [Theory]
    [InlineData(841)]
    [InlineData(-841)]
    [InlineData(int.MaxValue)]
    public async Task 偏移超出全球时区范围_返回400且不入库(int offsetMinutes)
    {
        var result = await CreateController(new DateTime(2026, 3, 1, 8, 30, 0))
            .ImportWeChatBill(new ImportRequest { CsvContent = "x", UtcOffsetMinutes = offsetMinutes, LedgerId = _ledgerId });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0L, await CountBillsAsync());
    }

    [Theory]
    [InlineData(840)]
    [InlineData(-840)]
    [InlineData(0)]
    public async Task 偏移处于范围边界_被接受(int offsetMinutes)
    {
        var result = await CreateController(new DateTime(2026, 3, 1, 8, 30, 0))
            .ImportWeChatBill(new ImportRequest { CsvContent = "x", UtcOffsetMinutes = offsetMinutes, LedgerId = _ledgerId });

        Assert.Equal(1, GetInt(result, "successCount"));
    }
}
