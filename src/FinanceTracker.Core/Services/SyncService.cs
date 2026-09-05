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

            // 0. 先同步账本/分类与支付渠道：账单外键依赖它们，
            //    且拉取账单合并前本地必须已存在对应账本/分类/渠道
            await SyncLedgersAsync(userId);
            await SyncCategoriesAsync(userId);
            await SyncPaymentChannelsAsync(userId);
            // 共享账本：同步成员关系缓存（拉取共享账本内他人账单前必须先有本地成员关系与用户存根）
            await SyncLedgerMembersAsync(userId);

            // 获取本地待同步的账单
            var pendingBills = await GetPendingBillsAsync(userId);

            // 1. 批量推送到云端，服务端按 UpdatedAt 做“后写入优先”冲突裁决。
            //    注意：无待同步账单时只跳过推送，拉取必须执行——
            //    云端可能有导入或其他端录入的账单，跳过拉取会导致本地账单列表看不到云端数据。
            if (pendingBills.Any())
            {
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
            }

            // 2. 拉取云端更新（其他设备新增/修改的账单 + 共享账本内其他成员的账单）合并到本地
            //    先补建本地缺失的分类/支付渠道/用户存根，避免合并账单时外键违例
            var cloudBills = await _cloudSyncClient.PullBillsAsync(userId);
            foreach (var cloudBill in cloudBills)
            {
                await cloudBill.EnsureLedgerExistsAsync(_context, userId);
                await cloudBill.EnsureCategoryExistsAsync(_context, userId);
                await cloudBill.EnsurePaymentChannelExistsAsync(_context, userId);

                // 共享账本内他人账单：本地需要创建者用户存根（仅满足外键与展示，不可登录）
                if (cloudBill.UserId != Guid.Empty && cloudBill.UserId != userId)
                {
                    await EnsureUserStubAsync(cloudBill.UserId);
                }
            }

            foreach (var cloudBill in cloudBills)
            {
                // IgnoreQueryFilters：本地软删除的账单也要能查到，否则会被误判为“新建”导致主键冲突
                var existing = await _context.Bills
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(b => b.Id == cloudBill.Id);
                if (existing == null)
                {
                    // 本地不存在 → 新增（云端为权威）。
                    // 共享账本内他人账单保留原作者 UserId（UserId 为空时兜底为本地用户）
                    var ownerId = cloudBill.UserId != Guid.Empty ? cloudBill.UserId : userId;
                    var newLocal = cloudBill.ToEntity(ownerId);
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

    /// <inheritdoc />
    public async Task SyncLedgersAsync(Guid userId)
    {
        // 1. 推送本地全部账本（含软删除，删除操作借此传播）
        var localLedgers = await _context.Ledgers
            .IgnoreQueryFilters()
            .Where(l => l.UserId == userId)
            .ToListAsync();

        var pushResponse = await _cloudSyncClient.PushLedgersAsync(
            userId,
            localLedgers.Select(LedgerSyncDto.FromEntity).ToList());

        foreach (var item in pushResponse.Results)
        {
            var local = localLedgers.FirstOrDefault(l => l.Id == item.LedgerId);
            if (local == null)
            {
                continue;
            }

            if (string.Equals(item.Action, "pulled", StringComparison.OrdinalIgnoreCase) && item.AuthoritativeLedger != null)
            {
                if (!item.AuthoritativeLedger.ContentEquals(local))
                {
                    item.AuthoritativeLedger.ApplyTo(local);
                    await _context.SaveChangesAsync();
                }
                _logger.LogInformation("账本 {LedgerId} 云端版本更新，采用云端数据", item.LedgerId);
            }
            else if (string.Equals(item.Action, "skipped", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("账本 {LedgerId} 推送被跳过: {Error}", item.LedgerId, item.Error);
            }
            else if (string.Equals(item.Action, "failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("账本 {LedgerId} 推送失败: {Error}", item.LedgerId, item.Error);
            }
        }

        // 2. 拉取云端账本合并到本地
        var cloudLedgers = await _cloudSyncClient.PullLedgersAsync(userId);
        var merged = false;

        foreach (var dto in cloudLedgers)
        {
            // 共享账本（OwnerId 为他人）合并到本地时，需要所有者用户存根满足外键
            if (dto.OwnerId.HasValue && dto.OwnerId.Value != userId)
            {
                await EnsureUserStubAsync(dto.OwnerId.Value);
            }

            var existing = await _context.Ledgers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.Id == dto.Id);

            if (existing == null)
            {
                _context.Ledgers.Add(dto.ToEntity(userId));
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
            .Include(b => b.Ledger)
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

    /// <summary>
    /// 合并云端成员关系到本地缓存（供本地账单可见性判定与"我的共享账本"展示）。
    /// 顺手补建用户/账本存根，避免本地外键违例。
    /// </summary>
    public async Task SyncLedgerMembersAsync(Guid userId)
    {
        var cloudMembers = await _cloudSyncClient.PullLedgerMembersAsync(userId);
        var merged = false;

        foreach (var dto in cloudMembers)
        {
            // 他人用户 / 他人账本需要本地存根
            if (dto.UserId != userId)
            {
                await EnsureUserStubAsync(dto.UserId, dto.UserPhoneNumber);
            }

            if (dto.LedgerOwnerId.HasValue && dto.LedgerOwnerId.Value != userId)
            {
                await EnsureUserStubAsync(dto.LedgerOwnerId.Value);
            }

            var existing = await _context.LedgerMembers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == dto.Id);

            if (existing == null)
            {
                _context.LedgerMembers.Add(new LedgerMember
                {
                    Id = dto.Id,
                    LedgerId = dto.LedgerId,
                    UserId = dto.UserId,
                    Role = dto.Role,
                    Status = dto.Status,
                    IsDeleted = dto.IsDeleted,
                    CreatedAt = dto.CreatedAt,
                    UpdatedAt = dto.UpdatedAt
                });
                merged = true;
            }
            else if (dto.UpdatedAt > existing.UpdatedAt && !dto.ContentEquals(existing))
            {
                existing.Role = dto.Role;
                existing.Status = dto.Status;
                existing.IsDeleted = dto.IsDeleted;
                merged = true;
            }
        }

        if (merged)
        {
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 确保本地存在指定用户（不存在则创建仅含 Id/手机号的用户存根，满足账单/账本/成员关系外键）。
    /// 无手机号时生成确定性占位手机号（stub_ + 15 位 hex，满足唯一索引且不会与真实手机号冲突），
    /// 后续成员同步携带真实手机号时可升级。
    /// </summary>
    private async Task EnsureUserStubAsync(Guid userId, string? phoneNumber = null)
    {
        var exists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id == userId);

        if (!exists)
        {
            _context.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = string.IsNullOrEmpty(phoneNumber)
                    ? BuildStubPhoneNumber(userId)
                    : phoneNumber
            });
            await _context.SaveChangesAsync();
        }
        else if (!string.IsNullOrEmpty(phoneNumber))
        {
            // 存根已存在但为占位手机号时升级为真实手机号，便于成员列表展示
            var user = await _context.Users
                .IgnoreQueryFilters()
                .FirstAsync(u => u.Id == userId);

            if (user.PhoneNumber.StartsWith("stub_") && user.PhoneNumber != phoneNumber)
            {
                user.PhoneNumber = phoneNumber;
                await _context.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// 生成确定性占位手机号（stub_ + 15 位 hex，共 20 字符，满足唯一索引且与真实 11 位手机号格式天然区分）
    /// </summary>
    private static string BuildStubPhoneNumber(Guid userId)
    {
        return $"stub_{userId.ToString("N")[..15]}";
    }
}

