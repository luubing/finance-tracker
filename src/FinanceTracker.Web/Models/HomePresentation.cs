using System.Globalization;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 首页的引导状态：新用户 / 没账本 / 没账单时告诉用户下一步做什么
/// </summary>
public enum HomeGuide
{
    /// <summary>不需要引导</summary>
    None,

    /// <summary>没有账本、也没有任何账单：整块空状态，引导先新建账本</summary>
    CreateLedger,

    /// <summary>没有账本，但有历史账单（历史无账本的账单）：只在顶部放一行提示，不挡住已有数据</summary>
    CreateLedgerHint,

    /// <summary>有账本、还没有账单：最近账单区域引导记第一笔</summary>
    FirstBill,
}

/// <summary>
/// 首页的展示规则（纯函数，单元测试覆盖）：日期行、待确认横幅与角标文案、预算重点项挑选、引导状态。
/// </summary>
public static class HomePresentation
{
    /// <summary>首页「最近账单」展示的笔数</summary>
    public const int RecentBillCount = 5;

    /// <summary>首页「本月预算」最多展示的条数（其余点「管理」查看）</summary>
    public const int BudgetHighlightCount = 3;

    /// <summary>横幅副标题里最多列出的渠道数</summary>
    public const int BannerChannelLimit = 3;

    private static readonly string[] Weekdays = ["日", "一", "二", "三", "四", "五", "六"];

    /// <summary>
    /// Hero 的日期行：「11月25日 周三」（参数是应用时区的墙上时间）
    /// </summary>
    public static string DateLine(DateTime nowLocal) =>
        $"{nowLocal.Month}月{nowLocal.Day}日 周{Weekdays[(int)nowLocal.DayOfWeek]}";

    /// <summary>
    /// 通知入口的角标文字：超过 99 显示 99+
    /// </summary>
    public static string BellBadge(int pendingCount) =>
        pendingCount > 99 ? "99+" : pendingCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 通知入口的读屏名称（角标是图形，读屏要读出笔数）
    /// </summary>
    public static string BellLabel(int pendingCount) => pendingCount > 0 ? $"待确认账单，{pendingCount} 笔" : "待确认账单";

    /// <summary>
    /// 待确认横幅的标题：「3 笔支付等待确认」
    /// </summary>
    public static string BannerTitle(int pendingCount) => $"{pendingCount} 笔支付等待确认";

    /// <summary>
    /// 待确认横幅的副标题：列出最近几笔的来源渠道（「微信支付 · 支付宝 · 银行短信」），多于 3 种时加「等」。
    /// 渠道为空的记录用来源（短信 / 通知栏）代替；都没有信息时给一句通用提示。
    /// </summary>
    /// <param name="pending">待确认账单（按捕获时间倒序，最近的在前）</param>
    public static string BannerSubtitle(IReadOnlyList<PendingBill> pending)
    {
        var origins = pending
            .Select(p => string.IsNullOrWhiteSpace(p.Channel) ? SourceLabel(p.Source) : p.Channel.Trim())
            .Where(o => !string.IsNullOrEmpty(o))
            .Distinct()
            .ToList();

        if (origins.Count == 0)
        {
            return "点按查看并确认入账";
        }

        var shown = string.Join(" · ", origins.Take(BannerChannelLimit));
        return origins.Count > BannerChannelLimit ? $"{shown} 等" : shown;
    }

    /// <summary>
    /// 数据来源的文字（待确认账单只会来自短信识别或通知栏识别）
    /// </summary>
    public static string SourceLabel(BillSource source) => source switch
    {
        BillSource.SmsRecognition => "短信识别",
        BillSource.Notification => "通知栏",
        _ => string.Empty,
    };

    /// <summary>
    /// 预算区域展示哪几项：总预算在前（按传入顺序），然后是使用率最高的分类预算（「重点」= 最需要关注的），
    /// 一共最多 <paramref name="max"/> 项。
    /// </summary>
    public static IReadOnlyList<BudgetStatus> PickBudgetHighlights(IReadOnlyList<BudgetStatus> statuses, int max = BudgetHighlightCount)
    {
        var totals = statuses.Where(s => s.CategoryId is null);
        var categories = statuses
            .Where(s => s.CategoryId is not null)
            .OrderByDescending(s => s.UsagePercentage)
            .ThenBy(s => s.CategoryName, StringComparer.Ordinal);

        return totals.Concat(categories).Take(Math.Max(0, max)).ToList();
    }

    /// <summary>
    /// 预算区域下方的提示：预算多于展示条数时说明还有更多
    /// </summary>
    public static string? BudgetFooter(int totalCount, int shownCount) =>
        totalCount > shownCount ? $"共 {totalCount} 项预算，点「管理」查看全部" : null;

    /// <summary>
    /// 引导状态：没有账本 → 引导新建账本（有历史账单时降级为一行提示）；有账本没账单 → 引导记第一笔
    /// </summary>
    /// <param name="ledgerCount">用户自己的账本数</param>
    /// <param name="hasBills">是否有任何账单</param>
    public static HomeGuide ResolveGuide(int ledgerCount, bool hasBills)
    {
        if (ledgerCount <= 0)
        {
            return hasBills ? HomeGuide.CreateLedgerHint : HomeGuide.CreateLedger;
        }

        return hasBills ? HomeGuide.None : HomeGuide.FirstBill;
    }
}
