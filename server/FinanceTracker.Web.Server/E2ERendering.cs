using FinanceTracker.Web.Services;

namespace FinanceTracker.Web.Server;

/// <summary>
/// E2E 专用的「渲染档位」假实现——与 <see cref="E2ECapture"/> 同一个开关（配置 <c>E2E:FakeCapture=true</c>，tests/e2e/server.ps1 启动时打开）才注册；
/// 平时 <c>dotnet run</c> 仍是 <see cref="NoOpRenderingProfileService"/>（恒为不降级），行为与改造前完全一致。
/// <para>
/// 真机上「该不该把毛玻璃降级成不透明」由原生宿主判定（Android 低端设备 / iOS 系统「降低透明度」），浏览器里没有这个信号，
/// 「宿主判定 → UiRuntime → <c>&lt;html data-ft-solid&gt;</c> → 设计令牌」这条链路就无从验收。
/// 假实现把判定结果放在进程内，由 E2E 经 <c>POST /__e2e/rendering</c>（<c>{"solid": true}</c>）设置、<c>GET</c> 观察；
/// 设置时同样触发 <see cref="IRenderingProfileService.PreferSolidMaterialsChanged"/>，页面里已经打开的 UiRuntime 即时跟随。
/// 状态是整个进程共享的（E2E 脚本串行执行）：用到它的脚本开始前与结束后各设置一次 <c>false</c>，免得互相影响。
/// </para>
/// </summary>
public static class E2ERendering
{
    /// <summary>设置 / 查看假实现状态的地址</summary>
    public const string Route = "/__e2e/rendering";

    public static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<E2ERenderingProfileService>();
        services.AddSingleton<IRenderingProfileService>(sp => sp.GetRequiredService<E2ERenderingProfileService>());
    }

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet(Route, (E2ERenderingProfileService service) => Results.Json(new { solid = service.PreferSolidMaterials }));
        app.MapPost(Route, (E2ERenderingRequest request, E2ERenderingProfileService service) =>
        {
            service.Set(request.Solid);
            return Results.Json(new { solid = service.PreferSolidMaterials });
        });
    }
}

/// <summary>E2E 设置假实现的请求：<c>Solid</c> 为 true 即「宿主判定应当降级为不透明」。</summary>
public sealed record E2ERenderingRequest(bool Solid);

public sealed class E2ERenderingProfileService : IRenderingProfileService
{
    private volatile bool _solid;

    public bool PreferSolidMaterials => _solid;

    public event EventHandler<bool>? PreferSolidMaterialsChanged;

    public void Set(bool solid)
    {
        if (_solid == solid)
        {
            return;
        }

        _solid = solid;
        PreferSolidMaterialsChanged?.Invoke(this, solid);
    }
}
