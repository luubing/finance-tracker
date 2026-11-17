using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 账单服务实现
/// </summary>
public class BillService : IBillService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<BillService> _logger;

    public BillService(IApplicationDbContext context, ILogger<BillService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<Bill>> GetBillsAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? categoryId = null,
        Guid? paymentChannelId = null,
        Guid? ledgerId = null,
        BillType? type = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _context.Bills
            .Where(b => b.UserId == userId && !b.IsDeleted);

        if (ledgerId.HasValue)
        {
            // 共享账本可见性：当筛选的账本为当前用户的共享账本时，
            // 账本内全部成员的账单对该用户可见（只读展示，仅排除已删除）
            var sharedLedgerIds = await GetSharedLedgerIdsAsync(userId);

            if (sharedLedgerIds.Contains(ledgerId.Value))
            {
                query = _context.Bills
                    .Where(b => b.LedgerId == ledgerId.Value && !b.IsDeleted);
            }
            else
            {
                query = query.Where(b => b.LedgerId == ledgerId.Value);
            }
        }

        // 筛选日期是应用时区的墙上时间，库里的 TransactionTime 是 UTC：先换算再比较
        if (startDate.HasValue)
        {
            var startUtc = AppTimeZone.ToUtc(startDate.Value);
            query = query.Where(b => b.TransactionTime >= startUtc);
        }

        if (endDate.HasValue)
        {
            var endUtc = AppTimeZone.ToUtc(endDate.Value);
            query = query.Where(b => b.TransactionTime <= endUtc);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(b => b.CategoryId == categoryId.Value);
        }

        if (paymentChannelId.HasValue)
        {
            query = query.Where(b => b.PaymentChannelId == paymentChannelId.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(b => b.Type == type.Value);
        }

        return await query
            .OrderByDescending(b => b.TransactionTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(b => b.Category)
            .Include(b => b.PaymentChannel)
            .Include(b => b.Ledger)
            .ToListAsync();
    }

    public async Task<Bill?> GetBillByIdAsync(Guid billId)
    {
        return await _context.Bills
            .Include(b => b.Category)
            .Include(b => b.PaymentChannel)
            .Include(b => b.Ledger)
            .FirstOrDefaultAsync(b => b.Id == billId);
    }

    public async Task<Bill> CreateBillAsync(Bill bill)
    {
        try
        {
            // 先校验外键引用，避免把「FOREIGN KEY constraint failed」这类用户看不懂的数据库错误抛到界面
            await EnsureReferencesExistAsync(bill);

            bill.Id = Guid.NewGuid();
            bill.SyncStatus = SyncStatus.Pending;
            // 数据来源（Source）由调用方决定（手动录入/导入/短信识别/通知栏/语音识别），
            // 不在此处强制覆盖，否则语音等非手动来源的账单会被错误标记为手动录入

            // 入库统一为 UTC：调用方传入的 Local/Unspecified 都视为应用时区的墙上时间（见 AppTimeZone）
            bill.TransactionTime = AppTimeZone.ToUtc(bill.TransactionTime);

            _context.Bills.Add(bill);
            await _context.SaveChangesAsync();

            // 重新加载包含导航属性的数据
            return await GetBillByIdAsync(bill.Id) ?? bill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建账单失败，用户: {UserId}, 金额: {Amount}, 分类: {CategoryId}",
                bill.UserId, bill.Amount, bill.CategoryId);
            throw;
        }
    }

    public async Task<Bill> UpdateBillAsync(Bill bill)
    {
        try
        {
            var existingBill = await _context.Bills
                .FirstOrDefaultAsync(b => b.Id == bill.Id && b.UserId == bill.UserId);

            if (existingBill == null)
            {
                throw new ArgumentException("账单不存在");
            }

            // 必须在修改已跟踪的 existingBill 之前校验：校验失败时不能把改了一半的实体留在 ChangeTracker 里，
            // 否则会连带污染之后的每一次保存
            await EnsureReferencesExistAsync(bill);

            existingBill.Amount = bill.Amount;
            existingBill.Type = bill.Type;
            existingBill.CategoryId = bill.CategoryId;
            existingBill.PaymentChannelId = bill.PaymentChannelId;
            existingBill.LedgerId = bill.LedgerId;

            // 入库统一为 UTC：调用方传入的 Local/Unspecified 都视为应用时区的墙上时间（见 AppTimeZone）
            existingBill.TransactionTime = AppTimeZone.ToUtc(bill.TransactionTime);

            existingBill.Note = bill.Note;
            existingBill.SyncStatus = SyncStatus.Pending;

            await _context.SaveChangesAsync();

            // 重新加载包含导航属性的数据
            return await GetBillByIdAsync(existingBill.Id) ?? existingBill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新账单失败，账单Id: {BillId}, 用户: {UserId}", bill.Id, bill.UserId);
            throw;
        }
    }

    public async Task<bool> DeleteBillAsync(Guid billId, Guid userId)
    {
        try
        {
            var bill = await _context.Bills
                .FirstOrDefaultAsync(b => b.Id == billId && b.UserId == userId);

            if (bill == null)
            {
                return false;
            }

            bill.IsDeleted = true;
            bill.SyncStatus = SyncStatus.Pending;

            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除账单失败，账单Id: {BillId}, 用户: {UserId}", billId, userId);
            throw;
        }
    }

    public async Task<int> AssignBillsToLedgerAsync(List<Guid> billIds, Guid userId, Guid? ledgerId)
    {
        try
        {
            if (billIds == null || billIds.Count == 0)
            {
                return 0;
            }

            // 目标账本必须属于当前用户且未删除（移出账本时 ledgerId 为 null，无需校验）
            if (ledgerId.HasValue)
            {
                var ledgerExists = await _context.Ledgers
                    .AnyAsync(l => l.Id == ledgerId.Value && l.UserId == userId && !l.IsDeleted);

                if (!ledgerExists)
                {
                    throw new ArgumentException("账本不存在");
                }
            }

            var bills = await _context.Bills
                .Where(b => billIds.Contains(b.Id) && b.UserId == userId && !b.IsDeleted)
                .ToListAsync();

            foreach (var bill in bills)
            {
                bill.LedgerId = ledgerId;
                bill.SyncStatus = SyncStatus.Pending;
            }

            await _context.SaveChangesAsync();

            return bills.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量分配账单到账本失败，用户: {UserId}, 账本Id: {LedgerId}, 账单数量: {Count}",
                userId, ledgerId, billIds?.Count ?? 0);
            throw;
        }
    }

    public async Task<int> GetBillCountAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? categoryId = null,
        Guid? paymentChannelId = null,
        Guid? ledgerId = null,
        BillType? type = null)
    {
        var query = _context.Bills
            .Where(b => b.UserId == userId && !b.IsDeleted);

        if (ledgerId.HasValue)
        {
            // 与 GetBillsAsync 保持一致：共享账本内全部成员账单可见
            var sharedLedgerIds = await GetSharedLedgerIdsAsync(userId);

            if (sharedLedgerIds.Contains(ledgerId.Value))
            {
                query = _context.Bills
                    .Where(b => b.LedgerId == ledgerId.Value && !b.IsDeleted);
            }
            else
            {
                query = query.Where(b => b.LedgerId == ledgerId.Value);
            }
        }

        // 与 GetBillsAsync 一致：筛选日期是应用时区的墙上时间，先换算为 UTC 再与库里的值比较
        if (startDate.HasValue)
        {
            var startUtc = AppTimeZone.ToUtc(startDate.Value);
            query = query.Where(b => b.TransactionTime >= startUtc);
        }

        if (endDate.HasValue)
        {
            var endUtc = AppTimeZone.ToUtc(endDate.Value);
            query = query.Where(b => b.TransactionTime <= endUtc);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(b => b.CategoryId == categoryId.Value);
        }

        if (paymentChannelId.HasValue)
        {
            query = query.Where(b => b.PaymentChannelId == paymentChannelId.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(b => b.Type == type.Value);
        }

        return await query.CountAsync();
    }

    /// <summary>
    /// 保存前校验账单引用的用户/分类/支付渠道/账本是否存在，
    /// 把数据库外键错误提前转换为明确、可操作的业务提示。
    /// 使用 IgnoreQueryFilters：只校验外键行是否存在（与数据库约束语义一致），
    /// 因此编辑引用了「已软删除分类/渠道/账本」的历史账单不会被误拦截。
    /// </summary>
    /// <param name="bill">待保存的账单</param>
    /// <exception cref="ArgumentException">引用的实体不存在</exception>
    private async Task EnsureReferencesExistAsync(Bill bill)
    {
        if (!await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == bill.UserId))
        {
            throw new ArgumentException("当前登录用户不存在，请退出并重新登录后再试");
        }

        if (!await _context.Categories.IgnoreQueryFilters().AnyAsync(c => c.Id == bill.CategoryId))
        {
            throw new ArgumentException("分类不存在，请重新选择分类");
        }

        if (!await _context.PaymentChannels.IgnoreQueryFilters().AnyAsync(c => c.Id == bill.PaymentChannelId))
        {
            throw new ArgumentException("支付渠道不存在，请重新选择支付渠道");
        }

        if (bill.LedgerId.HasValue &&
            !await _context.Ledgers.IgnoreQueryFilters().AnyAsync(l => l.Id == bill.LedgerId.Value))
        {
            throw new ArgumentException("账本不存在或尚未同步，请重新选择账本");
        }
    }

    /// <summary>
    /// 获取当前用户作为生效成员（非 Owner）参与的共享账本ID列表（含 Owner 角色行：
    /// Owner 也是成员，但自有账本走 UserId 过滤即可，此处含 Owner 无副作用）
    /// </summary>
    private async Task<List<Guid>> GetSharedLedgerIdsAsync(Guid userId)
    {
        return await _context.LedgerMembers
            .Where(m => m.UserId == userId && m.Status == LedgerMemberStatus.Active && !m.IsDeleted)
            .Select(m => m.LedgerId)
            .ToListAsync();
    }
}
