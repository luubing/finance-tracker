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

        // 用 DisplayName：账本级预算带账本名，多个账本各有"总预算"时才分得清是哪个超支
        var message = worst.RemainingAmount < 0
            ? $"【{worst.DisplayName}】已超支 {Math.Abs(worst.RemainingAmount):N2} 元，请注意控制支出"
            : $"【{worst.DisplayName}】本月预算已用完，请注意控制支出";

        var delivered = await _notificationService.NotifyBudgetExceededAsync("预算超支提醒", message);
        if (delivered)
        {
            // 仅在通知真正发出后才占用「今日已提醒」额度；
            // 未授权通知权限/发送失败时保持未标记，下次检查仍可再次提醒
            _notificationService.MarkNotifiedToday(year, month);
        }
    }
}