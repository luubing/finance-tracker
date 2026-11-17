using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 统计服务实现。
/// <para>
/// 时间口径（见 <see cref="AppTimeZone"/> 与 docs/adr/0005-time-zone-handling.md）：
/// 账单时间在库中是 UTC，而"某月/某年/某天"按<b>应用时区的自然日历</b>划分——
/// 查询时先把本地边界换算成 UTC（统一左闭右开），分组时先把 UTC 转回本地时间再取日期/月份。
/// 否则东八区用户 3 月 1 日凌晨记的账（UTC 为 2 月 28 日）会被算进 2 月。
/// </para>
/// </summary>
public class StatisticsService : IStatisticsService
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    /// <param name="context">数据库上下文</param>
    /// <param name="timeProvider">时钟（可选，默认系统时钟）；整年同比需要"今天"来确定当年的截止日，测试可注入固定时间</param>
    public StatisticsService(IApplicationDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MonthlyStatistics> GetMonthlyStatisticsAsync(Guid userId, int year, int month, Guid? ledgerId = null)
    {
        var monthStart = new DateTime(year, month, 1);

        var bills = await BillsInRange(userId, monthStart, monthStart.AddMonths(1), ledgerId)
            .ToListAsync();

        return new MonthlyStatistics
        {
            Year = year,
            Month = month,
            TotalExpense = SumAmount(bills, BillType.Expense),
            TotalIncome = SumAmount(bills, BillType.Income),
            BillCount = bills.Count
        };
    }

    public async Task<List<CategoryStatistics>> GetCategoryStatisticsAsync(Guid userId, int year, int month, BillType type, Guid? ledgerId = null)
    {
        var monthStart = new DateTime(year, month, 1);

        // 与月度统计共用同一个区间查询（用户/账本/左闭右开的自然月），两处的月份边界不会各自漂移
        var query = from b in BillsInRange(userId, monthStart, monthStart.AddMonths(1), ledgerId)
                    join c in _context.Categories on b.CategoryId equals c.Id
                    where b.Type == type
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

    public async Task<List<TrendData>> GetTrendDataAsync(Guid userId, DateTime startDate, DateTime endDate, TrendDimension dimension, Guid? ledgerId = null)
    {
        // 先确定分组方式：维度不合法时在查询数据库之前就失败
        Func<DateTime, DateTime> bucketOf = dimension switch
        {
            TrendDimension.Day => localTime => localTime.Date,
            TrendDimension.Week => GetWeekStart,
            TrendDimension.Month => localTime => new DateTime(localTime.Year, localTime.Month, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "不支持的趋势维度")
        };

        // startDate / endDate 是应用时区的时刻，且 endDate 为含端点的结束时刻，沿用 <= 语义（本接口的既有契约：
        // "最近 N 天"这类滑动窗口以"此刻"为终点）。需要覆盖整天/整月时，调用方用 TimeBoundaries.EndOfDay/EndOfMonth
        // 取最后一个 tick——手写"减 1 秒"会漏掉最后一秒内的账单，而月度/分类汇总是左闭右开的，两处口径就对不上
        var startUtc = AppTimeZone.ToUtc(startDate);
        var endUtc = AppTimeZone.ToUtc(endDate);

        var bills = await _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= startUtc &&
                       b.TransactionTime <= endUtc &&
                       (!ledgerId.HasValue || b.LedgerId == ledgerId))
            .ToListAsync();

        return bills
            .GroupBy(b => bucketOf(AppTimeZone.ToLocal(b.TransactionTime)))
            .OrderBy(g => g.Key)
            .Select(g => new TrendData
            {
                Date = g.Key,
                Expense = SumAmount(g, BillType.Expense),
                Income = SumAmount(g, BillType.Income)
            })
            .ToList();
    }

    /// <summary>
    /// 所在周的周一（本地日期）
    /// </summary>
    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-1 * diff).Date;
    }

    public async Task<AnnualStatistics> GetAnnualStatisticsAsync(Guid userId, int year, Guid? ledgerId = null)
    {
        var yearStart = new DateTime(year, 1, 1);

        var bills = await BillsInRange(userId, yearStart, yearStart.AddYears(1), ledgerId)
            .Include(b => b.Category)
            .ToListAsync();

        // 按应用时区的月份归属（而不是 UTC 月份）
        var billsByMonth = bills.ToLookup(b => AppTimeZone.ToLocal(b.TransactionTime).Month);

        var monthlyData = new List<MonthlyStatistics>();
        for (var month = 1; month <= 12; month++)
        {
            var monthBills = billsByMonth[month].ToList();
            monthlyData.Add(new MonthlyStatistics
            {
                Year = year,
                Month = month,
                TotalExpense = SumAmount(monthBills, BillType.Expense),
                TotalIncome = SumAmount(monthBills, BillType.Income),
                BillCount = monthBills.Count
            });
        }

        return new AnnualStatistics
        {
            Year = year,
            TotalExpense = SumAmount(bills, BillType.Expense),
            TotalIncome = SumAmount(bills, BillType.Income),
            BillCount = bills.Count,
            MonthlyData = monthlyData,
            CategoryStats = BuildCategoryStats(bills)
        };
    }

    public async Task<YearOverYearData> GetYearOverYearDataAsync(Guid userId, int year, int month, Guid? ledgerId = null)
    {
        var currentStart = new DateTime(year, month, 1);
        var currentEnd = currentStart.AddMonths(1);

        // 去年同月：月初永远是 1 日，AddYears 不会遇到 2 月 29 日问题
        return await BuildYearOverYearAsync(
            userId, ledgerId, year, month,
            currentStart, currentEnd,
            currentStart.AddYears(-1), currentEnd.AddYears(-1));
    }

    /// <inheritdoc />
    public async Task<YearOverYearData> GetAnnualYearOverYearDataAsync(Guid userId, int year, Guid? ledgerId = null)
    {
        var today = AppTimeZone.ToLocal(_timeProvider.GetUtcNow().UtcDateTime).Date;

        var currentStart = new DateTime(year, 1, 1);
        var previousStart = currentStart.AddYears(-1);

        // 区间均为左闭右开。
        // 当年只比较到今天（含）为止，去年同期取到去年的同月同日（闰日 2/29 回退到 2/28）；
        // 已结束的年份（以及还没到的未来年份）比较整年。
        DateTime currentEnd;
        DateTime previousEnd;
        if (year == today.Year)
        {
            currentEnd = today.AddDays(1);
            previousEnd = today.AddYears(-1).AddDays(1);
        }
        else
        {
            currentEnd = currentStart.AddYears(1);
            previousEnd = currentStart;
        }

        return await BuildYearOverYearAsync(
            userId, ledgerId, year, month: null,
            currentStart, currentEnd, previousStart, previousEnd);
    }

    /// <summary>
    /// 统计当前期间与去年同期（区间均为应用时区自然日的左闭右开区间），并带上对比期间的起止日供界面展示
    /// </summary>
    private async Task<YearOverYearData> BuildYearOverYearAsync(
        Guid userId,
        Guid? ledgerId,
        int year,
        int? month,
        DateTime currentStart,
        DateTime currentEndExclusive,
        DateTime previousStart,
        DateTime previousEndExclusive)
    {
        var currentBills = await BillsInRange(userId, currentStart, currentEndExclusive, ledgerId)
            .ToListAsync();

        var previousBills = await BillsInRange(userId, previousStart, previousEndExclusive, ledgerId)
            .ToListAsync();

        return new YearOverYearData
        {
            CurrentYear = year,
            CurrentMonth = month,
            CurrentPeriodStart = currentStart,
            CurrentPeriodEnd = currentEndExclusive.AddDays(-1),
            PreviousPeriodStart = previousStart,
            PreviousPeriodEnd = previousEndExclusive.AddDays(-1),
            CurrentExpense = SumAmount(currentBills, BillType.Expense),
            CurrentIncome = SumAmount(currentBills, BillType.Income),
            PreviousYearExpense = SumAmount(previousBills, BillType.Expense),
            PreviousYearIncome = SumAmount(previousBills, BillType.Income)
        };
    }

    /// <inheritdoc />
    public async Task<CustomStatistics> GetCustomStatisticsAsync(Guid userId, DateTime startDate, DateTime endDate, Guid? ledgerId = null)
    {
        // 归一化为"含当天"的日期范围（应用时区的自然日）
        var start = startDate.Date;
        var endExclusive = endDate.Date.AddDays(1);

        var bills = await BillsInRange(userId, start, endExclusive, ledgerId)
            .Include(b => b.Category)
            .ToListAsync();

        var result = new CustomStatistics
        {
            StartDate = start,
            EndDate = endDate.Date,
            TotalExpense = SumAmount(bills, BillType.Expense),
            TotalIncome = SumAmount(bills, BillType.Income),
            BillCount = bills.Count,
            DailyTrend = bills
                .GroupBy(b => AppTimeZone.ToLocal(b.TransactionTime).Date)
                .OrderBy(g => g.Key)
                .Select(g => new TrendData
                {
                    Date = g.Key,
                    Expense = SumAmount(g, BillType.Expense),
                    Income = SumAmount(g, BillType.Income)
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
        var currentEnd = start.AddDays(dayCount);
        var previousStart = start.AddDays(-dayCount);

        var currentBills = await BillsInRange(userId, start, currentEnd, ledgerId)
            .Where(b => b.Type == BillType.Expense)
            .Include(b => b.Category)
            .ToListAsync();

        var previousBills = await BillsInRange(userId, previousStart, start, ledgerId)
            .Where(b => b.Type == BillType.Expense)
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
    /// 查询应用时区区间 [startLocal, endLocalExclusive)（左闭右开）内的账单。
    /// 边界在这里统一换算成 UTC，与库中的 UTC 时间直接比较；软删除由 Bill 的全局查询过滤器排除。
    /// </summary>
    private IQueryable<Bill> BillsInRange(Guid userId, DateTime startLocal, DateTime endLocalExclusive, Guid? ledgerId)
    {
        var startUtc = AppTimeZone.ToUtc(startLocal);
        var endUtc = AppTimeZone.ToUtc(endLocalExclusive);

        var query = _context.Bills
            .Where(b => b.UserId == userId &&
                       b.TransactionTime >= startUtc &&
                       b.TransactionTime < endUtc);

        if (ledgerId.HasValue)
        {
            query = query.Where(b => b.LedgerId == ledgerId.Value);
        }

        return query;
    }

    private static decimal SumAmount(IEnumerable<Bill> bills, BillType type)
    {
        return bills.Where(b => b.Type == type).Sum(b => b.Amount);
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
