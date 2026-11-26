namespace FinanceTracker.Web.Services;

/// <summary>
/// 渲染档位服务：宿主（MAUI 原生壳）告诉 Web 层「这台设备该不该把毛玻璃降级成不透明实色」（23 号 ticket，ADR 0007 §26）。
/// Web 层（<c>UiRuntime</c>）读到 <see cref="PreferSolidMaterials"/> 为真时，把根开关 <c>&lt;html data-ft-solid="true"&gt;</c> 打开——
/// 设计令牌（tokens.css）因此一处切换：所有玻璃表面换成不透明、不模糊的实色，页面代码不用关心。
/// <list type="bullet">
/// <item>Android：低端设备判定（<see cref="LowEndDeviceCriteria"/>：系统标记的低内存设备，或总内存低于 3 GiB）。</item>
/// <item>iOS：系统「降低透明度」（<c>UIAccessibility.IsReduceTransparencyEnabled</c>）。网页引擎自己也会按
/// <c>prefers-reduced-transparency</c> 媒体特性降级——两条路取值相同、任一成立即不透明。</item>
/// <item>Blazor Server（Web.Server）与其余平台：<see cref="NoOpRenderingProfileService"/>，恒为 <c>false</c>
/// （浏览器按媒体特性降级；E2E 用假实现驱动，见 server/FinanceTracker.Web.Server/E2ERendering.cs）。</item>
/// </list>
/// 读取必须廉价且线程安全；实现不得抛出异常——判定失败时按「不降级」处理（保持设计稿的完整质感）。
/// </summary>
public interface IRenderingProfileService
{
    /// <summary>是否应当把玻璃材质降级为不透明实色。</summary>
    bool PreferSolidMaterials { get; }

    /// <summary>
    /// <see cref="PreferSolidMaterials"/> 变化时触发（例如用户在系统设置里开关了「降低透明度」），参数是新值；可能在非 UI 线程上触发。
    /// </summary>
    event EventHandler<bool>? PreferSolidMaterialsChanged;
}

/// <summary>没有渲染档位判定的宿主（Blazor Server / 浏览器 / 桌面）使用的空实现：恒为 false，永不触发变化事件。</summary>
public sealed class NoOpRenderingProfileService : IRenderingProfileService
{
    public bool PreferSolidMaterials => false;

    // 空实现永不触发；显式 add/remove 避免"事件从未使用"的编译警告
    public event EventHandler<bool>? PreferSolidMaterialsChanged
    {
        add { }
        remove { }
    }
}
