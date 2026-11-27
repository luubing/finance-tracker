using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 「不计入统计」的口径收口：统计 Tab、首页本月概览（走 <see cref="StatisticsService"/>）与预算已用金额（<see cref="BudgetService"/>）
/// 都经这里取要计入的账单，规则只写这一处——两处各写一份，迟早有一处漏改，首页与统计页的本月支出就对不上了。
/// 决策与范围见 docs/adr/0008-category-exclude-from-statistics.md。
/// <para>
/// 规则：账单所属分类是<b>当前用户自己</b>的自建分类，且该分类开着 <see cref="Category.ExcludeFromStatistics"/>，这笔账单就不计入。
/// </para>
/// <list type="bullet">
/// <item>在查询时按分类当前的开关判定，不是入库时的快照：改开关会让历史账单一并重新计入 / 不再计入。</item>
/// <item>只认当前用户自己的分类：这是个人偏好，别人的开关不能影响我的统计；预设分类（UserId 为 null）没有开关。</item>
/// <item>绕过分类自己的软删除过滤器：已删除的自建分类仍按它删除前的开关判定。没有这一条，"删掉分类"会让它名下残留的账单
/// （比如另一台设备删除后同步过来的）悄悄重新计入统计。</item>
/// </list>
/// <para>
/// <b>实现上必须先单独查出被排除的分类，再把结果传进账单查询</b>，不能把"分类查询"作为子查询拼进账单查询：
/// 上面第三条要对分类表 <c>IgnoreQueryFilters()</c>，而 EF Core 的 <c>IgnoreQueryFilters()</c> 是<b>整条查询</b>级的开关——
/// 拼进去，账单（以及同一条查询里别的实体）自己的软删除过滤器就跟着失效，已删除的账单会重新算进统计 / 预算。
/// 被排除的分类只是用户亲手打开了开关的那几个，单独多一次小查询的代价可以忽略。
/// 回归用例见 <c>StatisticsSoftDeleteTests</c>。
/// </para>
/// 账单列表（<see cref="BillService"/>）不走这里——被排除的账单在列表里照常显示。
/// </summary>
internal static class StatisticsScope
{
    /// <summary>被排除的分类（统计页提示用：名称、图标、类型）</summary>
    public sealed record ExcludedCategory(Guid Id, string Name, string Icon, BillType Type);

    /// <summary>
    /// 当前用户开着「不计入统计」的分类 Id（含已软删除的自建分类）。
    /// 单独执行的查询，只涉及分类表，所以这里的 <c>IgnoreQueryFilters()</c> 不会波及账单的过滤器；用 <see cref="List{T}"/> 返回，
    /// 让 EF 把后面的 <c>Contains</c> 稳定地翻成 IN。
    /// </summary>
    public static Task<List<Guid>> ExcludedCategoryIdsAsync(IApplicationDbContext context, Guid userId) =>
        context.Categories
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.ExcludeFromStatistics)
            .Select(c => c.Id)
            .ToListAsync();

    /// <summary>
    /// 当前用户开着「不计入统计」的分类（含已软删除的自建分类）及其展示信息。同样是只涉及分类表的单独查询。
    /// </summary>
    public static Task<List<ExcludedCategory>> ExcludedCategoriesAsync(IApplicationDbContext context, Guid userId) =>
        context.Categories
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.ExcludeFromStatistics)
            .Select(c => new ExcludedCategory(c.Id, c.Name, c.Icon, c.Type))
            .ToListAsync();

    /// <summary>
    /// 去掉属于被排除分类的账单，剩下的就是要计入统计 / 预算的账单。
    /// </summary>
    /// <param name="bills">账单查询（保持它自己的软删除过滤器）</param>
    /// <param name="excludedCategoryIds"><see cref="ExcludedCategoryIdsAsync"/> 的结果；为空时原样返回</param>
    public static IQueryable<Bill> WithoutExcluded(this IQueryable<Bill> bills, List<Guid> excludedCategoryIds) =>
        excludedCategoryIds.Count == 0
            ? bills
            : bills.Where(b => !excludedCategoryIds.Contains(b.CategoryId));

    /// <summary>
    /// 这个分类对当前用户来说是不是「不计入统计」（只涉及分类表的单独查询）。
    /// </summary>
    public static Task<bool> IsExcludedAsync(IApplicationDbContext context, Guid userId, Guid categoryId) =>
        context.Categories
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == categoryId && c.UserId == userId && c.ExcludeFromStatistics);
}
