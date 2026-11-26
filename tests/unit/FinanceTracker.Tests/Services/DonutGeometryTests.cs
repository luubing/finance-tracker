using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 构成圆环的几何：占比、弧长、起点、段间缺口，以及零 / 负金额、单段、极小切片这些边界
/// </summary>
public class DonutGeometryTests
{
    private const int Precision = 6;

    private static IReadOnlyList<DonutArc> Arcs(params decimal[] amounts) => DonutGeometry.Arcs(amounts);

    [Fact]
    public void 没有金额或全是零_返回空列表_由页面画灰色空环()
    {
        Assert.Empty(Arcs());
        Assert.Empty(Arcs(0m, 0m));
        Assert.Empty(Arcs(-5m, 0m));
    }

    [Fact]
    public void 只有一段正金额时画整圈_不留缺口()
    {
        var arc = Assert.Single(Arcs(0m, 120m, 0m));

        Assert.Equal(1, arc.Index);
        Assert.Equal(1d, arc.Fraction, Precision);
        Assert.Equal(DonutGeometry.Circumference, arc.Length, Precision);
        Assert.Equal(0d, arc.Offset, Precision);
    }

    [Fact]
    public void 占比是金额除以正金额总和_所有段之和为一()
    {
        var arcs = Arcs(580m, 120m, 300m);

        Assert.Equal(new[] { 0.58, 0.12, 0.30 }, arcs.Select(a => a.Fraction).ToArray(), new FractionComparer());
        Assert.Equal(1d, arcs.Sum(a => a.Fraction), Precision);
    }

    [Fact]
    public void 两段等额_各占半圈减去缺口_起点让出缺口的一半()
    {
        var arcs = Arcs(50m, 50m);
        var half = DonutGeometry.Circumference / 2d;

        Assert.Equal(2, arcs.Count);
        Assert.Equal(half - DonutGeometry.Gap, arcs[0].Length, Precision);
        Assert.Equal(half - DonutGeometry.Gap, arcs[1].Length, Precision);
        Assert.Equal(DonutGeometry.Gap / 2d, arcs[0].Offset, Precision);
        Assert.Equal(half + DonutGeometry.Gap / 2d, arcs[1].Offset, Precision);
    }

    [Fact]
    public void 相邻两段之间正好隔一个缺口_首尾相接处也是()
    {
        var arcs = Arcs(580m, 120m, 300m, 40m);

        for (var i = 0; i < arcs.Count - 1; i++)
        {
            var end = arcs[i].Offset + arcs[i].Length;
            Assert.Equal(DonutGeometry.Gap, arcs[i + 1].Offset - end, Precision);
        }

        var last = arcs[^1];
        var wrapGap = arcs[0].Offset + DonutGeometry.Circumference - (last.Offset + last.Length);
        Assert.Equal(DonutGeometry.Gap, wrapGap, Precision);
    }

    [Fact]
    public void 弧长之和加上所有缺口等于周长()
    {
        var arcs = Arcs(580m, 120m, 300m, 40m);

        Assert.Equal(DonutGeometry.Circumference, arcs.Sum(a => a.Length) + arcs.Count * DonutGeometry.Gap, Precision);
    }

    [Fact]
    public void 零与负金额的项不画_序号仍按输入列表计_配色不会错位()
    {
        var arcs = Arcs(30m, 0m, 70m, -4m);

        Assert.Equal(new[] { 0, 2 }, arcs.Select(a => a.Index));
        Assert.Equal(new[] { 0.3, 0.7 }, arcs.Select(a => a.Fraction).ToArray(), new FractionComparer());
    }

    [Fact]
    public void 占比极小的切片也至少剩一半弧长_不会被缺口吃掉()
    {
        // 0.1 / 1000.1 约 0.01%：弧长远小于缺口，缺口被限制为该段弧长的一半
        var arcs = Arcs(1000m, 0.1m);
        var tiny = arcs[1];
        var span = tiny.Fraction * DonutGeometry.Circumference;

        Assert.True(tiny.Length > 0d);
        Assert.Equal(span / 2d, tiny.Length, Precision);
        Assert.True(arcs[0].Length > 0d);
    }

    [Fact]
    public void 半径与缺口可调_几何不变量不依赖具体数值()
    {
        const double radius = 10d;
        const double gap = 1d;
        var circumference = 2d * Math.PI * radius;

        var arcs = DonutGeometry.Arcs([1m, 1m, 2m], radius, gap);

        Assert.Equal(circumference, arcs.Sum(a => a.Length) + arcs.Count * gap, Precision);
        Assert.Equal(gap / 2d, arcs[0].Offset, Precision);
        Assert.Equal(circumference / 4d - gap, arcs[0].Length, Precision);
        Assert.Equal(circumference / 2d - gap, arcs[2].Length, Precision);
    }

    [Fact]
    public void 零缺口时各段首尾严丝合缝()
    {
        var circumference = 2d * Math.PI * 50d;

        var arcs = DonutGeometry.Arcs([25m, 25m, 50m], 50d, 0d);

        Assert.Equal(0d, arcs[0].Offset, Precision);
        Assert.Equal(arcs[0].Offset + arcs[0].Length, arcs[1].Offset, Precision);
        Assert.Equal(arcs[1].Offset + arcs[1].Length, arcs[2].Offset, Precision);
        Assert.Equal(circumference, arcs[2].Offset + arcs[2].Length, Precision);
    }

    [Fact]
    public void 传入空引用抛出参数异常()
    {
        Assert.Throws<ArgumentNullException>(() => DonutGeometry.Arcs(null!));
    }

    [Fact]
    public void 视口与圆环尺寸与设计稿一致_孔径约为外径的七成()
    {
        var outer = DonutGeometry.Radius + DonutGeometry.StrokeWidth / 2d;
        var inner = DonutGeometry.Radius - DonutGeometry.StrokeWidth / 2d;

        Assert.Equal(200d, DonutGeometry.ViewBox);
        Assert.True(outer <= DonutGeometry.ViewBox / 2d, "圆环外缘不能超出视口");
        Assert.InRange(inner / outer, 0.65, 0.75);
    }

    /// <summary>按 1e-6 比较两个占比序列（decimal 除法转成 double 后的误差）</summary>
    private sealed class FractionComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-6;

        public int GetHashCode(double obj) => 0;
    }
}
