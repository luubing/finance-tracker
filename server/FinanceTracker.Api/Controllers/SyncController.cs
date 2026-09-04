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

    public SyncController(IApplicationDbContext context)
    {
        _context = context;
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
                // 先补建账单引用的分类/支付渠道（远端缺失时），避免 Bills 外键违例
                await dto.EnsureCategoryExistsAsync(_context, userId);
                await dto.EnsurePaymentChannelExistsAsync(_context, userId);

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

        // IgnoreQueryFilters：软删除的账单也要返回，删除操作才能同步到其他设备
        // （客户端会按 IsDeleted 标记本地数据，全局过滤由客户端各页面自行处理）
        var query = _context.Bills.IgnoreQueryFilters().Where(b => b.UserId == userId);
        if (since.HasValue)
        {
            query = query.Where(b => b.UpdatedAt >= since.Value);
        }

        // Include 导航属性：让 DTO 携带分类/渠道信息，客户端据此"缺则补建"
        var bills = await query
            .OrderBy(b => b.UpdatedAt)
            .Include(b => b.Category)
            .Include(b => b.PaymentChannel)
            .ToListAsync();

        return Ok(new SyncPullResponse
        {
            Bills = bills.Select(BillSyncDto.FromEntity).ToList()
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
}

