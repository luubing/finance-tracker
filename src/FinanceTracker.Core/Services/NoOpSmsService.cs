using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 不支持短信读取的平台（Web / iOS / Windows 等）的空实现。
/// 用于避免在那些平台上 DI 无法解析 ISmsService 导致页面崩溃。
/// </summary>
public class NoOpSmsService : ISmsService
{
    public Task<bool> HasPermissionAsync() => Task.FromResult(false);

    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);

    public Task<List<SmsMessage>> ReadPaymentSmsAsync(DateTime fromDate)
        => Task.FromResult(new List<SmsMessage>());
}