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

            // 同步到云端（模拟实现）
            foreach (var bill in bills)
            {
                try
                {
                    // 模拟云端同步，返回云端版本时间
                    var cloudVersion = await SimulateCloudSyncAsync(bill);

                    // 冲突解决：后写入优先
                    if (cloudVersion > bill.UpdatedAt)
                    {
                        // 云端版本更新，需要拉取云端数据更新本地
                        _logger.LogInformation("账单 {BillId} 云端版本更新，拉取云端数据", bill.Id);

                        // 模拟从云端获取最新数据并更新本地
                        // 实际实现中应该调用云端 API 获取最新数据
                        bill.UpdatedAt = cloudVersion;
                        bill.SyncStatus = SyncStatus.Synced;
                    }
                    else
                    {
                        // 本地版本更新或相同，推送到云端成功
                        _logger.LogInformation("账单 {BillId} 本地版本更新，推送成功", bill.Id);
                        bill.SyncStatus = SyncStatus.Synced;
                    }

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

    /// <summary>
    /// 模拟云端同步（实际实现中应该调用真正的云端 API）
    /// </summary>
    private async Task<DateTime> SimulateCloudSyncAsync(Bill bill)
    {
        // 模拟网络延迟
        await Task.Delay(100);

        // 模拟云端返回的时间戳（实际应该从云端获取）
        return DateTime.UtcNow.AddSeconds(-new Random().Next(0, 60));
    }
}
