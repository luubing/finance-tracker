using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using FinanceTracker.Infrastructure.Services;
using FinanceTracker.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 短信识别页读到的短信放进「待确认账单」的规则（19 号 · 判断题 4）：
/// 不再直接创建正式账单；识别出金额的进待确认列表（与实时短信监听同一份解析 / 同一个服务 / 同样的去重），没识别出金额的跳过，
/// 批量读取按剩余容量加入、不顶掉用户还没处理的待确认账单。
/// 大部分用例走真实的 <see cref="PendingBillService"/>（SQLite 内存库）：去重、容量规则都是服务的真实行为，不是 fake 的。
/// </summary>
public sealed class SmsCaptureTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 25, 12, 0, 0);

    private SqliteConnection _connection = null!;
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private PendingBillService _pending = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, b => b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite"))
            .Options;

        await using (var context = new ApplicationDbContext(_options))
        {
            await context.Database.MigrateAsync();
        }

        _pending = new PendingBillService(new TestDbContextFactory(_options));
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    private static SmsMessage Sms(long id, string body, DateTime date, string address = "95588") =>
        new() { Id = id, Address = address, Body = body, Date = date };

    /// <summary>一条能识别出金额的支付短信</summary>
    private static SmsMessage Pay(long id, decimal amount, DateTime date) =>
        Sms(id, FormattableString.Invariant($"【招商银行】您尾号1234的储蓄卡支出{amount:0.00}元，请注意资金安全"), date);

    private const string CodeBody = "您的验证码是123456，5分钟内有效";

    /// <summary>往待确认列表里预先放 count 条别的来源（通知栏）的待确认账单：金额各不相同，时间间隔足够远，互不去重</summary>
    private async Task PrefillAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var added = await _pending.AddAsync(new PendingBill
            {
                Amount = 1000 + i,
                Type = BillType.Expense,
                Channel = "微信支付",
                Note = $"已有{i}",
                TransactionTime = T0.AddDays(-100).AddMinutes(i * 10),
                Source = BillSource.Notification,
                CapturedAt = T0.AddDays(-100).AddMinutes(i * 10),
            });
            Assert.True(added);
        }
    }

    // ---------------------------------------------------------------- 一条短信 → 一条待确认账单

    [Fact]
    public void 支出短信转成待确认账单_金额类型渠道原文时间和来源都按短信来()
    {
        var sms = Sms(7, "微信支付成功，消费35.50元", new DateTime(2026, 11, 25, 12, 8, 59), "106905");
        var parsed = PaymentContentParser.Parse(sms.Body);

        var bill = SmsCapture.ToPendingBill(sms, parsed);

        Assert.Equal(35.50m, bill.Amount);
        Assert.Equal(BillType.Expense, bill.Type);
        Assert.Equal("微信支付", bill.Channel);
        Assert.Equal(sms.Body, bill.Note);
        Assert.Equal(BillSource.SmsRecognition, bill.Source);
        Assert.Equal(new DateTime(2026, 11, 25, 12, 8, 59), bill.TransactionTime);
        Assert.NotEqual(Guid.Empty, bill.Id);
    }

    [Fact]
    public void 收入短信转成待确认账单_类型是收入()
    {
        var sms = Sms(8, "您尾号1234的账户到账5000元", T0);

        var bill = SmsCapture.ToPendingBill(sms, PaymentContentParser.Parse(sms.Body));

        Assert.Equal(5000m, bill.Amount);
        Assert.Equal(BillType.Income, bill.Type);
    }

    [Fact]
    public void 捕获时间取短信时间_不取读取的此刻_待确认列表仍按付款先后排()
    {
        var sms = Pay(9, 12m, T0.AddDays(-20));

        var bill = SmsCapture.ToPendingBill(sms, PaymentContentParser.Parse(sms.Body));

        Assert.Equal(sms.Date, bill.CapturedAt);
        Assert.Equal(sms.Date, bill.TransactionTime);
    }

    // ---------------------------------------------------------------- 读取结果

    [Fact]
    public async Task 识别出金额的加入待确认列表_没识别出金额的跳过_结果顺序与读到的一致()
    {
        var messages = new List<SmsMessage>
        {
            Pay(1, 35.5m, T0.AddMinutes(30)),
            Sms(2, CodeBody, T0.AddMinutes(20)),
            Pay(3, 12m, T0.AddMinutes(10)),
        };

        var result = await SmsCapture.CaptureAsync(_pending, messages);

        Assert.Equal(
            new[] { SmsCaptureOutcome.Added, SmsCaptureOutcome.NoAmount, SmsCaptureOutcome.Added },
            result.Items.Select(i => i.Outcome).ToArray());
        Assert.Equal(new long[] { 1, 2, 3 }, result.Items.Select(i => i.Sms.Id).ToArray());
        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.NoAmount);
        Assert.Equal(0, result.Duplicate);

        var stored = await _pending.GetPendingBillsAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, b => Assert.Equal(BillSource.SmsRecognition, b.Source));
        Assert.DoesNotContain(stored, b => b.Note == CodeBody);
        // 待确认列表按捕获时间（= 短信时间）倒序：最近的付款在最上面
        Assert.Equal(new[] { 35.5m, 12m }, stored.Select(b => b.Amount).ToArray());
    }

    [Fact]
    public async Task 没有短信时是空结果_没有有金额的短信时不碰待确认列表()
    {
        var empty = await SmsCapture.CaptureAsync(_pending, Array.Empty<SmsMessage>());
        Assert.Empty(empty.Items);

        var fake = new ScriptedPendingBillService { ThrowOnGet = true };
        var onlyCodes = await SmsCapture.CaptureAsync(fake, new List<SmsMessage> { Sms(1, CodeBody, T0) });

        Assert.Equal(SmsCaptureOutcome.NoAmount, Assert.Single(onlyCodes.Items).Outcome);
        Assert.Equal(0, fake.GetCalls);
        Assert.Equal(0, fake.AddCalls);
        Assert.Empty(await _pending.GetPendingBillsAsync());
    }

    // ---------------------------------------------------------------- 去重

    [Fact]
    public async Task 重复读取同一批短信_第二次全是已在待确认列表_列表不增加()
    {
        var messages = new List<SmsMessage> { Pay(1, 35.5m, T0.AddMinutes(30)), Pay(2, 12m, T0.AddMinutes(10)) };

        var first = await SmsCapture.CaptureAsync(_pending, messages);
        var second = await SmsCapture.CaptureAsync(_pending, messages);

        Assert.Equal(2, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, second.Duplicate);
        Assert.All(second.Items, i => Assert.Equal(SmsCaptureOutcome.Duplicate, i.Outcome));
        Assert.Equal(2, (await _pending.GetPendingBillsAsync()).Count);
    }

    [Fact]
    public async Task 去重沿用待确认账单服务的规则_一分钟内同金额算同一笔_更久之后算另一笔()
    {
        var messages = new List<SmsMessage>
        {
            Pay(1, 20m, T0),
            Pay(2, 20m, T0.AddSeconds(40)),
            Pay(3, 20m, T0.AddMinutes(5)),
            Pay(4, 21m, T0.AddSeconds(40)),
        };

        var result = await SmsCapture.CaptureAsync(_pending, messages);

        // 由新到旧处理：#3（5 分钟后）先加入；#2 与已加入的 #3 相隔 4 分多钟，算另一笔，加入；
        // #1 与已加入的 #2 同金额、相差 40 秒，去重；金额不同的 #4 照常加入
        Assert.Equal(SmsCaptureOutcome.Added, result.Items[2].Outcome);
        Assert.Equal(SmsCaptureOutcome.Added, result.Items[1].Outcome);
        Assert.Equal(SmsCaptureOutcome.Duplicate, result.Items[0].Outcome);
        Assert.Equal(SmsCaptureOutcome.Added, result.Items[3].Outcome);
        Assert.Equal(3, (await _pending.GetPendingBillsAsync()).Count);
    }

    [Fact]
    public async Task 实时捕获过的同一笔短信_读取时不再重复加入()
    {
        // 实时短信监听（SmsBroadcastReceiver）加入时的样子：来源 = 短信识别、备注 = 原文、交易时间 = 收到短信的时刻（与短信时间差不到一分钟）
        var sms = Pay(1, 35.5m, T0);
        await _pending.AddAsync(new PendingBill
        {
            Amount = 35.5m,
            Type = BillType.Expense,
            Channel = "银行卡",
            Note = sms.Body,
            TransactionTime = T0.AddSeconds(20),
            Source = BillSource.SmsRecognition,
            CapturedAt = T0.AddSeconds(20),
        });

        var result = await SmsCapture.CaptureAsync(_pending, new List<SmsMessage> { sms });

        Assert.Equal(SmsCaptureOutcome.Duplicate, Assert.Single(result.Items).Outcome);
        Assert.Single(await _pending.GetPendingBillsAsync());
    }

    // ---------------------------------------------------------------- 容量

    [Fact]
    public async Task 容量不够时按剩余容量由新到旧加入_较早的付款是列表已满_已有的待确认账单一条不丢()
    {
        await PrefillAsync(IPendingBillService.MaxCount - 2);
        var existingIds = (await _pending.GetPendingBillsAsync()).Select(b => b.Id).ToHashSet();

        // 故意按由旧到新的顺序传入：加入的取舍只看时间，不看读到的顺序
        var messages = new List<SmsMessage>
        {
            Pay(1, 11m, T0.AddMinutes(10)),
            Pay(2, 22m, T0.AddMinutes(20)),
            Pay(3, 33m, T0.AddMinutes(30)),
            Pay(4, 44m, T0.AddMinutes(40)),
            Pay(5, 55m, T0.AddMinutes(50)),
        };

        var result = await SmsCapture.CaptureAsync(_pending, messages);

        Assert.Equal(
            new[]
            {
                SmsCaptureOutcome.OverCapacity, SmsCaptureOutcome.OverCapacity, SmsCaptureOutcome.OverCapacity,
                SmsCaptureOutcome.Added, SmsCaptureOutcome.Added,
            },
            result.Items.Select(i => i.Outcome).ToArray());
        Assert.Equal(2, result.Added);
        Assert.Equal(3, result.OverCapacity);

        var stored = await _pending.GetPendingBillsAsync();
        Assert.Equal(IPendingBillService.MaxCount, stored.Count);
        Assert.True(existingIds.IsSubsetOf(stored.Select(b => b.Id).ToHashSet()), "已有的待确认账单不能被这次读取顶掉");
        Assert.Contains(stored, b => b.Amount == 55m);
        Assert.Contains(stored, b => b.Amount == 44m);
        Assert.DoesNotContain(stored, b => b.Amount == 33m);
    }

    [Fact]
    public async Task 列表已经满了_一条都加不进去_已有的一条不丢()
    {
        await PrefillAsync(IPendingBillService.MaxCount);
        var before = (await _pending.GetPendingBillsAsync()).Select(b => b.Id).OrderBy(id => id).ToList();

        var result = await SmsCapture.CaptureAsync(_pending, new List<SmsMessage> { Pay(1, 11m, T0), Pay(2, 22m, T0.AddHours(1)) });

        Assert.All(result.Items, i => Assert.Equal(SmsCaptureOutcome.OverCapacity, i.Outcome));
        var after = (await _pending.GetPendingBillsAsync()).Select(b => b.Id).OrderBy(id => id).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task 已在列表里的重复短信不占容量预算()
    {
        // 剩余容量只有 1：最新的一条是重复的（不占预算），其后最新的那条用掉预算，再往后的是列表已满
        await PrefillAsync(IPendingBillService.MaxCount - 2);
        var repeated = Pay(1, 99m, T0.AddMinutes(50));
        await _pending.AddAsync(SmsCapture.ToPendingBill(repeated, PaymentContentParser.Parse(repeated.Body)));

        var messages = new List<SmsMessage>
        {
            Pay(2, 22m, T0.AddMinutes(20)),
            Pay(3, 33m, T0.AddMinutes(30)),
            repeated,
        };

        var result = await SmsCapture.CaptureAsync(_pending, messages);

        Assert.Equal(SmsCaptureOutcome.OverCapacity, result.Items[0].Outcome);
        Assert.Equal(SmsCaptureOutcome.Added, result.Items[1].Outcome);
        Assert.Equal(SmsCaptureOutcome.Duplicate, result.Items[2].Outcome);
        Assert.Equal(IPendingBillService.MaxCount, (await _pending.GetPendingBillsAsync()).Count);
    }

    [Fact]
    public async Task 列表已满时_已经在列表里的短信仍记为重复_不是列表已满()
    {
        // 列表正好 200 条（含一条读过的短信）：再读时，读过的是「已在待确认列表」，没读过的才是「列表已满」；一条都不加、不顶掉任何一条
        await PrefillAsync(IPendingBillService.MaxCount - 1);
        var repeated = Pay(1, 99m, T0.AddMinutes(50));
        Assert.True(await _pending.AddAsync(SmsCapture.ToPendingBill(repeated, PaymentContentParser.Parse(repeated.Body))));
        var before = (await _pending.GetPendingBillsAsync()).Select(b => b.Id).OrderBy(id => id).ToList();
        Assert.Equal(IPendingBillService.MaxCount, before.Count);

        var result = await SmsCapture.CaptureAsync(_pending, new List<SmsMessage> { Pay(2, 22m, T0.AddMinutes(60)), repeated });

        Assert.Equal(SmsCaptureOutcome.OverCapacity, result.Items[0].Outcome);
        Assert.Equal(SmsCaptureOutcome.Duplicate, result.Items[1].Outcome);
        var after = (await _pending.GetPendingBillsAsync()).Select(b => b.Id).OrderBy(id => id).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task 这一批里额度刚用完_后面与刚加入那条重复的短信也记为重复()
    {
        // 剩余容量 1：#2（最新）用掉额度；#1 与 #2 同金额、相差 20 秒，是同一笔，不是「列表已满」；金额不同的 #3 才是列表已满
        await PrefillAsync(IPendingBillService.MaxCount - 1);

        var result = await SmsCapture.CaptureAsync(_pending, new List<SmsMessage>
        {
            Pay(1, 50m, T0.AddMinutes(30)),
            Pay(2, 50m, T0.AddMinutes(30).AddSeconds(20)),
            Pay(3, 77m, T0.AddMinutes(10)),
        });

        Assert.Equal(SmsCaptureOutcome.Duplicate, result.Items[0].Outcome);
        Assert.Equal(SmsCaptureOutcome.Added, result.Items[1].Outcome);
        Assert.Equal(SmsCaptureOutcome.OverCapacity, result.Items[2].Outcome);
        Assert.Equal(IPendingBillService.MaxCount, (await _pending.GetPendingBillsAsync()).Count);
    }

    // ---------------------------------------------------------------- 出错

    [Fact]
    public async Task 读不到现有条数_整批有金额的短信记为加入失败_不写入_没金额的仍是没识别出金额()
    {
        var fake = new ScriptedPendingBillService { ThrowOnGet = true };

        var result = await SmsCapture.CaptureAsync(fake, new List<SmsMessage>
        {
            Pay(1, 11m, T0),
            Sms(2, CodeBody, T0),
            Pay(3, 33m, T0.AddMinutes(1)),
        });

        Assert.Equal(
            new[] { SmsCaptureOutcome.Failed, SmsCaptureOutcome.NoAmount, SmsCaptureOutcome.Failed },
            result.Items.Select(i => i.Outcome).ToArray());
        Assert.Equal(0, fake.AddCalls);
    }

    [Fact]
    public async Task 单条写入出错记为加入失败_其余照常处理()
    {
        var fake = new ScriptedPendingBillService { ThrowOnAddAmount = 22m };

        var result = await SmsCapture.CaptureAsync(fake, new List<SmsMessage>
        {
            Pay(1, 11m, T0),
            Pay(2, 22m, T0.AddMinutes(10)),
            Pay(3, 33m, T0.AddMinutes(20)),
        });

        Assert.Equal(
            new[] { SmsCaptureOutcome.Added, SmsCaptureOutcome.Failed, SmsCaptureOutcome.Added },
            result.Items.Select(i => i.Outcome).ToArray());
        Assert.Equal(new[] { 33m, 11m }, fake.Added.Select(b => b.Amount).ToArray());
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task 写入出错的短信不占容量预算()
    {
        // 剩余容量 1：最新的一条写入出错（没写进去，不占预算），预算留给下一条
        var fake = new ScriptedPendingBillService { ExistingCount = IPendingBillService.MaxCount - 1, ThrowOnAddAmount = 33m };

        var result = await SmsCapture.CaptureAsync(fake, new List<SmsMessage>
        {
            Pay(1, 11m, T0),
            Pay(2, 22m, T0.AddMinutes(10)),
            Pay(3, 33m, T0.AddMinutes(20)),
        });

        Assert.Equal(SmsCaptureOutcome.Failed, result.Items[2].Outcome);
        Assert.Equal(SmsCaptureOutcome.Added, result.Items[1].Outcome);
        Assert.Equal(SmsCaptureOutcome.OverCapacity, result.Items[0].Outcome);
    }

    /// <summary>脚本化的待确认账单服务：可以让读条数 / 某个金额的写入抛异常，并记录调用，用来验证出错路径</summary>
    private sealed class ScriptedPendingBillService : IPendingBillService
    {
        public bool ThrowOnGet { get; init; }

        public decimal? ThrowOnAddAmount { get; init; }

        public int ExistingCount { get; init; }

        public int GetCalls { get; private set; }

        public int AddCalls { get; private set; }

        public List<PendingBill> Added { get; } = new();

        public event EventHandler? PendingBillsChanged
        {
            add { }
            remove { }
        }

        public Task<List<PendingBill>> GetPendingBillsAsync()
        {
            GetCalls++;
            if (ThrowOnGet)
            {
                throw new InvalidOperationException("读不出来");
            }

            return Task.FromResult(Enumerable.Range(0, ExistingCount).Select(_ => new PendingBill()).ToList());
        }

        public Task<bool> AddAsync(PendingBill pendingBill)
        {
            AddCalls++;
            if (ThrowOnAddAmount == pendingBill.Amount)
            {
                throw new InvalidOperationException("写不进去");
            }

            Added.Add(pendingBill);
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(Guid id) => Task.FromResult(false);

        public Task ClearAsync() => Task.CompletedTask;
    }
}
