using Android.Views;
using FinanceTracker.Web.Services;
using Microsoft.Maui.ApplicationModel;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// Android 触感反馈：在窗口根视图上执行 <c>PerformHapticFeedback(CLOCK_TICK)</c>（时钟刻度，轻而短）。
/// 走的是视图级触感，受系统「触摸反馈」开关控制，不需要 VIBRATE 权限。
/// </summary>
public sealed class HapticFeedbackService : IHapticFeedbackService
{
    public void SelectionChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var view = Platform.CurrentActivity?.Window?.DecorView;
                view?.PerformHapticFeedback(FeedbackConstants.ClockTick);
            }
            catch (Exception ex)
            {
                // 触感只是点按确认，失败不应影响操作
                System.Diagnostics.Debug.WriteLine($"触感反馈失败: {ex.Message}");
            }
        });
    }
}
