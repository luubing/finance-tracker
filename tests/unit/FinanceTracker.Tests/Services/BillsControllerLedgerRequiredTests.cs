using System.Security.Claims;
using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账单 REST 接口的「账本必填」契约：
/// <list type="bullet">
/// <item>新建/编辑未传账本 → 400「请选择账本」（编辑时也必须是 400：UpdateBill 会把 BillService 的 ArgumentException 统一映射成 404，
/// 所以控制器要先于服务层拦截，否则用户会看到误导性的"账单不存在"）</item>
/// <item>账本不存在 → 400；账本是别人的 → 403（已认证但无权，不能是 401/500）</item>
/// </list>
/// </summary>
public sealed class BillsControllerLedgerRequiredTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;
    private Ledger _ledger = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _ledger = await _db.GetDefaultLedgerAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private BillsController CreateController() =>
        new(
            new BillService(_db.Context, NullLogger<BillService>.Instance),
            new LedgerMemberService(_db.Context),
            new CategoryService(_db.Context, NullLogger<CategoryService>.Instance))
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

    private BillRequest NewRequest(Guid? ledgerId) => new()
    {
        Amount = 18.5m,
        Type = BillType.Expense,
        CategoryId = _db.ExpenseCategory.Id,
        PaymentChannelId = _db.Channel.Id,
        LedgerId = ledgerId,
        TransactionTime = new DateTime(2026, 3, 1, 12, 0, 0),
        Note = "测试"
    };

    /// <summary>历史账单：账本必填之前创建、没有账本的账单（BillService 已不允许创建，只能直接入库来模拟）</summary>
    private async Task<Bill> SeedLegacyBillAsync()
    {
        var bill = new Bill
        {
            Id = Guid.NewGuid(),
            UserId = _db.User.Id,
            Amount = 8m,
            Type = BillType.Expense,
            CategoryId = _db.ExpenseCategory.Id,
            PaymentChannelId = _db.Channel.Id,
            LedgerId = null,
            TransactionTime = new DateTime(2026, 3, 1, 4, 0, 0, DateTimeKind.Utc),
            Source = BillSource.Manual,
            SyncStatus = SyncStatus.Synced
        };

        _db.Context.Bills.Add(bill);
        await _db.Context.SaveChangesAsync();

        return bill;
    }

    private Task<int> CountBillsAsync() => _db.Context.Bills.IgnoreQueryFilters().CountAsync();

    private static string MessageOf(IActionResult result)
    {
        var value = Assert.IsAssignableFrom<ObjectResult>(result).Value;

        return JsonSerializer.SerializeToElement(value).GetProperty("message").GetString()!;
    }

    private async Task<Guid?> StoredLedgerIdOfAsync(Guid billId)
    {
        _db.Context.ChangeTracker.Clear();
        return (await _db.Context.Bills.AsNoTracking().SingleAsync(b => b.Id == billId)).LedgerId;
    }

    // ───────────────────────── 新建 ─────────────────────────

    [Fact]
    public async Task 创建账单_没传账本_返回400请选择账本且不入库()
    {
        var result = await CreateController().CreateBill(NewRequest(null));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("请选择账本", MessageOf(result));
        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 创建账单_账本是空Guid_同样返回400请选择账本()
    {
        var result = await CreateController().CreateBill(NewRequest(Guid.Empty));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("请选择账本", MessageOf(result));
        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 创建账单_账本不存在_返回400且不入库()
    {
        var result = await CreateController().CreateBill(NewRequest(Guid.NewGuid()));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("账本不存在", MessageOf(result));
        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 创建账单_账本是别人的_返回403而不是401或500_且不入库()
    {
        var stranger = await _db.CreateUserAsync("13900000002");
        var theirs = await _db.CreateLedgerAsync(stranger.Id, "别人的账本");

        var result = await CreateController().CreateBill(NewRequest(theirs.Id));

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(0, await CountBillsAsync());
    }

    [Fact]
    public async Task 创建账单_自己的账本_返回201且账单归属该账本()
    {
        var result = await CreateController().CreateBill(NewRequest(_ledger.Id));

        Assert.IsType<CreatedAtActionResult>(result);
        var stored = await _db.Context.Bills.AsNoTracking().SingleAsync();
        Assert.Equal(_ledger.Id, stored.LedgerId);
    }

    // ───────────────────────── 编辑 ─────────────────────────

    [Fact]
    public async Task 编辑账单_清空账本_返回400而不是404_账单保持原账本()
    {
        var controller = CreateController();
        var created = Assert.IsType<CreatedAtActionResult>(await controller.CreateBill(NewRequest(_ledger.Id)));
        var billId = (Guid)created.RouteValues!["id"]!;

        var result = await controller.UpdateBill(billId, NewRequest(null));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("请选择账本", MessageOf(result));
        Assert.Equal(_ledger.Id, await StoredLedgerIdOfAsync(billId));
    }

    [Fact]
    public async Task 编辑历史未归属账本的账单_不补账本被拒绝_补上账本后返回200()
    {
        var legacy = await SeedLegacyBillAsync();
        var controller = CreateController();

        var rejected = await controller.UpdateBill(legacy.Id, NewRequest(null));
        Assert.IsType<BadRequestObjectResult>(rejected);
        Assert.Null(await StoredLedgerIdOfAsync(legacy.Id));

        var accepted = await controller.UpdateBill(legacy.Id, NewRequest(_ledger.Id));

        Assert.IsType<OkObjectResult>(accepted);
        Assert.Equal(_ledger.Id, await StoredLedgerIdOfAsync(legacy.Id));
    }

    [Fact]
    public async Task 编辑账单_改到别人的账本_返回403_账单保持原账本()
    {
        var controller = CreateController();
        var created = Assert.IsType<CreatedAtActionResult>(await controller.CreateBill(NewRequest(_ledger.Id)));
        var billId = (Guid)created.RouteValues!["id"]!;
        var stranger = await _db.CreateUserAsync("13900000002");
        var theirs = await _db.CreateLedgerAsync(stranger.Id, "别人的账本");

        var result = await controller.UpdateBill(billId, NewRequest(theirs.Id));

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(_ledger.Id, await StoredLedgerIdOfAsync(billId));
    }
}
