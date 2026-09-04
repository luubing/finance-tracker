using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 账本服务实现
/// </summary>
public class LedgerService : ILedgerService
{
    private readonly IApplicationDbContext _context;

    public LedgerService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Ledger>> GetLedgersAsync(Guid userId)
    {
        return await _context.Ledgers
            .Where(l => !l.IsDeleted && l.UserId == userId)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<Ledger?> GetLedgerByIdAsync(Guid ledgerId)
    {
        return await _context.Ledgers
            .FirstOrDefaultAsync(l => l.Id == ledgerId && !l.IsDeleted);
    }

    public async Task<Ledger> CreateLedgerAsync(Ledger ledger)
    {
        ledger.Id = Guid.NewGuid();

        _context.Ledgers.Add(ledger);
        await _context.SaveChangesAsync();

        return ledger;
    }

    public async Task<Ledger> UpdateLedgerAsync(Ledger ledger)
    {
        var existingLedger = await _context.Ledgers
            .FirstOrDefaultAsync(l => l.Id == ledger.Id && l.UserId == ledger.UserId);

        if (existingLedger == null)
        {
            throw new ArgumentException("账本不存在");
        }

        existingLedger.Name = ledger.Name;
        existingLedger.Icon = ledger.Icon;
        existingLedger.SortOrder = ledger.SortOrder;

        await _context.SaveChangesAsync();

        return existingLedger;
    }

    public async Task<bool> DeleteLedgerAsync(Guid ledgerId, Guid userId)
    {
        var ledger = await _context.Ledgers
            .FirstOrDefaultAsync(l => l.Id == ledgerId && l.UserId == userId);

        if (ledger == null)
        {
            return false;
        }

        // 检查是否有关联的账单
        var hasBills = await _context.Bills
            .AnyAsync(b => b.LedgerId == ledgerId);

        if (hasBills)
        {
            throw new InvalidOperationException("该账本下有账单记录，无法删除");
        }

        ledger.IsDeleted = true;
        await _context.SaveChangesAsync();

        return true;
    }
}
