namespace FinanceTracker.Core.Enums;

/// <summary>
/// 趋势统计的时间粒度
/// </summary>
public enum TrendDimension
{
    /// <summary>
    /// 按日
    /// </summary>
    Day = 0,

    /// <summary>
    /// 按周（周一为一周的起始）
    /// </summary>
    Week = 1,

    /// <summary>
    /// 按月
    /// </summary>
    Month = 2
}

/// <summary>
/// <see cref="TrendDimension"/> 的字符串解析（API 查询参数、页面按钮值等外部输入的唯一入口）
/// </summary>
public static class TrendDimensionParser
{
    /// <summary>
    /// 解析 day / week / month（不区分大小写，忽略首尾空白）。
    /// 只接受名称，不接受数字（避免 <c>?dimension=1</c> 这类歧义输入被悄悄当成合法值）。
    /// </summary>
    public static bool TryParse(string? value, out TrendDimension dimension)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "day":
                dimension = TrendDimension.Day;
                return true;
            case "week":
                dimension = TrendDimension.Week;
                return true;
            case "month":
                dimension = TrendDimension.Month;
                return true;
            default:
                dimension = default;
                return false;
        }
    }
}
