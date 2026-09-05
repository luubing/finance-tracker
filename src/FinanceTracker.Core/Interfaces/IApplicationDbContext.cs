using FinanceTracker.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 应用程序数据库上下文接口
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Bill> Bills { get; }
    DbSet<Category> Categories { get; }
    DbSet<PaymentChannel> PaymentChannels { get; }
    DbSet<Ledger> Ledgers { get; }
    DbSet<LedgerMember> LedgerMembers { get; }
    DbSet<Budget> Budgets { get; }

    DbSet<PendingBill> PendingBills { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
