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
    /// <returns>月度统计数据</returns>
    Task<MonthlyStatistics> GetMonthlyStatisticsAsync(Guid userId, int year, int month);

    /// <summary>
    /// 获取分类统计数据
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="type">账单类型</param>
    /// <returns>分类统计数据</returns>
    Task<List<CategoryStatistics>> GetCategoryStatisticsAsync(Guid userId, int year, int month, BillType type);

    /// <summary>
    /// 获取趋势数据
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="dimension">维度（day/week/month）</param>
    /// <returns>趋势数据</returns>
    Task<List<TrendData>> GetTrendDataAsync(Guid userId, DateTime startDate, DateTime endDate, string dimension);
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
