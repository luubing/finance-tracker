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

    public async Task<MonthlyStatistics> GetMonthlyStatisticsAsync(Guid userId, int year, int month, Guid? ledgerId = null)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddSeconds(-1);

        var bills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= startDate &&
                       b.TransactionTime <= endDate &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
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

    public async Task<List<CategoryStatistics>> GetCategoryStatisticsAsync(Guid userId, int year, int month, BillType type, Guid? ledgerId = null)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddSeconds(-1);

        var query = from b in _context.Bills
                    join c in _context.Categories on b.CategoryId equals c.Id
                    where b.UserId == userId &&
                          b.TransactionTime >= startDate &&
                          b.TransactionTime <= endDate &&
                          b.Type == type &&
                          (!ledgerId.HasValue || b.LedgerId == ledgerId)
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

    public async Task<List<TrendData>> GetTrendDataAsync(Guid userId, DateTime startDate, DateTime endDate, string dimension, Guid? ledgerId = null)
    {
        var bills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= startDate &&
                       b.TransactionTime <= endDate &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
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

    public async Task<AnnualStatistics> GetAnnualStatisticsAsync(Guid userId, int year, Guid? ledgerId = null)
    {
        var startDate = new DateTime(year, 1, 1);
        var endDate = startDate.AddYears(1).AddSeconds(-1);

        var bills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= startDate &&
                       b.TransactionTime <= endDate &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
            .Include(b => b.Category)
            .ToListAsync();

        var monthlyData = new List<MonthlyStatistics>();
        for (int month = 1; month <= 12; month++)
        {
            var monthBills = bills.Where(b => b.TransactionTime.Month == month).ToList();
            monthlyData.Add(new MonthlyStatistics
            {
                Year = year,
                Month = month,
                TotalExpense = monthBills.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
                TotalIncome = monthBills.Where(b => b.Type == BillType.Income).Sum(b => b.Amount),
                BillCount = monthBills.Count
            });
        }

        var categoryStats = bills
            .GroupBy(b => new { b.CategoryId, b.Category.Name, b.Category.Icon })
            .Select(g => new CategoryStatistics
            {
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.Name,
                CategoryIcon = g.Key.Icon,
                Amount = g.Sum(b => b.Amount),
                Count = g.Count()
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var totalAmount = categoryStats.Sum(c => c.Amount);
        if (totalAmount > 0)
        {
            foreach (var stat in categoryStats)
            {
                stat.Percentage = Math.Round(stat.Amount / totalAmount * 100, 2);
            }
        }

        return new AnnualStatistics
        {
            Year = year,
            TotalExpense = bills.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
            TotalIncome = bills.Where(b => b.Type == BillType.Income).Sum(b => b.Amount),
            BillCount = bills.Count,
            MonthlyData = monthlyData,
            CategoryStats = categoryStats
        };
    }

    public async Task<YearOverYearData> GetYearOverYearDataAsync(Guid userId, int year, int month, Guid? ledgerId = null)
    {
        var currentStartDate = new DateTime(year, month, 1);
        var currentEndDate = currentStartDate.AddMonths(1).AddSeconds(-1);
        var previousStartDate = currentStartDate.AddYears(-1);
        var previousEndDate = currentEndDate.AddYears(-1);

        var currentBills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= currentStartDate &&
                       b.TransactionTime <= currentEndDate &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
            .ToListAsync();

        var previousBills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= previousStartDate &&
                       b.TransactionTime <= previousEndDate &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
            .ToListAsync();

        return new YearOverYearData
        {
            CurrentYear = year,
            CurrentMonth = month,
            CurrentExpense = currentBills.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
            CurrentIncome = currentBills.Where(b => b.Type == BillType.Income).Sum(b => b.Amount),
            PreviousYearExpense = previousBills.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
            PreviousYearIncome = previousBills.Where(b => b.Type == BillType.Income).Sum(b => b.Amount)
        };
    }
}
