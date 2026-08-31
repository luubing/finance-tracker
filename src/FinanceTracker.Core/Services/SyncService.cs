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
    private readonly INetworkService _networkService;
    private readonly ISyncQueueService _syncQueueService;
    private readonly ILogger<SyncService> _logger;
    private const int MaxOfflineCacheCount = 1000;
    private const int BatchSize = 10;

    public SyncService(
        IApplicationDbContext context,
        INetworkService networkService,
        ISyncQueueService syncQueueService,
        ILogger<SyncService> logger)
    {
        _context = context;
        _networkService = networkService;
        _syncQueueService = syncQueueService;
        _logger = logger;
    }

    public async Task<SyncResult> SyncBillsAsync(Guid userId)
    {
        var result = new SyncResult();

        try
        {
            // 检查网络连接
            if (!_networkService.IsConnected())
            {
                result.Success = false;
                result.ErrorMessage = "无网络连接";
                return result;
            }

            // 从队列中获取待同步的账单
            var pendingBillIds = await _syncQueueService.DequeueAsync(BatchSize);

            if (!pendingBillIds.Any())
            {
                // 如果队列为空，从数据库获取待同步的账单
                var pendingBills = await GetPendingBillsAsync(userId);
                pendingBillIds = pendingBills.Select(b => b.Id).ToList();

                // 将账单添加到队列
                foreach (var billId in pendingBillIds)
                {
                    await _syncQueueService.EnqueueAsync(billId);
                }

                pendingBillIds = await _syncQueueService.DequeueAsync(BatchSize);
            }

            if (!pendingBillIds.Any())
            {
                result.Success = true;
                return result;
            }

            // 获取账单详情
            var bills = await _context.Bills
                .Where(b => pendingBillIds.Contains(b.Id))
                .ToListAsync();

            // 模拟同步到云端（实际实现中应该调用云端API）
            foreach (var bill in bills)
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
                    bill.SyncStatus = SyncStatus.Failed;
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
            .Take(MaxOfflineCacheCount)
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
        return await Task.FromResult(_networkService.IsConnected());
    }

    public async Task<int> GetOfflineCacheCountAsync(Guid userId)
    {
        return await _context.Bills
            .CountAsync(b => b.UserId == userId && b.SyncStatus == SyncStatus.Pending);
    }
}
