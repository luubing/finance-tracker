using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Tests.TestSupport;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 趋势维度（S11）：外部输入（API 查询参数、页面按钮值）只能经 <see cref="TrendDimensionParser"/> 进入，
/// 服务层只接受枚举，且对未定义的枚举值显式报错而不是静默返回空列表。
/// </summary>
public sealed class TrendDimensionTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await SqliteTestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Theory]
    [InlineData("day", TrendDimension.Day)]
    [InlineData("Day", TrendDimension.Day)]
    [InlineData("WEEK", TrendDimension.Week)]
    [InlineData(" month ", TrendDimension.Month)]
    public void 合法名称_不区分大小写并忽略首尾空白(string input, TrendDimension expected)
    {
        Assert.True(TrendDimensionParser.TryParse(input, out var dimension));
        Assert.Equal(expected, dimension);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("year")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void 非法输入_解析失败_且数字不被当成合法值(string? input)
    {
        Assert.False(TrendDimensionParser.TryParse(input, out _));
    }

    [Fact]
    public async Task 服务对未定义的枚举值_显式抛出_而不是静默返回空列表()
    {
        var service = new StatisticsService(_db.Context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetTrendDataAsync(
            _db.User.Id,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 31),
            (TrendDimension)99));
    }

    [Theory]
    [InlineData(TrendDimension.Day)]
    [InlineData(TrendDimension.Week)]
    [InlineData(TrendDimension.Month)]
    public async Task 三种已定义维度_空数据时都返回空列表(TrendDimension dimension)
    {
        var service = new StatisticsService(_db.Context);

        var result = await service.GetTrendDataAsync(
            _db.User.Id,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 31),
            dimension);

        Assert.Empty(result);
    }
}
