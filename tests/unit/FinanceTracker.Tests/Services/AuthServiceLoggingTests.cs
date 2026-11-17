using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 手机号是唯一标识，又因为没有验证码而等同于凭据；错误日志现在还能在"我的 → 错误日志"里导出，
/// 所以登录/注册失败时不能把完整手机号写进日志（格式化消息和结构化属性都不行）。
/// </summary>
public sealed class AuthServiceLoggingTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await SqliteTestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /// <summary>保存必然失败的上下文：用来触发 AuthService 的 catch 分支</summary>
    private sealed class SaveFailsDbContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("模拟保存失败");
    }

    [Fact]
    public async Task 注册失败时_错误日志里不出现完整手机号()
    {
        const string phone = "13912345678";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_db.Connection, b => b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite"))
            .Options;
        await using var failingContext = new SaveFailsDbContext(options);
        var logger = new ListLogger<AuthService>();
        var service = new AuthService(failingContext, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterOrLoginAsync(phone));

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.DoesNotContain(phone, entry.Message);
        Assert.DoesNotContain(phone, entry.Properties);
        // 仍要能定位是哪一类号码出问题：保留前 3 后 4（与"我的"页面的脱敏展示一致）
        Assert.Contains("139****5678", entry.Message);
    }
}
