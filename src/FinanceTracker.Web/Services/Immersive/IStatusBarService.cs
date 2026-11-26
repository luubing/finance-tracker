namespace FinanceTracker.Web.Services;

/// <summary>状态栏图标（时间、信号、电量）的颜色风格</summary>
public enum StatusBarIconStyle
{
    /// <summary>浅色（白色）图标：用于深色背景（紫色顶栏、深色 Hero 等）</summary>
    Light,

    /// <summary>深色图标：用于浅色背景（白色 / 浅灰页面）</summary>
    Dark,
}

/// <summary>
/// 状态栏样式服务：由 Web 层根据状态栏下方的页面背景明暗决定图标风格，交给宿主原生设置。
/// <list type="bullet">
/// <item>Android：<c>WindowInsetsControllerCompat.AppearanceLightStatusBars</c>（系统限制，切换无过渡）。</item>
/// <item>iOS：窗口 <c>OverrideUserInterfaceStyle</c>，状态栏随系统样式更新。</item>
/// <item>Blazor Server（Web.Server）：<see cref="NoOpStatusBarService"/>，浏览器没有状态栏。</item>
/// </list>
/// </summary>
public interface IStatusBarService
{
    /// <summary>设置状态栏图标风格；实现需自行切换到 UI 线程，且不应抛出异常。</summary>
    void SetIconStyle(StatusBarIconStyle style);
}

/// <summary>没有状态栏的宿主（Blazor Server / 浏览器）使用的空实现。</summary>
public sealed class NoOpStatusBarService : IStatusBarService
{
    public void SetIconStyle(StatusBarIconStyle style)
    {
    }
}
