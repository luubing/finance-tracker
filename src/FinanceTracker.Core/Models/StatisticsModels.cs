namespace FinanceTracker.Core.Models;

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
/// 账本统计数据（某月按账本汇总的金额；"全部账本"视图下用它代替分类统计）
/// </summary>
public class LedgerStatistics
{
    /// <summary>
    /// "未归属账本"的显示名称：账本必填之前创建、尚未补上账本的历史账单归入此项
    /// </summary>
    public const string UnassignedLedgerName = "未归属账本";

    /// <summary>
    /// 账本ID；null 表示历史上未归属任何账本的账单
    /// </summary>
    public Guid? LedgerId { get; set; }

    /// <summary>
    /// 账本名称
    /// </summary>
    public string LedgerName { get; set; } = string.Empty;

    /// <summary>
    /// 账本图标
    /// </summary>
    public string LedgerIcon { get; set; } = string.Empty;

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
    /// 分类统计（支出与收入的分类放在一起汇总，占比是占两者合计的比例）。
    /// REST 接口（<c>GET /api/statistics/annual</c>）返回的就是这个口径，保持兼容；
    /// 统计页的年度视图改为用 <see cref="ExpenseCategoryStats"/> / <see cref="IncomeCategoryStats"/> 分别展示支出构成与收入构成
    /// </summary>
    public List<CategoryStatistics> CategoryStats { get; set; } = new();

    /// <summary>
    /// 支出分类统计：只含支出账单，占比是占全年总支出的比例，按金额降序
    /// </summary>
    public List<CategoryStatistics> ExpenseCategoryStats { get; set; } = new();

    /// <summary>
    /// 收入分类统计：只含收入账单，占比是占全年总收入的比例，按金额降序
    /// </summary>
    public List<CategoryStatistics> IncomeCategoryStats { get; set; } = new();
}

/// <summary>
/// 同比数据：当前期间与去年同期的对比。
/// 期间可以是某个月（<see cref="CurrentMonth"/> 有值），也可以是整年或"年初至今"（<see cref="CurrentMonth"/> 为 null）。
/// </summary>
public class YearOverYearData
{
    /// <summary>
    /// 当前年份
    /// </summary>
    public int CurrentYear { get; set; }

    /// <summary>
    /// 当前月份；按整年（或年初至今）对比时为 null
    /// </summary>
    public int? CurrentMonth { get; set; }

    /// <summary>
    /// 当前期间的起始日（应用时区的自然日）
    /// </summary>
    public DateTime CurrentPeriodStart { get; set; }

    /// <summary>
    /// 当前期间的结束日（含当天）。当年的整年同比截止到今天，已结束的年份为 12 月 31 日
    /// </summary>
    public DateTime CurrentPeriodEnd { get; set; }

    /// <summary>
    /// 去年同期的起始日
    /// </summary>
    public DateTime PreviousPeriodStart { get; set; }

    /// <summary>
    /// 去年同期的结束日（含当天）。当年的整年同比为去年的同月同日（闰日 2 月 29 日回退到 2 月 28 日）
    /// </summary>
    public DateTime PreviousPeriodEnd { get; set; }

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
    /// 支出同比变化率（%）。去年同期支出为 0 时没有对比基数，返回 null——
    /// 界面应显示"—"，而不是一个会被误读成"没有变化"的 0%。
    /// </summary>
    public decimal? ExpenseChangeRate => PreviousYearExpense > 0
        ? (CurrentExpense - PreviousYearExpense) / PreviousYearExpense * 100
        : null;

    /// <summary>
    /// 收入同比变化率（%）。去年同期收入为 0 时没有对比基数，返回 null
    /// </summary>
    public decimal? IncomeChangeRate => PreviousYearIncome > 0
        ? (CurrentIncome - PreviousYearIncome) / PreviousYearIncome * 100
        : null;
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
