using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// 配置 MASA Blazor
builder.Services.AddMasaBlazor();

// 配置 EF Core + SQLite (本地数据库)
var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "finance_tracker.db");
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
builder.Services.AddSingleton<ISyncQueueService, SyncQueueService>();

// 注册网络服务（Web 端使用简单实现）
builder.Services.AddSingleton<INetworkService, WebNetworkService>();

// 注册应用服务
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<HttpService>();

// 注册 HttpClient
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("https://localhost:5001/") });

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
    context.Database.Migrate();

    var presetService = scope.ServiceProvider.GetRequiredService<IPresetDataService>();
    presetService.InitializePresetDataAsync().Wait();
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
