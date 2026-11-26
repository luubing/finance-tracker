using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;

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
    /// 获取账本统计数据（某月当前用户的账单按账本汇总，金额降序）。
    /// 用于"全部账本"视图：此时再按分类汇总意义不大，改为看钱花在了哪些账本上。
    /// 账单没有账本（账本必填之前创建的历史账单）时归入 <see cref="LedgerStatistics.UnassignedLedgerName"/>。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="type">账单类型</param>
    /// <returns>账本统计数据</returns>
    Task<List<LedgerStatistics>> GetLedgerStatisticsAsync(Guid userId, int year, int month, BillType type);

    /// <summary>
    /// 获取趋势数据
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="dimension">时间粒度（日/周/月）</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>趋势数据</returns>
    Task<List<TrendData>> GetTrendDataAsync(Guid userId, DateTime startDate, DateTime endDate, TrendDimension dimension, Guid? ledgerId = null);

    /// <summary>
    /// 获取年度统计数据：全年及各月的收支汇总，分类统计有三份——
    /// <see cref="AnnualStatistics.CategoryStats"/>（收支混合，REST 接口沿用）、
    /// <see cref="AnnualStatistics.ExpenseCategoryStats"/>（只含支出）、<see cref="AnnualStatistics.IncomeCategoryStats"/>（只含收入），后两份的占比各自占本类合计
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>年度统计数据</returns>
    Task<AnnualStatistics> GetAnnualStatisticsAsync(Guid userId, int year, Guid? ledgerId = null);

    /// <summary>
    /// 获取月度同比数据（某月 vs 去年同月）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">当前年份</param>
    /// <param name="month">当前月份</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>同比数据；去年同月没有收入/支出时对应变化率为 null</returns>
    Task<YearOverYearData> GetYearOverYearDataAsync(Guid userId, int year, int month, Guid? ledgerId = null);

    /// <summary>
    /// 获取整年同比数据（年度报告使用）。
    /// 已结束的年份：整年 vs 上一整年；当年：年初至今（含今天）vs 去年同期（截至去年的同月同日），
    /// 避免"今年只过了 11 个月、却拿去年 12 个月比"导致的虚假下降。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">要查看的年份</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>同比数据（<see cref="YearOverYearData.CurrentMonth"/> 为 null）；去年同期没有收入/支出时对应变化率为 null</returns>
    Task<YearOverYearData> GetAnnualYearOverYearDataAsync(Guid userId, int year, Guid? ledgerId = null);

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
