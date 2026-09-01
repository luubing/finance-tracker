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
builder.Services.AddScoped<IBillService, BillService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICsvParserService, CsvParserService>();

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

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 启用 Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "记账本 API V1");
        c.RoutePrefix = string.Empty; // 在根路径访问 Swagger UI
    });
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
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
    public event EventHandler<bool>? ConnectivityChanged;

    public bool IsConnected()
    {
        // API 端始终认为有网络
        return true;
    }
}
