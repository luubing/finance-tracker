using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Web.Services;
using Masa.Blazor.Presets;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

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

// 配置 EF Core + SQLite (本地数据库)
// 迁移程序集指向 SQLite 专用项目（与 API 的 Npgsql 迁移分离）
var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "finance_tracker.db");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}", b =>
        b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite")));

// 注册 DbContext 接口
builder.Services.AddScoped<IApplicationDbContext>(provider =>
    provider.GetRequiredService<ApplicationDbContext>());

// 注册网络服务（Web 端使用简单实现）- 必须在 SyncService 之前注册
builder.Services.AddSingleton<INetworkService, WebNetworkService>();

// 注册短信服务（Web 端不支持短信读取，使用空实现避免页面崩溃）
builder.Services.AddSingleton<ISmsService, NoOpSmsService>();

// 注册通知使用权服务与待确认账单服务（Web 端不支持自动捕获，使用空实现避免页面崩溃）
builder.Services.AddSingleton<INotificationListenerPermissionService, NoOpNotificationListenerPermissionService>();
builder.Services.AddSingleton<IPendingBillService, NoOpPendingBillService>();

// 注册业务服务
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPresetDataService, PresetDataService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IPaymentChannelService, PaymentChannelService>();
builder.Services.AddScoped<IBillService, BillService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddSingleton<ISyncQueueService, SyncQueueService>();

// 注册 HttpClient - 从配置文件读取 Api 地址
var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5270";

// 注册应用服务
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<BillEventService>();
builder.Services.AddScoped<HttpService>();
builder.Services.AddScoped<ICloudSyncClient>(sp =>
    new HttpCloudSyncClient(sp.GetRequiredService<HttpService>(), apiBaseUrl));
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// 初始化数据库和预设数据
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await context.Database.MigrateAsync();

    var presetService = scope.ServiceProvider.GetRequiredService<IPresetDataService>();
    await presetService.InitializePresetDataAsync();
}

app.Run();

/// <summary>
/// Web 端网络服务实现
/// </summary>
public class WebNetworkService : INetworkService
{
    public event EventHandler<bool>? ConnectivityChanged;

    public bool IsConnected()
    {
        // Web 端始终认为有网络
        return true;
    }
}
