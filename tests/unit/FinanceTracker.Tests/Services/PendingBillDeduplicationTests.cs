using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 待确认账单的去重规则（来源 + 金额 + 交易时间 ±1 分钟）：同一个条件既交给数据库查询（<c>PendingBillService.AddAsync</c>），
/// 也在内存里判断（短信识别页在列表已满时）——两边必须得出同样的结论。
/// </summary>
public sealed class PendingBillDeduplicationTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 25, 12, 0, 0);

    private SqliteConnection _connection = null!;
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, b => b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite"))
            .Options;

        await using var context = new ApplicationDbContext(_options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static PendingBill Bill(decimal amount, DateTime time, BillSource source = BillSource.SmsRecognition) => new()
    {
        Amount = amount,
        Type = BillType.Expense,
        Channel = "微信支付",
        Note = "去重",
        TransactionTime = time,
        Source = source,
        CapturedAt = time,
    };

    [Fact]
    public async Task 数据库判断与内存判断一致_来源和金额相同且时间相差不超过一分钟才算同一笔()
    {
        var existing = Bill(7.00m, T0);
        await using (var seed = new ApplicationDbContext(_options))
        {
            seed.PendingBills.Add(existing);
            await seed.SaveChangesAsync();
        }

        var cases = new (string Name, PendingBill Candidate, bool Expected)[]
        {
            ("完全相同", Bill(7.00m, T0), true),
            ("晚 30 秒", Bill(7.00m, T0.AddSeconds(30)), true),
            ("早 30 秒", Bill(7.00m, T0.AddSeconds(-30)), true),
            ("正好晚 1 分钟（含边界）", Bill(7.00m, T0.AddMinutes(1)), true),
            ("正好早 1 分钟（含边界）", Bill(7.00m, T0.AddMinutes(-1)), true),
            ("晚 61 秒", Bill(7.00m, T0.AddSeconds(61)), false),
            ("早 61 秒", Bill(7.00m, T0.AddSeconds(-61)), false),
            ("金额不同", Bill(7.01m, T0), false),
            ("来源不同（通知栏与短信各算各的）", Bill(7.00m, T0, BillSource.Notification), false),
        };

        await using var db = new ApplicationDbContext(_options);
        foreach (var (name, candidate, expected) in cases)
        {
            var inDatabase = await db.PendingBills.AsNoTracking().AnyAsync(PendingBillDeduplication.IsDuplicateOf(candidate));
            var inMemory = PendingBillDeduplication.ContainsDuplicate(new[] { existing }, candidate);

            Assert.True(expected == inDatabase, $"{name}：数据库判断应为 {expected}，实际 {inDatabase}");
            Assert.True(expected == inMemory, $"{name}：内存判断应为 {expected}，实际 {inMemory}");
        }
    }

    [Fact]
    public async Task 金额在库里是文本_去重的相等比较按文本来_E2E造数据要写成同一种写法()
    {
        // Microsoft.Data.Sqlite 把 decimal 存成 "0.0###…" 的文本：至少一位小数、去掉多余的尾零。
        // 数据库里按金额 `=` 比较（AddAsync 去重）比的就是这段文本，所以 "7" / "7.00" 与应用写入的 "7.0" 不相等——
        // tests/e2e 的 to_db_decimal 照这个写法造待确认账单，这里把假设锁住。
        await using (var seed = new ApplicationDbContext(_options))
        {
            seed.PendingBills.AddRange(Bill(7.00m, T0), Bill(8.5m, T0.AddHours(1)), Bill(12345.5m, T0.AddHours(2)), Bill(35m, T0.AddHours(3)));
            await seed.SaveChangesAsync();
        }

        var stored = new List<string>();
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT Amount FROM PendingBills ORDER BY TransactionTime";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                stored.Add(reader.GetString(0));
            }
        }

        Assert.Equal(new[] { "7.0", "8.5", "12345.5", "35.0" }, stored);
    }

    [Fact]
    public void 列表里什么都没有时不算重复()
    {
        Assert.False(PendingBillDeduplication.ContainsDuplicate(Array.Empty<PendingBill>(), Bill(7m, T0)));
    }

    [Fact]
    public void 参数为空时抛异常()
    {
        Assert.Throws<ArgumentNullException>(() => PendingBillDeduplication.IsDuplicateOf(null!));
        Assert.Throws<ArgumentNullException>(() => PendingBillDeduplication.ContainsDuplicate(null!, Bill(7m, T0)));
    }
}
