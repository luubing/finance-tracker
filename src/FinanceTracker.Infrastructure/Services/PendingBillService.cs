using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Infrastructure.Services;

/// <summary>
/// 待确认账单服务（SQLite 持久化实现，单例注册）。
/// Android 后台服务（通知监听/短信广播）与 Blazor 页面共享同一实例：
/// 后台捕获 → AddAsync 落库 → 触发 PendingBillsChanged → 页面实时刷新 → 用户确认后转为正式账单。
/// 每次操作通过 IDbContextFactory 创建独立 DbContext（工厂为单例，可被后台线程并发使用），
/// 写操作用信号量串行化，避免 SQLite 并发写冲突。
/// </summary>
public class PendingBillService : IPendingBillService
{
    /// <summary>
    /// 待确认账单上限，防止异常场景下无限增长
    /// </summary>
    public const int MaxCount = 200;

    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    // 串行化写操作（SQLite 不支持并发写）
    private readonly SemaphoreSlim _writeMutex = new(1, 1);

    public event EventHandler? PendingBillsChanged;

    public PendingBillService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<PendingBill>> GetPendingBillsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.PendingBills
            .AsNoTracking()
            .OrderByDescending(b => b.CapturedAt)
            .ToListAsync();
    }

    public async Task<bool> AddAsync(PendingBill pendingBill)
    {
        bool added;

        await _writeMutex.WaitAsync();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            // 去重：同一条支付可能同时触发短信+通知（如银行扣款短信 + 微信通知），
            // 或系统对同一通知多次回调，按 来源+交易时间(±1分钟)+金额 判重
            var duplicate = await db.PendingBills.AsNoTracking().AnyAsync(b =>
                b.Source == pendingBill.Source &&
                b.Amount == pendingBill.Amount &&
                b.TransactionTime >= pendingBill.TransactionTime.AddMinutes(-1) &&
                b.TransactionTime <= pendingBill.TransactionTime.AddMinutes(1));

            if (duplicate)
            {
                added = false;
            }
            else
            {
                var count = await db.PendingBills.CountAsync();
                if (count >= MaxCount)
                {
                    var oldest = await db.PendingBills.OrderBy(b => b.CapturedAt).FirstAsync();
                    db.PendingBills.Remove(oldest);
                }

                db.PendingBills.Add(pendingBill);
                await db.SaveChangesAsync();
                added = true;
            }
        }
        finally
        {
            _writeMutex.Release();
        }

        if (added)
        {
            PendingBillsChanged?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }

    public async Task<bool> RemoveAsync(Guid id)
    {
        bool removed;

        await _writeMutex.WaitAsync();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var bill = await db.PendingBills.FindAsync(id);
            removed = bill != null;
            if (bill != null)
            {
                db.PendingBills.Remove(bill);
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            _writeMutex.Release();
        }

        if (removed)
        {
            PendingBillsChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public async Task ClearAsync()
    {
        await _writeMutex.WaitAsync();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            await db.PendingBills.ExecuteDeleteAsync();
        }
        finally
        {
            _writeMutex.Release();
        }

        PendingBillsChanged?.Invoke(this, EventArgs.Empty);
    }
}
