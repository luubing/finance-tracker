using Android.App;
using Android.Service.Notification;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// 通知栏支付监听服务（方案一，数据来源 BillSource.Notification）。
/// 用户在系统设置中授予"通知使用权"后，系统会自动绑定本服务并回调所有应用的通知；
/// 过滤微信/支付宝的支付类通知，解析出金额后进入待确认账单队列，并发本地通知提醒用户确认。
/// 本服务由系统管理生命周期，无需手动 Start/Stop。
/// </summary>
[Service(Name = "com.financetracker.NotificationCaptureService",
         Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
         Exported = false)]
[global::Android.App.IntentFilter(new[] { "android.service.notification.NotificationListenerService" })]
public class NotificationCaptureService : NotificationListenerService
{
    // 支付类通知关键词：命中才进行解析，避免把聊天消息等无关通知当作支付
    private static readonly string[] PaymentKeywords =
    {
        "支付", "收款", "付款", "消费", "扣款", "到账", "转入", "退款", "转账"
    };

    // 最近处理过的通知 key（包名+内容+时间戳哈希），防止系统对同一通知多次回调造成重复入队
    private static readonly object _seenLock = new();
    private static readonly List<int> _seenKeys = new();

    public override void OnNotificationPosted(StatusBarNotification? sbn)
    {
        try
        {
            if (sbn?.Notification?.Extras == null)
            {
                return;
            }

            // 只处理微信/支付宝的通知
            var packageName = sbn.PackageName;
            if (packageName == null || !PaymentContentParser.PaymentAppPackageNames.Contains(packageName))
            {
                return;
            }

            var extras = sbn.Notification.Extras;
            var title = extras.GetCharSequence(global::Android.App.Notification.ExtraTitle)?.ToString() ?? string.Empty;
            var text = extras.GetCharSequence(global::Android.App.Notification.ExtraText)?.ToString() ?? string.Empty;
            var bigText = extras.GetCharSequence(global::Android.App.Notification.ExtraBigText)?.ToString() ?? string.Empty;

            // 标题+正文（+长文本）合并后参与解析，微信/支付宝支付通知的关键信息分布在这几个字段
            var content = string.Join(" ",
                new[] { title, text, bigText }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct());
            if (string.IsNullOrWhiteSpace(content) ||
                !PaymentKeywords.Any(k => content.Contains(k)))
            {
                return;
            }

            // 同一通知去重（按 包名+内容+时间戳）
            var key = (packageName + content + sbn.PostTime).GetHashCode();
            lock (_seenLock)
            {
                if (_seenKeys.Contains(key))
                {
                    return;
                }

                _seenKeys.Add(key);
                if (_seenKeys.Count > 100)
                {
                    _seenKeys.RemoveAt(0);
                }
            }

            var parseResult = PaymentContentParser.Parse(content);
            if (parseResult.Amount <= 0)
            {
                // 无法提取金额的通知不生成待确认账单（如"您的账单已生成"等营销/汇总类通知）
                System.Diagnostics.Debug.WriteLine($"[通知捕获] 无法识别金额，跳过: {content}");
                return;
            }

            var transactionTime = DateTimeOffset.FromUnixTimeMilliseconds(sbn.PostTime).LocalDateTime;
            var pendingBill = new PendingBill
            {
                Amount = parseResult.Amount,
                Type = parseResult.IsIncome ? BillType.Income : BillType.Expense,
                Channel = parseResult.Channel,
                Note = $"{title}：{text}",
                TransactionTime = transactionTime,
                Source = BillSource.Notification,
                CapturedAt = DateTime.Now
            };

            // OnNotificationPosted 在系统主线程回调，数据库写入改为后台异步执行
            _ = CaptureAsync(pendingBill);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[通知捕获] 处理通知失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步保存待确认账单，成功后发本地通知提醒用户确认。
    /// 未授予 POST_NOTIFICATIONS 权限时系统会静默丢弃，待确认账单仍会出现在 App 内列表。
    /// </summary>
    private async Task CaptureAsync(PendingBill pendingBill)
    {
        try
        {
            var service = PendingBillServiceLocator.Instance;
            if (service == null)
            {
                return;
            }

            var added = await service.AddAsync(pendingBill);
            if (added)
            {
                CaptureNotifier.Notify(this,
                    $"捕获到{(pendingBill.Type == BillType.Income ? "收入" : "支出")} ¥{pendingBill.Amount:0.##}",
                    $"来自 {pendingBill.Channel}，点击进入待确认账单");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[通知捕获] 保存待确认账单失败: {ex.Message}");
        }
    }
}
