using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Models;

/// <summary>
/// 账单查询条件。账单列表、账单总数与账单汇总共用这一份条件：
/// <see cref="Interfaces.IBillService"/> 的三个读取方法内部走同一个查询构造，
/// 「汇总口径＝列表口径」由结构保证，而不是靠两处各写一遍再人工对齐。
/// <para>
/// 所有条件可选，条件之间取交集；全部为空 = 当前用户可见的全部账单。
/// 要增加新的条件只需在这里加一个属性，并在 BillService 的查询构造里加一个分支——
/// 列表、总数与汇总会同时生效（<see cref="Keyword"/> 就是这样加进来的）。
/// </para>
/// </summary>
public sealed record BillFilter
{
    /// <summary>开始时刻（应用时区的墙上时间，含端点）</summary>
    public DateTime? StartDate { get; init; }

    /// <summary>
    /// 结束时刻（应用时区的墙上时间，<b>含端点</b>）：要覆盖整天请传当天最后一个 tick
    /// （<see cref="Time.TimeBoundaries.EndOfDay"/>），不要传"减 1 秒"
    /// </summary>
    public DateTime? EndDate { get; init; }

    /// <summary>
    /// 分类（可多选）：账单属于其中<b>任意一个</b>分类即符合（同一个维度内取并集，与其他条件仍取交集）；
    /// null / 空 = 不限分类。重复的 Id 等价于一个。
    /// </summary>
    public IReadOnlyList<Guid>? CategoryIds { get; init; }

    /// <summary>支付渠道</summary>
    public Guid? PaymentChannelId { get; init; }

    /// <summary>账本；筛选的是当前用户的共享账本时，账本内全部成员的账单都可见（只读展示）</summary>
    public Guid? LedgerId { get; init; }

    /// <summary>
    /// 只看「未归属账本」的账单：账本必填之前创建、<see cref="Entities.Bill.LedgerId"/> 为空的历史账单（只可能是当前用户自己的，
    /// 共享账本里的账单都有账本）。用来找出它们再批量「归属账本」。
    /// 与 <see cref="LedgerId"/> 同时给出时按「取交集」处理——两个条件互斥，结果必然为空；界面上二者是同一个「账本」维度的不同取值，不会同时设置。
    /// </summary>
    public bool UnassignedLedger { get; init; }

    /// <summary>账单类型（支出 / 收入）</summary>
    public BillType? Type { get; init; }

    /// <summary>
    /// 搜索关键字（账单 Tab 的搜索框）：匹配<b>备注</b>或<b>分类名称</b>（不区分大小写的包含匹配）；
    /// 关键字能解析为数字时，同时匹配<b>金额等于该数值</b>的账单（数值相等，12.5 与 12.50 相等、不匹配 112.5；解析与区域设置无关，见 <see cref="BillKeyword"/>）。
    /// 首尾空白忽略；为 null / 空 / 全空白时没有这个条件，行为与不带关键字完全一致。
    /// 目前只在应用内部使用，不暴露到 REST 接口。
    /// </summary>
    public string? Keyword { get; init; }

    /// <summary>是否没有任何条件</summary>
    public bool IsEmpty =>
        StartDate is null
        && EndDate is null
        && (CategoryIds is null || CategoryIds.Count == 0)
        && PaymentChannelId is null
        && LedgerId is null
        && !UnassignedLedger
        && Type is null
        && BillKeyword.Parse(Keyword) is null;
}

/// <summary>
/// 账单汇总：给定一份 <see cref="BillFilter"/>，符合条件的<b>全部</b>账单（不受分页影响）的支出合计、收入合计与笔数。
/// 条件里限定了账单类型时，另一类的合计自然是 0。
/// </summary>
/// <param name="TotalExpense">支出合计</param>
/// <param name="TotalIncome">收入合计</param>
/// <param name="Count">账单笔数</param>
public sealed record BillSummary(decimal TotalExpense, decimal TotalIncome, int Count)
{
    /// <summary>没有账单</summary>
    public static BillSummary Empty { get; } = new(0m, 0m, 0);
}
