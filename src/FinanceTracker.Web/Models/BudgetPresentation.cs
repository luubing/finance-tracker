using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 预算执行的三种状态（与现有预警口径一致：使用率 ≥ 80% 预警、≥ 100% 超支）
/// </summary>
public enum BudgetState
{
    /// <summary>正常（使用率 &lt; 80%）</summary>
    Normal,

    /// <summary>接近上限（使用率 ≥ 80% 且 &lt; 100%）</summary>
    Warning,

    /// <summary>超支（使用率 ≥ 100%）</summary>
    Over,
}

/// <summary>
/// 预算行渲染所需的全部文字与配色
/// </summary>
/// <param name="BudgetId">预算 Id</param>
/// <param name="Name">名称：「总预算」或分类名</param>
/// <param name="Tag">名称后面的灰色小字：全部账本的总预算标「全部账本」，账本级预算标账本名，分类预算没有</param>
/// <param name="Icon">图标（已校验）</param>
/// <param name="Tone">图标块配色</param>
/// <param name="State">状态</param>
/// <param name="ShownPercent">显示的使用百分比（向下取整，所以「≥ 80%」与「预警」严格对应）</param>
/// <param name="BarPercent">进度条宽度百分比（0–100，超支时撑满）</param>
/// <param name="RemainText">右上角文字：剩余 / 仅剩 / 超支 + 金额</param>
/// <param name="AriaValueText">进度条的读屏文案：已用百分比 + 状态</param>
public sealed record BudgetRowModel(
    Guid BudgetId,
    string Name,
    string? Tag,
    string Icon,
    string Tone,
    BudgetState State,
    int ShownPercent,
    decimal BarPercent,
    string RemainText,
    string AriaValueText);

/// <summary>
/// 预算展示规则（纯函数，单元测试覆盖）：状态判定、文字说明（颜色之外的文字线索）、行模型。
/// </summary>
public static class BudgetPresentation
{
    /// <summary>预警阈值（使用率达到该百分比即「接近上限」）</summary>
    public const decimal WarningPercent = 80m;

    /// <summary>超支阈值（使用率达到该百分比即「超支」）</summary>
    public const decimal OverPercent = 100m;

    /// <summary>总预算的图标</summary>
    public const string TotalIcon = "mdi-wallet";

    /// <summary>
    /// 使用率 → 状态
    /// </summary>
    public static BudgetState StateOf(decimal usagePercentage) => usagePercentage switch
    {
        >= OverPercent => BudgetState.Over,
        >= WarningPercent => BudgetState.Warning,
        _ => BudgetState.Normal,
    };

    /// <summary>
    /// 显示的使用百分比：向下取整。79.6% 显示 79%（还是「正常」），99.6% 显示 99%（还没「超支」），
    /// 避免显示值与状态对不上。
    /// </summary>
    public static int ShownPercent(decimal usagePercentage) =>
        (int)Math.Floor(Math.Max(0m, Math.Min(usagePercentage, 100_000m)));

    /// <summary>
    /// 进度条宽度：使用率限制在 0–100，保留一位小数
    /// </summary>
    public static decimal BarPercent(decimal usagePercentage) =>
        Math.Round(Math.Clamp(usagePercentage, 0m, 100m), 1, MidpointRounding.ToZero);

    /// <summary>
    /// 右上角文字：正常「剩余 ¥…」、接近上限「仅剩 ¥…」、超支「超支 ¥…」、刚好用完「已用完」。
    /// 状态除了颜色还有文字线索（剩余 / 仅剩 / 超支 / 已用完）。
    /// </summary>
    public static string RemainText(BudgetState state, decimal remainingAmount)
    {
        if (remainingAmount < 0)
        {
            return $"超支 {MoneyPresentation.Format(-remainingAmount)}";
        }

        if (remainingAmount == 0)
        {
            return "已用完";
        }

        var verb = state == BudgetState.Warning ? "仅剩" : "剩余";
        return $"{verb} {MoneyPresentation.Format(remainingAmount)}";
    }

    /// <summary>
    /// 进度条的读屏文案，例如「已用 87%，接近上限」
    /// </summary>
    public static string AriaValueText(BudgetState state, int shownPercent) => state switch
    {
        BudgetState.Over => $"已用 {shownPercent}%，已超支",
        BudgetState.Warning => $"已用 {shownPercent}%，接近上限",
        _ => $"已用 {shownPercent}%",
    };

    /// <summary>
    /// 名称后面的灰色小字：全部账本的总预算「全部账本」；账本级预算显示账本名；分类预算不显示
    /// </summary>
    public static string? TagOf(BudgetStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.LedgerName))
        {
            return status.LedgerName;
        }

        return status.CategoryId is null ? "全部账本" : null;
    }

    /// <summary>
    /// 把预算执行情况转成预算行
    /// </summary>
    public static BudgetRowModel ToRow(BudgetStatus status)
    {
        var isTotal = status.CategoryId is null;
        var state = StateOf(status.UsagePercentage);
        var shown = ShownPercent(status.UsagePercentage);

        return new BudgetRowModel(
            status.BudgetId,
            string.IsNullOrWhiteSpace(status.CategoryName) ? "总预算" : status.CategoryName,
            TagOf(status),
            isTotal ? TotalIcon : BillPresentation.CategoryIcon(status.CategoryIcon),
            isTotal ? "purple" : BillPresentation.CategoryTone(status.CategoryName, status.CategoryId),
            state,
            shown,
            BarPercent(status.UsagePercentage),
            RemainText(state, status.RemainingAmount),
            AriaValueText(state, shown));
    }

    /// <summary>
    /// 状态对应的进度条配色
    /// </summary>
    public static string StateKey(BudgetState state) => state switch
    {
        BudgetState.Over => "over",
        BudgetState.Warning => "warning",
        _ => "normal",
    };
}
