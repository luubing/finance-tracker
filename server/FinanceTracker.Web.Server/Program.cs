using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Web.Design;
using FinanceTracker.Web.Models;
using FinanceTracker.Web.Server;
using FinanceTracker.Web.Services;
using Masa.Blazor.Presets;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// 配置 MASA Blazor（全局默认值与主题色统一在 UseFinanceTrackerDesign，MAUI 宿主调用同一个方法；主题 primary 与设计令牌品牌色同步）
builder.Services.AddMasaBlazor(options => options.UseFinanceTrackerDesign()).AddMobileComponents();

// 配置 EF Core + SQLite (本地数据库)
// 迁移程序集指向 SQLite 专用项目（与 API 的 Npgsql 迁移分离）
var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "finance_tracker.db");
// 与 MAUI 宿主一致使用 DbContextFactory：待确认账单服务是单例（真机上被后台捕获线程调用），每次操作从工厂取独立上下文；
// AddDbContextFactory 同时会注册 Scoped 的 ApplicationDbContext 供 Blazor 页面使用
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}", b =>
        b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite")));

// 注册 DbContext 接口
builder.Services.AddScoped<IApplicationDbContext>(provider =>
    provider.GetRequiredService<ApplicationDbContext>());

// 注册网络服务（Web 端使用简单实现）- 必须在 SyncService 之前注册
builder.Services.AddSingleton<INetworkService, WebNetworkService>();

// 短信服务与通知使用权服务（Web 端不支持短信读取 / 自动捕获，使用空实现避免页面崩溃）。
// E2E 专用：开发环境（Development）下配置 E2E:FakeCapture=true（tests/e2e/server.ps1 启动时打开）时换成可由 E2E 设置状态的假实现，
// 待确认账单页的「均已开启」状态与短信识别页的读取流程才能在浏览器里验收；平时不打开，行为不变（见 E2ECapture）
var fakeCapture = E2ECapture.IsEnabled(builder.Configuration, builder.Environment);
if (fakeCapture)
{
    E2ECapture.AddServices(builder.Services);
}
else
{
    builder.Services.AddSingleton<ISmsService, NoOpSmsService>();
    builder.Services.AddSingleton<INotificationListenerPermissionService, NoOpNotificationListenerPermissionService>();
}
// 待确认账单服务：Web 端虽然没有短信 / 通知栏自动捕获，但读写的是同一张本地库表——
// 使用与 App 一致的 SQLite 实现，首页的待确认横幅 / 角标与「待确认账单」页才能在浏览器里验收（向 PendingBills 表写入数据即可模拟捕获）
builder.Services.AddSingleton<IPendingBillService, FinanceTracker.Infrastructure.Services.PendingBillService>();

// 注册预算超支通知服务（Web 端无本地通知，使用空实现）与预算预警服务
builder.Services.AddSingleton<IBudgetNotificationService, NoOpBudgetNotificationService>();

// 沉浸式：浏览器没有原生安全区与状态栏，使用空实现（安全区回落 CSS env()=0；E2E 通过 --ft-safe-* 注入模拟刘海 / 手势条）
builder.Services.AddSingleton<ISafeAreaService, NoOpSafeAreaService>();
builder.Services.AddSingleton<IStatusBarService, NoOpStatusBarService>();

// 渲染档位：浏览器没有「低端设备」判定，使用空实现（恒为不降级；浏览器自己按 prefers-reduced-transparency 媒体特性降级）。
// E2E 专用：配置 E2E:FakeCapture=true 时换成可由 E2E 驱动的假实现，验证「宿主判定 → UiRuntime → 根开关 → 令牌」的整条链路（见 E2ERendering）
if (fakeCapture)
{
    E2ERendering.AddServices(builder.Services);
}
else
{
    builder.Services.AddSingleton<IRenderingProfileService, NoOpRenderingProfileService>();
}

// 触感反馈：浏览器没有可靠的触感能力，使用空实现
builder.Services.AddSingleton<IHapticFeedbackService, NoOpHapticFeedbackService>();

// 注册业务服务
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPresetDataService, PresetDataService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IPaymentChannelService, PaymentChannelService>();
builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
// 账本成员服务：管理操作走云端 API，权限判定读本地缓存（云端数据库为真相源）
builder.Services.AddScoped<ILedgerMemberService, HttpLedgerMemberService>();
builder.Services.AddScoped<IBillService, BillService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<IUserDefaultService, UserDefaultService>();
// 日志查看：共享的「我的」页在两个宿主里都有"错误日志"入口，Web.Server 也必须能解析该服务，
// 否则点进 /log-viewer 会因缺少注册而抛 DI 异常。Web.Server 没有配置 Serilog 文件日志，页面会显示为空。
// E2E 专用：开发环境（Development）下配置 E2E:LogDirectory（tests/e2e/server.ps1 启动时设置）时读那个专用目录，E2E 才能造日志数据、验证「清除日志」而不碰真实日志（见 E2ELogs）
var e2eLogDirectory = E2ELogs.Directory(builder.Configuration, builder.Environment);
builder.Services.AddScoped<ILogViewerService>(sp =>
    new LogViewerService(sp.GetRequiredService<ILogger<LogViewerService>>(), e2eLogDirectory));

// 注册语音记账服务：
// - 文本解析（中文口语 → 账单草稿）使用规则解析器，服务端/客户端通用
// - BillVoiceInputService 负责"语音 → 文本"的分发（服务端为空实现时自动降级浏览器 Web Speech API）
// - Blazor Server 服务端进程不做真实语音识别（空实现），实际识别走浏览器 Web Speech API（JS interop）
builder.Services.AddScoped<IBillVoiceParser, BillVoiceParser>();
builder.Services.AddSingleton<ISpeechService, NoOpSpeechService>();
builder.Services.AddScoped<BillVoiceInputService>();

// 注册 HttpClient - 从配置文件读取 Api 地址
var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5270";

// 注册应用服务
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<BillEventService>();
// 页面读库的单车道：Tab 根页面常驻，一次账单变更会让多个页面同时重新加载，它们共用同一个 DbContext，必须串行（见 PageDataGate）
builder.Services.AddScoped<PageDataGate>();
// 「记一笔」Sheet 的开关：Tab 栏的 ＋、首页 / 账单页的入口调用它，布局里常驻的 AddBillSheet 订阅它（见 AddBillSheetService）
builder.Services.AddScoped<AddBillSheetService>();
// 「请统计 Tab 落在某个分段」的一次性请求：旧地址 /trend、/annual-report 的跳转页登记，统计页取走（见 StatisticsSegmentRequest）
builder.Services.AddScoped<StatisticsSegmentRequest>();
builder.Services.AddScoped<BudgetAlertService>();
builder.Services.AddScoped<HttpService>();
// 云端可用性的短期记忆（单例，所有电路共享）：云端连不上时，页面加载顺带的成员缓存刷新只等 3 秒，
// 失败后 30 秒内直接读本地缓存，不再每次都白等连接超时
builder.Services.AddSingleton(new CloudAvailability());
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

// 旧地址（趋势分析 / 年度报告）：统计 Tab 的「趋势」「年度」分段取代了它们。
// 直接访问（书签、刷新）在 HTTP 层 302 到新地址，浏览器历史里不留旧地址；
// 应用内导航到旧地址（页面栈 Push）由 Web 项目里的 StatisticsRedirect 页处理，MAUI 宿主也只有这一条路
foreach (var (path, segment) in StatisticsPresentation.LegacyRoutes)
{
    var target = StatisticsPresentation.UriForSegment(segment);
    app.MapGet(path, () => Results.Redirect(target));
}

// 旧地址 /add-bill（新建账单页）与 /add-bill/{BillId}（编辑账单页）：「记一笔」与编辑现在是布局级的同一个 Sheet，不再是页面。
// 直接访问 302 到 /?add-bill=1、/?add-bill={BillId}，Sheet 在电路接管后的第一次渲染读到这个参数升起（见 AddBillSheetService.ReadOpenRequest）。
// 编辑地址限定为 GUID：别的写法（/add-bill/abc）不属于旧地址，落到应用的「未找到」页
app.MapGet(AddBillSheetService.LegacyRoute, () => Results.Redirect(AddBillSheetService.OpenUri));
app.MapGet(AddBillSheetService.LegacyEditRoute, (Guid billId) => Results.Redirect(AddBillSheetService.EditUri(billId)));

if (fakeCapture)
{
    E2ECapture.MapEndpoints(app);
    E2ERendering.MapEndpoints(app);
}

if (e2eLogDirectory is not null)
{
    E2ELogs.MapEndpoints(app, e2eLogDirectory);
}

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
    // 始终在线，不会有连通性变化：空访问器（订阅无效果），避免"声明了从不触发的事件"的 CS0067 警告
    public event EventHandler<bool>? ConnectivityChanged
    {
        add { }
        remove { }
    }

    public bool IsConnected()
    {
        // Web 端始终认为有网络
        return true;
    }
}
