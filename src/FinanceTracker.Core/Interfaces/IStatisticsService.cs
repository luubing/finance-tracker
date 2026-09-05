using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 统计服务接口
/// </summary>
public interface IStatisticsService
{
    /// <summary>
    /// 获取月度统计数据
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>月度统计数据</returns>
    Task<MonthlyStatistics> GetMonthlyStatisticsAsync(Guid userId, int year, int month, Guid? ledgerId = null);

    /// <summary>
    /// 获取分类统计数据
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="type">账单类型</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>分类统计数据</returns>
    Task<List<CategoryStatistics>> GetCategoryStatisticsAsync(Guid userId, int year, int month, BillType type, Guid? ledgerId = null);

    /// <summary>
    /// 获取趋势数据
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="dimension">维度（day/week/month）</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>趋势数据</returns>
    Task<List<TrendData>> GetTrendDataAsync(Guid userId, DateTime startDate, DateTime endDate, string dimension, Guid? ledgerId = null);

    /// <summary>
    /// 获取年度统计数据
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>年度统计数据</returns>
    Task<AnnualStatistics> GetAnnualStatisticsAsync(Guid userId, int year, Guid? ledgerId = null);

    /// <summary>
    /// 获取同比数据
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">当前年份</param>
    /// <param name="month">当前月份</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>同比数据</returns>
    Task<YearOverYearData> GetYearOverYearDataAsync(Guid userId, int year, int month, Guid? ledgerId = null);

    /// <summary>
    /// 获取自定义时间范围统计数据（汇总 + 支出/收入分类统计 + 日粒度趋势）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（含当天）</param>
    /// <param name="endDate">结束日期（含当天）</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>自定义范围统计数据</returns>
    Task<CustomStatistics> GetCustomStatisticsAsync(Guid userId, DateTime startDate, DateTime endDate, Guid? ledgerId = null);

    /// <summary>
    /// 获取分类环比对比（支出分类：当前周期 vs 上一等长周期）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（含当天）</param>
    /// <param name="endDate">结束日期（含当天）</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>分类环比对比列表（按变化绝对值降序）</returns>
    Task<List<CategoryComparisonData>> GetCategoryComparisonAsync(Guid userId, DateTime startDate, DateTime endDate, Guid? ledgerId = null);
}

/// <summary>
/// 月度统计数据
/// </summary>
public class MonthlyStatistics
{
    /// <summary>
    /// 年份
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// 月份
    /// </summary>
    public int Month { get; set; }

    /// <summary>
    /// 总支出
    /// </summary>
    public decimal TotalExpense { get; set; }

    /// <summary>
    /// 总收入
    /// </summary>
    public decimal TotalIncome { get; set; }

    /// <summary>
    /// 净收支
    /// </summary>
    public decimal NetIncome => TotalIncome - TotalExpense;

    /// <summary>
    /// 账单数量
    /// </summary>
    public int BillCount { get; set; }
}

/// <summary>
/// 分类统计数据
/// </summary>
public class CategoryStatistics
{
    /// <summary>
    /// 分类ID
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// 分类名称
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// 分类图标
    /// </summary>
    public string CategoryIcon { get; set; } = string.Empty;

    /// <summary>
    /// 金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 占比（百分比）
    /// </summary>
    public decimal Percentage { get; set; }

    /// <summary>
    /// 账单数量
    /// </summary>
    public int Count { get; set; }
}

/// <summary>
/// 趋势数据
/// </summary>
public class TrendData
{
    /// <summary>
    /// 日期
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 支出
    /// </summary>
    public decimal Expense { get; set; }

    /// <summary>
    /// 收入
    /// </summary>
    public decimal Income { get; set; }

    /// <summary>
    /// 净收支
    /// </summary>
    public decimal NetIncome => Income - Expense;
}

/// <summary>
/// 年度统计数据
/// </summary>
public class AnnualStatistics
{
    /// <summary>
    /// 年份
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// 总支出
    /// </summary>
    public decimal TotalExpense { get; set; }

    /// <summary>
    /// 总收入
    /// </summary>
    public decimal TotalIncome { get; set; }

    /// <summary>
    /// 净收支
    /// </summary>
    public decimal NetIncome => TotalIncome - TotalExpense;

    /// <summary>
    /// 账单数量
    /// </summary>
    public int BillCount { get; set; }

    /// <summary>
    /// 月度数据
    /// </summary>
    public List<MonthlyStatistics> MonthlyData { get; set; } = new();

    /// <summary>
    /// 分类统计
    /// </summary>
    public List<CategoryStatistics> CategoryStats { get; set; } = new();
}

/// <summary>
/// 同比数据
/// </summary>
public class YearOverYearData
{
    /// <summary>
    /// 当前年份
    /// </summary>
    public int CurrentYear { get; set; }

    /// <summary>
    /// 当前月份
    /// </summary>
    public int CurrentMonth { get; set; }

    /// <summary>
    /// 当前期间支出
    /// </summary>
    public decimal CurrentExpense { get; set; }

    /// <summary>
    /// 当前期间收入
    /// </summary>
    public decimal CurrentIncome { get; set; }

    /// <summary>
    /// 去年同期支出
    /// </summary>
    public decimal PreviousYearExpense { get; set; }

    /// <summary>
    /// 去年同期收入
    /// </summary>
    public decimal PreviousYearIncome { get; set; }

    /// <summary>
    /// 支出同比变化率
    /// </summary>
    public decimal ExpenseChangeRate => PreviousYearExpense > 0
        ? (CurrentExpense - PreviousYearExpense) / PreviousYearExpense * 100
        : 0;

    /// <summary>
    /// 收入同比变化率
    /// </summary>
    public decimal IncomeChangeRate => PreviousYearIncome > 0
        ? (CurrentIncome - PreviousYearIncome) / PreviousYearIncome * 100
        : 0;
}

/// <summary>
/// 自定义时间范围统计数据
/// </summary>
public class CustomStatistics
{
    /// <summary>
    /// 开始日期（含当天）
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// 结束日期（含当天）
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// 总支出
    /// </summary>
    public decimal TotalExpense { get; set; }

    /// <summary>
    /// 总收入
    /// </summary>
    public decimal TotalIncome { get; set; }

    /// <summary>
    /// 净收支
    /// </summary>
    public decimal NetIncome => TotalIncome - TotalExpense;

    /// <summary>
    /// 账单数量
    /// </summary>
    public int BillCount { get; set; }

    /// <summary>
    /// 支出分类统计
    /// </summary>
    public List<CategoryStatistics> ExpenseCategoryStats { get; set; } = new();

    /// <summary>
    /// 收入分类统计
    /// </summary>
    public List<CategoryStatistics> IncomeCategoryStats { get; set; } = new();

    /// <summary>
    /// 日粒度趋势数据
    /// </summary>
    public List<TrendData> DailyTrend { get; set; } = new();
}

/// <summary>
/// 分类环比对比数据（当前周期 vs 上一等长周期）
/// </summary>
public class CategoryComparisonData
{
    /// <summary>
    /// 分类ID
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// 分类名称
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// 分类图标
    /// </summary>
    public string CategoryIcon { get; set; } = string.Empty;

    /// <summary>
    /// 当前周期金额
    /// </summary>
    public decimal CurrentAmount { get; set; }

    /// <summary>
    /// 上一周期金额
    /// </summary>
    public decimal PreviousAmount { get; set; }

    /// <summary>
    /// 变化金额（正数表示增加）
    /// </summary>
    public decimal ChangeAmount => CurrentAmount - PreviousAmount;

    /// <summary>
    /// 变化率（%，正数表示增加；上期为 0 时返回 null，表示无法计算）
    /// </summary>
    public decimal? ChangeRate => PreviousAmount == 0
        ? null
        : Math.Round((CurrentAmount - PreviousAmount) / PreviousAmount * 100, 1);
}
