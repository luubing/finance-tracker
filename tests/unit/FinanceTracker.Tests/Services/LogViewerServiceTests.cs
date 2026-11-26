using System.Text.Json;
using FinanceTracker.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 错误日志的读取：应用写日志用的是 Serilog 的 <c>CompactJsonFormatter</c>（只写模板 <c>@mt</c>、不写渲染后的 <c>@m</c>），
/// 测试里的日志行按它真实的样子构造——早先的实现只认 <c>@m</c>，真机上每条日志的消息都是空的。
/// 每个用例用自己的临时目录（服务可以指定日志目录），不碰开发机上真实的日志文件。
/// </summary>
public sealed class LogViewerServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ft_logviewer_tests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private LogViewerService CreateService() => new(NullLogger<LogViewerService>.Instance, _directory);

    /// <summary>一行紧凑 JSON 日志；时间戳是 UTC（与 Serilog 一致）</summary>
    private static string Line(
        DateTime localTime,
        string? template = null,
        string? level = "Error",
        string? exception = null,
        string? source = null,
        string? renderedMessage = null,
        IDictionary<string, object?>? properties = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["@t"] = DateTime.SpecifyKind(localTime, DateTimeKind.Local).ToUniversalTime().ToString("O"),
        };

        if (template is not null) fields["@mt"] = template;
        if (renderedMessage is not null) fields["@m"] = renderedMessage;
        if (level is not null) fields["@l"] = level;
        if (exception is not null) fields["@x"] = exception;
        if (source is not null) fields["SourceContext"] = source;
        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                fields[key] = value;
            }
        }

        return JsonSerializer.Serialize(fields);
    }

    private void WriteLog(string fileName, params string[] lines)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllLines(Path.Combine(_directory, fileName), lines);
    }

    private static DateTime Noon(DateTime day) => day.Date.AddHours(12);

    // ---- 消息 ----

    [Fact]
    public async Task 紧凑格式只有模板时_消息由属性代入模板渲染()
    {
        WriteLog("app-20260101.log", Line(
            Noon(new DateTime(2026, 1, 1)),
            template: "注册/登录失败，手机号: {PhoneNumber}",
            properties: new Dictionary<string, object?> { ["PhoneNumber"] = "138****5678" }));

        var (logs, total) = await CreateService().GetLogsAsync();

        Assert.Equal(1, total);
        Assert.Equal("注册/登录失败，手机号: 138****5678", logs[0].Message);
    }

    [Fact]
    public async Task 同时有渲染后的消息时_直接用它()
    {
        WriteLog("app-20260101.log", Line(
            Noon(new DateTime(2026, 1, 1)),
            template: "模板 {X}",
            renderedMessage: "渲染好的消息",
            properties: new Dictionary<string, object?> { ["X"] = "不应出现" }));

        var (logs, _) = await CreateService().GetLogsAsync();

        Assert.Equal("渲染好的消息", logs[0].Message);
    }

    [Fact]
    public async Task 非字符串的属性值按JSON文本代入()
    {
        WriteLog("app-20260101.log", Line(
            Noon(new DateTime(2026, 1, 1)),
            template: "同步 {Count} 条，成功 {Ok}",
            properties: new Dictionary<string, object?> { ["Count"] = 12, ["Ok"] = true }));

        var (logs, _) = await CreateService().GetLogsAsync();

        Assert.Equal("同步 12 条，成功 True", logs[0].Message);
    }

    // ---- 异常 / 来源 / 属性 ----

    [Fact]
    public async Task 有异常时_完整信息是异常文本_异常类型取第一行的类型名()
    {
        const string exception = "System.InvalidOperationException: 数据库被锁定\n   at FinanceTracker.Core.Services.AuthService.RegisterOrLoginAsync()";
        WriteLog("app-20260101.log", Line(Noon(new DateTime(2026, 1, 1)), template: "注册失败", exception: exception));

        var (logs, _) = await CreateService().GetLogsAsync();

        Assert.Equal("注册失败", logs[0].Message);
        Assert.Equal(exception, logs[0].FullMessage);
        Assert.Equal("System.InvalidOperationException", logs[0].ExceptionType);
    }

    [Fact]
    public async Task 没有异常时_完整信息等于消息_异常类型为空()
    {
        WriteLog("app-20260101.log", Line(Noon(new DateTime(2026, 1, 1)), template: "只是个错误"));

        var (logs, _) = await CreateService().GetLogsAsync();

        Assert.Equal(logs[0].Message, logs[0].FullMessage);
        Assert.Equal(string.Empty, logs[0].ExceptionType);
    }

    [Fact]
    public async Task 来源取SourceContext_上下文属性不含保留字段与来源()
    {
        WriteLog("app-20260101.log", Line(
            Noon(new DateTime(2026, 1, 1)),
            template: "x",
            source: "FinanceTracker.Core.Services.SyncService",
            properties: new Dictionary<string, object?> { ["App"] = "FinanceTracker", ["Platform"] = "Android" }));

        var (logs, _) = await CreateService().GetLogsAsync();

        Assert.Equal("FinanceTracker.Core.Services.SyncService", logs[0].Source);
        Assert.Equal(new[] { "App", "Platform" }, logs[0].Properties.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("Android", logs[0].Properties["Platform"]);
    }

    [Fact]
    public async Task 时间戳按本机时区读出()
    {
        var local = new DateTime(2026, 3, 5, 14, 30, 15, 123);
        WriteLog("app-20260305.log", Line(local, template: "x"));

        var (logs, _) = await CreateService().GetLogsAsync();

        Assert.Equal(local, logs[0].Timestamp);
    }

    [Fact]
    public async Task 损坏的行被跳过_不影响其它行()
    {
        WriteLog("app-20260101.log", "这不是JSON", string.Empty, Line(Noon(new DateTime(2026, 1, 1)), template: "好的"));

        var (logs, total) = await CreateService().GetLogsAsync();

        Assert.Equal(1, total);
        Assert.Equal("好的", logs[0].Message);
    }

    // ---- 筛选 / 排序 / 分页 ----

    [Fact]
    public async Task 按时间倒序_分页取数()
    {
        var lines = Enumerable.Range(1, 5)
            .Select(i => Line(new DateTime(2026, 1, i, 10, 0, 0), template: $"第{i}条"))
            .ToArray();
        WriteLog("app-20260101.log", lines);

        var service = CreateService();
        var (first, total) = await service.GetLogsAsync(page: 1, pageSize: 2);
        var (second, _) = await service.GetLogsAsync(page: 2, pageSize: 2);
        var (third, _) = await service.GetLogsAsync(page: 3, pageSize: 2);

        Assert.Equal(5, total);
        Assert.Equal(new[] { "第5条", "第4条" }, first.Select(l => l.Message).ToArray());
        Assert.Equal(new[] { "第3条", "第2条" }, second.Select(l => l.Message).ToArray());
        Assert.Equal(new[] { "第1条" }, third.Select(l => l.Message).ToArray());
    }

    [Fact]
    public async Task 按级别筛选_可多选()
    {
        WriteLog("app-20260101.log",
            Line(new DateTime(2026, 1, 1, 10, 0, 0), template: "a", level: "Error"),
            Line(new DateTime(2026, 1, 1, 11, 0, 0), template: "b", level: "Fatal"),
            Line(new DateTime(2026, 1, 1, 12, 0, 0), template: "c", level: "Warning"));

        var service = CreateService();
        var (fatal, _) = await service.GetLogsAsync(levels: ["Fatal"]);
        var (both, _) = await service.GetLogsAsync(levels: ["Error", "Fatal"]);

        Assert.Equal(new[] { "b" }, fatal.Select(l => l.Message).ToArray());
        Assert.Equal(new[] { "b", "a" }, both.Select(l => l.Message).ToArray());
    }

    [Fact]
    public async Task 关键字不区分大小写_匹配消息_异常类型_来源与完整信息()
    {
        WriteLog("app-20260101.log",
            Line(new DateTime(2026, 1, 1, 10, 0, 0), template: "Sync Timeout", source: "A.B"),
            Line(new DateTime(2026, 1, 1, 11, 0, 0), template: "x", exception: "System.IO.IOException: 磁盘已满"),
            Line(new DateTime(2026, 1, 1, 12, 0, 0), template: "y", source: "FinanceTracker.Core.Services.AuthService"),
            Line(new DateTime(2026, 1, 1, 13, 0, 0), template: "无关"));

        var service = CreateService();

        Assert.Equal(new[] { "Sync Timeout" }, (await service.GetLogsAsync(searchText: "sync timeout")).Logs.Select(l => l.Message).ToArray());
        Assert.Equal(new[] { "x" }, (await service.GetLogsAsync(searchText: "IOEXCEPTION")).Logs.Select(l => l.Message).ToArray());
        Assert.Equal(new[] { "x" }, (await service.GetLogsAsync(searchText: "磁盘")).Logs.Select(l => l.Message).ToArray());
        Assert.Equal(new[] { "y" }, (await service.GetLogsAsync(searchText: "authservice")).Logs.Select(l => l.Message).ToArray());
    }

    [Fact]
    public async Task 日期范围含起止两天_结束日期取到当天末尾()
    {
        WriteLog("app-20260101.log",
            Line(new DateTime(2026, 1, 1, 23, 59, 59), template: "前一天"),
            Line(new DateTime(2026, 1, 2, 0, 0, 1), template: "起始日"),
            Line(new DateTime(2026, 1, 3, 23, 59, 59), template: "结束日"),
            Line(new DateTime(2026, 1, 4, 0, 0, 1), template: "后一天"));

        var (logs, total) = await CreateService().GetLogsAsync(startDate: new DateTime(2026, 1, 2), endDate: new DateTime(2026, 1, 3));

        Assert.Equal(2, total);
        Assert.Equal(new[] { "结束日", "起始日" }, logs.Select(l => l.Message).ToArray());
    }

    [Fact]
    public async Task 多个日志文件合并读取()
    {
        WriteLog("app-20260101.log", Line(new DateTime(2026, 1, 1, 10, 0, 0), template: "旧文件"));
        WriteLog("app-20260102.log", Line(new DateTime(2026, 1, 2, 10, 0, 0), template: "新文件"));
        File.WriteAllText(Path.Combine(_directory, "other.log"), Line(new DateTime(2026, 1, 3, 10, 0, 0), template: "不该被读到"));

        var (logs, total) = await CreateService().GetLogsAsync();

        Assert.Equal(2, total);
        Assert.Equal(new[] { "新文件", "旧文件" }, logs.Select(l => l.Message).ToArray());
    }

    // ---- 统计 ----

    [Fact]
    public async Task 统计总数_级别分布_时间范围_今日与本周数量()
    {
        var today = DateTime.Today;
        WriteLog("app-20260101.log",
            Line(today.AddHours(6), template: "今天的错误", level: "Error"),
            Line(today.AddDays(-60).AddHours(12), template: "很久以前的致命错误", level: "Fatal"),
            Line(today.AddDays(-61).AddHours(12), template: "更早的错误", level: "Error"));

        var statistics = await CreateService().GetStatisticsAsync();

        Assert.Equal(3, statistics.TotalCount);
        Assert.Equal(1, statistics.TodayCount);
        Assert.Equal(1, statistics.WeekCount);
        Assert.Equal(2, statistics.LevelCounts["Error"]);
        Assert.Equal(1, statistics.LevelCounts["Fatal"]);
        Assert.Equal(today.AddDays(-61).AddHours(12), statistics.EarliestLog);
        Assert.Equal(today.AddHours(6), statistics.LatestLog);
        Assert.True(statistics.TotalSizeBytes > 0);
    }

    [Fact]
    public async Task 日志目录不存在时_列表与统计都是空()
    {
        var service = CreateService();

        var (logs, total) = await service.GetLogsAsync();
        var statistics = await service.GetStatisticsAsync();

        Assert.Empty(logs);
        Assert.Equal(0, total);
        Assert.Equal(0, statistics.TotalCount);
    }

    // ---- 读取失败 ----

    [Fact]
    public async Task 有日志文件但全部读不出来时_抛出异常_不当成没有日志()
    {
        // 独占打开：别的读取者读它会得到共享冲突（页面靠这个异常显示「加载失败 + 重试」，而不是「暂无错误日志」）
        WriteLog("app-20260101.log", Line(new DateTime(2026, 1, 1, 10, 0, 0), template: "a"));
        using var locked = new FileStream(Path.Combine(_directory, "app-20260101.log"), FileMode.Open, FileAccess.Read, FileShare.None);
        var service = CreateService();

        await Assert.ThrowsAsync<IOException>(() => service.GetLogsAsync());
        await Assert.ThrowsAsync<IOException>(() => service.GetStatisticsAsync());
    }

    [Fact]
    public async Task 个别日志文件读不出来时_其余文件照常返回()
    {
        WriteLog("app-20260101.log", Line(new DateTime(2026, 1, 1, 10, 0, 0), template: "读不到的"));
        WriteLog("app-20260102.log", Line(new DateTime(2026, 1, 2, 10, 0, 0), template: "读得到的"));
        using var locked = new FileStream(Path.Combine(_directory, "app-20260101.log"), FileMode.Open, FileAccess.Read, FileShare.None);

        var (logs, total) = await CreateService().GetLogsAsync();

        Assert.Equal(1, total);
        Assert.Equal("读得到的", logs[0].Message);
    }

    // ---- 清除 / 导出 ----

    [Fact]
    public async Task 清除日志只删除app前缀的日志文件()
    {
        WriteLog("app-20260101.log", Line(new DateTime(2026, 1, 1, 10, 0, 0), template: "a"));
        WriteLog("app-20260102.log", Line(new DateTime(2026, 1, 2, 10, 0, 0), template: "b"));
        File.WriteAllText(Path.Combine(_directory, "keep.txt"), "别删我");

        var cleared = await CreateService().ClearLogsAsync();

        Assert.True(cleared);
        Assert.Empty(Directory.GetFiles(_directory, "app-*.log"));
        Assert.True(File.Exists(Path.Combine(_directory, "keep.txt")));
        Assert.Equal(0, (await CreateService().GetLogsAsync()).TotalCount);
    }

    [Fact]
    public async Task 日志目录不存在时清除也算成功()
    {
        Assert.True(await CreateService().ClearLogsAsync());
    }

    [Fact]
    public async Task 导出日志先创建目录_写出JSON数组_按日期范围筛选()
    {
        WriteLog("app-20260101.log",
            Line(new DateTime(2026, 1, 1, 10, 0, 0), template: "范围外"),
            Line(new DateTime(2026, 1, 2, 10, 0, 0), template: "范围内", exception: "System.Exception: 出错了"));
        var target = Path.Combine(_directory, "导出", "新目录", "logs.json");

        var ok = await CreateService().ExportLogsAsync(target, new DateTime(2026, 1, 2), new DateTime(2026, 1, 2));

        Assert.True(ok);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(target));
        var items = document.RootElement.EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("范围内", items[0].GetProperty("Message").GetString());
        Assert.Equal("System.Exception", items[0].GetProperty("ExceptionType").GetString());
    }

    [Fact]
    public async Task 导出失败时返回false_不抛异常()
    {
        // 目标路径的「目录」其实是一个已存在的文件：创建目录必然失败
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "x");

        var ok = await CreateService().ExportLogsAsync(Path.Combine(blocker, "logs.json"));

        Assert.False(ok);
    }
}
