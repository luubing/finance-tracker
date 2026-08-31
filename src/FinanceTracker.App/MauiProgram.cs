using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 配置 Blazor WebView
        builder.Services.AddMauiBlazorWebView();

        // 配置 EF Core + SQLite (本地数据库)
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "finance_tracker.db");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // 注册 DbContext 接口
        builder.Services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());

        // 注册业务服务
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IPresetDataService, PresetDataService>();
        builder.Services.AddScoped<ICategoryService, CategoryService>();
        builder.Services.AddScoped<IPaymentChannelService, PaymentChannelService>();
        builder.Services.AddScoped<IBillService, BillService>();
        builder.Services.AddScoped<IStatisticsService, StatisticsService>();
        builder.Services.AddScoped<ISyncService, SyncService>();

        // 注册应用服务
        builder.Services.AddScoped<AuthenticationService>();
        builder.Services.AddScoped<HttpService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // 初始化数据库和预设数据
        InitializeDatabaseAsync(app.Services).Wait();

        return app;
    }

    private static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();

        var presetService = scope.ServiceProvider.GetRequiredService<IPresetDataService>();
        await presetService.InitializePresetDataAsync();
    }
}
