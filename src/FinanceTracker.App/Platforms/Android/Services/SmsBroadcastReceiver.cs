using Android.Content;
using Android.Provider;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// 短信支付实时监听（方案二，数据来源 BillSource.SmsRecognition）。
/// 监听系统 SMS_RECEIVED 广播，收到支付类短信后解析金额并进入待确认账单队列。
/// 需要 RECEIVE_SMS 运行时权限（与短信识别共用，在"待确认账单"或"短信识别"页面授权）。
/// 注意：SMS_RECEIVED 是受保护的系统广播，只有系统可以发送，Exported = true 是安全的。
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, Label = "短信支付监听")]
[global::Android.App.IntentFilter(new[] { "android.provider.Telephony.SMS_RECEIVED" }, Priority = 999)]
public class SmsBroadcastReceiver : BroadcastReceiver
{
    // 支付类短信关键词：命中才进行解析
    private static readonly string[] PaymentKeywords =
    {
        "支付", "消费", "支出", "扣款", "收款", "到账", "转入", "退款", "转账"
    };

    public override void OnReceive(Context? context, Intent? intent)
    {
        try
        {
            if (intent?.Action != Telephony.Sms.Intents.SmsReceivedAction)
            {
                return;
            }

            var messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
            if (messages == null || messages.Length == 0)
            {
                return;
            }

            // 多段短信（长短信拆分）按顺序拼接
            var address = messages[0].OriginatingAddress ?? string.Empty;
            var body = string.Concat(messages.Select(m => m.DisplayMessageBody ?? string.Empty));
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            var content = $"{address} {body}";
            if (!PaymentKeywords.Any(k => content.Contains(k)))
            {
                return;
            }

            var parseResult = PaymentContentParser.Parse(body);
            if (parseResult.Amount <= 0)
            {
                // 无法提取金额的短信跳过（验证码短信等已被关键词初筛过滤）
                System.Diagnostics.Debug.WriteLine($"[短信捕获] 无法识别金额，跳过: {body}");
                return;
            }

            // 取最后一段短信的时间戳（即完整短信接收完成的时间）
            var timestampMillis = messages[^1].TimestampMillis;
            var transactionTime = timestampMillis > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestampMillis).LocalDateTime
                : DateTime.Now;

            var pendingBill = new PendingBill
            {
                Amount = parseResult.Amount,
                Type = parseResult.IsIncome ? BillType.Income : BillType.Expense,
                Channel = parseResult.Channel,
                Note = body,
                TransactionTime = transactionTime,
                Source = BillSource.SmsRecognition,
                CapturedAt = DateTime.Now
            };

            // OnReceive 在系统主线程回调，数据库写入改为后台异步执行
            _ = CaptureAsync(pendingBill);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[短信捕获] 处理短信失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步保存待确认账单
    /// </summary>
    private async Task CaptureAsync(PendingBill pendingBill)
    {
        try
        {
            var service = PendingBillServiceLocator.Instance;
            if (service != null)
            {
                await service.AddAsync(pendingBill);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[短信捕获] 保存待确认账单失败: {ex.Message}");
        }
    }
}
