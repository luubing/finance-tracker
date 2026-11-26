namespace FinanceTracker.Web.Server;

/// <summary>
/// E2E 专用：让「错误日志」页读一个专用目录，而不是开发机上真实的日志目录。
/// 只有开发环境（Development）下配置了 <c>E2E:LogDirectory</c>（环境变量 <c>E2E__LogDirectory</c>）才生效，tests/e2e/server.ps1 启动时会设置它；
/// 平时 <c>dotnet run</c> 没有这个配置，日志页行为与改造前完全一致。
/// <para>
/// 原因：Web.Server 本身不写 Serilog 文件日志，E2E 要自己往目录里写日志行来造数据；
/// 而「清除日志」会删掉目录里所有 <c>app-*.log</c>——必须保证那是一个只属于测试的目录。
/// E2E 经 <c>GET /__e2e/log-directory</c> 取得这个目录（同时确认 Web.Server 是由 server.ps1 启动的）。
/// </para>
/// </summary>
public static class E2ELogs
{
    /// <summary>目录的配置键（环境变量写作 E2E__LogDirectory）</summary>
    public const string ConfigKey = "E2E:LogDirectory";

    /// <summary>查询日志目录的地址</summary>
    public const string Route = "/__e2e/log-directory";

    /// <summary>
    /// 配置的日志目录；没有配置、或不是开发环境（Development）时返回 null。
    /// 只在开发环境生效：「清除日志」会删光这个目录里的 <c>app-*.log</c>，不能让生产环境误带的环境变量把它指向真实日志目录。
    /// </summary>
    public static string? Directory(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsDevelopment() && !string.IsNullOrWhiteSpace(configuration[ConfigKey]) ? configuration[ConfigKey] : null;

    public static void MapEndpoints(WebApplication app, string directory) =>
        app.MapGet(Route, () => Results.Json(new { directory }));
}
