using FinanceTracker.Core.Entities;
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

    /// <inheritdoc />
    public async Task<CustomStatistics> GetCustomStatisticsAsync(Guid userId, DateTime startDate, DateTime endDate, Guid? ledgerId = null)
    {
        // 归一化为"含当天"的日期范围
        var start = startDate.Date;
        var end = endDate.Date.AddDays(1).AddSeconds(-1);

        var bills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= start &&
                       b.TransactionTime <= end &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
            .Include(b => b.Category)
            .ToListAsync();

        var result = new CustomStatistics
        {
            StartDate = start,
            EndDate = endDate.Date,
            TotalExpense = bills.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
            TotalIncome = bills.Where(b => b.Type == BillType.Income).Sum(b => b.Amount),
            BillCount = bills.Count,
            DailyTrend = bills
                .GroupBy(b => b.TransactionTime.Date)
                .OrderBy(g => g.Key)
                .Select(g => new TrendData
                {
                    Date = g.Key,
                    Expense = g.Where(b => b.Type == BillType.Expense).Sum(b => b.Amount),
                    Income = g.Where(b => b.Type == BillType.Income).Sum(b => b.Amount)
                })
                .ToList()
        };

        result.ExpenseCategoryStats = BuildCategoryStats(bills.Where(b => b.Type == BillType.Expense));
        result.IncomeCategoryStats = BuildCategoryStats(bills.Where(b => b.Type == BillType.Income));

        return result;
    }

    /// <inheritdoc />
    public async Task<List<CategoryComparisonData>> GetCategoryComparisonAsync(Guid userId, DateTime startDate, DateTime endDate, Guid? ledgerId = null)
    {
        var start = startDate.Date;

        // 上一等长周期：按整数天数紧邻回推（含当天口径，避免浮点天数带来的边界秒级偏差）
        var dayCount = (int)(endDate.Date - startDate.Date).TotalDays + 1;
        var end = start.AddDays(dayCount).AddSeconds(-1);
        var previousEnd = start.AddSeconds(-1);
        var previousStart = start.AddDays(-dayCount);

        var currentBills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= start &&
                       b.TransactionTime <= end &&
                       b.Type == BillType.Expense &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
            .Include(b => b.Category)
            .ToListAsync();

        var previousBills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= previousStart &&
                       b.TransactionTime <= previousEnd &&
                       b.Type == BillType.Expense &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
            .Include(b => b.Category)
            .ToListAsync();

        var currentStats = BuildCategoryStats(currentBills);
        var previousDict = BuildCategoryStats(previousBills)
            .ToDictionary(s => s.CategoryId, s => s.Amount);

        // 并集：本期有或上期有的分类都参与对比（上期金额缺省 0）
        var categoryIds = currentStats.Select(s => s.CategoryId)
            .Union(previousDict.Keys)
            .Distinct();

        var comparison = new List<CategoryComparisonData>();
        var currentDict = currentStats.ToDictionary(s => s.CategoryId);

        foreach (var categoryId in categoryIds)
        {
            var current = currentDict.GetValueOrDefault(categoryId);
            comparison.Add(new CategoryComparisonData
            {
                CategoryId = categoryId,
                CategoryName = current?.CategoryName ?? "未知分类",
                CategoryIcon = current?.CategoryIcon ?? "mdi-tag",
                CurrentAmount = current?.Amount ?? 0,
                PreviousAmount = previousDict.GetValueOrDefault(categoryId)
            });
        }

        // 按变化绝对值降序（变化最大的分类排前面）
        return comparison
            .OrderByDescending(c => Math.Abs(c.ChangeAmount))
            .ToList();
    }

    /// <summary>
    /// 按分类聚合账单并计算占比（金额降序）
    /// </summary>
    private static List<CategoryStatistics> BuildCategoryStats(IEnumerable<Bill> bills)
    {
        var stats = bills
            .GroupBy(b => new { b.CategoryId, b.Category!.Name, b.Category.Icon })
            .Select(g => new CategoryStatistics
            {
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.Name,
                CategoryIcon = g.Key.Icon,
                Amount = g.Sum(b => b.Amount),
                Count = g.Count()
            })
            .OrderByDescending(s => s.Amount)
            .ToList();

        var totalAmount = stats.Sum(s => s.Amount);
        if (totalAmount > 0)
        {
            foreach (var stat in stats)
            {
                stat.Percentage = Math.Round(stat.Amount / totalAmount * 100, 2);
            }
        }

        return stats;
    }
}
