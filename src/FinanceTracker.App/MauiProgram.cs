using FinanceTracker.App.Services;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

#if ANDROID
using FinanceTracker.App.Platforms.Android.Services;
#endif

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
        // 迁移程序集指向 SQLite 专用项目（与 API 的 Npgsql 迁移分离）
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "finance_tracker.db");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}", b =>
                b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite")));

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
        builder.Services.AddSingleton<ISyncQueueService, SyncQueueService>();

        // 注册平台服务
        builder.Services.AddSingleton<INetworkService, NetworkService>();

#if ANDROID
        builder.Services.AddSingleton<ISmsService, SmsService>();
#endif

        // 注册应用服务
        builder.Services.AddScoped<AuthenticationService>();
        builder.Services.AddScoped<HttpService>();
        builder.Services.AddSingleton<BackgroundSyncService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // 初始化数据库和预设数据（使用 Task.Run 避免死锁）
        Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync();

            var presetService = scope.ServiceProvider.GetRequiredService<IPresetDataService>();
            await presetService.InitializePresetDataAsync();
        }).GetAwaiter().GetResult();

        return app;
    }
}
