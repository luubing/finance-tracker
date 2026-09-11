using FinanceTracker.App.Services;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Infrastructure.Services;
using FinanceTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Masa.Blazor.Presets;
using Serilog;
using Serilog.Formatting.Compact;


#if ANDROID
using FinanceTracker.App.Platforms.Android.Services;
#endif

namespace FinanceTracker.App;

public static class MauiProgram
{
    /// <summary>
    /// 云端 API 基址（只需域名/根路径，不要带 /api 后缀，因为 HttpCloudSyncClient 会统一拼上 /api/）。
    /// Android 模拟器通过 10.0.2.2 访问宿主机；真机（或生产环境）请改为 API 的实际域名/地址。
    /// 注意：端口必须与 FinanceTracker.Api 实际绑定端口一致，否则同步会因连接失败而无数据。
    /// 开发环境（docker-compose）API 监听 5270；若用 dotnet run 则本地开发默认为 5065，请同步修改。
    /// 同时 Android 已允许明文 HTTP（见 AndroidManifest.xml 的 usesCleartextTraffic）。
    /// </summary>
    private const string CloudApiBaseUrl = "https://finance.peiran.site/";

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

        // 配置 MASA Blazor
        builder.Services.AddMasaBlazor(options =>
        {
            // 配置全局选项
            options.Defaults = new Dictionary<string, IDictionary<string, object?>?>()
            {
                { nameof(PStackPageBar), new Dictionary<string, object?>()
                    {
                        { nameof(PStackPageBar.Height), 44 }
                    }
                }
            };
        }).AddMobileComponents();

        // 配置 Serilog 日志（仅记录错误级别）
        var logPath = Path.Combine(FileSystem.AppDataDirectory, "Logs", "app-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Error()  // 仅记录 Error 及以上级别（Error、Fatal）
            .Enrich.WithProperty("App", "FinanceTracker")
            .Enrich.WithProperty("Platform", DeviceInfo.Platform.ToString())
            .Enrich.WithProperty("DeviceModel", DeviceInfo.Model)
            .Enrich.WithProperty("DeviceManufacturer", DeviceInfo.Manufacturer)
            .Enrich.WithProperty("AppVersion", AppInfo.VersionString)
            .Enrich.WithProperty("OSVersion", DeviceInfo.VersionString)
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,  // 保留最近14天的日志
                fileSizeLimitBytes: 20 * 1024 * 1024,  // 单个文件最大20MB
                rollOnFileSizeLimit: true,
                formatter: new CompactJsonFormatter(),  // JSON 格式，便于结构化查询
                buffered: false,  // 立即写入，不缓冲（调试时确保日志不丢失）
                flushToDiskInterval: TimeSpan.FromSeconds(1)  // 每秒刷新到磁盘
            )
            .CreateLogger();

        // 将 Serilog 添加到日志管道
        builder.Logging.AddSerilog(dispose: true);

        // 捕获全局未处理异常
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            Log.Fatal(exception, "全局未处理异常（AppDomain.UnhandledException）");
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Log.Error(args.Exception, "未观察到的任务异常（TaskScheduler.UnobservedTaskException）");
            args.SetObserved();
        };

        // 配置 EF Core + SQLite (本地数据库)
        // 迁移程序集指向 SQLite 专用项目（与 API 的 Npgsql 迁移分离）
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "finance_tracker.db");
        // 使用 DbContextFactory（单例）注册：PendingBillService 为单例服务（Android 后台捕获线程调用），
        // AddDbContextFactory 同时会注册 Scoped 的 ApplicationDbContext 供 Blazor 页面使用
        builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
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
        builder.Services.AddScoped<ILedgerService, LedgerService>();
        builder.Services.AddScoped<IBudgetService, BudgetService>();
        builder.Services.AddScoped<IUserDefaultService, UserDefaultService>();
        // 账本成员服务：管理操作走云端 API，权限判定读本地缓存（云端数据库为真相源）
        builder.Services.AddScoped<ILedgerMemberService, HttpLedgerMemberService>();
        builder.Services.AddScoped<IBillService, BillService>();
        builder.Services.AddScoped<IStatisticsService, StatisticsService>();
        builder.Services.AddScoped<ISyncService, SyncService>();
        builder.Services.AddSingleton<ISyncQueueService, SyncQueueService>();

        // 预算超支本地通知（App 端原生实现：Android/iOS，其他平台内部静默跳过）
        builder.Services.AddSingleton<IBudgetNotificationService, BudgetNotificationService>();

        // 注册平台服务
        builder.Services.AddSingleton<INetworkService, NetworkService>();

#if ANDROID
        builder.Services.AddSingleton<ISmsService, SmsService>();
        // 通知使用权权限服务（方案一：通知栏支付监听，只能引导用户到系统设置开启）
        builder.Services.AddSingleton<INotificationListenerPermissionService, NotificationListenerPermissionService>();
        // 自动捕获的待确认账单：单例服务 + SQLite 持久化（重启后未确认记录不丢失）
        builder.Services.AddSingleton<IPendingBillService, PendingBillService>();

#else
        builder.Services.AddSingleton<ISmsService, NoOpSmsService>();
        builder.Services.AddSingleton<INotificationListenerPermissionService, NoOpNotificationListenerPermissionService>();
        builder.Services.AddSingleton<IPendingBillService, NoOpPendingBillService>();
#endif

        // 语音记账：App 端原生语音识别（Android SpeechRecognizer / iOS SFSpeechRecognizer），其他平台空实现
#if ANDROID
        builder.Services.AddSingleton<ISpeechService, SpeechService>();
#elif IOS
        builder.Services.AddSingleton<ISpeechService, FinanceTracker.App.Platforms.iOS.Services.SpeechService>();
#else
        builder.Services.AddSingleton<ISpeechService, NoOpSpeechService>();
#endif

        // 注册应用服务
        builder.Services.AddScoped<AuthenticationService>();
        builder.Services.AddScoped<BillEventService>();
        builder.Services.AddScoped<BudgetAlertService>();
        builder.Services.AddScoped<HttpService>();
        builder.Services.AddScoped<ILogViewerService, LogViewerService>();
        // 语音记账：文本解析（中文口语 → 账单草稿）+ 语音录入桥接服务
        // （App 端 SpeechService 注册进 BillVoiceInputService 后走原生识别，见 ISpeechService 注册）
        builder.Services.AddScoped<IBillVoiceParser, BillVoiceParser>();
        builder.Services.AddScoped<BillVoiceInputService>();
        builder.Services.AddScoped<ICloudSyncClient>(sp =>
            new HttpCloudSyncClient(sp.GetRequiredService<HttpService>(), CloudApiBaseUrl));
        builder.Services.AddSingleton<BackgroundSyncService>();

        // 注册 HttpClient（设置基址以支持相对路径的 API 调用，如导入接口）
        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(CloudApiBaseUrl) });

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

#if ANDROID
        // Android 后台组件（SmsBroadcastReceiver / NotificationCaptureService）不走 DI 容器，
        // 通过静态入口获取与容器中相同的待确认账单单例，保证后台捕获能实时反映到页面
        PendingBillServiceLocator.Instance = app.Services.GetRequiredService<IPendingBillService>();
#endif

        // 异步初始化数据库（不阻塞主线程）
        InitializeDatabaseAsync(app);

        return app;
    }

    private static async void InitializeDatabaseAsync(MauiApp app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync();

            var presetService = scope.ServiceProvider.GetRequiredService<IPresetDataService>();
            await presetService.InitializePresetDataAsync();
        }
        catch (Exception ex)
        {
            // 记录错误但不阻止应用启动。
            // 注意：必须输出完整异常（含类型与调用栈），之前只输出 ex.Message，导致
            // iOS 上的 EF Core 初始化错误（NativeAOT 模型构建失败）被掩盖，
            // 直到用户登录时才以"登录失败"的形式暴露出来。
            Log.Error(ex, "数据库初始化失败");
        }
    }
}
