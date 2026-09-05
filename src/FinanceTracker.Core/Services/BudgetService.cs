using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 预算服务实现
/// </summary>
public class BudgetService : IBudgetService
{
    private readonly IApplicationDbContext _context;

    public BudgetService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Budget>> GetBudgetsAsync(Guid userId, int year, int month, Guid? ledgerId = null)
    {
        var query = _context.Budgets
            .Where(b => b.UserId == userId && b.Year == year && b.Month == month);

        if (ledgerId.HasValue)
        {
            // 指定账本时：返回该账本的预算 + 全部账本的预算
            query = query.Where(b => b.LedgerId == ledgerId.Value || b.LedgerId == null);
        }
        else
        {
            // 未指定账本时：仅返回全部账本的预算（与 GetBudgetStatusAsync 口径一致，
            // 避免页面同时取列表与状态时按账本预算匹配不到执行情况而被跳过）
            query = query.Where(b => b.LedgerId == null);
        }

        return await query
            .OrderBy(b => b.CategoryId != null)
            .ThenBy(b => b.CreatedAt)
            .ToListAsync();
    }

    public Task<Budget?> GetBudgetByIdAsync(Guid budgetId)
    {
        return _context.Budgets
            .FirstOrDefaultAsync(b => b.Id == budgetId);
    }

    public async Task<Budget> CreateBudgetAsync(Budget budget)
    {
        ValidateBudget(budget);

        // 同一用户、同一年月、同一账本范围下，同一分类的预算不可重复（CategoryId/LedgerId 均为 null 表示总预算）
        var exists = await _context.Budgets
            .AnyAsync(b => b.UserId == budget.UserId
                           && b.Year == budget.Year
                           && b.Month == budget.Month
                           && b.LedgerId == budget.LedgerId
                           && b.CategoryId == budget.CategoryId);

        if (exists)
        {
            throw new InvalidOperationException("该月份已存在相同的预算，请直接编辑");
        }

        budget.Id = Guid.NewGuid();

        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        return budget;
    }

    public async Task<Budget> UpdateBudgetAsync(Budget budget)
    {
        ValidateBudget(budget);

        var existingBudget = await _context.Budgets
            .FirstOrDefaultAsync(b => b.Id == budget.Id && b.UserId == budget.UserId);

        if (existingBudget == null)
        {
            throw new ArgumentException("预算不存在");
        }

        // 排除自身后查重
        var exists = await _context.Budgets
            .AnyAsync(b => b.Id != budget.Id
                           && b.UserId == budget.UserId
                           && b.Year == budget.Year
                           && b.Month == budget.Month
                           && b.LedgerId == budget.LedgerId
                           && b.CategoryId == budget.CategoryId);

        if (exists)
        {
            throw new InvalidOperationException("该月份已存在相同的预算，请直接编辑");
        }

        existingBudget.Year = budget.Year;
        existingBudget.Month = budget.Month;
        existingBudget.Amount = budget.Amount;
        existingBudget.LedgerId = budget.LedgerId;
        existingBudget.CategoryId = budget.CategoryId;

        await _context.SaveChangesAsync();

        return existingBudget;
    }

    public async Task<bool> DeleteBudgetAsync(Guid budgetId, Guid userId)
    {
        var budget = await _context.Budgets
            .FirstOrDefaultAsync(b => b.Id == budgetId && b.UserId == userId);

        if (budget == null)
        {
            return false;
        }

        budget.IsDeleted = true;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<List<BudgetStatus>> GetBudgetStatusAsync(Guid userId, int year, int month, Guid? ledgerId = null)
    {
        ValidateYearMonth(year, month);

        // 预算范围：指定账本时 = 该账本的预算 + 全部账本的预算；未指定账本时 = 仅全部账本的预算
        var budgetsQuery = _context.Budgets
            .Where(b => b.UserId == userId && b.Year == year && b.Month == month);

        if (ledgerId.HasValue)
        {
            budgetsQuery = budgetsQuery.Where(b => b.LedgerId == ledgerId.Value || b.LedgerId == null);
        }
        else
        {
            budgetsQuery = budgetsQuery.Where(b => b.LedgerId == null);
        }

        var budgets = await budgetsQuery.ToListAsync();

        if (budgets.Count == 0)
        {
            return new List<BudgetStatus>();
        }

        // 已用金额按支出账单统计（软删除由 Bill 的全局查询过滤器自动排除）。
        // 注意：账单一次性加载且不按 ledgerId 过滤——"全部账本"预算(LedgerId=null)的
        // 已用额必须统计所有账单，按账本预算(LedgerId=X)只统计 X 的账单，各自独立匹配
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1);

        var bills = await _context.Bills
            .Where(bill => bill.UserId == userId
                           && bill.Type == BillType.Expense
                           && bill.TransactionTime >= startDate
                           && bill.TransactionTime < endDate)
            .ToListAsync();

        // 加载预算关联的分类与账本名称
        var categoryIds = budgets
            .Where(b => b.CategoryId.HasValue)
            .Select(b => b.CategoryId!.Value)
            .Distinct()
            .ToList();
        var ledgerIds = budgets
            .Where(b => b.LedgerId.HasValue)
            .Select(b => b.LedgerId!.Value)
            .Distinct()
            .ToList();

        var categories = categoryIds.Count == 0
            ? new Dictionary<Guid, Category>()
            : await _context.Categories
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);

        var ledgers = ledgerIds.Count == 0
            ? new Dictionary<Guid, Ledger>()
            : await _context.Ledgers
                .Where(l => ledgerIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id);

        var result = new List<BudgetStatus>();

        foreach (var budget in budgets)
        {
            // 已用金额：总预算统计全部支出；按账本预算只统计该账本支出；分类预算只统计该分类支出
            var usedAmount = bills
                .Where(bill => (budget.LedgerId == null || bill.LedgerId == budget.LedgerId)
                               && (budget.CategoryId == null || bill.CategoryId == budget.CategoryId))
                .Sum(bill => bill.Amount);

            result.Add(new BudgetStatus
            {
                BudgetId = budget.Id,
                Amount = budget.Amount,
                UsedAmount = usedAmount,
                CategoryId = budget.CategoryId,
                CategoryName = budget.CategoryId.HasValue
                    ? categories.GetValueOrDefault(budget.CategoryId.Value)?.Name ?? "未知分类"
                    : "总预算",
                CategoryIcon = budget.CategoryId.HasValue
                    ? categories.GetValueOrDefault(budget.CategoryId.Value)?.Icon ?? "mdi-tag"
                    : "mdi-wallet",
                LedgerId = budget.LedgerId,
                LedgerName = budget.LedgerId.HasValue
                    ? ledgers.GetValueOrDefault(budget.LedgerId.Value)?.Name
                    : null
            });
        }

        // 总预算在前，分类预算按名称排序
        return result
            .OrderByDescending(s => s.CategoryId == null)
            .ThenBy(s => s.CategoryName)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<BudgetAlert?> GetBudgetAlertAsync(Guid userId, int year, int month, Guid? ledgerId, Guid? categoryId)
    {
        var statuses = await GetBudgetStatusAsync(userId, year, month, ledgerId);

        // 与该账单相关的预算：总预算 + 该分类的分类预算
        var relevant = statuses
            .Where(s => s.CategoryId == null || (categoryId.HasValue && s.CategoryId == categoryId.Value))
            .ToList();

        if (relevant.Count == 0)
        {
            return null;
        }

        // 取使用率最高的一条作为预警对象
        var worst = relevant.OrderByDescending(s => s.Amount == 0 ? 0 : s.UsedAmount / s.Amount).First();
        var usagePercentage = worst.Amount == 0 ? 0 : Math.Round(worst.UsedAmount / worst.Amount * 100, 1);

        if (usagePercentage < 80)
        {
            return null;
        }

        return new BudgetAlert
        {
            BudgetId = worst.BudgetId,
            Amount = worst.Amount,
            UsedAmount = worst.UsedAmount,
            UsagePercentage = usagePercentage,
            CategoryName = worst.CategoryName,
            IsExceeded = usagePercentage >= 100,
            IsWarning = usagePercentage >= 80 && usagePercentage < 100
        };
    }

    private static void ValidateBudget(Budget budget)
    {
        ValidateYearMonth(budget.Year, budget.Month);

        if (budget.Amount <= 0)
        {
            throw new ArgumentException("预算金额必须大于0");
        }
    }

    private static void ValidateYearMonth(int year, int month)
    {
        if (year < 2000 || year > 2100)
        {
            throw new ArgumentException("年份无效");
        }

        if (month < 1 || month > 12)
        {
            throw new ArgumentException("月份无效");
        }
    }
}
