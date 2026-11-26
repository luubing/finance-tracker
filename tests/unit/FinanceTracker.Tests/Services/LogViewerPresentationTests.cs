using FinanceTracker.Web.Models;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 错误日志页的展示与判定规则（22 号）：级别排序与样式、行标题 / 副标题 / 读屏名称、按天分组、筛选条件与空状态的文案、复制格式与各类提示。
/// 规则集中在 <see cref="LogViewerPresentation"/>，页面只负责取数与交互。
/// </summary>
public sealed class LogViewerPresentationTests
{
    private static readonly DateTime Today = new(2026, 11, 25);

    private static LogEntry Entry(
        string level = "Error",
        DateTime? timestamp = null,
        string message = "同步失败",
        string source = "FinanceTracker.Core.Services.SyncService",
        string exceptionType = "",
        string fullMessage = "",
        string id = "1") => new()
        {
            Id = id,
            Level = level,
            Timestamp = timestamp ?? new DateTime(2026, 11, 25, 12, 0, 14, 123),
            Message = message,
            Source = source,
            ExceptionType = exceptionType,
            FullMessage = string.IsNullOrEmpty(fullMessage) ? message : fullMessage,
        };

    // ───────────────────────── 级别 ─────────────────────────

    [Fact]
    public void 级别按严重程度排序_旧页面的Critical与Serilog的Fatal同级()
    {
        Assert.Equal(LogViewerPresentation.Severity("Fatal"), LogViewerPresentation.Severity("Critical"));

        var ordered = new[] { "Debug", "Information", "Error", "Warning", "Fatal", "Verbose" }
            .OrderBy(LogViewerPresentation.Severity)
            .ToList();

        Assert.Equal(new[] { "Fatal", "Error", "Warning", "Information", "Debug", "Verbose" }, ordered);
        Assert.True(LogViewerPresentation.Severity("Mystery") > LogViewerPresentation.Severity("Verbose"));
        Assert.True(LogViewerPresentation.Severity(null) > LogViewerPresentation.Severity("Verbose"));
    }

    [Theory]
    [InlineData("Fatal", "mdi-alert-octagon", "purple")]
    [InlineData("Critical", "mdi-alert-octagon", "purple")]
    [InlineData("Error", "mdi-alert-circle", "red")]
    [InlineData("Warning", "mdi-alert", "orange")]
    [InlineData("Information", "mdi-information", "blue")]
    [InlineData("Debug", "mdi-bug", "green")]
    [InlineData("Mystery", "mdi-help-circle", "gray")]
    [InlineData(null, "mdi-help-circle", "gray")]
    public void 级别的图标与图标块配色(string? level, string icon, string tone)
    {
        Assert.Equal(new LogLevelStyle(icon, tone), LogViewerPresentation.StyleOf(level));
    }

    [Fact]
    public void 级别列表去重去空并按严重程度排序()
    {
        Assert.Equal(new[] { "Fatal", "Error", "Warning" }, LogViewerPresentation.OrderLevels(new[] { "Warning", "Error", "", "Fatal", "Error" }));
        Assert.Empty(LogViewerPresentation.OrderLevels(null));
        Assert.Empty(LogViewerPresentation.OrderLevels(Array.Empty<string>()));
    }

    [Fact]
    public void 筛选Sheet里Fatal与Error一定有_其它级别只在出现过或已选中时才列出()
    {
        Assert.Equal(new[] { "Fatal", "Error" }, LogViewerPresentation.FilterLevels(null, null));
        Assert.Equal(new[] { "Fatal", "Error" }, LogViewerPresentation.FilterLevels(new[] { "Error" }, Array.Empty<string>()));
        Assert.Equal(new[] { "Fatal", "Error", "Warning" }, LogViewerPresentation.FilterLevels(new[] { "Warning" }, null));
        Assert.Equal(new[] { "Fatal", "Error", "Debug" }, LogViewerPresentation.FilterLevels(null, new[] { "Debug" }));
        Assert.Equal(new[] { "Fatal", "Error", "Warning", "Debug" }, LogViewerPresentation.FilterLevels(new[] { "Debug", "Warning" }, new[] { "Debug" }));
    }

    [Fact]
    public void 级别分布条数多的在前_同数按严重程度_零条的不列()
    {
        var distribution = LogViewerPresentation.LevelDistribution(new Dictionary<string, int>
        {
            ["Warning"] = 3,
            ["Error"] = 3,
            ["Fatal"] = 1,
            ["Information"] = 0,
            ["Debug"] = 7,
        });

        Assert.Equal(
            new[] { new LogLevelCount("Debug", 7), new LogLevelCount("Error", 3), new LogLevelCount("Warning", 3), new LogLevelCount("Fatal", 1) },
            distribution);
        Assert.Empty(LogViewerPresentation.LevelDistribution(null));
    }

    // ───────────────────────── 概览 ─────────────────────────

    [Fact]
    public void 概览时间范围_同年省略年份_跨年带年份_缺一端是无()
    {
        Assert.Equal("11-20 09:12 ~ 11-25 10:30", LogViewerPresentation.TimeRangeText(new DateTime(2026, 11, 20, 9, 12, 0), new DateTime(2026, 11, 25, 10, 30, 0), Today));
        Assert.Equal("2025-12-30 23:59 ~ 2026-01-02 00:01", LogViewerPresentation.TimeRangeText(new DateTime(2025, 12, 30, 23, 59, 0), new DateTime(2026, 1, 2, 0, 1, 0), Today));
        Assert.Equal("2025-03-01 08:00 ~ 2025-03-02 08:00", LogViewerPresentation.TimeRangeText(new DateTime(2025, 3, 1, 8, 0, 0), new DateTime(2025, 3, 2, 8, 0, 0), Today));
        Assert.Equal("无", LogViewerPresentation.TimeRangeText(null, null, Today));
        Assert.Equal("无", LogViewerPresentation.TimeRangeText(new DateTime(2026, 11, 20), null, Today));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(85, "85")]
    [InlineData(1234, "1,234")]
    [InlineData(1234567, "1,234,567")]
    public void 数字用千分位(int number, string expected)
    {
        Assert.Equal(expected, LogViewerPresentation.NumberText(number));
        Assert.Equal($"{expected} 条", LogViewerPresentation.CountText(number));
    }

    [Fact]
    public void 列表末尾的进度_加载完是共几条_否则是已显示几条()
    {
        Assert.Equal("已显示 20 / 共 85 条", LogViewerPresentation.LoadedText(20, 85));
        Assert.Equal("共 85 条", LogViewerPresentation.LoadedText(85, 85));
        Assert.Equal("共 1,200 条", LogViewerPresentation.LoadedText(1200, 1200));
        Assert.Equal("共 0 条", LogViewerPresentation.LoadedText(0, 0));
    }

    [Fact]
    public void 搜索结果条数只在有关键字且加载完成后播报()
    {
        var searching = LogFilter.None.WithKeyword("超时");

        Assert.Equal("找到 3 条日志", LogViewerPresentation.SearchStatusText(searching, 3, loading: false));
        Assert.Equal("没有找到相关日志", LogViewerPresentation.SearchStatusText(searching, 0, loading: false));
        Assert.Equal(string.Empty, LogViewerPresentation.SearchStatusText(searching, 3, loading: true));
        Assert.Equal(string.Empty, LogViewerPresentation.SearchStatusText(LogFilter.None, 3, loading: false));
        Assert.Equal(string.Empty, LogViewerPresentation.SearchStatusText(LogFilter.None.WithLevels(new[] { "Error" }), 3, loading: false));
    }

    // ───────────────────────── 列表行 ─────────────────────────

    [Fact]
    public void 时间未知的日志显示时间未知_不显示0001年()
    {
        var unknown = DateTime.MinValue;

        Assert.True(LogViewerPresentation.IsUnknownTime(unknown));
        Assert.False(LogViewerPresentation.IsUnknownTime(new DateTime(2026, 11, 25)));
        Assert.Equal("时间未知", LogViewerPresentation.TimeOfDay(unknown));
        Assert.Equal("时间未知", LogViewerPresentation.FullTime(unknown));
        Assert.Equal("12:00:14", LogViewerPresentation.TimeOfDay(new DateTime(2026, 11, 25, 12, 0, 14, 123)));
        Assert.Equal("2026-11-25 12:00:14.123", LogViewerPresentation.FullTime(new DateTime(2026, 11, 25, 12, 0, 14, 123)));
    }

    [Theory]
    [InlineData("FinanceTracker.Core.Services.SyncService", "SyncService")]
    [InlineData("SyncService", "SyncService")]
    [InlineData("  FinanceTracker.Web.Pages.Index  ", "Index")]
    [InlineData("Namespace.", "Namespace.")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void 来源显示短名(string? source, string expected)
    {
        Assert.Equal(expected, LogViewerPresentation.SourceName(source));
    }

    [Theory]
    [InlineData("System.IO.IOException", "IOException")]
    [InlineData("System.Runtime.InteropServices.COMException (0x80004005)", "COMException")]
    [InlineData("TimeoutException", "TimeoutException")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 异常类型显示短名并去掉后缀(string? exceptionType, string expected)
    {
        Assert.Equal(expected, LogViewerPresentation.ExceptionShortName(exceptionType));
    }

    [Fact]
    public void 行标题取消息的第一行非空文本_多行堆栈在列表里只看第一行()
    {
        Assert.Equal("同步失败", LogViewerPresentation.RowTitle(Entry(message: "同步失败")));
        Assert.Equal("第一行", LogViewerPresentation.RowTitle(Entry(message: "\n  \n  第一行  \n第二行")));
    }

    [Fact]
    public void 消息为空时行标题退到异常类型短名_再到完整信息的第一行_都没有是无消息()
    {
        Assert.Equal(
            "IOException",
            LogViewerPresentation.RowTitle(Entry(message: "", exceptionType: "System.IO.IOException", fullMessage: "System.IO.IOException: x")));

        // 认不出类型的异常，类型只是占位的 UnknownException：不如完整信息的第一行有用
        Assert.Equal(
            "System.InvalidOperationException: 出错",
            LogViewerPresentation.RowTitle(Entry(message: "", exceptionType: "UnknownException", fullMessage: "System.InvalidOperationException: 出错\n   at A.B()")));

        Assert.Equal("（无消息）", LogViewerPresentation.RowTitle(Entry(message: "", exceptionType: "", fullMessage: "   ")));
    }

    [Fact]
    public void 行副标题是时间_级别_来源短名_没有来源时省略最后一段()
    {
        Assert.Equal("12:00:14 · Error · SyncService", LogViewerPresentation.RowSubtitle(Entry()));
        Assert.Equal("12:00:14 · Error", LogViewerPresentation.RowSubtitle(Entry(source: "")));
        Assert.Equal("时间未知 · Fatal · SyncService", LogViewerPresentation.RowSubtitle(Entry(level: "Fatal", timestamp: DateTime.MinValue)));
        Assert.Equal("12:00:14 · SyncService", LogViewerPresentation.RowSubtitle(Entry(level: "")));
    }

    [Fact]
    public void 行的读屏名称带完整时间_副标题里的时间只有时分秒()
    {
        Assert.Equal("Error，2026-11-25 12:00:14.123，同步失败", LogViewerPresentation.RowLabel(Entry()));
        Assert.Equal("时间未知，同步失败", LogViewerPresentation.RowLabel(Entry(level: "", timestamp: DateTime.MinValue)));
    }

    // ───────────────────────── 按天分组 ─────────────────────────

    [Fact]
    public void 按天分组_保持传入顺序_今天昨天用称呼_更早的用日期()
    {
        var entries = new[]
        {
            Entry(timestamp: new DateTime(2026, 11, 25, 12, 0, 0), id: "a"),
            Entry(timestamp: new DateTime(2026, 11, 25, 8, 0, 0), id: "b"),
            Entry(timestamp: new DateTime(2026, 11, 24, 23, 59, 0), id: "c"),
            Entry(timestamp: new DateTime(2026, 11, 20, 10, 0, 0), id: "d"),
            Entry(timestamp: new DateTime(2025, 12, 31, 10, 0, 0), id: "e"),
        };

        var groups = LogViewerPresentation.GroupByDay(entries, Today);

        Assert.Equal(new[] { "2026-11-25", "2026-11-24", "2026-11-20", "2025-12-31" }, groups.Select(g => g.Key));
        Assert.Equal(new[] { "今天", "昨天", "11月20日", "2025年12月31日" }, groups.Select(g => g.Title));
        Assert.Equal(new[] { "11月25日 周三", "11月24日 周二", "周五", "周三" }, groups.Select(g => g.DateText));
        Assert.Equal(new[] { "2 条", "1 条", "1 条", "1 条" }, groups.Select(g => g.CountText));
        Assert.Equal(new[] { "a", "b" }, groups[0].Items.Select(i => i.Id));
    }

    [Fact]
    public void 按天分组_时间未知的日志归入一组_不参与日期推算()
    {
        var entries = new[]
        {
            Entry(timestamp: new DateTime(2026, 11, 25, 12, 0, 0), id: "a"),
            Entry(timestamp: DateTime.MinValue, id: "u1"),
            Entry(timestamp: DateTime.MinValue, id: "u2"),
        };

        var groups = LogViewerPresentation.GroupByDay(entries, Today);

        Assert.Equal(2, groups.Count);
        var unknown = groups[1];
        Assert.Equal("unknown", unknown.Key);
        Assert.Equal("时间未知", unknown.Title);
        Assert.Equal(string.Empty, unknown.DateText);
        Assert.Equal("2 条", unknown.CountText);
    }

    [Fact]
    public void 按天分组_加载更多后对全部已加载的日志重新分组_跨页的同一天合并()
    {
        var page1 = Enumerable.Range(0, 3).Select(i => Entry(timestamp: new DateTime(2026, 11, 24, 20 - i, 0, 0), id: $"p1-{i}")).ToList();
        var page2 = Enumerable.Range(0, 2).Select(i => Entry(timestamp: new DateTime(2026, 11, 24, 10 - i, 0, 0), id: $"p2-{i}")).ToList();

        var groups = LogViewerPresentation.GroupByDay(page1.Concat(page2), Today);

        var only = Assert.Single(groups);
        Assert.Equal("昨天", only.Title);
        Assert.Equal("5 条", only.CountText);
    }

    [Fact]
    public void 没有日志时没有分组()
    {
        Assert.Empty(LogViewerPresentation.GroupByDay(Array.Empty<LogEntry>(), Today));
    }

    // ───────────────────────── 筛选条件 ─────────────────────────

    [Fact]
    public void 关键字不是筛选条件_级别与时间才是()
    {
        var keywordOnly = LogFilter.None.WithKeyword("  超时 ");

        Assert.Equal("超时", keywordOnly.Keyword);
        Assert.True(keywordOnly.HasKeyword);
        Assert.False(keywordOnly.HasConditions);
        Assert.True(keywordOnly.HasAny);

        var withLevel = LogFilter.None.WithLevels(new[] { "Warning", "Fatal" });
        Assert.Equal(new[] { "Fatal", "Warning" }, withLevel.Levels);
        Assert.True(withLevel.HasConditions);
        Assert.False(withLevel.HasKeyword);
        Assert.False(withLevel.HasTimeRange);

        var withEnd = LogFilter.None.WithRange(null, new DateTime(2026, 11, 25, 15, 30, 0));
        Assert.True(withEnd.HasTimeRange);
        Assert.Equal(new DateTime(2026, 11, 25), withEnd.End);

        Assert.False(LogFilter.None.HasAny);
    }

    [Fact]
    public void 清除条件保留搜索关键字()
    {
        var filter = LogFilter.None
            .WithKeyword("超时")
            .WithLevels(new[] { "Error" })
            .WithRange(new DateTime(2026, 11, 1), new DateTime(2026, 11, 25));

        var cleared = filter.ClearConditions();

        Assert.Equal("超时", cleared.Keyword);
        Assert.False(cleared.HasConditions);
        Assert.Empty(cleared.Levels);
        Assert.Null(cleared.Start);
        Assert.Null(cleared.End);
    }

    [Fact]
    public void 条件标签的文案()
    {
        Assert.Equal("Fatal、Error", LogViewerPresentation.LevelsText(new[] { "Error", "Fatal" }));
        Assert.Equal("Error", LogViewerPresentation.LevelsText(new[] { "Error", "Error" }));
        Assert.Equal(string.Empty, LogViewerPresentation.LevelsText(null));

        Assert.Equal("11月1日至11月25日", LogViewerPresentation.TimeText(new DateTime(2026, 11, 1), new DateTime(2026, 11, 25), Today));
        Assert.Equal("11月25日", LogViewerPresentation.TimeText(new DateTime(2026, 11, 25), new DateTime(2026, 11, 25), Today));
        Assert.Equal("11月1日起", LogViewerPresentation.TimeText(new DateTime(2026, 11, 1), null, Today));
        Assert.Equal("至11月25日", LogViewerPresentation.TimeText(null, new DateTime(2026, 11, 25), Today));
        Assert.Equal(string.Empty, LogViewerPresentation.TimeText(null, null, Today));

        Assert.Equal("级别：Error，点按调整", LogViewerPresentation.ChipLabel("级别", "Error"));
        Assert.Equal("移除级别条件：Error", LogViewerPresentation.ChipRemoveLabel("级别", "Error"));
    }

    [Fact]
    public void 起止日期只在两端都填且结束早于开始时报错()
    {
        Assert.Null(LogViewerPresentation.ValidateRange(null, null));
        Assert.Null(LogViewerPresentation.ValidateRange(new DateTime(2026, 11, 1), null));
        Assert.Null(LogViewerPresentation.ValidateRange(null, new DateTime(2026, 11, 1)));
        Assert.Null(LogViewerPresentation.ValidateRange(new DateTime(2026, 11, 25), new DateTime(2026, 11, 25)));
        Assert.Null(LogViewerPresentation.ValidateRange(new DateTime(2026, 11, 1), new DateTime(2026, 11, 25)));
        Assert.Equal("结束日期不能早于开始日期", LogViewerPresentation.ValidateRange(new DateTime(2026, 11, 25), new DateTime(2026, 11, 24)));
    }

    // ───────────────────────── 空状态 ─────────────────────────

    [Fact]
    public void 没有任何日志是好消息_有条件时是没有符合条件的日志_两者分开说()
    {
        var none = LogViewerPresentation.EmptyState(LogFilter.None);
        Assert.Equal("暂无错误日志", none.Title);
        Assert.Equal("mdi-check-circle-outline", none.Icon);

        var filtered = LogViewerPresentation.EmptyState(LogFilter.None.WithLevels(new[] { "Error" }));
        Assert.Equal("没有符合条件的日志", filtered.Title);
        Assert.Equal("mdi-text-box-search-outline", filtered.Icon);
        Assert.Equal("换个筛选条件试试，或清除筛选", filtered.Text);

        var searched = LogViewerPresentation.EmptyState(LogFilter.None.WithKeyword("x"));
        Assert.Equal("没有符合条件的日志", searched.Title);
        Assert.Equal("换个关键字试试，或清除搜索", searched.Text);

        var both = LogViewerPresentation.EmptyState(LogFilter.None.WithKeyword("x").WithLevels(new[] { "Error" }));
        Assert.Equal("换个关键字试试，或清除搜索与筛选", both.Text);

        // 空状态不能把「筛选后为空」说成「没有错误」（旧页面的缺陷）
        Assert.DoesNotContain("太棒了", filtered.Text);
        Assert.DoesNotContain("太棒了", searched.Text);
    }

    [Fact]
    public void 空状态里的清除按钮与当前生效的条件对应()
    {
        Assert.Equal("清除筛选", LogViewerPresentation.ClearConditionsText(LogFilter.None.WithLevels(new[] { "Error" })));
        Assert.Equal("清除搜索", LogViewerPresentation.ClearConditionsText(LogFilter.None.WithKeyword("x")));
        Assert.Equal("清除搜索与筛选", LogViewerPresentation.ClearConditionsText(LogFilter.None.WithKeyword("x").WithRange(new DateTime(2026, 11, 1), null)));
    }

    // ───────────────────────── 详情与复制 ─────────────────────────

    [Fact]
    public void 详情里完整异常信息只在和消息不同时单独显示()
    {
        Assert.False(LogViewerPresentation.HasFullDetail(Entry(message: "同步失败")));
        Assert.False(LogViewerPresentation.HasFullDetail(Entry(message: "同步失败", fullMessage: "")));
        Assert.True(LogViewerPresentation.HasFullDetail(Entry(message: "同步失败", fullMessage: "同步失败\nSystem.IO.IOException: x\n   at A.B()")));
    }

    [Fact]
    public void 详情里的来源与异常类型()
    {
        Assert.Equal("FinanceTracker.Core.Services.SyncService", LogViewerPresentation.SourceText(Entry()));
        Assert.Equal("未知", LogViewerPresentation.SourceText(Entry(source: "")));
        Assert.Equal("未知", LogViewerPresentation.SourceText(Entry(source: "   ")));

        Assert.True(LogViewerPresentation.HasExceptionType(Entry(exceptionType: "System.IO.IOException")));
        Assert.False(LogViewerPresentation.HasExceptionType(Entry(exceptionType: "")));
        Assert.False(LogViewerPresentation.HasExceptionType(Entry(exceptionType: "UnknownException")));
    }

    [Fact]
    public void 复制的文本包含时间级别异常类型消息来源与完整信息()
    {
        var entry = Entry(
            message: "同步失败",
            exceptionType: "System.IO.IOException",
            fullMessage: "同步失败\nSystem.IO.IOException: x\n   at A.B()");

        var text = LogViewerPresentation.CopyText(entry);

        Assert.Equal(
            "时间: 2026-11-25 12:00:14\n" +
            "级别: Error\n" +
            "异常类型: System.IO.IOException\n" +
            "消息: 同步失败\n" +
            "来源: FinanceTracker.Core.Services.SyncService\n" +
            "完整信息:\n" +
            "同步失败\nSystem.IO.IOException: x\n   at A.B()",
            text);
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void 复制的文本在没有异常类型时省略这一行_时间未知时写时间未知()
    {
        var text = LogViewerPresentation.CopyText(Entry(timestamp: DateTime.MinValue));

        Assert.DoesNotContain("异常类型", text);
        Assert.StartsWith("时间: 时间未知\n", text);
    }

    // ───────────────────────── 菜单、确认与提示 ─────────────────────────

    [Fact]
    public void 清除确认写明会删多少条_不可恢复()
    {
        Assert.Equal("共 85 条日志将被删除，此操作不可恢复。", LogViewerPresentation.ClearMessage(85));
        Assert.Equal("共 1,200 条日志将被删除，此操作不可恢复。", LogViewerPresentation.ClearMessage(1200));
        Assert.Equal("日志文件将被删除，此操作不可恢复。", LogViewerPresentation.ClearMessage(0));
        Assert.Equal("清除", LogViewerPresentation.ClearConfirmText);
    }

    [Fact]
    public void 各类提示的文案()
    {
        Assert.Equal("日志已导出到: C:\\x\\a.json", LogViewerPresentation.ExportSuccessMessage("C:\\x\\a.json"));
        Assert.Equal("导出失败: 磁盘已满", LogViewerPresentation.ExportFailedWithReason("磁盘已满"));
        Assert.Equal("清除失败: 被占用", LogViewerPresentation.ClearFailedWithReason("被占用"));
        Assert.Equal("复制失败: 无权限", LogViewerPresentation.CopyFailedWithReason("无权限"));
        Assert.Equal("加载日志失败: 超时", LogViewerPresentation.LoadFailedWithReason("超时"));
    }

    [Fact]
    public void 搜索框的长度上限与分页大小与旧页面一致()
    {
        Assert.Equal(20, LogViewerPresentation.PageSize);
        Assert.Equal(50, LogViewerPresentation.SearchMaxLength);
    }

    // ───────────────────────── 术语 ─────────────────────────

    [Fact]
    public void 界面文案遵守领域术语_不出现被禁用的词()
    {
        var filter = LogFilter.None.WithKeyword("x").WithLevels(new[] { "Error" });
        var entry = Entry(exceptionType: "System.IO.IOException", fullMessage: "同步失败\n   at A.B()");
        var texts = new List<string>
        {
            LogViewerPresentation.SearchPlaceholder, LogViewerPresentation.SearchLabel, LogViewerPresentation.SearchClearLabel,
            LogViewerPresentation.MoreLabel, LogViewerPresentation.UnknownTimeText, LogViewerPresentation.NoMessageText,
            LogViewerPresentation.UnknownSourceText, LogViewerPresentation.LevelDimension, LogViewerPresentation.TimeDimension,
            LogViewerPresentation.TimeRangeLabel, LogViewerPresentation.ClearChipText, LogViewerPresentation.FilterTitle,
            LogViewerPresentation.FilterApplyText, LogViewerPresentation.LoadFailedTitle, LogViewerPresentation.LoadFailedText,
            LogViewerPresentation.DetailTitle, LogViewerPresentation.ClearTitle, LogViewerPresentation.ClearConfirmText,
            LogViewerPresentation.ClearSuccessMessage, LogViewerPresentation.ClearFailedMessage, LogViewerPresentation.ExportFailedMessage,
            LogViewerPresentation.CopySuccessMessage, LogViewerPresentation.ClearMessage(3), LogViewerPresentation.ClearMessage(0),
            LogViewerPresentation.ClearConditionsText(filter), LogViewerPresentation.SearchStatusText(filter, 0, false),
            LogViewerPresentation.CopyText(entry),
        };
        foreach (var f in new[] { LogFilter.None, filter, LogFilter.None.WithKeyword("x") })
        {
            var (_, title, text) = LogViewerPresentation.EmptyState(f);
            texts.Add(title);
            texts.Add(text);
        }

        foreach (var text in texts)
        {
            // 「异常类型」是日志自己的字段名（异常的 .NET 类型），不是「分类」的别称：先去掉它再查「类型」
            var withoutLogField = text.Replace("异常类型", string.Empty, StringComparison.Ordinal);

            foreach (var banned in new[] { "交易", "记录", "流水", "标签", "类型", "支付方式", "付款方式", "账簿", "笔记本", "录入方式" })
            {
                Assert.DoesNotContain(banned, withoutLogField);
            }
        }
    }
}
