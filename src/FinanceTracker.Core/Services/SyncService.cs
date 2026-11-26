using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
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

        // 同步作用域内保存不自动盖章：只标记同步状态不算编辑，采用云端版本时沿用云端的更新时间
        // （原因见 SyncTimestampScope）
        using var timestampScope = SyncTimestampScope.Begin();

        try
        {
            // 检查网络连接
            if (!_networkService.IsConnected())
            {
                result.Success = false;
                result.ErrorMessage = "无网络连接";
                return result;
            }

            // 0. 先弄清"云端把我识别成哪个用户"（本地与云端的用户 Id 各自生成、互不相同），
            //    并把历史上误挂在云端用户 Id 名下的本地数据归还给本地用户
            var cloudUserId = await PrepareOwnershipAsync(userId);

            // 1. 先同步账本/分类与支付渠道：账单外键依赖它们，
            //    且拉取账单合并前本地必须已存在对应账本/分类/渠道
            await SyncLedgersCoreAsync(userId, cloudUserId);
            await SyncCategoriesAsync(userId);
            await SyncPaymentChannelsAsync(userId);
            // 共享账本：同步成员关系缓存（拉取共享账本内他人账单前必须先有本地成员关系与用户存根）
            await SyncLedgerMembersCoreAsync(userId, cloudUserId);

            // 获取本地待同步的账单
            var pendingBills = await GetPendingBillsAsync(userId);

            // 2. 批量推送到云端，服务端按 UpdatedAt 做“后写入优先”冲突裁决。
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
                        // 云端版本更新，采用云端权威数据覆盖本地（连同云端的更新时间，不能盖成本机现在）
                        if (item.AuthoritativeBill != null)
                        {
                            item.AuthoritativeBill.ApplyTo(bill);
                            bill.UpdatedAt = item.AuthoritativeBill.UpdatedAt;
                        }

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

            // 3. 拉取云端更新（其他设备新增/修改的账单 + 共享账本内其他成员的账单）合并到本地
            //    先补建本地缺失的分类/支付渠道/用户存根，避免合并账单时外键违例
            var cloudBills = await _cloudSyncClient.PullBillsAsync(userId);
            foreach (var cloudBill in cloudBills)
            {
                // 数据来自云端（可信）：本地缺预设时按预设补建，自愈历史上用随机 GUID 建过预设的库。
                // 服务端处理客户端推送时则不允许（默认 false），见 BillSyncDto.EnsureCategoryExistsAsync
                await cloudBill.EnsureLedgerExistsAsync(_context, userId);
                await cloudBill.EnsureCategoryExistsAsync(_context, userId, allowCreatePreset: true);
                await cloudBill.EnsurePaymentChannelExistsAsync(_context, userId, allowCreatePreset: true);

                // 共享账本内他人账单：本地需要创建者用户存根（仅满足外键与展示，不可登录）。
                // 账单作者是"云端的我"时就是本地用户自己（本地与云端用户 Id 不同），不需要存根
                var billOwnerId = MapToLocalUserId(cloudBill.UserId, userId, cloudUserId);
                if (billOwnerId != userId)
                {
                    await EnsureUserStubAsync(billOwnerId);
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
                    // 自己的账单归属本地用户；共享账本内他人账单保留原作者 UserId（云端 UserId 为空时兜底为本地用户）
                    var newLocal = cloudBill.ToEntity(MapToLocalUserId(cloudBill.UserId, userId, cloudUserId));
                    newLocal.SyncStatus = SyncStatus.Synced;
                    _context.Bills.Add(newLocal);
                    result.SyncedCount++;
                }
                else if (existing.SyncStatus == SyncStatus.Synced && cloudBill.UpdatedAt > existing.UpdatedAt)
                {
                    // 本地已同步且云端更新 → 覆盖本地（连同云端的更新时间，否则之后云端更晚的编辑会被误判为"比本机旧"）
                    cloudBill.ApplyTo(existing);
                    existing.UpdatedAt = cloudBill.UpdatedAt;
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
            // 展示真实原因（DbUpdateException 的外层消息是恒定的通用文案，没有排查价值）
            result.ErrorMessage = ex.GetUserMessage();
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SyncCategoriesAsync(Guid userId)
    {
        using var timestampScope = SyncTimestampScope.Begin();

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
                    local.UpdatedAt = item.AuthoritativeCategory.UpdatedAt;
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
                // 新增云端分类：沿用云端的创建/更新时间（同步作用域内 DbContext 不会再盖章）
                var entity = dto.ToEntity(userId);
                entity.CreatedAt = dto.CreatedAt;
                entity.UpdatedAt = dto.UpdatedAt;
                _context.Categories.Add(entity);
                merged = true;
            }
            else if (dto.UpdatedAt > existing.UpdatedAt && !dto.ContentEquals(existing))
            {
                dto.ApplyTo(existing);
                existing.UpdatedAt = dto.UpdatedAt;
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
        using var timestampScope = SyncTimestampScope.Begin();

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
                    local.UpdatedAt = item.AuthoritativeChannel.UpdatedAt;
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
                var entity = dto.ToEntity(userId);
                entity.CreatedAt = dto.CreatedAt;
                entity.UpdatedAt = dto.UpdatedAt;
                _context.PaymentChannels.Add(entity);
                merged = true;
            }
            else if (dto.UpdatedAt > existing.UpdatedAt && !dto.ContentEquals(existing))
            {
                dto.ApplyTo(existing);
                existing.UpdatedAt = dto.UpdatedAt;
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
        using var timestampScope = SyncTimestampScope.Begin();

        var cloudUserId = await PrepareOwnershipAsync(userId);
        await SyncLedgersCoreAsync(userId, cloudUserId);
    }

    private async Task SyncLedgersCoreAsync(Guid userId, Guid? cloudUserId)
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
                    local.UpdatedAt = item.AuthoritativeLedger.UpdatedAt;
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
            // 账本所有者是"云端的我"时就是本地用户自己（本地与云端用户 Id 不同）；
            // 共享账本（OwnerId 为他人）合并到本地时，需要所有者用户存根满足外键
            if (dto.OwnerId.HasValue)
            {
                dto.OwnerId = MapToLocalUserId(dto.OwnerId.Value, userId, cloudUserId);
                if (dto.OwnerId.Value != userId)
                {
                    await EnsureUserStubAsync(dto.OwnerId.Value);
                }
            }

            var existing = await _context.Ledgers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.Id == dto.Id);

            if (existing == null)
            {
                // 沿用云端的创建/更新时间（同步作用域内 DbContext 不会再盖章）
                var entity = dto.ToEntity(userId);
                entity.CreatedAt = dto.CreatedAt;
                entity.UpdatedAt = dto.UpdatedAt;
                _context.Ledgers.Add(entity);
                merged = true;
            }
            else if (dto.UpdatedAt > existing.UpdatedAt && !dto.ContentEquals(existing))
            {
                dto.ApplyTo(existing);
                existing.UpdatedAt = dto.UpdatedAt;
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
        // 只改同步状态不算编辑：不能把账单的更新时间改写成"同步时刻"（见 SyncTimestampScope）
        using var timestampScope = SyncTimestampScope.Begin();

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
        using var timestampScope = SyncTimestampScope.Begin();

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
    public async Task SyncLedgerMembersAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var timestampScope = SyncTimestampScope.Begin();

        // 先拉取、再弄清"云端把我识别成谁"：拉取会顺带换取云端令牌，成功后读取云端用户 Id 不再产生网络请求。
        // 反过来先读 Id，云端连不上时要白白多连一次（换取令牌失败 → 拉取里再换一次 → 再发拉取请求），
        // 而每次连接失败在部分环境下要等好几秒，页面加载（账单/账本/记一笔都会先刷新成员关系）就被多拖一截。
        // 两件事互不依赖：Id 映射和历史数据归还只影响下面的合并，拉取回来的原始数据与本地状态无关。
        // 取消令牌只交给联网的拉取（换取令牌 + 拉取）：它取消时整次同步中止、本地缓存原样不动；
        // 拉取成功之后的读 Id（此时令牌已在手，不再联网）和本地合并不受取消影响，避免合并做到一半被打断
        var cloudMembers = await _cloudSyncClient.PullLedgerMembersAsync(userId, cancellationToken: cancellationToken);
        var cloudUserId = await PrepareOwnershipAsync(userId);
        await MergeLedgerMembersCoreAsync(userId, cloudUserId, cloudMembers);
    }

    /// <inheritdoc />
    public async Task MergeLedgerMembersAsync(Guid userId, IReadOnlyList<LedgerMemberSyncDto> members)
    {
        using var timestampScope = SyncTimestampScope.Begin();

        var cloudUserId = await PrepareOwnershipAsync(userId);
        await MergeLedgerMembersCoreAsync(userId, cloudUserId, members);
    }

    private async Task SyncLedgerMembersCoreAsync(Guid userId, Guid? cloudUserId)
    {
        var cloudMembers = await _cloudSyncClient.PullLedgerMembersAsync(userId);
        await MergeLedgerMembersCoreAsync(userId, cloudUserId, cloudMembers);
    }

    /// <summary>
    /// 把云端成员关系合并到本地缓存。拉取全部成员关系和"刚收到一条成员关系"（例如邀请接口的返回）共用这一套规则：
    /// 用户 Id 映射、用户/账本存根、(账本, 用户) 对账、沿用云端的更新时间。
    /// </summary>
    private async Task MergeLedgerMembersCoreAsync(
        Guid userId, Guid? cloudUserId, IReadOnlyList<LedgerMemberSyncDto> cloudMembers)
    {
        var merged = false;

        foreach (var dto in cloudMembers)
        {
            // 成员 / 账本所有者是"云端的我"时就是本地用户自己（本地与云端用户 Id 不同）
            var memberUserId = MapToLocalUserId(dto.UserId, userId, cloudUserId);
            Guid? ledgerOwnerId = dto.LedgerOwnerId.HasValue
                ? MapToLocalUserId(dto.LedgerOwnerId.Value, userId, cloudUserId)
                : null;

            // 他人用户 / 他人账本需要本地存根
            if (memberUserId != userId)
            {
                await EnsureUserStubAsync(memberUserId, dto.UserPhoneNumber);
            }

            if (ledgerOwnerId.HasValue && ledgerOwnerId.Value != userId)
            {
                await EnsureUserStubAsync(ledgerOwnerId.Value);
            }

            // 成员关系引用的账本本地可能还没有（例如刚收到别人的邀请、账本还没同步下来）：
            // 按 DTO 附带的信息补建账本存根以满足外键
            await EnsureLedgerStubAsync(dto, ledgerOwnerId ?? memberUserId);

            var existing = await _context.LedgerMembers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == dto.Id);

            if (existing == null)
            {
                // 唯一索引是 (LedgerId, UserId)：本地可能已有同一成员的另一条记录
                // （例如本地懒补的 Owner 行，Id 是本地随机生成的，与云端的不同）。
                // 成员关系以云端为真相源，直接用云端这条取代它（Id 也随之对齐），
                // 而不是新增一条去撞唯一索引。先删后增分两次保存，避免同一批次内撞索引。
                var sameMember = await _context.LedgerMembers
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.LedgerId == dto.LedgerId && m.UserId == memberUserId);
                if (sameMember != null)
                {
                    _context.LedgerMembers.Remove(sameMember);
                    await _context.SaveChangesAsync();
                }

                _context.LedgerMembers.Add(new LedgerMember
                {
                    Id = dto.Id,
                    LedgerId = dto.LedgerId,
                    UserId = memberUserId,
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
                existing.UpdatedAt = dto.UpdatedAt;
                merged = true;
            }
        }

        if (merged)
        {
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 弄清"云端把我识别成哪个用户"，并把历史上误挂在该云端用户 Id 名下的本地数据归还给本地用户。
    /// 返回云端用户 Id；拿不到时返回 null（本次不做 Id 映射）。云端 Id 与本地用户 Id 相同时无需映射，也无需归还。
    /// </summary>
    private async Task<Guid?> PrepareOwnershipAsync(Guid userId)
    {
        Guid? cloudUserId;
        try
        {
            cloudUserId = await _cloudSyncClient.GetCloudUserIdAsync();
        }
        catch (Exception ex)
        {
            // 拿不到不阻断同步：后续推送/拉取会暴露真正的连接/认证错误；这次只是不做 Id 映射
            _logger.LogWarning(ex, "读取云端用户 Id 失败，本次同步不做用户 Id 映射");
            return null;
        }

        if (cloudUserId.HasValue && cloudUserId.Value != userId)
        {
            await ReassignMisownedDataAsync(userId, cloudUserId.Value);
        }

        return cloudUserId;
    }

    /// <summary>
    /// 把云端返回的用户 Id 映射成本地用户 Id：
    /// 等于"云端把我识别成的 Id"（或为空，旧服务端兜底）时就是我自己，换成本地用户 Id；
    /// 其余（共享账本里的他人）保持原样。
    /// </summary>
    private static Guid MapToLocalUserId(Guid cloudSideUserId, Guid localUserId, Guid? cloudUserId) =>
        cloudSideUserId == Guid.Empty || (cloudUserId.HasValue && cloudSideUserId == cloudUserId.Value)
            ? localUserId
            : cloudSideUserId;

    /// <summary>
    /// 归还历史上误挂在云端用户 Id 名下的本地数据（幂等；没有可归还的数据时只做几次查询）。
    ///
    /// 背景：本地与云端的用户 Id 各自生成、互不相同，早期版本把拉取到的"我自己的账单/账本/成员关系"
    /// 当成别人的数据，挂到了以云端 Id 建立的用户存根名下——于是账单列表看不到、账本不属于自己。
    /// </summary>
    private async Task ReassignMisownedDataAsync(Guid userId, Guid cloudUserId)
    {
        var changed = false;

        var bills = await _context.Bills.IgnoreQueryFilters().Where(b => b.UserId == cloudUserId).ToListAsync();
        foreach (var bill in bills)
        {
            bill.UserId = userId;
            changed = true;
        }

        var ledgers = await _context.Ledgers.IgnoreQueryFilters().Where(l => l.UserId == cloudUserId).ToListAsync();
        foreach (var ledger in ledgers)
        {
            ledger.UserId = userId;
            changed = true;
        }

        var members = await _context.LedgerMembers.IgnoreQueryFilters().Where(m => m.UserId == cloudUserId).ToListAsync();
        foreach (var member in members)
        {
            // 本地已有同一账本里"我"的成员记录时，误挂的这条是重复的，直接删除（唯一索引 LedgerId+UserId）
            var duplicated = await _context.LedgerMembers
                .IgnoreQueryFilters()
                .AnyAsync(m => m.LedgerId == member.LedgerId && m.UserId == userId);
            if (duplicated)
            {
                _context.LedgerMembers.Remove(member);
            }
            else
            {
                member.UserId = userId;
            }

            changed = true;
        }

        if (changed)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "已把误挂在云端用户 Id 名下的本地数据归还给本地用户：账单 {Bills} 条、账本 {Ledgers} 个、成员关系 {Members} 条",
                bills.Count, ledgers.Count, members.Count);
        }
    }

    /// <summary>
    /// 确保本地存在指定用户（不存在则创建仅含 Id/手机号的用户存根，满足账单/账本/成员关系外键）。
    /// 无手机号时生成确定性占位手机号（stub_ + 15 位 hex，满足唯一索引且不会与真实手机号冲突），
    /// 后续成员同步携带真实手机号时可升级。
    ///
    /// 手机号有唯一索引：真实手机号已被本地另一个用户占用时（例如同一台设备上登录过多个账号，
    /// 其中一个恰好是云端成员），存根只能用占位手机号，否则整次成员合并都会因唯一索引失败。
    /// </summary>
    private async Task EnsureUserStubAsync(Guid userId, string? phoneNumber = null)
    {
        var exists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id == userId);

        if (!exists)
        {
            var useRealPhone = !string.IsNullOrEmpty(phoneNumber) && !await IsPhoneNumberTakenAsync(phoneNumber, userId);
            _context.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = useRealPhone ? phoneNumber! : BuildStubPhoneNumber(userId)
            });
            await _context.SaveChangesAsync();
        }
        else if (!string.IsNullOrEmpty(phoneNumber))
        {
            // 存根已存在但为占位手机号时升级为真实手机号，便于成员列表展示
            var user = await _context.Users
                .IgnoreQueryFilters()
                .FirstAsync(u => u.Id == userId);

            if (User.IsStubPhoneNumber(user.PhoneNumber) && user.PhoneNumber != phoneNumber
                && !await IsPhoneNumberTakenAsync(phoneNumber, userId))
            {
                user.PhoneNumber = phoneNumber;
                await _context.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// 本地是否已有别的用户（含软删除）占用了这个手机号
    /// </summary>
    private Task<bool> IsPhoneNumberTakenAsync(string phoneNumber, Guid exceptUserId) =>
        _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.PhoneNumber == phoneNumber && u.Id != exceptUserId);

    /// <summary>
    /// 确保本地存在成员关系引用的账本；缺失时按 DTO 附带的信息补建存根。
    /// 存根不带时间戳（同步作用域内不会盖章），时间最旧，真实账本随后拉取时会用云端数据覆盖存根。
    /// </summary>
    private async Task EnsureLedgerStubAsync(LedgerMemberSyncDto dto, Guid ownerId)
    {
        var exists = await _context.Ledgers
            .IgnoreQueryFilters()
            .AnyAsync(l => l.Id == dto.LedgerId);

        if (exists)
        {
            return;
        }

        _context.Ledgers.Add(new Ledger
        {
            Id = dto.LedgerId,
            UserId = ownerId,
            Name = string.IsNullOrWhiteSpace(dto.LedgerName) ? "共享账本" : dto.LedgerName!,
            Icon = string.IsNullOrWhiteSpace(dto.LedgerIcon) ? "mdi-book" : dto.LedgerIcon!,
            SortOrder = 99
        });
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 生成确定性占位手机号（stub_ + 15 位 hex，共 20 字符，满足唯一索引且与真实 11 位手机号格式天然区分）
    /// </summary>
    private static string BuildStubPhoneNumber(Guid userId)
    {
        return $"{User.StubPhonePrefix}{userId.ToString("N")[..15]}";
    }
}

