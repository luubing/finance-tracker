using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.Provider;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// Android 短信服务实现
/// </summary>
public class SmsService : ISmsService
{
    private readonly Context _context;

    public SmsService()
    {
        _context = Platform.CurrentActivity ?? throw new InvalidOperationException("无法获取当前 Activity");
    }

    public Task<bool> HasPermissionAsync()
    {
        var permission = ContextCompat.CheckSelfPermission(_context, Manifest.Permission.ReadSms);
        return Task.FromResult(permission == Permission.Granted);
    }

    public Task<bool> RequestPermissionAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null) return Task.FromResult(false);

        var permissions = new[] { Manifest.Permission.ReadSms };
        ActivityCompat.RequestPermissions(activity, permissions, 1001);

        // 注意：实际应用中需要等待用户授权结果
        return Task.FromResult(true);
    }

    public Task<List<Core.Interfaces.SmsMessage>> ReadPaymentSmsAsync(DateTime fromDate)
    {
        var messages = new List<Core.Interfaces.SmsMessage>();

        if (!HasPermissionAsync().Result)
        {
            return Task.FromResult(messages);
        }

        try
        {
            var uri = Telephony.Sms.ContentUri;
            var projection = new[] { "_id", "address", "body", "date" };
            var selection = "date > ?";
            var selectionArgs = new[] { fromDate.Ticks.ToString() };
            var sortOrder = "date DESC";

            using var cursor = _context.ContentResolver.Query(uri, projection, selection, selectionArgs, sortOrder);

            if (cursor != null)
            {
                while (cursor.MoveToNext())
                {
                    var address = cursor.GetString(cursor.GetColumnIndex("address"));
                    var body = cursor.GetString(cursor.GetColumnIndex("body"));
                    var date = cursor.GetLong(cursor.GetColumnIndex("date"));

                    // 过滤支付类短信
                    if (IsPaymentSms(address, body))
                    {
                        // Android SMS 的 date 是毫秒级 Unix 时间戳
                        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(date).LocalDateTime;

                        messages.Add(new Core.Interfaces.SmsMessage
                        {
                            Id = cursor.GetLong(cursor.GetColumnIndex("_id")),
                            Address = address,
                            Body = body,
                            Date = dateTime
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取短信失败: {ex.Message}");
        }

        return Task.FromResult(messages);
    }

    private bool IsPaymentSms(string address, string body)
    {
        // 支付类短信特征
        var paymentKeywords = new[]
        {
            "支付", "消费", "支出", "扣款", "转账",
            "微信支付", "支付宝", "京东支付", "美团支付",
            "银行", "信用卡", "借记卡"
        };

        var content = $"{address} {body}";
        return paymentKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
