using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;
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
    private readonly ICloudSyncClient _cloudSyncClient;
    private readonly ILogger<SyncService> _logger;
    private const int MaxOfflineCacheCount = 1000;

    public SyncService(
        IApplicationDbContext context,
        INetworkService networkService,
        ICloudSyncClient cloudSyncClient,
        ILogger<SyncService> logger)
    {
        _context = context;
        _networkService = networkService;
        _cloudSyncClient = cloudSyncClient;
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

            // 0. 先同步分类与支付渠道：账单外键依赖它们，
            //    且拉取账单合并前本地必须已存在对应分类/渠道
            await SyncCategoriesAsync(userId);
            await SyncPaymentChannelsAsync(userId);

            // 获取本地待同步的账单
            var pendingBills = await GetPendingBillsAsync(userId);

            if (!pendingBills.Any())
            {
                result.Success = true;
                return result;
            }

            // 1. 批量推送到云端，服务端按 UpdatedAt 做“后写入优先”冲突裁决
            var pushResponse = await _cloudSyncClient.PushBillsAsync(
                userId,
                pendingBills.Select(BillSyncDto.FromEntity).ToList());
            // 处理推送结果
            foreach (var item in pushResponse.Results)
            {
                var bill = pendingBills.FirstOrDefault(b => b.Id == item.BillId);
                if (bill == null)
                {
                    continue;
                }

                if (string.Equals(item.Action, "pulled", StringComparison.OrdinalIgnoreCase))
                {
                    // 云端版本更新，采用云端权威数据覆盖本地
                    item.AuthoritativeBill?.ApplyTo(bill);
                    _logger.LogInformation("账单 {BillId} 云端版本更新，采用云端数据", bill.Id);
                }
                else if (string.Equals(item.Action, "pushed", StringComparison.OrdinalIgnoreCase))
                {
                    // 本地版本更新或相同，推送成功
                    _logger.LogInformation("账单 {BillId} 推送成功", bill.Id);
                }
                else
                {
                    // failed
                    _logger.LogWarning("账单 {BillId} 同步失败: {Error}", bill.Id, item.Error);
                    bill.SyncStatus = SyncStatus.Failed;
                    result.FailedCount++;
                    continue;
                }

                bill.SyncStatus = SyncStatus.Synced;
                result.SyncedCount++;
            }

            // 2. 拉取云端更新（其他设备新增/修改的账单）合并到本地
            //    先补建本地缺失的分类/支付渠道，避免合并账单时外键违例
            var cloudBills = await _cloudSyncClient.PullBillsAsync(userId);
            foreach (var cloudBill in cloudBills)
            {
                await cloudBill.EnsureCategoryExistsAsync(_context, userId);
                await cloudBill.EnsurePaymentChannelExistsAsync(_context, userId);
            }

            foreach (var cloudBill in cloudBills)
            {
                // IgnoreQueryFilters：本地软删除的账单也要能查到，否则会被误判为“新建”导致主键冲突
                var existing = await _context.Bills
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(b => b.Id == cloudBill.Id);
                if (existing == null)
                {
                    // 本地不存在 → 新增（云端为权威）
                    var newLocal = cloudBill.ToEntity(userId);
                    newLocal.SyncStatus = SyncStatus.Synced;
                    _context.Bills.Add(newLocal);
                    result.SyncedCount++;
                }
                else if (existing.SyncStatus == SyncStatus.Synced && cloudBill.UpdatedAt > existing.UpdatedAt)
                {
                    // 本地已同步且云端更新 → 覆盖本地
                    cloudBill.ApplyTo(existing);
                    existing.SyncStatus = SyncStatus.Synced;
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

    /// <inheritdoc />
    public async Task SyncCategoriesAsync(Guid userId)
    {
        // 1. 推送本地全部自定义分类（含软删除，删除操作借此传播；数量少，全量推送由服务端 LWW+内容比较去重）
        var localCategories = await _context.Categories
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && !c.IsPreset)
            .ToListAsync();

        var pushResponse = await _cloudSyncClient.PushCategoriesAsync(
            userId,
            localCategories.Select(CategorySyncDto.FromEntity).ToList());

        foreach (var item in pushResponse.Results)
        {
            var local = localCategories.FirstOrDefault(c => c.Id == item.CategoryId);
            if (local == null)
            {
                continue;
            }

            if (string.Equals(item.Action, "pulled", StringComparison.OrdinalIgnoreCase) && item.AuthoritativeCategory != null)
            {
                if (!item.AuthoritativeCategory.ContentEquals(local))
                {
                    item.AuthoritativeCategory.ApplyTo(local);
                    await _context.SaveChangesAsync();
                }
                _logger.LogInformation("分类 {CategoryId} 云端版本更新，采用云端数据", item.CategoryId);
            }
            else if (string.Equals(item.Action, "skipped", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("分类 {CategoryId} 推送被跳过: {Error}", item.CategoryId, item.Error);
            }
            else if (string.Equals(item.Action, "failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("分类 {CategoryId} 推送失败: {Error}", item.CategoryId, item.Error);
            }
        }

        // 2. 拉取云端自定义分类合并到本地
        var cloudCategories = await _cloudSyncClient.PullCategoriesAsync(userId);
        var merged = false;

        foreach (var dto in cloudCategories)
        {
            var existing = await _context.Categories
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == dto.Id);

            if (existing == null)
            {
                _context.Categories.Add(dto.ToEntity(userId));
                merged = true;
            }
            else if (dto.UpdatedAt > existing.UpdatedAt && !dto.ContentEquals(existing))
            {
                dto.ApplyTo(existing);
                merged = true;
            }
        }

        if (merged)
        {
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc />
    public async Task SyncPaymentChannelsAsync(Guid userId)
    {
        // 1. 推送本地全部自定义支付渠道（含软删除）
        var localChannels = await _context.PaymentChannels
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && !c.IsPreset)
            .ToListAsync();

        var pushResponse = await _cloudSyncClient.PushPaymentChannelsAsync(
            userId,
            localChannels.Select(PaymentChannelSyncDto.FromEntity).ToList());

        foreach (var item in pushResponse.Results)
        {
            var local = localChannels.FirstOrDefault(c => c.Id == item.PaymentChannelId);
            if (local == null)
            {
                continue;
            }

            if (string.Equals(item.Action, "pulled", StringComparison.OrdinalIgnoreCase) && item.AuthoritativeChannel != null)
            {
                if (!item.AuthoritativeChannel.ContentEquals(local))
                {
                    item.AuthoritativeChannel.ApplyTo(local);
                    await _context.SaveChangesAsync();
                }
                _logger.LogInformation("支付渠道 {ChannelId} 云端版本更新，采用云端数据", item.PaymentChannelId);
            }
            else if (string.Equals(item.Action, "skipped", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("支付渠道 {ChannelId} 推送被跳过: {Error}", item.PaymentChannelId, item.Error);
            }
            else if (string.Equals(item.Action, "failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("支付渠道 {ChannelId} 推送失败: {Error}", item.PaymentChannelId, item.Error);
            }
        }

        // 2. 拉取云端自定义支付渠道合并到本地
        var cloudChannels = await _cloudSyncClient.PullPaymentChannelsAsync(userId);
        var merged = false;

        foreach (var dto in cloudChannels)
        {
            var existing = await _context.PaymentChannels
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == dto.Id);

            if (existing == null)
            {
                _context.PaymentChannels.Add(dto.ToEntity(userId));
                merged = true;
            }
            else if (dto.UpdatedAt > existing.UpdatedAt && !dto.ContentEquals(existing))
            {
                dto.ApplyTo(existing);
                merged = true;
            }
        }

        if (merged)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<Bill>> GetPendingBillsAsync(Guid userId)
    {
        // Include 导航属性：推送 DTO 需要携带分类/渠道信息，供云端“缺则补建”
        // Pending 之外同时取 Failed：失败的账单必须在下次同步时自动重试，
        // 否则会永久卡在“同步失败”状态（例如曾因时间格式问题推送失败的账单）
        return await _context.Bills
            .Where(b => b.UserId == userId &&
                (b.SyncStatus == SyncStatus.Pending || b.SyncStatus == SyncStatus.Failed))
            .OrderBy(b => b.CreatedAt)
            .Take(MaxOfflineCacheCount)
            .Include(b => b.Category)
            .Include(b => b.PaymentChannel)
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
        // 与 GetPendingBillsAsync 保持一致：Failed 的账单也会在下次同步时重试
        return await _context.Bills
            .CountAsync(b => b.UserId == userId &&
                (b.SyncStatus == SyncStatus.Pending || b.SyncStatus == SyncStatus.Failed));
    }
}

