namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 预算超支本地通知服务（由各宿主提供实现：MAUI 原生通知 / 不支持本地通知的平台使用空实现）
/// </summary>
public interface IBudgetNotificationService
{
    /// <summary>
    /// 发送预算超支本地通知
    /// </summary>
    /// <param name="title">通知标题</param>
    /// <param name="message">通知内容</param>
    Task NotifyBudgetExceededAsync(string title, string message);

    /// <summary>
    /// 当天是否已发送过预算超支通知（每日最多提醒一次）
    /// </summary>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    bool IsNotifiedToday(int year, int month);

    /// <summary>
    /// 标记当天已发送预算超支通知
    /// </summary>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    void MarkNotifiedToday(int year, int month);
}