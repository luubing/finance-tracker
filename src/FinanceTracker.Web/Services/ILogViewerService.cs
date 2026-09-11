namespace FinanceTracker.Web.Services;

/// <summary>
/// 日志查看服务接口
/// </summary>
public interface ILogViewerService
{
    /// <summary>
    /// 获取日志列表
    /// </summary>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="searchText">搜索文本（可选）</param>
    /// <param name="page">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <param name="levels">日志级别筛选（可选）</param>
    /// <returns>日志列表和总数</returns>
    Task<(List<LogEntry> Logs, int TotalCount)> GetLogsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? searchText = null,
        int page = 1,
        int pageSize = 20,
        List<string>? levels = null);

    /// <summary>
    /// 获取日志详情
    /// </summary>
    /// <param name="logId">日志ID</param>
    /// <returns>日志详情</returns>
    Task<LogEntry?> GetLogDetailAsync(string logId);

    /// <summary>
    /// 清除所有日志
    /// </summary>
    /// <returns>是否成功</returns>
    Task<bool> ClearLogsAsync();

    /// <summary>
    /// 导出日志到文件
    /// </summary>
    /// <param name="filePath">导出文件路径</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>是否成功</returns>
    Task<bool> ExportLogsAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// 获取日志统计信息
    /// </summary>
    /// <returns>日志统计</returns>
    Task<LogStatistics> GetStatisticsAsync();
}

/// <summary>
/// 日志条目
/// </summary>
public class LogEntry
{
    /// <summary>
    /// 日志ID（基于时间戳和行号生成）
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 日志级别
    /// </summary>
    public string Level { get; set; } = "Error";

    /// <summary>
    /// 异常类型
    /// </summary>
    public string ExceptionType { get; set; } = string.Empty;

    /// <summary>
    /// 错误消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 完整错误信息（包含堆栈跟踪）
    /// </summary>
    public string FullMessage { get; set; } = string.Empty;

    /// <summary>
    /// 来源（服务名称、类名等）
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 属性字典（包含上下文信息）
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>
    /// 是否显示详情
    /// </summary>
    public bool ShowDetail { get; set; }

    /// <summary>
    /// 日志文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;
}

/// <summary>
/// 日志统计信息
/// </summary>
public class LogStatistics
{
    /// <summary>
    /// 总日志数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 今日日志数量
    /// </summary>
    public int TodayCount { get; set; }

    /// <summary>
    /// 本周日志数量
    /// </summary>
    public int WeekCount { get; set; }

    /// <summary>
    /// 日志文件总大小（字节）
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// 最早的日志时间
    /// </summary>
    public DateTime? EarliestLog { get; set; }

    /// <summary>
    /// 最新的日志时间
    /// </summary>
    public DateTime? LatestLog { get; set; }

    /// <summary>
    /// 按异常类型分组的统计
    /// </summary>
    public Dictionary<string, int> ExceptionTypeCounts { get; set; } = new();

    /// <summary>
    /// 按来源分组的统计
    /// </summary>
    public Dictionary<string, int> SourceCounts { get; set; } = new();

    /// <summary>
    /// 按日志级别分组的统计
    /// </summary>
    public Dictionary<string, int> LevelCounts { get; set; } = new();
}