using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 测试用 CSV 解析器：不解析任何文本，直接返回预设好的账单列表。
/// 导入接口的测试关心的是"解析之后怎么入库"（时区、账本……），而不是 CSV 格式本身。
/// </summary>
internal sealed class StubCsvParser : ICsvParserService
{
    public List<ImportedBill> Bills { get; init; } = new();

    public Task<List<ImportedBill>> ParseWeChatCsvAsync(string csvContent) => Task.FromResult(Bills);

    public Task<CsvParseResult> ParseWeChatCsvWithStatsAsync(string csvContent) =>
        Task.FromResult(new CsvParseResult { Bills = Bills });

    public Task<List<ImportedBill>> ParseAlipayCsvAsync(string csvContent) => Task.FromResult(Bills);
}
