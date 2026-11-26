using AndroidX.Core.View;
using FinanceTracker.Web.Services;
using Microsoft.Maui.ApplicationModel;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// Android 状态栏图标风格服务：<c>WindowInsetsControllerCompat.AppearanceLightStatusBars</c>。
/// 注意语义：<c>AppearanceLightStatusBars = true</c> 表示"浅色状态栏"，系统绘制<b>深色</b>图标。
/// 系统不支持该切换的过渡动画，切换即时生效。
/// </summary>
public sealed class StatusBarService : IStatusBarService
{
    public void SetIconStyle(StatusBarIconStyle style)
    {
        // 图标风格由 Web 层的 JS 回调触发，统一切到主线程操作窗口
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var window = Platform.CurrentActivity?.Window;
                if (window is null)
                {
                    return;
                }

                var controller = WindowCompat.GetInsetsController(window, window.DecorView);
                controller?.AppearanceLightStatusBars = style == StatusBarIconStyle.Dark;
            }
            catch (Exception ex)
            {
                // 状态栏样式只是外观增强，失败不应影响页面
                System.Diagnostics.Debug.WriteLine($"设置状态栏图标风格失败: {ex.Message}");
            }
        });
    }
}
