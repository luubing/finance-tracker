namespace FinanceTracker.Web.Models;

/// <summary>
/// 进度环的几何（纯函数，单元测试覆盖）：一圈粗环，从 12 点钟方向顺时针画到当前进度。
/// 坐标系是 100 × 100 的 viewBox（外径刚好撑满），环宽占外径的 12%，孔的直径占外径的 76%
/// （设计稿 screens 09-budgets：外径约 118pt、环宽约 14pt）。
/// </summary>
public static class RingGeometry
{
    /// <summary>viewBox 边长</summary>
    public const double ViewBox = 100d;

    /// <summary>环中线的半径（外缘 = 半径 + 环宽 / 2 = 50，正好贴满 viewBox）</summary>
    public const double Radius = 44d;

    /// <summary>环宽</summary>
    public const double StrokeWidth = 12d;

    /// <summary>环中线的周长</summary>
    public static double Circumference { get; } = 2d * Math.PI * Radius;

    /// <summary>进度弧的长度：进度限制在 0–100% 之间</summary>
    public static double ArcLength(decimal percent) =>
        Circumference * (double)Math.Clamp(percent, 0m, 100m) / 100d;
}
