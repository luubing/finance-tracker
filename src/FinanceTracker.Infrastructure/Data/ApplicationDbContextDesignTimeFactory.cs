using FinanceTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace FinanceTracker.Infrastructure.Data;

/// <summary>
/// 设计时工厂：供 dotnet-ef 在不启动宿主应用（MAUI/Api/Web.Server）的情况下
/// 创建 ApplicationDbContext 并生成迁移。默认使用 SQLite
/// （迁移程序集 FinanceTracker.Infrastructure.Sqlite 所需）；
/// 生成 Npgsql 迁移时可通过环境变量切换：$env:FT_DB_PROVIDER='postgresql'。
/// </summary>
public class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("FT_DB_PROVIDER") ?? "sqlite";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=design_time");
        }
        else
        {
            optionsBuilder.UseSqlite("Data Source=design_time.db",
                b => b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite"));
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}