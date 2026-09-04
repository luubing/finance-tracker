using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 不支持自动捕获/本地数据库的平台（Web.Server 之外的特殊宿主等）的空实现。
/// 用于避免那些平台上 DI 无法解析 IPendingBillService 导致页面崩溃。
/// SQLite 持久化的真实实现位于 FinanceTracker.Infrastructure.Services.PendingBillService。
/// </summary>
public class NoOpPendingBillService : IPendingBillService
{
    public event EventHandler? PendingBillsChanged
    {
        add { }
        remove { }
    }

    public Task<List<PendingBill>> GetPendingBillsAsync() => Task.FromResult(new List<PendingBill>());

    public Task<bool> AddAsync(PendingBill pendingBill) => Task.FromResult(false);

    public Task<bool> RemoveAsync(Guid id) => Task.FromResult(false);

    public Task ClearAsync() => Task.CompletedTask;
}

