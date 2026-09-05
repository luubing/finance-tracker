using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 预算预警服务：存在超支预算时发送本地通知（每日最多一次）
/// </summary>
public class BudgetAlertService
{
    private readonly IBudgetNotificationService _notificationService;

    public BudgetAlertService(IBudgetNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>
    /// 检查预算执行情况列表，存在超支预算时发送本地通知（每日最多一次）
    /// </summary>
    /// <param name="statuses">预算执行情况列表</param>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    public async Task NotifyIfExceededAsync(List<BudgetStatus> statuses, int year, int month)
    {
        // 取超支预算中使用率最高的一条
        var worst = statuses
            .Where(s => s.UsagePercentage >= 100)
            .OrderByDescending(s => s.UsagePercentage)
            .FirstOrDefault();

        if (worst == null || _notificationService.IsNotifiedToday(year, month))
        {
            return;
        }

        var message = worst.RemainingAmount < 0
            ? $"【{worst.CategoryName}】已超支 {Math.Abs(worst.RemainingAmount):N2} 元，请注意控制支出"
            : $"【{worst.CategoryName}】本月预算已用完，请注意控制支出";

        await _notificationService.NotifyBudgetExceededAsync("预算超支提醒", message);
        _notificationService.MarkNotifiedToday(year, month);
    }
}