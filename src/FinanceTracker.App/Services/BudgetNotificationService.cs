using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.App.Services;

/// <summary>
/// 预算超支本地通知服务：MAUI 平台原生通知（Android/iOS），
/// 通过 Preferences 记录当天是否已提醒，实现每日最多提醒一次
/// </summary>
public class BudgetNotificationService : IBudgetNotificationService
{
    private const string LastNotifiedKey = "budget_alert_last_notified";

    public async Task NotifyBudgetExceededAsync(string title, string message)
    {
        try
        {
#if ANDROID
            var context = Android.App.Application.Context;
            var manager = (Android.App.NotificationManager?)context.GetSystemService(Android.Content.Context.NotificationService);
            if (manager == null)
            {
                return;
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
#elif IOS
            var center = UserNotifications.UNUserNotificationCenter.Current;
            var settings = await center.GetNotificationSettingsAsync();
            var authorized = settings.AuthorizationStatus == UserNotifications.UNAuthorizationStatus.Authorized
                || settings.AuthorizationStatus == UserNotifications.UNAuthorizationStatus.Provisional;
            if (!authorized)
            {
                // 未授权时不申请权限，保持静默
                return;
            }

            var content = new UserNotifications.UNMutableNotificationContent
            {
                Title = title,
                Body = message,
                Sound = UserNotifications.UNNotificationSound.Default
            };
            var request = UserNotifications.UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, null);
            await center.AddNotificationRequestAsync(request);
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"发送预算超支本地通知失败: {ex.Message}");
        }
    }

    public bool IsNotifiedToday(int year, int month)
    {
        return Preferences.Get(LastNotifiedKey, string.Empty) == DateTime.Today.ToString("yyyyMMdd");
    }

    public void MarkNotifiedToday(int year, int month)
    {
        Preferences.Set(LastNotifiedKey, DateTime.Today.ToString("yyyyMMdd"));
    }
}