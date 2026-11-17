using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 预算超支通知空实现（不支持本地通知的平台使用，如 Web.Server / Windows）
/// </summary>
public class NoOpBudgetNotificationService : IBudgetNotificationService
{
    public Task<bool> NotifyBudgetExceededAsync(string title, string message)
    {
        // 空实现不会真正发出通知
        return Task.FromResult(false);
    }

    public bool IsNotifiedToday(int year, int month)
    {
        return false;
    }

    public void MarkNotifiedToday(int year, int month)
    {
    }
}