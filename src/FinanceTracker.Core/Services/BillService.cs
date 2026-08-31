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
        BillType? type = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _context.Bills
            .Where(b => b.UserId == userId);

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
            .ToListAsync();
    }

    public async Task<Bill?> GetBillByIdAsync(Guid billId)
    {
        return await _context.Bills
            .Include(b => b.Category)
            .Include(b => b.PaymentChannel)
            .FirstOrDefaultAsync(b => b.Id == billId);
    }

    public async Task<Bill> CreateBillAsync(Bill bill)
    {
        bill.Id = Guid.NewGuid();
        bill.SyncStatus = SyncStatus.Pending;
        bill.Source = BillSource.Manual;

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
        existingBill.TransactionTime = bill.TransactionTime;
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

    public async Task<int> GetBillCountAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? categoryId = null,
        Guid? paymentChannelId = null,
        BillType? type = null)
    {
        var query = _context.Bills
            .Where(b => b.UserId == userId);

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

        if (type.HasValue)
        {
            query = query.Where(b => b.Type == type.Value);
        }

        return await query.CountAsync();
    }
}
