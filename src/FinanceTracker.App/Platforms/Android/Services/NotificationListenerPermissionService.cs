using Android.Content;
using Android.Provider;
using FinanceTracker.Core.Interfaces;
using Microsoft.Maui.ApplicationModel;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// Android 通知使用权（NotificationListenerService）权限服务实现。
/// 该权限无法通过运行时弹窗授予，只能检测状态并引导用户到系统设置开启。
/// </summary>
public class NotificationListenerPermissionService : INotificationListenerPermissionService
{
    // 系统记录已启用通知监听器组件的 Secure 设置键，值为 "pkg/cls;pkg/cls" 形式的扁平字符串
    private const string EnabledListenersKey = "enabled_notification_listeners";

    public Task<bool> HasPermissionAsync()
    {
        try
        {
            var context = Platform.AppContext;
            var flat = Settings.Secure.GetString(context.ContentResolver, EnabledListenersKey);
            var enabled = !string.IsNullOrEmpty(flat) &&
                          flat.Contains(context.PackageName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(enabled);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"检查通知使用权失败: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public Task OpenSettingsAsync()
    {
        try
        {
            var context = Platform.AppContext;
            // ACTION_NOTIFICATION_LISTENER_SETTINGS 是隐藏 API，无法通过 Settings 类常量在低 SDK 上编译，
            // 这里直接使用字符串（Android 5.0+ 均支持）
            var intent = new Intent("android.settings.ACTION_NOTIFICATION_LISTENER_SETTINGS");
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"打开通知使用权设置失败: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
