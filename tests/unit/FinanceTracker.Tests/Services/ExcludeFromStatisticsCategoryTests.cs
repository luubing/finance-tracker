using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「不计入统计」开关本身：分类服务把它落库（预设分类不能有开关）、REST 请求 / 响应带上它（老调用方不带 = 保持原值）、
/// 统计接口 <c>GET /api/statistics/excluded</c> 的契约，以及同步 DTO 的四个转换点（FromEntity / ToEntity / ApplyTo / ContentEquals）都认它。
/// </summary>
public sealed class ExcludeFromStatisticsCategoryTests : ExcludeFromStatisticsTestBase
{
    // ───────────────────────── 分类服务 ─────────────────────────

    [Fact]
    public async Task 新建分类带开关_落库()
    {
        var savings = await AddCategoryAsync("存款", exclude: true);

        var stored = await Db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == savings.Id);

        Assert.True(stored.ExcludeFromStatistics);
    }

    [Fact]
    public async Task 新建分类缺省不排除()
    {
        var plain = await AddCategoryAsync("早餐");

        var stored = await Db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == plain.Id);

        Assert.False(stored.ExcludeFromStatistics);
    }

    [Fact]
    public async Task 更新分类_开关可以打开也可以关掉_并落库()
    {
        var savings = await AddCategoryAsync("存款");

        await SetExcludedAsync(savings, true);
        Assert.True((await Db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == savings.Id)).ExcludeFromStatistics);

        await SetExcludedAsync(savings, false);
        Assert.False((await Db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == savings.Id)).ExcludeFromStatistics);
    }

    [Fact]
    public async Task 更新分类的名称与开关一起改_互不影响()
    {
        var savings = await AddCategoryAsync("存款", exclude: true);

        await Categories.UpdateCategoryAsync(new Category
        {
            Id = savings.Id,
            UserId = savings.UserId,
            Name = "定期存款",
            Icon = savings.Icon,
            Type = savings.Type,
            SortOrder = 3,
            ExcludeFromStatistics = true
        });

        var stored = await Db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == savings.Id);
        Assert.Equal("定期存款", stored.Name);
        Assert.Equal(3, stored.SortOrder);
        Assert.True(stored.ExcludeFromStatistics);
    }

    [Fact]
    public async Task 预设分类没有开关_更新被拒绝且开关保持关闭()
    {
        var presetId = Db.ExpenseCategory.Id;

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Categories.UpdateCategoryAsync(new Category
        {
            Id = presetId,
            UserId = null,
            Name = Db.ExpenseCategory.Name,
            Icon = Db.ExpenseCategory.Icon,
            Type = Db.ExpenseCategory.Type,
            ExcludeFromStatistics = true
        }));

        var stored = await Db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == presetId);
        Assert.False(stored.ExcludeFromStatistics);
    }

    [Fact]
    public async Task 分类列表带出开关_供分类页与记一笔使用()
    {
        var savings = await AddCategoryAsync("存款", exclude: true);

        var list = await Categories.GetCategoriesAsync(Db.User.Id, BillType.Expense);

        Assert.True(list.Single(c => c.Id == savings.Id).ExcludeFromStatistics);
        Assert.All(list.Where(c => c.IsPreset), c => Assert.False(c.ExcludeFromStatistics));
    }

    // ───────────────────────── REST：分类接口 ─────────────────────────

    private CategoriesController CreateCategoriesController() =>
        new(Categories)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, Db.User.Id.ToString()) }, "test"))
                }
            }
        };

    private static JsonElement Body(IActionResult result) => result switch
    {
        OkObjectResult ok => JsonSerializer.SerializeToElement(ok.Value),
        CreatedAtActionResult created => JsonSerializer.SerializeToElement(created.Value),
        _ => throw new Xunit.Sdk.XunitException($"预期 200 / 201，实际是 {result.GetType().Name}")
    };

    [Fact]
    public async Task REST创建分类_带开关_响应里也带开关()
    {
        var body = Body(await CreateCategoriesController().CreateCategory(new CategoryRequest
        {
            Name = "存款",
            Icon = "mdi-piggy-bank",
            Type = BillType.Expense,
            ExcludeFromStatistics = true
        }));

        Assert.True(body.GetProperty("excludeFromStatistics").GetBoolean());
        var id = body.GetProperty("id").GetGuid();
        Assert.True((await Db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == id)).ExcludeFromStatistics);
    }

    [Fact]
    public async Task REST创建分类_老调用方不带开关_缺省为计入统计()
    {
        var body = Body(await CreateCategoriesController().CreateCategory(new CategoryRequest
        {
            Name = "早餐",
            Type = BillType.Expense
        }));

        Assert.False(body.GetProperty("excludeFromStatistics").GetBoolean());
    }

    [Fact]
    public async Task REST更新分类_老调用方不带开关_保持原值不被悄悄改回()
    {
        var savings = await AddCategoryAsync("存款", exclude: true);

        var body = Body(await CreateCategoriesController().UpdateCategory(savings.Id, new CategoryRequest
        {
            Name = "定期存款",
            Icon = savings.Icon,
            Type = savings.Type,
            ExcludeFromStatistics = null
        }));

        Assert.Equal("定期存款", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("excludeFromStatistics").GetBoolean());
        Assert.True((await Db.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == savings.Id)).ExcludeFromStatistics);
    }

    [Fact]
    public async Task REST更新分类_显式传开关_按传入值改()
    {
        var savings = await AddCategoryAsync("存款", exclude: true);
        var controller = CreateCategoriesController();

        var off = Body(await controller.UpdateCategory(savings.Id, new CategoryRequest
        {
            Name = savings.Name,
            Icon = savings.Icon,
            Type = savings.Type,
            ExcludeFromStatistics = false
        }));
        Assert.False(off.GetProperty("excludeFromStatistics").GetBoolean());

        var on = Body(await controller.UpdateCategory(savings.Id, new CategoryRequest
        {
            Name = savings.Name,
            Icon = savings.Icon,
            Type = savings.Type,
            ExcludeFromStatistics = true
        }));
        Assert.True(on.GetProperty("excludeFromStatistics").GetBoolean());
    }

    [Fact]
    public async Task REST获取分类列表_每一项都带开关字段()
    {
        await AddCategoryAsync("存款", exclude: true);

        var list = Body(await CreateCategoriesController().GetCategories(BillType.Expense));

        Assert.All(list.EnumerateArray(), item => Assert.True(item.TryGetProperty("excludeFromStatistics", out _)));
        Assert.Contains(list.EnumerateArray(),
            item => item.GetProperty("name").GetString() == "存款" && item.GetProperty("excludeFromStatistics").GetBoolean());
    }

    // ───────────────────────── REST：被排除账单汇总接口 ─────────────────────────

    private StatisticsController CreateStatisticsController() =>
        new(new StatisticsService(Db.Context))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, Db.User.Id.ToString()) }, "test"))
                }
            }
        };

    [Fact]
    public async Task REST被排除汇总_日期含结束当天_带类型金额笔数()
    {
        using var _ = UseUtcPlus8();
        var savings = await AddCategoryAsync("存款", exclude: true);
        await AddBillAsync(savings.Id, 1000m, new DateTime(2026, 3, 31, 23, 30, 0));   // 结束日当天的最后半小时
        await AddBillAsync(savings.Id, 200m, new DateTime(2026, 4, 1, 0, 0, 0));       // 次日零点：不属于

        var body = Body(await CreateStatisticsController().GetExcludedStatistics(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), null));

        var item = Assert.Single(body.EnumerateArray());
        Assert.Equal(savings.Id, item.GetProperty("categoryId").GetGuid());
        Assert.Equal("存款", item.GetProperty("categoryName").GetString());
        Assert.Equal("Expense", item.GetProperty("type").GetString());
        Assert.Equal(1000m, item.GetProperty("amount").GetDecimal());
        Assert.Equal(1, item.GetProperty("count").GetInt32());
        Assert.True(item.TryGetProperty("categoryIcon", out var icon));
        Assert.False(string.IsNullOrEmpty(icon.GetString()));
    }

    [Fact]
    public async Task REST被排除汇总_没有被排除的账单_返回空数组()
    {
        var body = Body(await CreateStatisticsController().GetExcludedStatistics(
            new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), null));

        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Empty(body.EnumerateArray());
    }

    [Fact]
    public async Task REST被排除汇总_日期范围非法_返回400()
    {
        var controller = CreateStatisticsController();

        var reversed = await controller.GetExcludedStatistics(new DateTime(2026, 3, 31), new DateTime(2026, 3, 1), null);
        var tooEarly = await controller.GetExcludedStatistics(new DateTime(1999, 1, 1), new DateTime(2026, 3, 1), null);

        Assert.IsType<BadRequestObjectResult>(reversed);
        Assert.IsType<BadRequestObjectResult>(tooEarly);
    }

    // ───────────────────────── 同步 DTO ─────────────────────────

    private static Category SampleCategory(bool exclude) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Name = "存款",
        Icon = "mdi-piggy-bank",
        Type = BillType.Expense,
        SortOrder = 1,
        ExcludeFromStatistics = exclude,
        CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void 同步DTO_FromEntity与ToEntity往返带着开关()
    {
        var entity = SampleCategory(true);

        var dto = CategorySyncDto.FromEntity(entity);
        var back = dto.ToEntity(entity.UserId!.Value);

        Assert.True(dto.ExcludeFromStatistics);
        Assert.True(back.ExcludeFromStatistics);
    }

    [Fact]
    public void 同步DTO_ApplyTo会覆盖开关_双向()
    {
        var target = SampleCategory(false);
        CategorySyncDto.FromEntity(SampleCategory(true)).ApplyTo(target);
        Assert.True(target.ExcludeFromStatistics);

        CategorySyncDto.FromEntity(SampleCategory(false)).ApplyTo(target);
        Assert.False(target.ExcludeFromStatistics);
    }

    [Fact]
    public void 同步DTO_只有开关不同也算内容不一致_否则这次修改会被当成没变而不同步()
    {
        var entity = SampleCategory(false);
        var sameButFlag = CategorySyncDto.FromEntity(entity);
        sameButFlag.ExcludeFromStatistics = true;

        Assert.False(sameButFlag.ContentEquals(entity));
        Assert.True(CategorySyncDto.FromEntity(entity).ContentEquals(entity));
    }

    [Fact]
    public void 同步DTO_老客户端的载荷里没有开关字段_按不排除处理()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // 新版本的载荷带着开关；老客户端的载荷 = 同一份载荷去掉开关字段（同步 JSON 走 Web 命名：camelCase）
        var payload = JsonSerializer.SerializeToNode(CategorySyncDto.FromEntity(SampleCategory(true)), options)!.AsObject();
        Assert.True(payload["excludeFromStatistics"]!.GetValue<bool>());
        Assert.True(payload.Remove("excludeFromStatistics"));

        var fromOldClient = payload.Deserialize<CategorySyncDto>(options)!;

        Assert.False(fromOldClient.ExcludeFromStatistics);
        Assert.Equal("存款", fromOldClient.Name);
    }
}
