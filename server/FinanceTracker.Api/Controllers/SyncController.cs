using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 同步控制器（云端账单的推送/拉取，供客户端调用）
/// </summary>
public class SyncController : BaseApiController
{
    private readonly IApplicationDbContext _context;
    private readonly ILedgerMemberService _ledgerMemberService;

    public SyncController(IApplicationDbContext context, ILedgerMemberService ledgerMemberService)
    {
        _context = context;
        _ledgerMemberService = ledgerMemberService;
    }

    /// <summary>
    /// 批量推送账单到云端，服务端按 UpdatedAt 做“后写入优先”冲突裁决
    /// </summary>
    /// <param name="request">待推送的账单列表</param>
    /// <returns>每个账单的冲突裁决结果</returns>
    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest request)
    {
        var userId = GetUserId();
        var results = new List<CloudSyncPushItemResult>();
        var syncedCount = 0;
        var failedCount = 0;

        foreach (var dto in request?.Bills ?? new List<BillSyncDto>())
        {
            try
            {
                // 先补建账单引用的账本/分类/支付渠道（远端缺失时），避免 Bills 外键违例。
                // 注意：补建必须在写权限校验之前——账本在云端尚不存在时（如账本推送曾失败），
                // 校验会因"账本不存在"抛异常导致该账单永远无法推送；补建仅会以推送者身份
                // 创建全新账本（已存在的账本不会被创建），不存在越权风险。
                await dto.EnsureLedgerExistsAsync(_context, userId);
                await dto.EnsureCategoryExistsAsync(_context, userId);
                await dto.EnsurePaymentChannelExistsAsync(_context, userId);

                if (dto.LedgerId.HasValue && dto.LedgerId.Value != Guid.Empty)
                {
                    // 共享账本写权限校验：Viewer/非成员不能把账单归属到共享账本
                    await _ledgerMemberService.EnsureCanWriteAsync(dto.LedgerId.Value, userId);
                }

                // IgnoreQueryFilters：云端账单可能处于软删除状态（IsDeleted=true），
                // 若被全局过滤器过滤掉会被误判为“新建”，导致主键冲突
                var cloud = await _context.Bills
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(b => b.Id == dto.Id && b.UserId == userId);

                if (cloud == null)
                {
                    // 本地有、云端没有 → 在云端创建（UserId 以服务端为准）
                    var newCloudBill = dto.ToEntity(userId);
                    newCloudBill.SyncStatus = SyncStatus.Synced;

                    _context.Bills.Add(newCloudBill);
                    await _context.SaveChangesAsync();
                    results.Add(new CloudSyncPushItemResult(dto.Id, "pushed", null));
                }
                else if (dto.UpdatedAt >= cloud.UpdatedAt)
                {
                    // 本地版本更新或相同 → 用本地数据覆盖云端（后写入优先）
                    dto.ApplyTo(cloud);
                    cloud.SyncStatus = SyncStatus.Synced;
                    await _context.SaveChangesAsync();
                    results.Add(new CloudSyncPushItemResult(dto.Id, "pushed", null));
                }
                else
                {
                    // 云端版本更新 → 返回云端权威数据，客户端据此覆盖本地
                    results.Add(new CloudSyncPushItemResult(dto.Id, "pulled", BillSyncDto.FromEntity(cloud)));
                }

                syncedCount++;
            }
            catch (Exception ex)
            {
                // 单条账单失败（如外键/校验错误）只影响该条，不中止整批推送；
                // 避免一条脏数据导致整批 500、远端库一条都写不进去。
                failedCount++;
                results.Add(new CloudSyncPushItemResult(dto.Id, "failed", null, ex.Message));

                // 把本次失败涉及的实体从跟踪器中分离，避免后续保存重试导致连锁失败。
                // 注意：IApplicationDbContext 不暴露 ChangeTracker，需向下转型到 DbContext。
                if (_context is DbContext dbContext)
                {
                    foreach (var entry in dbContext.ChangeTracker.Entries())
                    {
                        if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                }
            }
        }

        return Ok(new SyncPushResponse
        {
            Results = results,
            SyncedCount = syncedCount,
            FailedCount = failedCount
        });
    }

    /// <summary>
    /// 拉取云端账单更新（since 为空则返回该用户的全部云端账单）
    /// </summary>
    /// <param name="request">拉取条件</param>
    /// <returns>云端账单列表</returns>
    [HttpPost("pull")]
    public async Task<IActionResult> Pull([FromBody] SyncPullRequest? request)
    {
        var userId = GetUserId();
        var since = request?.Since;

        // 共享账本范围：用户作为生效成员参与的账本（成员可拉取账本内全部成员的账单，只读展示）
        var sharedLedgerIds = await _context.LedgerMembers
            .Where(m => m.UserId == userId && m.Status == LedgerMemberStatus.Active && !m.IsDeleted)
            .Select(m => m.LedgerId)
            .ToListAsync();

        // IgnoreQueryFilters：软删除的账单也要返回，删除操作才能同步到其他设备
        // （客户端会按 IsDeleted 标记本地数据，全局过滤由客户端各页面自行处理）
        var query = _context.Bills.IgnoreQueryFilters()
            .Where(b => b.UserId == userId ||
                (b.LedgerId != null && sharedLedgerIds.Contains(b.LedgerId.Value)));
        if (since.HasValue)
        {
            query = query.Where(b => b.UpdatedAt >= since.Value);
        }

        // Include 导航属性：让 DTO 携带分类/渠道/账本信息，客户端据此"缺则补建"
        var bills = await query
            .OrderBy(b => b.UpdatedAt)
            .Include(b => b.Category)
            .Include(b => b.PaymentChannel)
            .Include(b => b.Ledger)
            .ToListAsync();

        return Ok(new SyncPullResponse
        {
            Bills = bills.Select(BillSyncDto.FromEntity).ToList()
        });
    }

    /// <summary>
    /// 拉取账本成员关系（客户端缓存，用于本地账单可见性判定与"我的共享账本"展示）
    /// </summary>
    [HttpPost("ledgermembers/pull")]
    public async Task<IActionResult> PullLedgerMembers([FromBody] LedgerMemberSyncPullRequest? request)
    {
        var userId = GetUserId();
        var since = request?.Since;

        // 历史账本懒补 Owner 成员行（幂等）：共享功能上线前创建的账本尚无成员行，
        // 不补齐的话下面按成员行过滤的查询拉不到自己账本的 Owner 行，
        // 客户端本地缓存将永远缺失 Owner 身份（成员列表/邀请入口不可用）
        var ownedLedgerIds = await _context.Ledgers
            .Where(l => l.UserId == userId && !l.IsDeleted)
            .Select(l => l.Id)
            .ToListAsync();

        foreach (var ownedLedgerId in ownedLedgerIds)
        {
            await _ledgerMemberService.EnsureOwnerMemberRowAsync(ownedLedgerId);
        }

        // 我参与的成员关系（含待确认邀请）+ 我所在共享账本的全部成员关系（展示成员列表用）。
        // 注意：必须排除已删除（被移除/退出/拒绝）的成员关系，否则被移除成员仍可拉取该账本的成员列表（信息泄露）
        var myLedgerIds = await _context.LedgerMembers
            .Where(m => m.UserId == userId && !m.IsDeleted)
            .Select(m => m.LedgerId)
            .ToListAsync();

        var query = _context.LedgerMembers
            .Where(m => m.UserId == userId ||
                (m.Status == LedgerMemberStatus.Active && !m.IsDeleted && myLedgerIds.Contains(m.LedgerId)));

        if (since.HasValue)
        {
            query = query.Where(m => m.UpdatedAt >= since.Value);
        }

        var members = await query
            .OrderBy(m => m.UpdatedAt)
            .Include(m => m.User)
            .Include(m => m.Ledger)
            .ToListAsync();

        return Ok(new LedgerMemberSyncPullResponse
        {
            Members = members.Select(LedgerMemberSyncDto.FromEntity).ToList()
        });
    }

    /// <summary>
    /// 批量推送自定义分类到云端（服务端按 UpdatedAt 做“后写入优先”裁决；不允许覆盖预设或他人数据）
    /// </summary>
    [HttpPost("categories/push")]
    public async Task<IActionResult> PushCategories([FromBody] CategorySyncPushRequest request)
    {
        var userId = GetUserId();
        var results = new List<CategorySyncItemResult>();

        foreach (var dto in request?.Categories ?? new List<CategorySyncDto>())
        {
            try
            {
                var existing = await _context.Categories
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == dto.Id);

                if (existing == null)
                {
                    var entity = dto.ToEntity(userId);
                    _context.Categories.Add(entity);
                    await _context.SaveChangesAsync();
                    results.Add(new CategorySyncItemResult(dto.Id, "pushed"));
                }
                else if (existing.UserId != userId || existing.IsPreset)
                {
                    // 同 Id 但归属他人或是预设：拒绝覆盖（防越权）
                    results.Add(new CategorySyncItemResult(dto.Id, "skipped",
                        CategorySyncDto.FromEntity(existing), "数据归属其他用户或预设"));
                }
                else if (dto.UpdatedAt >= existing.UpdatedAt)
                {
                    // 内容一致时跳过写入，避免 UpdatedAt 被无意义刷新
                    if (!dto.ContentEquals(existing))
                    {
                        dto.ApplyTo(existing);
                        await _context.SaveChangesAsync();
                    }
                    results.Add(new CategorySyncItemResult(dto.Id, "pushed"));
                }
                else
                {
                    // 云端版本更新 → 返回权威数据，客户端据此覆盖本地
                    results.Add(new CategorySyncItemResult(dto.Id, "pulled", CategorySyncDto.FromEntity(existing)));
                }
            }
            catch (Exception ex)
            {
                results.Add(new CategorySyncItemResult(dto.Id, "failed", null, ex.Message));
                if (_context is DbContext dbContext)
                {
                    foreach (var entry in dbContext.ChangeTracker.Entries())
                    {
                        if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                }
            }
        }

        return Ok(new CategorySyncPushResponse { Results = results });
    }

    /// <summary>
    /// 拉取云端自定义分类（含软删除，删除操作借此同步到其他设备）
    /// </summary>
    [HttpPost("categories/pull")]
    public async Task<IActionResult> PullCategories([FromBody] CategorySyncPullRequest? request)
    {
        var userId = GetUserId();
        var since = request?.Since;

        var query = _context.Categories
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && !c.IsPreset);

        if (since.HasValue)
        {
            query = query.Where(c => c.UpdatedAt >= since.Value);
        }

        var categories = await query
            .OrderBy(c => c.UpdatedAt)
            .ToListAsync();

        return Ok(new CategorySyncPullResponse
        {
            Categories = categories.Select(CategorySyncDto.FromEntity).ToList()
        });
    }

    /// <summary>
    /// 批量推送自定义支付渠道到云端（服务端按 UpdatedAt 做“后写入优先”裁决；不允许覆盖预设或他人数据）
    /// </summary>
    [HttpPost("paymentchannels/push")]
    public async Task<IActionResult> PushPaymentChannels([FromBody] PaymentChannelSyncPushRequest request)
    {
        var userId = GetUserId();
        var results = new List<PaymentChannelSyncItemResult>();

        foreach (var dto in request?.PaymentChannels ?? new List<PaymentChannelSyncDto>())
        {
            try
            {
                var existing = await _context.PaymentChannels
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == dto.Id);

                if (existing == null)
                {
                    var entity = dto.ToEntity(userId);
                    _context.PaymentChannels.Add(entity);
                    await _context.SaveChangesAsync();
                    results.Add(new PaymentChannelSyncItemResult(dto.Id, "pushed"));
                }
                else if (existing.UserId != userId || existing.IsPreset)
                {
                    // 同 Id 但归属他人或是预设：拒绝覆盖（防越权）
                    results.Add(new PaymentChannelSyncItemResult(dto.Id, "skipped",
                        PaymentChannelSyncDto.FromEntity(existing), "数据归属其他用户或预设"));
                }
                else if (dto.UpdatedAt >= existing.UpdatedAt)
                {
                    // 内容一致时跳过写入，避免 UpdatedAt 被无意义刷新
                    if (!dto.ContentEquals(existing))
                    {
                        dto.ApplyTo(existing);
                        await _context.SaveChangesAsync();
                    }
                    results.Add(new PaymentChannelSyncItemResult(dto.Id, "pushed"));
                }
                else
                {
                    // 云端版本更新 → 返回权威数据，客户端据此覆盖本地
                    results.Add(new PaymentChannelSyncItemResult(dto.Id, "pulled", PaymentChannelSyncDto.FromEntity(existing)));
                }
            }
            catch (Exception ex)
            {
                results.Add(new PaymentChannelSyncItemResult(dto.Id, "failed", null, ex.Message));
                if (_context is DbContext dbContext)
                {
                    foreach (var entry in dbContext.ChangeTracker.Entries())
                    {
                        if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                }
            }
        }

        return Ok(new PaymentChannelSyncPushResponse { Results = results });
    }

    /// <summary>
    /// 拉取云端自定义支付渠道（含软删除，删除操作借此同步到其他设备）
    /// </summary>
    [HttpPost("paymentchannels/pull")]
    public async Task<IActionResult> PullPaymentChannels([FromBody] PaymentChannelSyncPullRequest? request)
    {
        var userId = GetUserId();
        var since = request?.Since;

        var query = _context.PaymentChannels
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && !c.IsPreset);

        if (since.HasValue)
        {
            query = query.Where(c => c.UpdatedAt >= since.Value);
        }

        var channels = await query
            .OrderBy(c => c.UpdatedAt)
            .ToListAsync();

        return Ok(new PaymentChannelSyncPullResponse
        {
            PaymentChannels = channels.Select(PaymentChannelSyncDto.FromEntity).ToList()
        });
    }

    /// <summary>
    /// 批量推送账本到云端，服务端按 UpdatedAt 做"后写入优先"冲突裁决
    /// </summary>
    /// <param name="request">待推送的账本列表</param>
    /// <returns>每个账本的冲突裁决结果</returns>
    [HttpPost("ledgers/push")]
    public async Task<IActionResult> PushLedgers([FromBody] LedgerSyncPushRequest request)
    {
        var userId = GetUserId();
        var results = new List<LedgerSyncItemResult>();

        foreach (var dto in request?.Ledgers ?? new List<LedgerSyncDto>())
        {
            try
            {
                var existing = await _context.Ledgers
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(l => l.Id == dto.Id);

                if (existing == null)
                {
                    var entity = dto.ToEntity(userId);
                    _context.Ledgers.Add(entity);
                    await _context.SaveChangesAsync();
                    results.Add(new LedgerSyncItemResult(dto.Id, "pushed"));
                }
                else if (existing.UserId != userId)
                {
                    // 同 Id 但归属他人：拒绝覆盖（防越权）
                    results.Add(new LedgerSyncItemResult(dto.Id, "skipped",
                        LedgerSyncDto.FromEntity(existing), "数据归属其他用户"));
                }
                else if (dto.UpdatedAt >= existing.UpdatedAt)
                {
                    // 内容一致时跳过写入，避免 UpdatedAt 被无意义刷新
                    if (!dto.ContentEquals(existing))
                    {
                        dto.ApplyTo(existing);
                        await _context.SaveChangesAsync();
                    }
                    results.Add(new LedgerSyncItemResult(dto.Id, "pushed"));
                }
                else
                {
                    // 云端版本更新 → 返回权威数据，客户端据此覆盖本地
                    results.Add(new LedgerSyncItemResult(dto.Id, "pulled", LedgerSyncDto.FromEntity(existing)));
                }
            }
            catch (Exception ex)
            {
                results.Add(new LedgerSyncItemResult(dto.Id, "failed", null, ex.Message));
                if (_context is DbContext dbContext)
                {
                    foreach (var entry in dbContext.ChangeTracker.Entries())
                    {
                        if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                }
            }
        }

        return Ok(new LedgerSyncPushResponse { Results = results });
    }

    /// <summary>
    /// 拉取云端账本（含软删除，删除操作借此同步到其他设备）
    /// </summary>
    [HttpPost("ledgers/pull")]
    public async Task<IActionResult> PullLedgers([FromBody] LedgerSyncPullRequest? request)
    {
        var userId = GetUserId();
        var since = request?.Since;

        // 共享账本范围：用户作为生效成员参与的账本（成员端拉取账本实体，用于本地外键与展示）
        var sharedLedgerIds = await _context.LedgerMembers
            .Where(m => m.UserId == userId && m.Status == LedgerMemberStatus.Active && !m.IsDeleted)
            .Select(m => m.LedgerId)
            .ToListAsync();

        var query = _context.Ledgers
            .IgnoreQueryFilters()
            .Where(l => l.UserId == userId || sharedLedgerIds.Contains(l.Id));

        if (since.HasValue)
        {
            query = query.Where(l => l.UpdatedAt >= since.Value);
        }

        var ledgers = await query
            .OrderBy(l => l.UpdatedAt)
            .ToListAsync();

        return Ok(new LedgerSyncPullResponse
        {
            Ledgers = ledgers.Select(LedgerSyncDto.FromEntity).ToList()
        });
    }
}

