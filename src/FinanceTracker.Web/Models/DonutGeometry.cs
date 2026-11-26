namespace FinanceTracker.Web.Models;

/// <summary>
/// 圆环图中的一段弧（SVG 圆的描边虚线表示法：<c>stroke-dasharray</c> 画弧、<c>stroke-dashoffset</c> 定起点）。
/// </summary>
/// <param name="Index">这一段对应输入金额列表里的序号（图例配色、读屏文案按它对应）</param>
/// <param name="Fraction">占整圈的比例（0–1，所有段之和为 1）</param>
/// <param name="Length">要画出来的弧长（用户单位）：占比对应的弧长减去切片之间的缺口</param>
/// <param name="Offset">弧的起点到 12 点钟方向的弧长（顺时针；已把缺口的一半让给了起点）</param>
public readonly record struct DonutArc(int Index, double Fraction, double Length, double Offset);

/// <summary>
/// 圆环图的几何计算（纯函数，单元测试覆盖）。
/// <para>
/// 圆环用 SVG <c>&lt;circle&gt;</c> 的描边画：每一段一个圆，用虚线只让属于自己的那一段弧显示出来，
/// 这样每一段是一个独立元素（可以单独上色 / 做动画），不需要手算椭圆弧路径。
/// 视口固定 <c>0 0 200 200</c>，圆心 (100, 100)；圆环轨道中线半径 <see cref="Radius"/>，线宽 <see cref="StrokeWidth"/>，
/// 外缘半径 = 83 + 15 = 98，内孔半径 = 83 - 15 = 68（孔径约为外径的 70%，与设计稿 screens 03-stats 一致）。
/// </para>
/// </summary>
public static class DonutGeometry
{
    /// <summary>视口边长（正方形）</summary>
    public const double ViewBox = 200d;

    /// <summary>圆环轨道中线半径</summary>
    public const double Radius = 83d;

    /// <summary>圆环线宽</summary>
    public const double StrokeWidth = 30d;

    /// <summary>切片之间的缺口（轨道中线上的弧长）</summary>
    public const double Gap = 3.5d;

    /// <summary>轨道中线的周长</summary>
    public static double Circumference { get; } = 2d * Math.PI * Radius;

    /// <summary>
    /// 按金额算出每一段弧。金额 ≤ 0 的项不画（序号仍按输入列表计，所以配色不会因为中间有零而错位）；
    /// 没有任何正金额时返回空列表（调用方画一圈灰色空环）。
    /// </summary>
    /// <param name="amounts">每一段的金额（按显示顺序，通常是金额降序）</param>
    public static IReadOnlyList<DonutArc> Arcs(IReadOnlyList<decimal> amounts) => Arcs(amounts, Radius, Gap);

    /// <summary>
    /// 同上，半径与缺口可调（单元测试用小圆验证几何不变量）。
    /// <list type="bullet">
    /// <item>只有一段正金额：画整圈，不留缺口。</item>
    /// <item>多段：每段两端各让出缺口的一半，缺口落在相邻两段的分界上；缺口不超过该段弧长的一半——
    /// 占比极小的切片（例如 0.2%）也至少剩一半弧长，仍然看得见，不会被缺口吃掉。</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<DonutArc> Arcs(IReadOnlyList<decimal> amounts, double radius, double gap)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        var total = 0m;
        var positives = 0;
        foreach (var amount in amounts)
        {
            if (amount > 0m)
            {
                total += amount;
                positives++;
            }
        }

        if (positives == 0)
        {
            return [];
        }

        var circumference = 2d * Math.PI * radius;
        var arcs = new List<DonutArc>(positives);
        var start = 0d;

        for (var i = 0; i < amounts.Count; i++)
        {
            if (amounts[i] <= 0m)
            {
                continue;
            }

            var fraction = (double)(amounts[i] / total);
            var span = fraction * circumference;
            var cut = positives == 1 ? 0d : Math.Min(gap, span * 0.5d);

            arcs.Add(new DonutArc(i, fraction, span - cut, start * circumference + cut / 2d));
            start += fraction;
        }

        return arcs;
    }
}
