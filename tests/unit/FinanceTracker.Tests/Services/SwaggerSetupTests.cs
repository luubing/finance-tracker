using System.Net;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// Swagger / SwaggerUI 默认只在 Development 开启：生产环境公开完整的接口清单会降低被探测的门槛
/// （登录只需要手机号、没有验证码）。显式配置 Swagger:Enabled（环境变量 Swagger__Enabled）可覆盖默认。
/// </summary>
public sealed class SwaggerSetupTests
{
    private const string SwaggerJsonPath = "/swagger/v1/swagger.json";

    [Theory]
    [InlineData(null, true, true)]      // 未配置：Development 开启
    [InlineData(null, false, false)]    // 未配置：生产关闭
    [InlineData(true, false, true)]     // 显式开启：生产也开启
    [InlineData(false, true, false)]    // 显式关闭：Development 也关闭
    public void 是否开启_显式配置优先_未配置时只在Development开启(bool? configured, bool isDevelopment, bool expected)
    {
        Assert.Equal(expected, SwaggerSetup.IsEnabled(configured, isDevelopment));
    }

    [Fact]
    public void 配置项名称与文档一致_环境变量写作Swagger__Enabled()
    {
        // README 与部署说明里让用户设置的是环境变量 Swagger__Enabled，对应配置键 Swagger:Enabled
        Assert.Equal("Swagger:Enabled", SwaggerSetup.EnabledConfigKey);
    }

    [Theory]
    [InlineData("Production", null, HttpStatusCode.NotFound)]
    [InlineData("Production", "true", HttpStatusCode.OK)]
    [InlineData("Production", "false", HttpStatusCode.NotFound)]
    [InlineData("Development", null, HttpStatusCode.OK)]
    [InlineData("Development", "false", HttpStatusCode.NotFound)]
    public async Task 真实管道里_swagger文档是否可访问(string environment, string? enabledSetting, HttpStatusCode expected)
    {
        var status = await GetStatusAsync(environment, enabledSetting, SwaggerJsonPath);

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task 生产默认关闭时_根路径的SwaggerUI也不可访问()
    {
        var status = await GetStatusAsync("Production", enabledSetting: null, "/");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task 显式开启时_根路径提供SwaggerUI()
    {
        var status = await GetStatusAsync("Production", enabledSetting: "true", "/");

        Assert.Equal(HttpStatusCode.OK, status);
    }

    /// <summary>
    /// Program.cs 必须通过 UseSwaggerWhenEnabled 注册，不能直接 app.UseSwagger()
    /// （否则上面测的策略就被绕过了——回到"所有环境都开启"）
    /// </summary>
    [Fact]
    public void Program_cs只通过策略注册Swagger_不直接调用UseSwagger()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "server", "FinanceTracker.Api", "Program.cs"));

        Assert.Contains("app.UseSwaggerWhenEnabled();", source);
        Assert.DoesNotContain("app.UseSwagger(", source);
        Assert.DoesNotContain("app.UseSwaggerUI(", source);
    }

    /// <summary>用真实的 Kestrel（回环地址、随机端口）+ 真实的 Swagger 服务注册，请求一次并返回状态码</summary>
    private static async Task<HttpStatusCode> GetStatusAsync(string environment, string? enabledSetting, string path)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (enabledSetting != null)
        {
            builder.Configuration[SwaggerSetup.EnabledConfigKey] = enabledSetting;
        }

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        await using var app = builder.Build();
        app.UseSwaggerWhenEnabled();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var response = await client.GetAsync(path);
            return response.StatusCode;
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "FinanceTracker.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("未能从测试输出目录向上找到仓库根目录（FinanceTracker.sln）");
    }
}
