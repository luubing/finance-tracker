using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 预算总览进度环的几何：弧长随进度线性变化、越界进度按边界处理、外缘正好撑满 viewBox
/// </summary>
public class RingGeometryTests
{
    private const int Precision = 6;

    [Fact]
    public void 零进度没有弧_满进度是整圈()
    {
        Assert.Equal(0d, RingGeometry.ArcLength(0m), Precision);
        Assert.Equal(RingGeometry.Circumference, RingGeometry.ArcLength(100m), Precision);
    }

    [Theory]
    [InlineData(25, 0.25)]
    [InlineData(50, 0.5)]
    [InlineData(87, 0.87)]
    public void 弧长随进度线性变化(int percent, double fraction) =>
        Assert.Equal(RingGeometry.Circumference * fraction, RingGeometry.ArcLength(percent), Precision);

    [Theory]
    [InlineData(-10)]
    [InlineData(-0.01)]
    public void 负进度按零处理(double percent) =>
        Assert.Equal(0d, RingGeometry.ArcLength((decimal)percent), Precision);

    [Theory]
    [InlineData(100.01)]
    [InlineData(169)]
    [InlineData(999)]
    public void 超过百分之百按整圈处理_超支用颜色与文字表达_不画第二圈(double percent) =>
        Assert.Equal(RingGeometry.Circumference, RingGeometry.ArcLength((decimal)percent), Precision);

    [Fact]
    public void 周长是中线半径的圆周长()
    {
        Assert.Equal(2d * Math.PI * RingGeometry.Radius, RingGeometry.Circumference, Precision);
    }

    [Fact]
    public void 外缘正好撑满viewBox_环宽不被裁掉也不留白()
    {
        Assert.Equal(RingGeometry.ViewBox / 2d, RingGeometry.Radius + RingGeometry.StrokeWidth / 2d, Precision);
    }
}
