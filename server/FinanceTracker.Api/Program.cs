using System.Text;
using FinanceTracker.Api.Middleware;
using FinanceTracker.Api.Models;
using FinanceTracker.Api.Services;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// 配置 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 配置 JWT
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

// 验证 JWT 配置
if (jwtSettings == null || string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
{
    throw new InvalidOperationException("JWT 配置缺失或 SecretKey 为空");
}

if (jwtSettings.SecretKey.Length < 32)
{
    throw new InvalidOperationException("JWT SecretKey 长度必须至少 32 个字符");
}

if (string.IsNullOrWhiteSpace(jwtSettings.Issuer) || string.IsNullOrWhiteSpace(jwtSettings.Audience))
{
    throw new InvalidOperationException("JWT Issuer 和 Audience 不能为空");
}

// 非 Development 环境禁止沿用仓库内公开的占位密钥（漏覆盖时任何人都能伪造 JWT）
StartupSecurityChecks.EnsureJwtSecretKeyIsNotPlaceholder(jwtSettings.SecretKey, builder.Environment.IsDevelopment());

// 手机号白名单（可选，默认关闭）：云端登录只凭手机号、没有验证码，公开部署时可用它把云端限定在自己人范围内。
// 配置 Auth:AllowedPhoneNumbers（环境变量 Auth__AllowedPhoneNumbers，英文逗号分隔）后，名单外的手机号不能登录 / 注册，
// 已签发的令牌也会被拦下（见 PhoneAllowListMiddleware）。详见 docs/adr/0009-cloud-access-hardening.md 与 SECURITY.md
var phoneAllowList = PhoneAllowList.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(phoneAllowList);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
    };
});

builder.Services.AddAuthorization();

// 配置 EF Core + PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 注册 DbContext 接口
builder.Services.AddScoped<IApplicationDbContext>(provider =>
    provider.GetRequiredService<ApplicationDbContext>());

// 注册网络服务
builder.Services.AddSingleton<INetworkService, ApiNetworkService>();

// 注册业务服务
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPresetDataService, PresetDataService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IPaymentChannelService, PaymentChannelService>();
builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ILedgerMemberService, LedgerMemberService>();
builder.Services.AddScoped<IBillService, BillService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICsvParserService, CsvParserService>();
builder.Services.AddScoped<IUserDefaultService, UserDefaultService>();

// 配置 CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 数据库口令仍是默认值时只告警（本地开发连接串常用默认口令，不阻止启动）
if (!app.Environment.IsDevelopment() &&
    StartupSecurityChecks.UsesDefaultDatabasePassword(app.Configuration.GetConnectionString("DefaultConnection")))
{
    app.Logger.LogWarning(
        "数据库连接串仍在使用默认口令 postgres，请通过环境变量 ConnectionStrings__DefaultConnection 或 appsettings.Production.json 覆盖");
}

// 手机号白名单的启动状态：启用时只记数量（手机号等同登录凭据，不进日志）；名单里有格式不对的条目时告警——
// 它们永远匹配不上任何人，多半是分隔符写错了（要用英文逗号分隔，每个号码 11 位）
if (phoneAllowList.IsEnabled)
{
    app.Logger.LogInformation("手机号白名单已启用，共 {Count} 个号码（{Key}）", phoneAllowList.Count, PhoneAllowList.ConfigurationKey);
    if (phoneAllowList.InvalidEntryCount > 0)
    {
        app.Logger.LogWarning(
            "手机号白名单里有 {Count} 个条目不是 11 位手机号，永远匹配不上任何人（请用英文逗号分隔，每个号码 11 位）",
            phoneAllowList.InvalidEntryCount);
    }
}
else if (!app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(
        "手机号白名单未启用：任何人只要知道一个手机号，就能注册并登录对应账号。仅自用 / 家用的部署建议配置 {Key}（环境变量 Auth__AllowedPhoneNumbers），详见 SECURITY.md",
        PhoneAllowList.ConfigurationKey);
}

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 启用 Swagger：Development 默认开启，其他环境默认关闭（生产需要时设置环境变量 Swagger__Enabled=true）
app.UseSwaggerWhenEnabled();

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
// 必须在认证之后：已认证请求的令牌手机号不在白名单内就回 403（名单未启用时什么都不做）
app.UseMiddleware<PhoneAllowListMiddleware>();
app.UseAuthorization();

app.MapControllers();

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
/// API 端网络服务实现
/// </summary>
public class ApiNetworkService : INetworkService
{
    // 始终在线，不会有连通性变化：空访问器（订阅无效果），避免"声明了从不触发的事件"的 CS0067 警告
    public event EventHandler<bool>? ConnectivityChanged
    {
        add { }
        remove { }
    }

    public bool IsConnected()
    {
        // API 端始终认为有网络
        return true;
    }
}
