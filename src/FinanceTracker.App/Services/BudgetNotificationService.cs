using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.App.Services;

/// <summary>
/// 预算超支本地通知服务：MAUI 平台原生通知（Android/iOS），
/// 通过 Preferences 记录当天是否已提醒，实现每日最多提醒一次
/// </summary>
public class BudgetNotificationService : IBudgetNotificationService
{
    private const string LastNotifiedKey = "budget_alert_last_notified";

    public async Task<bool> NotifyBudgetExceededAsync(string title, string message)
    {
        try
        {
#if ANDROID
            // Android 13+（API 33）：未授予 POST_NOTIFICATIONS 时系统会静默丢弃通知，
            // 因此必须先（按需）申请运行时权限；被拒绝则返回 false，不占用今日提醒额度
            if (OperatingSystem.IsAndroidVersionAtLeast(33) && !await EnsurePostNotificationsPermissionAsync())
            {
                return false;
            }

            var context = Android.App.Application.Context;
            var manager = (Android.App.NotificationManager?)context.GetSystemService(Android.Content.Context.NotificationService);
            if (manager == null)
            {
                return false;
            }

            const string channelId = "budget_alert";
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var channel = new Android.App.NotificationChannel(channelId, "预算超支提醒", Android.App.NotificationImportance.Default);
                manager.CreateNotificationChannel(channel);
            }

            // 点击通知回到应用首页
            var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
            var flags = Android.App.PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                flags |= Android.App.PendingIntentFlags.Immutable;
            }

            var pendingIntent = launchIntent != null
                ? Android.App.PendingIntent.GetActivity(context, 0, launchIntent, flags)
                : null;

            var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? new Android.App.Notification.Builder(context, channelId)
                : new Android.App.Notification.Builder(context);

            builder.SetContentTitle(title)
                   .SetContentText(message)
                   .SetSmallIcon(context.ApplicationInfo?.Icon ?? 0)
                   .SetAutoCancel(true);

            if (pendingIntent != null)
            {
                builder.SetContentIntent(pendingIntent);
            }

            manager.Notify(2001, builder.Build());
            return true;
#elif IOS
            var center = UserNotifications.UNUserNotificationCenter.Current;
            var settings = await center.GetNotificationSettingsAsync();
            var authorized = settings.AuthorizationStatus == UserNotifications.UNAuthorizationStatus.Authorized
                || settings.AuthorizationStatus == UserNotifications.UNAuthorizationStatus.Provisional;
            if (!authorized)
            {
                // 未授权时不申请权限，保持静默；返回 false 以免占用今日提醒额度
                return false;
            }

            var content = new UserNotifications.UNMutableNotificationContent
            {
                Title = title,
                Body = message,
                Sound = UserNotifications.UNNotificationSound.Default
            };
            var request = UserNotifications.UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, null);
            await center.AddNotificationRequestAsync(request);
            return true;
#else
            // 其他平台不支持本地通知
            await Task.CompletedTask;
            return false;
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"发送预算超支本地通知失败: {ex.Message}");
            return false;
        }
    }

#if ANDROID
    /// <summary>
    /// 确保已授予通知权限（Android 13+）：已授予直接返回；否则弹出系统授权对话框并等待用户选择
    /// </summary>
    private static async Task<bool> EnsurePostNotificationsPermissionAsync()
    {
        var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.CheckStatusAsync<Permissions.PostNotifications>());
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.PostNotifications>());
        return status == PermissionStatus.Granted;
    }
#endif

    public bool IsNotifiedToday(int year, int month)
    {
        return Preferences.Get(LastNotifiedKey, string.Empty) == DateTime.Today.ToString("yyyyMMdd");
    }

    public void MarkNotifiedToday(int year, int month)
    {
        Preferences.Set(LastNotifiedKey, DateTime.Today.ToString("yyyyMMdd"));
    }
}
