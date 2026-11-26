namespace FinanceTracker.Web.Services;

/// <summary>
/// 触感反馈服务：Web 层通过它触发设备的轻触反馈，由宿主按平台实现。
/// <list type="bullet">
/// <item>iOS：<c>UIImpactFeedbackGenerator(.light)</c>。</item>
/// <item>Android：<c>View.PerformHapticFeedback(CLOCK_TICK)</c>（时钟刻度）。</item>
/// <item>Blazor Server（Web.Server）与其余平台：<see cref="NoOpHapticFeedbackService"/>，浏览器里没有可靠的触感能力。</item>
/// </list>
/// 触感只是点按已生效的确认，不是功能的一部分：实现不得抛出异常，也不得阻塞调用线程。
/// </summary>
public interface IHapticFeedbackService
{
    /// <summary>选中某一项（如切换 Tab）时的轻触反馈；实现需自行切换到 UI 线程。</summary>
    void SelectionChanged();
}

/// <summary>没有触感能力的宿主（Blazor Server / 浏览器 / 桌面）使用的空实现。</summary>
public sealed class NoOpHapticFeedbackService : IHapticFeedbackService
{
    public void SelectionChanged()
    {
    }
}
