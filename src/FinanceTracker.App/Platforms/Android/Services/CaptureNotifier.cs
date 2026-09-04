using Android.App;
using Android.Content;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// 本地通知工具：捕获到支付后提醒用户到 App 确认。
/// </summary>
internal static class CaptureNotifier
{
    private const string ChannelId = "pending_bill";
    private const int NotificationId = 1001;

    public static void Notify(Context context, string title, string text)
    {
        try
        {
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            if (manager == null)
            {
                return;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var channel = new NotificationChannel(ChannelId, "待确认账单提醒", NotificationImportance.Default);
                manager.CreateNotificationChannel(channel);
            }

            var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                flags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = launchIntent != null
                ? PendingIntent.GetActivity(context, 0, launchIntent, flags)
                : null;

            var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? new Notification.Builder(context, ChannelId)
                : new Notification.Builder(context);

            builder.SetContentTitle(title)
                   .SetContentText(text)
                   .SetSmallIcon(context.ApplicationInfo?.Icon ?? 0)
                   .SetAutoCancel(true);

            if (pendingIntent != null)
            {
                builder.SetContentIntent(pendingIntent);
            }

            manager.Notify(NotificationId, builder.Build());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"发送本地通知失败: {ex.Message}");
        }
    }
}
