using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 登录接口上的手机号白名单（可选，默认关闭，见 <see cref="PhoneAllowList"/>）：
/// <list type="bullet">
/// <item>关闭时与此前完全一致：任何合法手机号都能注册 / 登录并拿到令牌</item>
/// <item>启用后名单外的号码回 403（已知身份但无权使用这个云端，不能是 401：客户端据此登出；也不能是 Forbid(string)：会 500），
/// 且必须发生在注册之前——被拒绝的号码不能在库里留下一个新用户</item>
/// <item>手机号格式校验仍然先于白名单（格式不对是 400，不是 403）</item>
/// </list>
/// 用真实的 SQLite + 迁移（与 BillsControllerLedgerRequiredTests 同一套基础设施）。
/// </summary>
public sealed class AuthControllerAllowListTests : IAsyncLifetime
{
    private const string Existing = "13800138000";   // SqliteTestDatabase 默认已注册的用户
    private const string Newcomer = "13900139000";   // 库里还没有的号码

    private static readonly TokenService Tokens = new(Options.Create(new JwtSettings
    {
        SecretKey = new string('k', 48),
        Issuer = "finance-tracker-tests",
        Audience = "finance-tracker-tests",
        ExpiryInMinutes = 60
    }));

    private SqliteTestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await SqliteTestDatabase.CreateAsync(Existing);

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private AuthController CreateController(PhoneAllowList allowList) =>
        new(
            new AuthService(_db.Context, NullLogger<AuthService>.Instance),
            Tokens,
            allowList,
            NullLogger<AuthController>.Instance);

    private Task<int> UserCountAsync() => _db.Context.Users.CountAsync();

    private static JsonElement ToJson(IActionResult result)
    {
        var value = Assert.IsAssignableFrom<ObjectResult>(result).Value;
        return JsonSerializer.SerializeToElement(value);
    }

    // ---------- 关闭（默认）：行为不变 ----------

    [Fact]
    public async Task 白名单关闭_新手机号照常注册并拿到令牌()
    {
        var before = await UserCountAsync();

        var result = await CreateController(PhoneAllowList.Disabled)
            .Login(new LoginRequest { PhoneNumber = Newcomer });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var json = ToJson(result);
        Assert.Equal(Newcomer, json.GetProperty("phoneNumber").GetString());
        Assert.Equal(Newcomer, JwtClaimReader.TryReadPhoneNumber(json.GetProperty("token").GetString()));
        Assert.Equal(before + 1, await UserCountAsync());
    }

    // ---------- 启用：名单内放行 ----------

    [Fact]
    public async Task 白名单启用_名单内的老用户可以登录()
    {
        var result = await CreateController(new PhoneAllowList(new[] { $"{Existing},{Newcomer}" }))
            .Login(new LoginRequest { PhoneNumber = Existing });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(_db.User.Id, ToJson(result).GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task 白名单启用_名单内的新手机号可以注册()
    {
        var before = await UserCountAsync();

        var result = await CreateController(new PhoneAllowList(new[] { Newcomer }))
            .Login(new LoginRequest { PhoneNumber = Newcomer });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(before + 1, await UserCountAsync());
    }

    // ---------- 启用：名单外拒绝 ----------

    [Fact]
    public async Task 白名单启用_名单外的手机号返回403和提示_且不在库里留下用户()
    {
        var before = await UserCountAsync();

        var result = await CreateController(new PhoneAllowList(new[] { Existing }))
            .Login(new LoginRequest { PhoneNumber = Newcomer });

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(PhoneAllowList.DeniedMessage, ToJson(result).GetProperty("message").GetString());
        Assert.Equal(before, await UserCountAsync());
        Assert.Null(await new AuthService(_db.Context, NullLogger<AuthService>.Instance).GetUserByPhoneNumberAsync(Newcomer));
    }

    [Fact]
    public async Task 白名单启用_已注册但不在名单内的用户同样被拒绝_不签发令牌()
    {
        var result = await CreateController(new PhoneAllowList(new[] { Newcomer }))
            .Login(new LoginRequest { PhoneNumber = Existing });

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.False(ToJson(result).TryGetProperty("token", out _));
    }

    [Fact]
    public async Task 白名单启用但名单写错_挡住所有人而不是悄悄放开()
    {
        // 用空格分隔两个号码是常见的手滑：只认逗号 / 分号 / 换行
        var allowList = new PhoneAllowList(new[] { $"{Existing} {Newcomer}" });

        var result = await CreateController(allowList).Login(new LoginRequest { PhoneNumber = Existing });

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ---------- 校验顺序 ----------

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("23800138000")]
    public async Task 手机号格式不对仍是400_先于白名单判断(string phoneNumber)
    {
        var result = await CreateController(new PhoneAllowList(new[] { Existing }))
            .Login(new LoginRequest { PhoneNumber = phoneNumber });

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
