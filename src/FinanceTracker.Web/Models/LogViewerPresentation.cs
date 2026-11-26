using System.Globalization;
using System.Text;
using FinanceTracker.Web.Services;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 错误日志页的筛选条件（不可变）：搜索关键字 + 级别（多选）+ 起止日期（含当天）。
/// 关键字不是「筛选条件」——它的展示就是搜索框里的文字，不计入条件标签（与账单页一致）。
/// </summary>
public sealed record LogFilter
{
    /// <summary>没有任何条件</summary>
    public static LogFilter None { get; } = new();

    /// <summary>搜索关键字（已去掉首尾空格）；匹配消息、异常类型、来源、完整信息，不区分大小写</summary>
    public string Keyword { get; init; } = string.Empty;

    /// <summary>选中的级别（按严重程度排好序）；空 = 不限</summary>
    public IReadOnlyList<string> Levels { get; init; } = [];

    /// <summary>开始日期（含当天）；空 = 不限</summary>
    public DateTime? Start { get; init; }

    /// <summary>结束日期（含当天）；空 = 不限</summary>
    public DateTime? End { get; init; }

    /// <summary>有搜索关键字</summary>
    public bool HasKeyword => Keyword.Length > 0;

    /// <summary>有筛选条件（级别 / 起止日期，不含关键字）</summary>
    public bool HasConditions => Levels.Count > 0 || Start.HasValue || End.HasValue;

    /// <summary>有时间范围（起止日期任一端）</summary>
    public bool HasTimeRange => Start.HasValue || End.HasValue;

    /// <summary>关键字或条件至少有一个</summary>
    public bool HasAny => HasKeyword || HasConditions;

    public LogFilter WithKeyword(string? keyword) => this with { Keyword = keyword?.Trim() ?? string.Empty };

    public LogFilter WithLevels(IEnumerable<string>? levels) => this with { Levels = LogViewerPresentation.OrderLevels(levels) };

    /// <summary>设置起止日期：只取日期部分（时间点没有意义，结束日期按「含当天」处理）</summary>
    public LogFilter WithRange(DateTime? start, DateTime? end) => this with { Start = start?.Date, End = end?.Date };

    /// <summary>清掉级别与起止日期（保留搜索关键字）</summary>
    public LogFilter ClearConditions() => this with { Levels = [], Start = null, End = null };
}

/// <summary>日志级别的展示：图标 + 图标块配色（purple / red / orange / blue / green / gray）</summary>
public sealed record LogLevelStyle(string Icon, string Tone);

/// <summary>某个级别的日志条数（概览里的「级别分布」）</summary>
public sealed record LogLevelCount(string Level, int Count);

/// <summary>日志列表里的一天</summary>
/// <param name="Key">分组键：yyyy-MM-dd；时间未知的日志是 unknown</param>
/// <param name="Title">组头标题：今天 / 昨天 / 11月23日</param>
/// <param name="DateText">组头日期（今天 / 昨天补具体日期与星期，更早的只补星期）</param>
/// <param name="CountText">当天条数：「3 条」</param>
/// <param name="Items">当天的日志（保持传入顺序，即时间倒序）</param>
public sealed record LogDayGroup(string Key, string Title, string DateText, string CountText, IReadOnlyList<LogEntry> Items);

/// <summary>
/// 错误日志页的展示与判定规则（纯函数，单元测试覆盖）：级别样式、行文案、按天分组、筛选条件的文案、空状态、复制格式与各类提示。
/// 页面只负责取数与交互，规则集中在这里。
/// </summary>
public static class LogViewerPresentation
{
    // ───────────────────────── 常量 ─────────────────────────

    /// <summary>每次加载的条数（「加载更多」每次再取一页）</summary>
    public const int PageSize = 20;

    /// <summary>搜索关键字的长度上限</summary>
    public const int SearchMaxLength = 50;

    /// <summary>搜索框的占位文字：直接说清楚能搜什么</summary>
    public const string SearchPlaceholder = "搜索消息、异常类型或来源";

    /// <summary>搜索框的读屏名称</summary>
    public const string SearchLabel = "搜索日志";

    /// <summary>搜索框清除按钮的读屏名称</summary>
    public const string SearchClearLabel = "清除搜索";

    /// <summary>「⋯」按钮的读屏名称</summary>
    public const string MoreLabel = "更多";

    /// <summary>时间戳解析失败（<see cref="DateTime.MinValue"/>）时显示的文字</summary>
    public const string UnknownTimeText = "时间未知";

    /// <summary>消息和异常都是空的日志在列表里显示的标题</summary>
    public const string NoMessageText = "（无消息）";

    /// <summary>来源为空时详情里显示的文字</summary>
    public const string UnknownSourceText = "未知";

    /// <summary>条件标签与筛选 Sheet 里的维度名</summary>
    public const string LevelDimension = "级别";

    /// <summary>条件标签与筛选 Sheet 里的维度名</summary>
    public const string TimeDimension = "时间";

    // ───────────────────────── 级别 ─────────────────────────

    // 筛选 Sheet 里一定提供的级别：应用只记录 Error 及以上（Error、Fatal），其余级别只在日志里真的出现过（或已被选中）时才列出
    private static readonly string[] AlwaysOffered = ["Fatal", "Error"];

    /// <summary>
    /// 级别的严重程度（越小越严重）：Fatal（旧页面叫 Critical，Serilog 的级别名是 Fatal，二者都认）→ Error → Warning → Information → Debug → Verbose → 其它
    /// </summary>
    public static int Severity(string? level) => level switch
    {
        "Fatal" or "Critical" => 0,
        "Error" => 1,
        "Warning" => 2,
        "Information" => 3,
        "Debug" => 4,
        "Verbose" or "Trace" => 5,
        _ => 6,
    };

    /// <summary>级别的图标与图标块配色；不认识的级别是灰色问号</summary>
    public static LogLevelStyle StyleOf(string? level) => level switch
    {
        "Fatal" or "Critical" => new("mdi-alert-octagon", "purple"),
        "Error" => new("mdi-alert-circle", "red"),
        "Warning" => new("mdi-alert", "orange"),
        "Information" => new("mdi-information", "blue"),
        "Debug" => new("mdi-bug", "green"),
        _ => new("mdi-help-circle", "gray"),
    };

    /// <summary>去重、去空并按严重程度排序（同一严重程度按名字）</summary>
    public static IReadOnlyList<string> OrderLevels(IEnumerable<string>? levels) =>
        (levels ?? [])
            .Where(level => !string.IsNullOrEmpty(level))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Severity)
            .ThenBy(level => level, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 筛选 Sheet 里可选的级别：Fatal、Error 一定有；其它级别只在日志里出现过（present）或已选中（selected）时才列出——
    /// 应用只记录 Error 及以上，一排永远查不到结果的 Warning / Information / Debug 只会添乱；以后放宽记录级别时它们会自动出现。
    /// </summary>
    public static IReadOnlyList<string> FilterLevels(IEnumerable<string>? present, IEnumerable<string>? selected) =>
        OrderLevels(AlwaysOffered.Concat(present ?? []).Concat(selected ?? []));

    /// <summary>概览里的级别分布：条数多的在前（同数按严重程度）</summary>
    public static IReadOnlyList<LogLevelCount> LevelDistribution(IReadOnlyDictionary<string, int>? counts) =>
        (counts ?? new Dictionary<string, int>())
            .Where(pair => pair.Value > 0 && !string.IsNullOrEmpty(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => Severity(pair.Key))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new LogLevelCount(pair.Key, pair.Value))
            .ToList();

    // ───────────────────────── 概览 ─────────────────────────

    /// <summary>
    /// 概览里的时间范围：同一年内是「11-20 09:12 ~ 11-25 10:30」，有一端不在今年时两端都带年份；缺任意一端是「无」
    /// </summary>
    public static string TimeRangeText(DateTime? earliest, DateTime? latest, DateTime today)
    {
        if (earliest is not { } from || latest is not { } to)
        {
            return "无";
        }

        var format = from.Year != today.Year || to.Year != today.Year ? "yyyy-MM-dd HH:mm" : "MM-dd HH:mm";
        return $"{from.ToString(format, CultureInfo.InvariantCulture)} ~ {to.ToString(format, CultureInfo.InvariantCulture)}";
    }

    /// <summary>概览时间范围一行的前缀</summary>
    public const string TimeRangeLabel = "时间范围";

    // ───────────────────────── 列表行 ─────────────────────────

    /// <summary>时间戳解析失败（没有 @t 或格式不对）</summary>
    public static bool IsUnknownTime(DateTime timestamp) => timestamp.Year <= 1;

    /// <summary>行内时间：「12:00:14」；时间未知的是「时间未知」</summary>
    public static string TimeOfDay(DateTime timestamp) =>
        IsUnknownTime(timestamp) ? UnknownTimeText : timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>完整时间（含毫秒）：「2026-11-25 12:00:14.123」；时间未知的是「时间未知」</summary>
    public static string FullTime(DateTime timestamp) =>
        IsUnknownTime(timestamp) ? UnknownTimeText : timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>来源的短名：<c>FinanceTracker.Core.Services.SyncService</c> → <c>SyncService</c>；空来源是空串</summary>
    public static string SourceName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var text = source.Trim();
        var dot = text.LastIndexOf('.');
        return dot >= 0 && dot < text.Length - 1 ? text[(dot + 1)..] : text;
    }

    /// <summary>异常类型的短名：<c>System.IO.IOException</c> → <c>IOException</c>（连同 <c>(0x80004005)</c> 之类的后缀一并去掉）</summary>
    public static string ExceptionShortName(string? exceptionType)
    {
        if (string.IsNullOrWhiteSpace(exceptionType))
        {
            return string.Empty;
        }

        var text = exceptionType.Trim();
        var space = text.IndexOf(' ');
        if (space >= 0)
        {
            text = text[..space];
        }

        var dot = text.LastIndexOf('.');
        return dot >= 0 && dot < text.Length - 1 ? text[(dot + 1)..] : text;
    }

    /// <summary>
    /// 行标题：消息的第一行；消息是空的就用异常类型的短名，再不行用完整信息的第一行，都没有是「（无消息）」。
    /// 多行消息（堆栈）在列表里只看第一行，全文在详情 Sheet 里。
    /// </summary>
    public static string RowTitle(LogEntry entry)
    {
        var message = FirstLine(entry.Message);
        if (message.Length > 0)
        {
            return message;
        }

        // 服务在没有异常信息时把异常类型留空，有异常但认不出类型时给「UnknownException」——后者不如完整信息的第一行有用
        var typeName = ExceptionShortName(entry.ExceptionType);
        if (typeName.Length > 0 && typeName != "UnknownException")
        {
            return typeName;
        }

        var full = FirstLine(entry.FullMessage);
        return full.Length > 0 ? full : NoMessageText;
    }

    /// <summary>行副标题：「12:00:14 · Error · SyncService」，没有来源时省略最后一段</summary>
    public static string RowSubtitle(LogEntry entry)
    {
        var parts = new List<string> { TimeOfDay(entry.Timestamp) };
        if (!string.IsNullOrEmpty(entry.Level))
        {
            parts.Add(entry.Level);
        }

        var source = SourceName(entry.Source);
        if (source.Length > 0)
        {
            parts.Add(source);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>行的读屏名称：级别、完整时间、标题（副标题里的时间只有时分秒，读屏需要日期）</summary>
    public static string RowLabel(LogEntry entry) =>
        string.Join("，", new[] { entry.Level, FullTime(entry.Timestamp), RowTitle(entry) }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>
    /// 按天分组（应用的本机日历日）：保持传入的顺序（服务按时间倒序给），同一天的日志归一组；
    /// 时间未知的日志归入末尾的「时间未知」一组。「加载更多」后对全部已加载的日志重新分组，跨页的同一天会合并。
    /// </summary>
    /// <param name="entries">按时间倒序排好的日志</param>
    /// <param name="today">本机的今天（组头「今天 / 昨天」的基准）</param>
    public static IReadOnlyList<LogDayGroup> GroupByDay(IEnumerable<LogEntry> entries, DateTime today) =>
        entries
            .GroupBy(entry => IsUnknownTime(entry.Timestamp) ? DateTime.MinValue : entry.Timestamp.Date)
            .Select(group =>
            {
                var items = group.ToList();
                var unknown = IsUnknownTime(group.Key);
                return new LogDayGroup(
                    unknown ? "unknown" : group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    unknown ? UnknownTimeText : BillListPresentation.DayTitle(group.Key, today),
                    unknown ? string.Empty : BillListPresentation.DayDateText(group.Key, today),
                    CountText(items.Count),
                    items);
            })
            .ToList();

    /// <summary>数字：千分位（概览里的总数、今日、本周）</summary>
    public static string NumberText(int number) => number.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>条数：「3 条」</summary>
    public static string CountText(int count) => $"{NumberText(count)} 条";

    /// <summary>列表末尾的进度：全部加载完是「共 85 条」，否则是「已显示 20 / 共 85 条」</summary>
    public static string LoadedText(int shown, int total) =>
        shown >= total
            ? $"共 {NumberText(total)} 条"
            : $"已显示 {NumberText(shown)} / 共 {NumberText(total)} 条";

    /// <summary>搜索框的读屏播报：搜索结果有多少条</summary>
    public static string SearchStatusText(LogFilter filter, int total, bool loading)
    {
        if (loading || !filter.HasKeyword)
        {
            return string.Empty;
        }

        return total == 0 ? "没有找到相关日志" : $"找到 {NumberText(total)} 条日志";
    }

    // ───────────────────────── 条件条 ─────────────────────────

    /// <summary>级别条件的显示文案：「Fatal、Error」（按严重程度排好）</summary>
    public static string LevelsText(IEnumerable<string>? levels) => string.Join("、", OrderLevels(levels));

    /// <summary>
    /// 时间条件的显示文案：「11月1日至11月25日」（和今年同年的省略年份），只填一端是「11月1日起」「至11月25日」，起止同一天只写这一天；
    /// 口径与账单页的自定义时间条件一致。没设置是空串。
    /// </summary>
    public static string TimeText(DateTime? start, DateTime? end, DateTime today) =>
        BillListPresentation.CustomRangeText(start, end, today);

    /// <summary>条件标签的读屏名称：「级别：Error，点按调整」</summary>
    public static string ChipLabel(string dimension, string value) => $"{dimension}：{value}，点按调整";

    /// <summary>条件标签上「移除」小按钮的读屏名称</summary>
    public static string ChipRemoveLabel(string dimension, string value) => $"移除{dimension}条件：{value}";

    /// <summary>条件条末尾「清除」标签的文案</summary>
    public const string ClearChipText = "清除条件";

    // ───────────────────────── 筛选 Sheet ─────────────────────────

    /// <summary>筛选 Sheet 的标题</summary>
    public const string FilterTitle = "筛选日志";

    /// <summary>筛选 Sheet 底部主按钮</summary>
    public const string FilterApplyText = "查看日志";

    /// <summary>
    /// 校验起止日期：两端都填了而结束早于开始时给提示，否则 null。
    /// 只填一端、或两端相同都是合法的（含当天）。
    /// </summary>
    public static string? ValidateRange(DateTime? start, DateTime? end) =>
        start is { } from && end is { } to && to.Date < from.Date ? "结束日期不能早于开始日期" : null;

    // ───────────────────────── 空状态 ─────────────────────────

    /// <summary>
    /// 列表为空时的图标与文案：没有任何日志是「好消息」，有搜索 / 筛选条件时是「没有符合条件的日志」，两者要分开说——
    /// 旧页面把筛选后为空也说成「太棒了！没有发现任何错误」，会让人以为真的没有错误。
    /// </summary>
    public static (string Icon, string Title, string Text) EmptyState(LogFilter filter)
    {
        if (!filter.HasAny)
        {
            return ("mdi-check-circle-outline", "暂无错误日志", "太棒了！没有发现任何错误");
        }

        var text = filter.HasKeyword
            ? (filter.HasConditions ? "换个关键字试试，或清除搜索与筛选" : "换个关键字试试，或清除搜索")
            : "换个筛选条件试试，或清除筛选";
        return ("mdi-text-box-search-outline", "没有符合条件的日志", text);
    }

    /// <summary>空状态里「清除」按钮的文案：与当前生效的条件对应（按钮清掉的是全部条件，含搜索关键字）</summary>
    public static string ClearConditionsText(LogFilter filter) => (filter.HasConditions, filter.HasKeyword) switch
    {
        (true, true) => "清除搜索与筛选",
        (false, true) => "清除搜索",
        _ => "清除筛选",
    };

    /// <summary>加载失败时的标题</summary>
    public const string LoadFailedTitle = "日志加载失败";

    /// <summary>加载失败时的说明</summary>
    public const string LoadFailedText = "请稍后重试";

    // ───────────────────────── 详情 Sheet ─────────────────────────

    /// <summary>详情 Sheet 的标题</summary>
    public const string DetailTitle = "日志详情";

    /// <summary>详情里「完整异常信息」是否需要单独显示：和消息不同、且不为空才显示（没有异常时两者相同）</summary>
    public static bool HasFullDetail(LogEntry entry) =>
        !string.IsNullOrEmpty(entry.FullMessage) && entry.FullMessage != entry.Message;

    /// <summary>详情里的来源：空来源显示「未知」</summary>
    public static string SourceText(LogEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Source) ? UnknownSourceText : entry.Source;

    /// <summary>详情里的异常类型是否显示（没有异常 / 认不出类型时不显示）</summary>
    public static bool HasExceptionType(LogEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.ExceptionType) && entry.ExceptionType != "UnknownException";

    /// <summary>
    /// 「复制」到剪贴板的文本：时间、级别、异常类型（有才写）、消息、来源、完整信息——贴给别人排查时一眼能看全。
    /// </summary>
    public static string CopyText(LogEntry entry)
    {
        var text = new StringBuilder();
        text.Append("时间: ").AppendLine(IsUnknownTime(entry.Timestamp) ? UnknownTimeText : entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        text.Append("级别: ").AppendLine(entry.Level);
        if (HasExceptionType(entry))
        {
            text.Append("异常类型: ").AppendLine(entry.ExceptionType);
        }

        text.Append("消息: ").AppendLine(entry.Message);
        text.Append("来源: ").AppendLine(entry.Source);
        text.AppendLine("完整信息:");
        text.Append(entry.FullMessage);
        return text.ToString().Replace("\r\n", "\n");
    }

    // ───────────────────────── 菜单、确认与提示 ─────────────────────────

    /// <summary>清除确认 Alert 的标题</summary>
    public const string ClearTitle = "清除所有错误日志？";

    /// <summary>清除确认 Alert 的说明：写明会删多少条（概览里的总数）</summary>
    public static string ClearMessage(int total) =>
        total > 0
            ? $"共 {NumberText(total)} 条日志将被删除，此操作不可恢复。"
            : "日志文件将被删除，此操作不可恢复。";

    /// <summary>清除确认 Alert 的确认按钮</summary>
    public const string ClearConfirmText = "清除";

    public const string ClearSuccessMessage = "日志已清除";

    public const string ClearFailedMessage = "清除失败";

    public static string ClearFailedWithReason(string detail) => $"清除失败: {detail}";

    public const string ExportFailedMessage = "导出失败";

    public static string ExportFailedWithReason(string detail) => $"导出失败: {detail}";

    public static string ExportSuccessMessage(string path) => $"日志已导出到: {path}";

    public const string CopySuccessMessage = "已复制到剪贴板";

    public static string CopyFailedWithReason(string detail) => $"复制失败: {detail}";

    public static string LoadFailedWithReason(string detail) => $"加载日志失败: {detail}";

    // ───────────────────────── 内部 ─────────────────────────

    /// <summary>第一行非空文本（去掉首尾空白）；全是空白是空串</summary>
    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }
}
