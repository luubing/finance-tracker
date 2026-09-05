using FinanceTracker.Core.Entities;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 预算服务接口
/// </summary>
public interface IBudgetService
{
    /// <summary>
    /// 获取预算列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="ledgerId">账本ID（可选，null 表示不按账本筛选）</param>
    /// <returns>预算列表（总预算在前，分类预算在后）</returns>
    Task<List<Budget>> GetBudgetsAsync(Guid userId, int year, int month, Guid? ledgerId = null);

    /// <summary>
    /// 根据ID获取预算
    /// </summary>
    /// <param name="budgetId">预算ID</param>
    /// <returns>预算信息</returns>
    Task<Budget?> GetBudgetByIdAsync(Guid budgetId);

    /// <summary>
    /// 创建预算（同一用户同一月份同一账本范围下，同一分类的预算不可重复）
    /// </summary>
    /// <param name="budget">预算信息</param>
    /// <returns>创建的预算</returns>
    Task<Budget> CreateBudgetAsync(Budget budget);

    /// <summary>
    /// 更新预算
    /// </summary>
    /// <param name="budget">预算信息</param>
    /// <returns>更新的预算</returns>
    Task<Budget> UpdateBudgetAsync(Budget budget);

    /// <summary>
    /// 删除预算（软删除）
    /// </summary>
    /// <param name="budgetId">预算ID</param>
    /// <param name="userId">用户ID</param>
    /// <returns>是否成功</returns>
    Task<bool> DeleteBudgetAsync(Guid budgetId, Guid userId);

    /// <summary>
    /// 获取预算执行情况（预算额、已用额、剩余额、使用百分比）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="ledgerId">账本ID（null 表示全部账本）</param>
    /// <returns>预算执行情况列表</returns>
    Task<List<BudgetStatus>> GetBudgetStatusAsync(Guid userId, int year, int month, Guid? ledgerId = null);

    /// <summary>
    /// 获取预算预警（单笔账单保存场景：返回与该账单相关的预算中使用率最高的一条预警状态）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="year">年份</param>
    /// <param name="month">月份</param>
    /// <param name="ledgerId">账单归属的账本ID（可空）</param>
    /// <param name="categoryId">账单的分类ID</param>
    /// <returns>无相关预算或未达到预警阈值（80%）时返回 null</returns>
    Task<BudgetAlert?> GetBudgetAlertAsync(Guid userId, int year, int month, Guid? ledgerId, Guid? categoryId);
}

/// <summary>
/// 预算执行情况
/// </summary>
public class BudgetStatus
{
    /// <summary>
    /// 预算ID
    /// </summary>
    public Guid BudgetId { get; set; }

    /// <summary>
    /// 预算金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 已使用金额
    /// </summary>
    public decimal UsedAmount { get; set; }

    /// <summary>
    /// 剩余金额（负数表示已超支）
    /// </summary>
    public decimal RemainingAmount => Amount - UsedAmount;

    /// <summary>
    /// 使用百分比（可超过 100）
    /// </summary>
    public decimal UsagePercentage => Amount > 0 ? UsedAmount / Amount * 100 : 0;

    /// <summary>
    /// 分类ID（null 表示总预算）
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// 分类名称（总预算为"总预算"）
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// 分类图标
    /// </summary>
    public string CategoryIcon { get; set; } = string.Empty;

    /// <summary>
    /// 账本ID（null 表示全部账本）
    /// </summary>
    public Guid? LedgerId { get; set; }

    /// <summary>
    /// 账本名称（全部账本时为 null）
    /// </summary>
    public string? LedgerName { get; set; }
}

/// <summary>
/// 预算预警（单笔账单保存场景下，与该账单相关的最严重预算预警状态）
/// </summary>
public class BudgetAlert
{
    /// <summary>
    /// 预算ID
    /// </summary>
    public Guid BudgetId { get; set; }

    /// <summary>
    /// 是否已超支（使用率 ≥ 100%）
    /// </summary>
    public bool IsExceeded { get; set; }

    /// <summary>
    /// 是否接近上限预警（80% ≤ 使用率 < 100%）
    /// </summary>
    public bool IsWarning { get; set; }

    /// <summary>
    /// 预算金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 已使用金额
    /// </summary>
    public decimal UsedAmount { get; set; }

    /// <summary>
    /// 剩余金额（负数表示已超支）
    /// </summary>
    public decimal RemainingAmount => Amount - UsedAmount;

    /// <summary>
    /// 使用百分比
    /// </summary>
    public decimal UsagePercentage { get; set; }

    /// <summary>
    /// 预算名称（总预算/分类名）
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;
}