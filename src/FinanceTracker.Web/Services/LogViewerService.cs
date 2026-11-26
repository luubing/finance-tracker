using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 日志查看服务实现
/// </summary>
public class LogViewerService : ILogViewerService
{
    private readonly ILogger<LogViewerService> _logger;
    private readonly string _logDirectory;

    /// <param name="logger">本服务自己的日志</param>
    /// <param name="logDirectory">
    /// Serilog 写日志文件的目录。
    /// MAUI 宿主必须传它写日志用的目录（<c>FileSystem.AppDataDirectory/Logs</c>）：本类编译在不引用 MAUI 的类库里，
    /// 自己算不出那个目录——早先用 <c>#if ANDROID || IOS</c> 取 <c>FileSystem</c> 的分支在这个类库里永远不会编译，
    /// 真机上读的是另一个目录，日志页永远是空的。
    /// 不传时用桌面默认目录（Web.Server 的开发场景；E2E 也传一个专用目录，「清除日志」才不会碰到开发机上真实的日志文件）。
    /// </param>
    public LogViewerService(ILogger<LogViewerService> logger, string? logDirectory = null)
    {
        _logger = logger;
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? DefaultLogDirectory() : logDirectory;
    }

    /// <summary>桌面默认日志目录：%LOCALAPPDATA%\FinanceTracker\Logs</summary>
    private static string DefaultLogDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FinanceTracker", "Logs");

    /// <summary>
    /// 获取日志列表。日志文件读不出来时直接抛出异常（不吞成空列表）：
    /// 调用方（错误日志页）靠它显示「加载失败 + 重试」，吞掉的话用户会把读取失败当成「暂无错误日志」。
    /// </summary>
    public async Task<(List<LogEntry> Logs, int TotalCount)> GetLogsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? searchText = null,
        int page = 1,
        int pageSize = 20,
        List<string>? levels = null)
    {
        var allLogs = await ReadAllLogsAsync();

        // 应用筛选条件
        var filteredLogs = allLogs.AsQueryable();

        if (startDate.HasValue)
        {
            filteredLogs = filteredLogs.Where(l => l.Timestamp >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            filteredLogs = filteredLogs.Where(l => l.Timestamp <= endDate.Value.AddDays(1).AddTicks(-1));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var searchLower = searchText.ToLower();
            filteredLogs = filteredLogs.Where(l =>
                l.Message.ToLower().Contains(searchLower) ||
                l.ExceptionType.ToLower().Contains(searchLower) ||
                l.Source.ToLower().Contains(searchLower) ||
                l.FullMessage.ToLower().Contains(searchLower));
        }

        // 按日志级别筛选
        if (levels != null && levels.Count > 0)
        {
            filteredLogs = filteredLogs.Where(l => levels.Contains(l.Level));
        }

        // 按时间倒序排序
        var sortedLogs = filteredLogs.OrderByDescending(l => l.Timestamp).ToList();

        var totalCount = sortedLogs.Count;

        // 分页
        var pagedLogs = sortedLogs
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (pagedLogs, totalCount);
    }

    /// <summary>
    /// 获取日志详情
    /// </summary>
    public async Task<LogEntry?> GetLogDetailAsync(string logId)
    {
        try
        {
            var allLogs = await ReadAllLogsAsync();
            return allLogs.FirstOrDefault(l => l.Id == logId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取日志详情失败");
            return null;
        }
    }

    /// <summary>
    /// 清除所有日志
    /// </summary>
    public async Task<bool> ClearLogsAsync()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                return true;
            }

            var logFiles = Directory.GetFiles(_logDirectory, "app-*.log");
            foreach (var file in logFiles)
            {
                File.Delete(file);
            }

            _logger.LogInformation("已清除所有日志文件");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清除日志失败");
            return false;
        }
    }

    /// <summary>
    /// 导出日志到文件
    /// </summary>
    public async Task<bool> ExportLogsAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var (logs, _) = await GetLogsAsync(startDate, endDate, pageSize: int.MaxValue);

            var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            // 导出目录可能还不存在（桌面端的缓存目录第一次用）：先建出来，否则第一次导出必然失败
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, json);

            _logger.LogInformation("日志已导出到: {FilePath}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出日志失败");
            return false;
        }
    }

    /// <summary>
    /// 获取日志统计信息。与 <see cref="GetLogsAsync"/> 一样，日志文件读不出来时抛出异常而不是返回全 0 的统计。
    /// </summary>
    public async Task<LogStatistics> GetStatisticsAsync()
    {
        var allLogs = await ReadAllLogsAsync();
        var today = DateTime.Today;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);

        return new LogStatistics
        {
            TotalCount = allLogs.Count,
            TodayCount = allLogs.Count(l => l.Timestamp.Date == today),
            WeekCount = allLogs.Count(l => l.Timestamp >= weekStart),
            TotalSizeBytes = CalculateTotalLogSize(),
            EarliestLog = allLogs.MinBy(l => l.Timestamp)?.Timestamp,
            LatestLog = allLogs.MaxBy(l => l.Timestamp)?.Timestamp,
            ExceptionTypeCounts = allLogs
                .GroupBy(l => l.ExceptionType)
                .ToDictionary(g => g.Key, g => g.Count()),
            SourceCounts = allLogs
                .GroupBy(l => l.Source)
                .ToDictionary(g => g.Key, g => g.Count()),
            LevelCounts = allLogs
                .GroupBy(l => l.Level)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    /// <summary>
    /// 读取所有日志文件并解析
    /// </summary>
    private async Task<List<LogEntry>> ReadAllLogsAsync()
    {
        var logs = new List<LogEntry>();

        if (!Directory.Exists(_logDirectory))
        {
            return logs;
        }

        var logFiles = Directory.GetFiles(_logDirectory, "app-*.log")
            .OrderByDescending(f => f)
            .ToList();

        Exception? firstFailure = null;
        var failedCount = 0;
        foreach (var file in logFiles)
        {
            try
            {
                var fileName = Path.GetFileName(file);
                var lines = await File.ReadAllLinesAsync(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var logEntry = ParseLogLine(line, fileName, i);
                    if (logEntry != null)
                    {
                        logs.Add(logEntry);
                    }
                }
            }
            catch (Exception ex)
            {
                // 个别文件读不出来（例如被占用）：记一条警告，其余文件照常展示
                _logger.LogWarning(ex, "读取日志文件失败: {FileName}", file);
                firstFailure ??= ex;
                failedCount++;
            }
        }

        // 有日志文件却一个都没读出来，这不是「没有日志」而是读取失败：抛出去，让调用方显示失败而不是空状态
        if (failedCount > 0 && failedCount == logFiles.Count)
        {
            throw new IOException($"{logFiles.Count} 个日志文件全部读取失败", firstFailure);
        }

        return logs;
    }

    /// <summary>
    /// 解析单行日志（JSON 格式）
    /// </summary>
    private LogEntry? ParseLogLine(string line, string fileName, int lineIndex)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var timestamp = DateTime.MinValue;
            if (root.TryGetProperty("@t", out var timestampElement))
            {
                DateTime.TryParse(timestampElement.GetString(), out timestamp);
            }

            var level = "Error";
            if (root.TryGetProperty("@l", out var levelElement))
            {
                level = levelElement.GetString() ?? "Error";
            }

            // 属性：除 Serilog 的保留字段（@ 开头）外都是上下文；SourceContext 单独作为「来源」，不放进上下文列表
            var properties = new Dictionary<string, string>();
            var renderable = new Dictionary<string, string>();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.StartsWith("@")) continue;

                // 渲染消息模板时 SourceContext 也是可引用的属性
                renderable[property.Name] = property.Value.ToString();
                if (property.Name == "SourceContext") continue;
                properties[property.Name] = property.Value.ToString();
            }

            // 消息：RenderedCompactJsonFormatter 写好了渲染后的 @m；应用用的 CompactJsonFormatter 只写模板 @mt（属性另列），
            // 此时要自己把属性代入模板，否则「消息」永远是空的
            var message = string.Empty;
            if (root.TryGetProperty("@m", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString() ?? string.Empty;
            }
            else if (root.TryGetProperty("@mt", out var templateElement) && templateElement.ValueKind == JsonValueKind.String)
            {
                message = LogMessageTemplate.Render(templateElement.GetString(), renderable);
            }

            var exceptionType = string.Empty;
            var fullMessage = message;

            if (root.TryGetProperty("@x", out var exceptionElement))
            {
                fullMessage = exceptionElement.GetString() ?? string.Empty;
                exceptionType = ExtractExceptionType(fullMessage);
            }

            var source = string.Empty;
            if (root.TryGetProperty("SourceContext", out var sourceElement))
            {
                source = sourceElement.GetString() ?? string.Empty;
            }

            // 生成唯一ID（使用时间戳+文件名+行号）
            var id = $"{timestamp:yyyyMMddHHmmssfff}_{fileName}_{lineIndex}";

            return new LogEntry
            {
                Id = id,
                Timestamp = timestamp,
                Level = level,
                ExceptionType = exceptionType,
                Message = message,
                FullMessage = fullMessage,
                Source = source,
                Properties = properties,
                FileName = fileName
            };
        }
        catch
        {
            // 解析失败，跳过该行
            return null;
        }
    }

    /// <summary>
    /// 从完整异常信息中提取异常类型
    /// </summary>
    private string ExtractExceptionType(string fullMessage)
    {
        if (string.IsNullOrWhiteSpace(fullMessage))
            return "UnknownException";

        // 尝试提取 "System.xxx.xxxException" 格式的异常类型
        var lines = fullMessage.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("Exception") || trimmed.Contains("Error"))
            {
                // 尝试提取异常类型
                var parts = trimmed.Split(':');
                if (parts.Length > 0)
                {
                    var exceptionPart = parts[0].Trim();
                    if (exceptionPart.Contains("."))
                    {
                        return exceptionPart;
                    }
                }
            }
        }

        return "UnknownException";
    }

    /// <summary>
    /// 计算日志文件总大小
    /// </summary>
    private long CalculateTotalLogSize()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                return 0;

            return Directory.GetFiles(_logDirectory, "app-*.log")
                .Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }
}