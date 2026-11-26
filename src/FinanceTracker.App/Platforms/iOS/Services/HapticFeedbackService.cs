using FinanceTracker.Web.Services;
using Microsoft.Maui.ApplicationModel;
using UIKit;

namespace FinanceTracker.App.Platforms.iOS.Services;

/// <summary>
/// iOS 触感反馈：<c>UIImpactFeedbackGenerator(.light)</c>（轻触）。
/// 生成器复用同一个实例并在每次触发后 <c>Prepare()</c> 下一次，降低首次触发的延迟；
/// 只能在主线程使用。
/// </summary>
public sealed class HapticFeedbackService : IHapticFeedbackService
{
    private UIImpactFeedbackGenerator? _generator;

    public void SelectionChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // 无视图参数的构造函数自 iOS 17.5 起标记为过时（推荐把生成器关联到具体视图，用于多窗口 / 外接屏的触感路由），
                // 但所有系统版本上仍然有效；本应用是单窗口、最低支持版本也早于 17.5，所以沿用这一构造，不为一次轻触引入版本分支
#pragma warning disable CA1422
                _generator ??= new UIImpactFeedbackGenerator(UIImpactFeedbackStyle.Light);
#pragma warning restore CA1422
                _generator.ImpactOccurred();
                _generator.Prepare();
            }
            catch (Exception ex)
            {
                // 触感只是点按确认，失败不应影响操作
                System.Diagnostics.Debug.WriteLine($"触感反馈失败: {ex.Message}");
            }
        });
    }
}
