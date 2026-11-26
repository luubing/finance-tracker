using System.Security.Claims;
using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 导入接口的「账本必填」：微信/支付宝正式导入必须指定目标账本，
/// 且当前用户必须对该账本有写权限（否则任何人都能把账单灌进别人的账本，见 ADR 0004）。
/// 只解析不入库的预览接口不受影响——用户可以先预览、再决定导入到哪个账本。
/// </summary>
public sealed class ImportLedgerRequiredTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private Ledger _ledger = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _ledger = await _db.GetDefaultLedgerAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private ImportController CreateController()
    {
        var parser = new StubCsvParser
        {
            Bills =
            {
                new ImportedBill
                {
                    TransactionTime = new DateTime(2026, 3, 1, 8, 30, 0),
                    Amount = 25m,
                    IsIncome = false,
                    Description = "测试商户",
                    MerchantName = "测试商户"
                },
                new ImportedBill
                {
                    TransactionTime = new DateTime(2026, 3, 2, 9, 0, 0),
                    Amount = 100m,
                    IsIncome = true,
                    Description = "测试收入",
                    MerchantName = "测试收入"
                }
            }
        };

        return new ImportController(
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
    }

    private Task<int> CountBillsAsync() => _db.Context.Bills.IgnoreQueryFilters().CountAsync();

    /// <summary>错误响应体 <c>{ message }</c> 里的提示文案</summary>
    private static string MessageOf(IActionResult result)
    {
        var value = Assert.IsAssignableFrom<ObjectResult>(result).Value;

        return JsonSerializer.SerializeToElement(value).GetProperty("message").GetString()!;
    }

    // ───────────────────────── 没选账本 ─────────────────────────

    [Fact]
    public async Task 微信导入_没选账本_返回400且不入库()
    {
        var result = await CreateController().ImportWeChatBill(new ImportRequest { CsvContent = "x" });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("请选择要导入到的账本", MessageOf(result));
        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 支付宝导入_没选账本_返回400且不入库()
    {
        var result = await CreateController().ImportAlipayBill(new ImportRequest { CsvContent = "x" });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("请选择要导入到的账本", MessageOf(result));
        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 导入_账本是空Guid_同样返回400且不入库()
    {
        var result = await CreateController()
            .ImportWeChatBill(new ImportRequest { CsvContent = "x", LedgerId = Guid.Empty });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("请选择要导入到的账本", MessageOf(result));
        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 导入_账本不存在_返回400且不入库()
    {
        var result = await CreateController()
            .ImportWeChatBill(new ImportRequest { CsvContent = "x", LedgerId = Guid.NewGuid() });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("账本不存在", MessageOf(result));
        Assert.Equal(0, await CountBillsAsync());
    }

    // ───────────────────────── 账本权限 ─────────────────────────

    [Fact]
    public async Task 导入到别人的账本_返回403而不是401或500_且不入库()
    {
        var stranger = await _db.CreateUserAsync("13900000002");
        var theirs = await _db.CreateLedgerAsync(stranger.Id, "别人的账本");

        var result = await CreateController()
            .ImportWeChatBill(new ImportRequest { CsvContent = "x", LedgerId = theirs.Id });

        // 已认证但无权操作：必须是 403（客户端不会因此登出）；401 会让客户端误判登录过期
        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(0, await CountBillsAsync());
    }

    // ───────────────────────── 正常导入 ─────────────────────────

    [Fact]
    public async Task 导入到自己的账本_全部账单归属该账本()
    {
        using var _ = AppTimeZone.UseZone(TimeZoneInfo.Utc);

        var result = await CreateController()
            .ImportWeChatBill(new ImportRequest { CsvContent = "x", UtcOffsetMinutes = 0, LedgerId = _ledger.Id });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal(2, body.GetProperty("successCount").GetInt32());
        Assert.Equal(0, body.GetProperty("failCount").GetInt32());

        var bills = await _db.Context.Bills.ToListAsync();
        Assert.Equal(2, bills.Count);
        Assert.All(bills, b =>
        {
            Assert.Equal(_ledger.Id, b.LedgerId);
            Assert.Equal(BillSource.Import, b.Source);
        });
    }

    [Fact]
    public async Task 预览不受账本必填影响_不传账本也能解析()
    {
        var wechat = await CreateController().PreviewWeChatBill(new ImportRequest { CsvContent = "x" });
        var alipay = await CreateController().PreviewAlipayBill(new ImportRequest { CsvContent = "x" });

        Assert.IsType<OkObjectResult>(wechat);
        Assert.IsType<OkObjectResult>(alipay);
        Assert.Equal(0, await CountBillsAsync());
    }
}
