using System.Globalization;
using Android.Content;
using Android.Database;
using Android.Provider;
using FinanceTracker.Core.Interfaces;
using Microsoft.Maui.ApplicationModel;

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

    public async Task<bool> HasPermissionAsync()
    {
        try
        {
            // 权限 API 需在主线程调用；Blazor 组件常运行在后台线程，这里统一调度到主线程
            var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.CheckStatusAsync<Permissions.Sms>());
            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"检查短信权限失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            // Permissions.RequestAsync 会弹出系统授权对话框，并等待用户作出选择后才返回
            // （Android 10 及以下会跳转到 App 设置页，用户返回后重查状态）
            // 必须在主线程发起授权请求，Blazor 组件常运行在后台线程，这里统一调度
            var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Sms>());
            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"请求短信权限失败: {ex.Message}");
            return false;
        }
    }

    public async Task<List<Core.Interfaces.SmsMessage>> ReadPaymentSmsAsync(DateTime fromDate)
    {
        var messages = new List<Core.Interfaces.SmsMessage>();

        // 统一走异步权限检查，避免 .Result 造成同步阻塞/死锁
        if (!await HasPermissionAsync())
        {
            return messages;
        }

        try
        {
            var uri = Telephony.Sms.ContentUri;
            var projection = new[] { "_id", "address", "body", "date" };
            var selection = "date > ?";
            // Android 短信库的 date 列是毫秒级 Unix 时间戳（13 位数字，如 1788486324000），
            // 而 .NET DateTime.Ticks 是自 0001-01-01 起的 100 纳秒数（18 位数字）。
            // 若直接用 fromDate.Ticks 作为查询参数，任何短信都无法满足 date > 条件，cursor 永远为空。
            var fromDateMillis = new DateTimeOffset(fromDate).ToUnixTimeMilliseconds();
            var selectionArgs = new[] { fromDateMillis.ToString(CultureInfo.InvariantCulture) };
            var sortOrder = "date DESC";

            using var cursor = _context.ContentResolver.Query(uri!, projection, selection, selectionArgs, sortOrder);

            if (cursor != null)
            {
                while (cursor.MoveToNext())
                {
                    var address = cursor.GetString(cursor.GetColumnIndex("address")) ?? string.Empty;
                    var body = cursor.GetString(cursor.GetColumnIndex("body")) ?? string.Empty;
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

        return messages;
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
