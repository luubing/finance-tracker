namespace FinanceTracker.Api.Services;

/// <summary>
/// Swagger / SwaggerUI 的开关策略。
/// 生产环境公开完整的接口清单会降低被探测的门槛（登录只需要手机号、没有验证码），所以默认只在 Development 开启；
/// 需要时用配置项 <see cref="EnabledConfigKey"/>（环境变量 <c>Swagger__Enabled</c>）显式覆盖：
/// <c>true</c> 在任何环境开启，<c>false</c> 在任何环境关闭。
/// </summary>
public static class SwaggerSetup
{
    /// <summary>
    /// 配置项名称：是否开启 Swagger / SwaggerUI。未配置时只在 Development 开启
    /// </summary>
    public const string EnabledConfigKey = "Swagger:Enabled";

    /// <summary>
    /// 是否开启 Swagger：显式配置优先；未配置时只在 Development 开启
    /// </summary>
    public static bool IsEnabled(bool? configured, bool isDevelopment) => configured ?? isDevelopment;

    /// <summary>
    /// 按策略启用 Swagger 与 SwaggerUI（UI 位于根路径）。
    /// 策略为关闭时什么都不注册，<c>/swagger/*</c> 与根路径都是 404。
    /// </summary>
    public static WebApplication UseSwaggerWhenEnabled(this WebApplication app)
    {
        var configured = app.Configuration.GetValue<bool?>(EnabledConfigKey);
        if (!IsEnabled(configured, app.Environment.IsDevelopment()))
        {
            return app;
        }

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "记账本 API V1");
            c.RoutePrefix = string.Empty; // 在根路径访问 Swagger UI
        });

        return app;
    }
}
