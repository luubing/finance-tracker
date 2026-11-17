using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 测试用 SQLite 本地库：真实 <see cref="ApplicationDbContext"/> + SQLite 迁移 + 预设数据，外键约束开启。
/// 使用真实 Provider 而不是 InMemory：InMemory 不校验外键，复现不出外键类问题。
/// 共享同一条已打开的内存连接，等价于一个长生命周期的本地库（与 MAUI / Blazor Server 的 DbContext 生命周期一致）。
/// </summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private SqliteTestDatabase(SqliteConnection connection, ApplicationDbContext context)
    {
        Connection = connection;
        Context = context;
    }

    /// <summary>底层连接（用于直接执行 SQL 断言）</summary>
    public SqliteConnection Connection { get; }

    /// <summary>应用数据库上下文</summary>
    public ApplicationDbContext Context { get; }

    /// <summary>默认登录用户</summary>
    public User User { get; private set; } = null!;

    /// <summary>预设的支出分类</summary>
    public Category ExpenseCategory { get; private set; } = null!;

    /// <summary>预设的支付渠道</summary>
    public PaymentChannel Channel { get; private set; } = null!;

    /// <summary>
    /// 创建并初始化测试库（迁移 + 预设数据 + 默认用户）
    /// </summary>
    public static async Task<SqliteTestDatabase> CreateAsync(string phoneNumber = "13800138000")
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection, b => b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite"))
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
        await new PresetDataService(context).InitializePresetDataAsync();

        var database = new SqliteTestDatabase(connection, context);
        database.User = await database.CreateUserAsync(phoneNumber);
        database.ExpenseCategory = await context.Categories
            .FirstAsync(c => c.Type == BillType.Expense && c.UserId == null);
        database.Channel = await context.PaymentChannels.FirstAsync(c => c.UserId == null);
        return database;
    }

    /// <summary>
    /// 创建（或登录）一个本地用户
    /// </summary>
    public Task<User> CreateUserAsync(string phoneNumber) =>
        new AuthService(Context, NullLogger<AuthService>.Instance).RegisterOrLoginAsync(phoneNumber);

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
