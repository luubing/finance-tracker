using System.Security.Cryptography;
using System.Text.Json;
using FinanceTracker.Api.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// S14：仓库提交的 appsettings.json 带有公开的占位 JWT 密钥。
/// 非 Development 环境漏覆盖时必须拒绝启动，否则任何人都能凭公开占位值伪造 JWT。
/// </summary>
public sealed class StartupSecurityChecksTests
{
    private const string Placeholder = "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
    // 运行时随机生成：不在仓库里放高熵字面量（会被 gitleaks、GitHub 密钥扫描等反复误报）
    private static readonly string RealLookingKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    [Fact]
    public void 非Development环境_占位密钥_拒绝启动并给出可操作的修复指引()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => StartupSecurityChecks.EnsureJwtSecretKeyIsNotPlaceholder(Placeholder, isDevelopment: false));

        Assert.Contains("Jwt__SecretKey", ex.Message);
        Assert.Contains("appsettings.Production.json", ex.Message);
    }

    [Fact]
    public void 占位密钥即使大小写或尾部被改动_也能识别()
    {
        Assert.True(StartupSecurityChecks.IsPlaceholderJwtSecretKey("yoursupersecretkey-changed-tail-0123456789"));
        Assert.True(StartupSecurityChecks.IsPlaceholderJwtSecretKey("YOURSUPERSECRETKEY"));
    }

    [Fact]
    public void Development环境_占位密钥_允许启动()
    {
        StartupSecurityChecks.EnsureJwtSecretKeyIsNotPlaceholder(Placeholder, isDevelopment: true);
    }

    [Fact]
    public void 非Development环境_已覆盖的真实密钥_允许启动()
    {
        StartupSecurityChecks.EnsureJwtSecretKeyIsNotPlaceholder(RealLookingKey, isDevelopment: false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空密钥不属于占位值_由既有的缺失校验负责(string? secretKey)
    {
        Assert.False(StartupSecurityChecks.IsPlaceholderJwtSecretKey(secretKey));
    }

    /// <summary>
    /// 把自检与仓库里真实的 appsettings.json 绑定：有人把占位文本改成别的写法而忘了同步自检时，这里会失败，
    /// 而不是让自检悄悄失效。
    /// </summary>
    [Fact]
    public void 仓库appsettings_json中的占位密钥_必须被自检识别()
    {
        var path = Path.Combine(FindRepositoryRoot(), "server", "FinanceTracker.Api", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var secretKey = document.RootElement.GetProperty("Jwt").GetProperty("SecretKey").GetString();

        Assert.True(
            StartupSecurityChecks.IsPlaceholderJwtSecretKey(secretKey),
            "appsettings.json 里的 Jwt:SecretKey 不再是自检能识别的占位值：要么改回占位前缀，要么同步更新 StartupSecurityChecks");
        Assert.Throws<InvalidOperationException>(
            () => StartupSecurityChecks.EnsureJwtSecretKeyIsNotPlaceholder(secretKey, isDevelopment: false));
    }

    [Theory]
    [InlineData("Host=localhost;Database=finance_tracker;Username=postgres;Password=postgres", true)]
    [InlineData("Host=localhost;Database=x;Username=postgres;Password=postgres;Port=5432", true)]
    [InlineData("Host=localhost;Database=x;Username=postgres;PASSWORD = postgres ", true)]
    [InlineData("Host=localhost;Database=x;Username=postgres;Pwd=postgres", true)]
    [InlineData("Host=localhost;Database=x;Username=postgres;Password=postgres123", false)]
    [InlineData("Host=localhost;Database=x;Username=postgres;Password=S3cret!Value", false)]
    [InlineData("Host=localhost;Database=postgres;Username=app", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 连接串默认口令识别(string? connectionString, bool expected)
    {
        Assert.Equal(expected, StartupSecurityChecks.UsesDefaultDatabasePassword(connectionString));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "FinanceTracker.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("未能从测试输出目录向上找到仓库根目录（FinanceTracker.sln）");
    }
}
