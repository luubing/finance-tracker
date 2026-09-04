using FinanceTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinanceTracker.Infrastructure.Sqlite;

/// <summary>
/// 设计时工厂：供 dotnet-ef 以本项目为启动项目生成 SQLite 迁移
/// （避免为了加载 MigrationsAssembly 而依赖 MAUI/Web 宿主）。
/// </summary>
public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlite("Data Source=design_time.db",
            b => b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite"));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
