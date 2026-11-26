using System.Globalization;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 日期输入行的共用规则（纯函数，单元测试覆盖）。
/// 这类行看到的是「2026年10月26日」，真正的输入是盖在整行上的透明 <c>&lt;input type="date"&gt;</c>（点按唤起系统的日期选择器），
/// 所以需要三件事：行右侧显示的中文日期、日期 → 输入控件的值、输入控件的值 → 日期。
/// 短信识别的读取范围、自定义报表的起止日期共用，两处不会各写各的。
/// </summary>
public static class DateInputPresentation
{
    /// <summary>日期输入控件的格式（HTML 规定的值格式，不随设备区域设置变化）</summary>
    public const string InputFormat = "yyyy-MM-dd";

    /// <summary>日期输入行右侧的文字：「2026年10月26日」</summary>
    public static string Label(DateTime date) => $"{date.Year}年{date.Month}月{date.Day}日";

    /// <summary>日期 → 日期输入控件的值（yyyy-MM-dd）</summary>
    public static string ToInputValue(DateTime date) => date.ToString(InputFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// 日期输入控件的值 → 日期（Kind 为 Unspecified，当天零点）。空或格式不对返回 false，由调用方决定怎么处理。
    /// 不用 <c>DateTime.Parse</c>：它依赖设备区域设置，同一个值换一台设备可能解析成别的日期。
    /// </summary>
    public static bool TryParse(string? value, out DateTime date) =>
        DateTime.TryParseExact(value, InputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
