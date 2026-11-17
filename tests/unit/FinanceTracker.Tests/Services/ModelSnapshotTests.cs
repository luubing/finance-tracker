using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 模型与迁移快照一致性：EF Core 9+ 在 <c>Migrate()</c> 时如果发现当前模型与最新迁移快照有差异会直接抛
/// <c>PendingModelChangesWarning</c>，导致应用/云端启动失败。
/// 给 <c>Bill.TransactionTime</c> 加值转换器（P6）这类"只改读写不改列"的变更，必须保证不会引入快照差异；
/// 反过来，今后如果有人改了实体却忘了生成迁移，这里也会第一时间报出来。
/// </summary>
public sealed class ModelSnapshotTests
{
    [Fact]
    public async Task SQLite本地库_模型与迁移快照一致()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();

        Assert.False(
            db.Context.Database.HasPendingModelChanges(),
            "SQLite 模型与 FinanceTracker.Infrastructure.Sqlite 的迁移快照不一致：请生成新的 SQLite 迁移");
    }

    [Fact]
    public void PostgreSQL云端库_模型与迁移快照一致()
    {
        // 只构建模型并与快照比较，不会真正连接数据库（显式指定版本，避免提供程序为探测版本而建立连接）
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=model_snapshot_check;Username=nobody;Password=nobody",
                b =>
                {
                    b.MigrationsAssembly("FinanceTracker.Infrastructure");
                    b.SetPostgresVersion(new Version(16, 0));
                })
            .Options;

        using var context = new ApplicationDbContext(options);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "PostgreSQL 模型与 FinanceTracker.Infrastructure 的迁移快照不一致：请生成新的 PostgreSQL 迁移");
    }
}
