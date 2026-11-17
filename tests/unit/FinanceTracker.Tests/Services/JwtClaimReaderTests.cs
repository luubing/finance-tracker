using System.Text;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using FinanceTracker.Core.Entities;
using FinanceTracker.Web.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 客户端要靠 JWT 知道"云端把我当作哪个用户、这枚令牌属于哪个手机号"
/// （本地与云端的用户 Id 不同，同步时需要映射；换号登录后旧令牌必须识别出来并作废）。
/// 这里用服务端真实的 TokenService 签发 token，再交给客户端读取器解析：
/// 一旦服务端改了声明名/缩写规则，客户端读取器会在这里先红，而不是在线上悄悄读不到。
/// </summary>
public class JwtClaimReaderTests
{
    private static TokenService CreateServerTokenService() =>
        new(Options.Create(new JwtSettings
        {
            SecretKey = new string('k', 48),
            Issuer = "finance-tracker-tests",
            Audience = "finance-tracker-tests",
            ExpiryInMinutes = 60
        }));

    [Fact]
    public void 能解析服务端真实签发的token里的用户Id()
    {
        var user = new User { Id = Guid.NewGuid(), PhoneNumber = "13800138000" };

        var token = CreateServerTokenService().GenerateToken(user);

        Assert.Equal(user.Id, JwtClaimReader.TryReadUserId(token));
    }

    [Fact]
    public void 能解析服务端真实签发的token里的手机号()
    {
        var user = new User { Id = Guid.NewGuid(), PhoneNumber = "13800138000" };

        var token = CreateServerTokenService().GenerateToken(user);

        Assert.Equal("13800138000", JwtClaimReader.TryReadPhoneNumber(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]                 // 载荷不是合法的 base64url/JSON
    [InlineData("a.e30.c")]               // 载荷是空对象 {}：没有任何声明
    public void 读不到声明时返回null(string? token)
    {
        Assert.Null(JwtClaimReader.TryReadUserId(token));
        Assert.Null(JwtClaimReader.TryReadPhoneNumber(token));
    }

    [Fact]
    public void 兼容sub声明()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, JwtClaimReader.TryReadUserId(BuildToken($$"""{"sub":"{{id}}"}""")));
    }

    [Fact]
    public void 兼容完整URI形式的声明名()
    {
        var id = Guid.NewGuid();
        var userJson = $$"""{"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier":"{{id}}"}""";
        var phoneJson = """{"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/mobilephone":"13900000001"}""";

        Assert.Equal(id, JwtClaimReader.TryReadUserId(BuildToken(userJson)));
        Assert.Equal("13900000001", JwtClaimReader.TryReadPhoneNumber(BuildToken(phoneJson)));
    }

    [Fact]
    public void 用户声明不是Guid或是空Guid时返回null()
    {
        Assert.Null(JwtClaimReader.TryReadUserId(BuildToken("""{"nameid":"not-a-guid"}""")));
        Assert.Null(JwtClaimReader.TryReadUserId(BuildToken($$"""{"nameid":"{{Guid.Empty}}"}""")));
    }

    [Fact]
    public void 手机号声明为空字符串时返回null()
    {
        Assert.Null(JwtClaimReader.TryReadPhoneNumber(BuildToken("""{"phone_number":""}""")));
    }

    internal static string BuildToken(string payloadJson)
    {
        static string B64(string text) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{B64("""{"alg":"none"}""")}.{B64(payloadJson)}.sig";
    }
}
