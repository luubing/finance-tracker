using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 「不计入统计」（<c>Category.ExcludeFromStatistics</c>）在界面上的文案与展示规则（纯函数，单元测试覆盖）：
/// 分类表单里的开关、分类列表与账单行上的标记、统计页底部的「已忽略」提示。
/// 业务规则（哪些账单不计入、影响哪些统计 / 预算）在 Core 的 <c>StatisticsScope</c>，见 docs/adr/0008-category-exclude-from-statistics.md；
/// 这里只管「怎么说」。界面上统一叫「不计入统计」，不要换成「忽略」「屏蔽」「隐藏」——账单本身仍然显示在账单列表里，只是不参与统计。
/// </summary>
public static class ExcludedFromStatisticsPresentation
{
    /// <summary>开关标题、分类 / 账单行标记、统计页提示里共用的叫法</summary>
    public const string Label = "不计入统计";

    /// <summary>统计页底部的提示里最多点名几个分类，其余折成「等 N 个分类」</summary>
    public const int MaxNamedCategories = 3;

    /// <summary>
    /// 分类表单里开关下面的说明：范围（统计 / 首页本月概览 / 预算，账单列表不受影响）加一个贴近收支方向的例子
    /// </summary>
    public static string SwitchFooter(BillType type) => type == BillType.Income
        ? $"开启后，这个分类下的账单{Scope}。适合「借入」「退款」这类不算收入的账单。"
        : $"开启后，这个分类下的账单{Scope}。适合「存款」「还信用卡」这类不算花销的账单。";

    private const string Scope = "不计入统计、首页本月概览和预算（账单列表仍会显示）";

    /// <summary>
    /// 预算表单里的说明：正在编辑的分类预算，它的分类后来开了「不计入统计」——账单不计入预算，这笔预算的已用金额不会再增加
    /// （新建分类预算时选不到这样的分类，只有编辑已有预算才会遇到）
    /// </summary>
    public const string BudgetNote = $"这个分类设了「{Label}」，它的账单不会累计到这笔预算里";

    /// <summary>
    /// 账单是否带「不计入统计」的标记：只有<b>查看者自己</b>的自定义分类上开着开关才算。
    /// 与统计口径一致（<c>StatisticsScope</c>）：统计只算当前用户自己记的账单，分类的开关只对分类的所有者生效——
    /// 预设分类（没有所有者）上的开关没有意义；共享账本里其他成员用他们自己的分类记的账单，对查看者来说不存在「被排除」。
    /// </summary>
    /// <param name="category">账单的分类（没带出来为空）</param>
    /// <param name="viewerId">当前登录用户；不知道是谁（为空）时一律不标记，宁可少标也不标错</param>
    public static bool IsExcluded(Category? category, Guid? viewerId) =>
        category is { ExcludeFromStatistics: true, UserId: { } owner } && viewerId is { } viewer && owner == viewer;

    /// <summary>
    /// 统计页底部的提示：「已忽略 3 笔「不计入统计」的账单：存款 -¥1,200.00」。
    /// 只有一个分类时不重复它的笔数；有几个分类时每个分类后面带上自己的笔数；点名的分类超过
    /// <see cref="MaxNamedCategories"/> 个时只列前几个（服务按金额从大到小给出）并补「等 N 个分类」。
    /// 金额按收支方向带符号（支出 -、收入 +），与账单行一致；没有被排除的账单返回 null（不显示这一行）。
    /// </summary>
    public static string? NoteText(IReadOnlyList<ExcludedCategoryStatistics> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var bills = rows.Sum(row => row.Count);
        var named = string.Join('、', rows.Take(MaxNamedCategories).Select(row => NotePart(row, withCount: rows.Count > 1)));
        var more = rows.Count > MaxNamedCategories ? $"等 {rows.Count} 个分类" : string.Empty;

        return $"已忽略 {bills} 笔「{Label}」的账单：{named}{more}";
    }

    private static string NotePart(ExcludedCategoryStatistics row, bool withCount)
    {
        var amount = MoneyPresentation.Signed(row.Amount, row.Type == BillType.Income);
        return withCount ? $"{row.CategoryName} {amount}（{row.Count} 笔）" : $"{row.CategoryName} {amount}";
    }
}
