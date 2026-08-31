using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 同步服务实现
/// </summary>
public class SyncService : ISyncService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<SyncService> _logger;
    private const int MaxOfflineCacheCount = 1000;

    public SyncService(IApplicationDbContext context, ILogger<SyncService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<SyncResult> SyncBillsAsync(Guid userId)
    {
        var result = new SyncResult();

        try
        {
            // 获取待同步的账单
            var pendingBills = await GetPendingBillsAsync(userId);

            if (!pendingBills.Any())
            {
                result.Success = true;
                return result;
            }

            // 模拟同步到云端（实际实现中应该调用云端API）
            foreach (var bill in pendingBills)
            {
                try
                {
                    // 模拟网络延迟
                    await Task.Delay(100);

                    // 标记为已同步
                    bill.SyncStatus = SyncStatus.Synced;
                    result.SyncedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "同步账单 {BillId} 失败", bill.Id);
                    bill.SyncStatus = SyncStatus.Synced;
                    result.FailedCount++;
                }
            }

            await _context.SaveChangesAsync();

            result.Success = result.FailedCount == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步过程中发生错误");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<List<Bill>> GetPendingBillsAsync(Guid userId)
    {
        return await _context.Bills
            .Where(b => b.UserId == userId && b.SyncStatus == SyncStatus.Pending)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> MarkBillsAsSyncedAsync(List<Guid> billIds)
    {
        var bills = await _context.Bills
            .Where(b => billIds.Contains(b.Id))
            .ToListAsync();

        foreach (var bill in bills)
        {
            bill.SyncStatus = SyncStatus.Synced;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkBillsAsSyncFailedAsync(List<Guid> billIds)
    {
        var bills = await _context.Bills
            .Where(b => billIds.Contains(b.Id))
            .ToListAsync();

        foreach (var bill in bills)
        {
            bill.SyncStatus = SyncStatus.Failed;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CanSyncAsync()
    {
        // 检查网络状态（简化实现）
        // 实际实现中应该检查网络连接
        return await Task.FromResult(true);
    }

    public async Task<int> GetOfflineCacheCountAsync(Guid userId)
    {
        return await _context.Bills
            .CountAsync(b => b.UserId == userId && b.SyncStatus == SyncStatus.Pending);
    }
}
