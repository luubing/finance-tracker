using FinanceTracker.Core.Interfaces;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 预算预警服务测试：「每日最多提醒一次」只能消耗在真正发出的通知上。
/// 通知因未授权（Android 13+ 的 POST_NOTIFICATIONS / iOS 未授权）等原因没发出时，
/// 不能标记「今日已提醒」，否则会白白用掉当天唯一一次提醒机会。
/// </summary>
public class BudgetAlertServiceTests
{
    private sealed class FakeNotificationService : IBudgetNotificationService
    {
        public bool DeliverResult { get; init; }
        public bool NotifiedToday { get; set; }
        public int NotifyCalls { get; private set; }
        public int MarkCalls { get; private set; }
        public string? LastMessage { get; private set; }

        public Task<bool> NotifyBudgetExceededAsync(string title, string message)
        {
            NotifyCalls++;
            LastMessage = message;
            return Task.FromResult(DeliverResult);
        }

        public bool IsNotifiedToday(int year, int month) => NotifiedToday;

        public void MarkNotifiedToday(int year, int month)
        {
            MarkCalls++;
            NotifiedToday = true;
        }
    }

    private static BudgetStatus Exceeded() => new()
    {
        Amount = 100m,
        UsedAmount = 150m,
        CategoryName = "餐饮"
    };

    private static BudgetStatus WithinLimit() => new()
    {
        Amount = 100m,
        UsedAmount = 50m,
        CategoryName = "交通"
    };

    [Fact]
    public async Task 通知已发出_标记今日已提醒()
    {
        var notifier = new FakeNotificationService { DeliverResult = true };
        var service = new BudgetAlertService(notifier);

        await service.NotifyIfExceededAsync(new List<BudgetStatus> { Exceeded() }, 2026, 11);

        Assert.Equal(1, notifier.NotifyCalls);
        Assert.Equal(1, notifier.MarkCalls);
    }

    [Fact]
    public async Task 通知未发出_不标记今日已提醒_下次仍可再次提醒()
    {
        var notifier = new FakeNotificationService { DeliverResult = false };
        var service = new BudgetAlertService(notifier);

        await service.NotifyIfExceededAsync(new List<BudgetStatus> { Exceeded() }, 2026, 11);
        await service.NotifyIfExceededAsync(new List<BudgetStatus> { Exceeded() }, 2026, 11);

        // 两次检查都尝试发送（额度没有被白白消耗），且从未标记
        Assert.Equal(2, notifier.NotifyCalls);
        Assert.Equal(0, notifier.MarkCalls);
    }

    [Fact]
    public async Task 今日已提醒_不再重复发送()
    {
        var notifier = new FakeNotificationService { DeliverResult = true, NotifiedToday = true };
        var service = new BudgetAlertService(notifier);

        await service.NotifyIfExceededAsync(new List<BudgetStatus> { Exceeded() }, 2026, 11);

        Assert.Equal(0, notifier.NotifyCalls);
    }

    [Fact]
    public async Task 没有超支预算_不发送通知()
    {
        var notifier = new FakeNotificationService { DeliverResult = true };
        var service = new BudgetAlertService(notifier);

        await service.NotifyIfExceededAsync(new List<BudgetStatus> { WithinLimit() }, 2026, 11);

        Assert.Equal(0, notifier.NotifyCalls);
        Assert.Equal(0, notifier.MarkCalls);
    }

    [Fact]
    public async Task 账本级预算超支_文案带账本名_多个账本的总预算才分得清()
    {
        var notifier = new FakeNotificationService { DeliverResult = true };
        var service = new BudgetAlertService(notifier);
        var ledgerBudget = new BudgetStatus
        {
            Amount = 100m,
            UsedAmount = 150m,
            CategoryName = "总预算",
            LedgerName = "旅行"
        };

        await service.NotifyIfExceededAsync(new List<BudgetStatus> { ledgerBudget }, 2026, 11);

        Assert.Contains("【旅行 · 总预算】", notifier.LastMessage);
    }

    [Fact]
    public async Task 全部账本预算超支_文案保持原样_不带账本前缀()
    {
        var notifier = new FakeNotificationService { DeliverResult = true };
        var service = new BudgetAlertService(notifier);

        await service.NotifyIfExceededAsync(new List<BudgetStatus> { Exceeded() }, 2026, 11);

        Assert.Contains("【餐饮】", notifier.LastMessage);
        Assert.DoesNotContain(" · ", notifier.LastMessage);
    }
}
