using FinanceTracker.Web.Services;
using Microsoft.Maui.ApplicationModel;
using UIKit;

namespace FinanceTracker.App.Platforms.iOS.Services;

/// <summary>
/// iOS 状态栏图标风格服务。
/// 状态栏内容颜色跟随顶层视图控制器的界面风格，因此通过关键窗口的 <c>OverrideUserInterfaceStyle</c> 切换：
/// 深色界面风格 → 浅色（白色）图标；浅色界面风格 → 深色图标。切换放在约 200ms 的动画块内，状态栏随之平滑过渡。
/// 副作用：WKWebView 的 <c>prefers-color-scheme</c> 随之变化；本应用仅有浅色主题，样式里不使用该媒体查询。
/// </summary>
public sealed class StatusBarService : IStatusBarService
{
    /// <summary>状态栏图标切换的过渡时长（秒）</summary>
    private const double TransitionSeconds = 0.2;

    public void SetIconStyle(StatusBarIconStyle style)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var window = UIApplication.SharedApplication.ConnectedScenes
                    .OfType<UIWindowScene>()
                    .SelectMany(scene => scene.Windows)
                    .FirstOrDefault(w => w.IsKeyWindow);
                if (window is null)
                {
                    return;
                }

                var target = style == StatusBarIconStyle.Light
                    ? UIUserInterfaceStyle.Dark
                    : UIUserInterfaceStyle.Light;

                if (window.OverrideUserInterfaceStyle != target)
                {
                    // 放进动画块：状态栏外观更新随之以约 200ms 过渡（见 UIViewController.SetNeedsStatusBarAppearanceUpdate 文档）
                    UIView.Animate(TransitionSeconds, () =>
                    {
                        window.OverrideUserInterfaceStyle = target;
                        window.RootViewController?.SetNeedsStatusBarAppearanceUpdate();
                    });
                }
            }
            catch (Exception ex)
            {
                // 状态栏样式只是外观增强，失败不应影响页面
                System.Diagnostics.Debug.WriteLine($"设置状态栏图标风格失败: {ex.Message}");
            }
        });
    }
}
