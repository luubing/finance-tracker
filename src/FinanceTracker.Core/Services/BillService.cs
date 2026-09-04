using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 账单服务实现
/// </summary>
public class BillService : IBillService
{
    private readonly IApplicationDbContext _context;

    public BillService(IApplicationDbContext context)
    {
        _context = context;
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

        if (startDate.HasValue)
        {
            query = query.Where(b => b.TransactionTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(b => b.TransactionTime <= endDate.Value);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(b => b.CategoryId == categoryId.Value);
        }

        if (paymentChannelId.HasValue)
        {
            query = query.Where(b => b.PaymentChannelId == paymentChannelId.Value);
        }

        if (ledgerId.HasValue)
        {
            query = query.Where(b => b.LedgerId == ledgerId.Value);
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
        bill.Id = Guid.NewGuid();
        bill.SyncStatus = SyncStatus.Pending;
        // 数据来源（Source）由调用方决定（手动录入/导入/短信识别/通知栏/语音识别），
        // 不在此处强制覆盖，否则语音等非手动来源的账单会被错误标记为手动录入

        // 确保 TransactionTime 为 UTC 时间
        if (bill.TransactionTime.Kind != DateTimeKind.Utc)
        {
            bill.TransactionTime = bill.TransactionTime.ToUniversalTime();
        }

        _context.Bills.Add(bill);
        await _context.SaveChangesAsync();

        // 重新加载包含导航属性的数据
        return await GetBillByIdAsync(bill.Id) ?? bill;
    }

    public async Task<Bill> UpdateBillAsync(Bill bill)
    {
        var existingBill = await _context.Bills
            .FirstOrDefaultAsync(b => b.Id == bill.Id && b.UserId == bill.UserId);

        if (existingBill == null)
        {
            throw new ArgumentException("账单不存在");
        }

        existingBill.Amount = bill.Amount;
        existingBill.Type = bill.Type;
        existingBill.CategoryId = bill.CategoryId;
        existingBill.PaymentChannelId = bill.PaymentChannelId;
        existingBill.LedgerId = bill.LedgerId;

        // 确保 TransactionTime 为 UTC 时间
        if (bill.TransactionTime.Kind != DateTimeKind.Utc)
        {
            existingBill.TransactionTime = bill.TransactionTime.ToUniversalTime();
        }
        else
        {
            existingBill.TransactionTime = bill.TransactionTime;
        }

        existingBill.Note = bill.Note;
        existingBill.SyncStatus = SyncStatus.Pending;

        await _context.SaveChangesAsync();

        // 重新加载包含导航属性的数据
        return await GetBillByIdAsync(existingBill.Id) ?? existingBill;
    }

    public async Task<bool> DeleteBillAsync(Guid billId, Guid userId)
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

    public async Task<int> AssignBillsToLedgerAsync(List<Guid> billIds, Guid userId, Guid? ledgerId)
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

        if (startDate.HasValue)
        {
            query = query.Where(b => b.TransactionTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(b => b.TransactionTime <= endDate.Value);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(b => b.CategoryId == categoryId.Value);
        }

        if (paymentChannelId.HasValue)
        {
            query = query.Where(b => b.PaymentChannelId == paymentChannelId.Value);
        }

        if (ledgerId.HasValue)
        {
            query = query.Where(b => b.LedgerId == ledgerId.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(b => b.Type == type.Value);
        }

        return await query.CountAsync();
    }
}
