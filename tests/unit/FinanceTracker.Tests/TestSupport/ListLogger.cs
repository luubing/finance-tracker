using Microsoft.Extensions.Logging;

namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 把日志写进内存列表的测试用 Logger。
/// 同时记录"格式化后的消息"和"结构化属性"：Serilog 文件日志会把属性单独序列化进 JSON，
/// 所以检查敏感信息是否泄漏时，两处都要看。
/// </summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"))
            : string.Empty;

        Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties, exception));
    }

    /// <summary>一条日志：级别、格式化消息、结构化属性（key=value 拼接）、异常</summary>
    public sealed record LogEntry(LogLevel Level, string Message, string Properties, Exception? Exception);
}
