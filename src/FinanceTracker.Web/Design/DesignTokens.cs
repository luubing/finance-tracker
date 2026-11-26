using Masa.Blazor;
using Masa.Blazor.Presets;

namespace FinanceTracker.Web.Design;

/// <summary>
/// 需要在 C# 侧使用的设计令牌取值。
/// 样式令牌的唯一来源是 <c>wwwroot/css/tokens.css</c>；这里只放必须与 CSS 保持一致的少数取值（MASA 主题色），
/// 单元测试 DesignTokensTests 校验两边相同，改品牌色时漏改任何一边都会被拦住。
/// </summary>
public static class DesignTokens
{
    /// <summary>品牌色（tokens.css 的 <c>--ft-brand</c>），同时是 MASA 主题的 primary。</summary>
    public const string BrandColor = "#4318FF";

    /// <summary>危险 / 超支（<c>--ft-danger</c>）。</summary>
    public const string DangerColor = "#FF3B30";

    /// <summary>预警（<c>--ft-warning</c>）。</summary>
    public const string WarningColor = "#FF9500";

    /// <summary>信息（<c>--ft-info</c>）。</summary>
    public const string InfoColor = "#007AFF";

    /// <summary>成功（<c>--ft-success</c>）。</summary>
    public const string SuccessColor = "#34C759";

    /// <summary>支出（<c>--ft-expense</c>）：ECharts 图表里的支出线 / 热力图终点——ECharts 画在 canvas 上，读不到 CSS 变量，只能在 C# 侧取同一个值。</summary>
    public const string ExpenseColor = "#E8362C";

    /// <summary>收入（<c>--ft-income</c>）：图表里的收入线。</summary>
    public const string IncomeColor = "#1FA544";

    /// <summary>主色浅底（<c>--ft-brand-soft</c> = 主色 10% 叠在白底上的实色）：热力图的最浅一档。</summary>
    public const string BrandTintColor = "#ECE8FF";

    /// <summary>图表坐标轴刻度文字（<c>--ft-label-2</c>）：同样画在 canvas 上，在 C# 侧写同一个值（改了令牌这里必须跟着改，DesignTokensTests 校验）。</summary>
    public const string ChartAxisTextColor = "rgba(60, 60, 67, 0.76)";

    /// <summary>图表坐标轴网格线（<c>--ft-separator</c>）。</summary>
    public const string ChartGridLineColor = "rgba(60, 60, 67, 0.13)";

    /// <summary>日历热力图里没有数据的格子（<c>--ft-bg</c>）。</summary>
    public const string ChartEmptyCellColor = "#F2F2F7";

    /// <summary>日历热力图里没有数据的格子的描边：iOS 系统灰 5，没有对应的 CSS 令牌（只有这张图用），实色，免得格子之间的缝随底色变。</summary>
    public const string ChartEmptyCellBorderColor = "#E5E5EA";
}

/// <summary>
/// 两个宿主（Web.Server 与 MAUI App）共用的 MASA Blazor 配置，保证两端主题与默认值完全一致。
/// </summary>
public static class MasaBlazorDesignExtensions
{
    /// <summary>
    /// 套用本应用的 MASA 配置：页面栈顶栏高度，以及与设计令牌对齐的主题色。
    /// </summary>
    public static void UseFinanceTrackerDesign(this MasaBlazorOptions options)
    {
        options.Defaults = new Dictionary<string, IDictionary<string, object?>?>
        {
            {
                nameof(PStackPageBar), new Dictionary<string, object?>
                {
                    { nameof(PStackPageBar.Height), 44 }
                }
            }
        };

        options.ConfigureTheme(ApplyTheme);
    }

    /// <summary>
    /// 把设计令牌里的品牌色与语义色写入 MASA 浅色主题（应用只提供浅色模式）。
    /// </summary>
    public static void ApplyTheme(Theme theme)
    {
        var light = theme.Themes.Light;
        light.Primary = DesignTokens.BrandColor;
        light.Error = DesignTokens.DangerColor;
        light.Warning = DesignTokens.WarningColor;
        light.Info = DesignTokens.InfoColor;
        light.Success = DesignTokens.SuccessColor;
    }
}
