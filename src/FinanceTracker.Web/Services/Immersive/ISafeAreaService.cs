namespace FinanceTracker.Web.Services;

/// <summary>
/// 安全区服务：把宿主（MAUI 原生壳）测得的安全区边距交给 Web 层，由 Web 层写成 CSS 变量 <c>--ft-safe-*</c>。
/// <list type="bullet">
/// <item>Android / iOS（MAUI）：原生实现，随旋转、窗口尺寸与系统键盘变化。</item>
/// <item>Blazor Server（Web.Server）：<see cref="NoOpSafeAreaService"/>，没有原生安全区，
/// <see cref="Current"/> 恒为 <c>null</c>，Web 层沿用 CSS <c>env(safe-area-inset-*)</c>（浏览器里为 0）。</item>
/// </list>
/// </summary>
public interface ISafeAreaService
{
    /// <summary>
    /// 当前原生安全区；宿主没有原生安全区（或原生窗口尚未就绪）时为 <c>null</c>。
    /// 读取必须廉价且线程安全：实现应返回缓存值（或在主线程上即时测量）。
    /// </summary>
    SafeAreaInsets? Current { get; }

    /// <summary>安全区变化（旋转、窗口尺寸变化、系统键盘弹出 / 收起）时触发；可能在非 UI 线程上触发。</summary>
    event EventHandler<SafeAreaInsets>? Changed;
}

/// <summary>没有原生安全区的宿主（Blazor Server / 浏览器）使用的空实现。</summary>
public sealed class NoOpSafeAreaService : ISafeAreaService
{
    public SafeAreaInsets? Current => null;

    // 空实现永不触发；显式 add/remove 避免"事件从未使用"的编译警告
    public event EventHandler<SafeAreaInsets>? Changed
    {
        add { }
        remove { }
    }
}
