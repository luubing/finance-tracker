using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 统计服务实现
/// </summary>
public class StatisticsService : IStatisticsService
{
    private readonly IApplicationDbContext _context;

    public StatisticsService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MonthlyStatistics> GetMonthlyStatisticsAsync(Guid userId, int year, int month)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddSeconds(-1);

        var bills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= startDate &&
                       b.TransactionTime <= endDate)
            .ToListAsync();

        return new MonthlyStatistics
        {
            Year = year,
            Month = month,
            TotalExpense = bills.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
            TotalIncome = bills.Where(b => b.Type == BillType.Income).Sum(b => b.Amount),
            BillCount = bills.Count
        };
    }

    public async Task<List<CategoryStatistics>> GetCategoryStatisticsAsync(Guid userId, int year, int month, BillType type)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddSeconds(-1);

        var query = from b in _context.Bills
                    join c in _context.Categories on b.CategoryId equals c.Id
                    where b.UserId == userId &&
                          b.TransactionTime >= startDate &&
                          b.TransactionTime <= endDate &&
                          b.Type == type
                    group b by new { b.CategoryId, c.Name, c.Icon } into g
                    select new CategoryStatistics
                    {
                        CategoryId = g.Key.CategoryId,
                        CategoryName = g.Key.Name,
                        CategoryIcon = g.Key.Icon,
                        Amount = g.Sum(b => b.Amount),
                        Count = g.Count()
                    };

        var result = await query.ToListAsync();

        var totalAmount = result.Sum(r => r.Amount);
        if (totalAmount > 0)
        {
            foreach (var item in result)
            {
                item.Percentage = Math.Round(item.Amount / totalAmount * 100, 2);
            }
        }

        return result.OrderByDescending(r => r.Amount).ToList();
    }

    public async Task<List<TrendData>> GetTrendDataAsync(Guid userId, DateTime startDate, DateTime endDate, string dimension)
    {
        var bills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= startDate &&
                       b.TransactionTime <= endDate)
            .ToListAsync();

        var result = new List<TrendData>();

        switch (dimension.ToLower())
        {
            case "day":
            var dailyGroups = bills.GroupBy(b => b.TransactionTime.Date);
            foreach (var group in dailyGroups.OrderBy(g => g.Key))
            {
                result.Add(new TrendData
                {
                    Date = group.Key,
                    Expense = group.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
                    Income = group.Where(b => b.Type == BillType.Income).Sum(b => b.Amount)
                });
            }
            break;

        case "week":
            var weeklyGroups = bills.GroupBy(b => GetWeekStart(b.TransactionTime));
            foreach (var group in weeklyGroups.OrderBy(g => g.Key))
            {
                result.Add(new TrendData
                {
                    Date = group.Key,
                    Expense = group.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
                    Income = group.Where(b => b.Type == BillType.Income).Sum(b => b.Amount)
                });
            }
            break;

        case "month":
            var monthlyGroups = bills.GroupBy(b => new DateTime(b.TransactionTime.Year, b.TransactionTime.Month, 1));
            foreach (var group in monthlyGroups.OrderBy(g => g.Key))
            {
                result.Add(new TrendData
                {
                    Date = group.Key,
                    Expense = group.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
                    Income = group.Where(b => b.Type == BillType.Income).Sum(b => b.Amount)
                });
            }
            break;
        }

        return result;
    }

    private DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-1 * diff).Date;
    }
}
