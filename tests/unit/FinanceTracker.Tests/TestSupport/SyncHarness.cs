using System.Security.Claims;
using System.Text.Json;
using FinanceTracker.Api.Controllers;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Services;
using FinanceTracker.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 云端替身：真实的 <see cref="SyncController"/> + SQLite（与生产同一份 EF 模型和迁移）。
/// 每次调用新建一个 DbContext 和控制器实例，等价于"每个 HTTP 请求一个 Scope"；
/// 请求与响应都过一遍 System.Text.Json，保留线上的序列化行为（例如 DateTime.Kind 的往返）。
/// 云端库里的 <see cref="Account"/> 就是"云端账号"，其 Id 即 JWT 里的用户 Id。
/// </summary>
public sealed class FakeCloud : IAsyncDisposable
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteTestDatabase _database;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    private FakeCloud(SqliteTestDatabase database)
    {
        _database = database;
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(database.Connection, b => b.MigrationsAssembly("FinanceTracker.Infrastructure.Sqlite"))
            .Options;
    }

    /// <summary>云端账号（默认登录用户）</summary>
    public User Account => _database.User;

    /// <summary>云端账号的用户 Id（JWT 的 NameIdentifier）</summary>
    public Guid AccountId => Account.Id;

    public static async Task<FakeCloud> CreateAsync(string phoneNumber = "13800138000") =>
        new(await SqliteTestDatabase.CreateAsync(phoneNumber));

    /// <summary>新建一个云端上下文（调用方负责释放），用于断言云端库内容或直接改云端数据</summary>
    public ApplicationDbContext CreateContext() => new(_options);

    /// <summary>
    /// 添加一台设备：独立的本地 SQLite + 真实 SyncService。
    /// 本地用户用与云端账号相同的手机号注册，但 Id 由本地库独立生成，与云端 Id 不同（这正是生产里的情形）。
    /// </summary>
    public async Task<SyncDevice> AddDeviceAsync(Guid? cloudAccountId = null, string? phoneNumber = null)
    {
        var localDatabase = await SqliteTestDatabase.CreateAsync(phoneNumber ?? Account.PhoneNumber);
        return new SyncDevice(localDatabase, new DirectCloudSyncClient(this, cloudAccountId ?? AccountId));
    }

    /// <summary>以指定云端账号的身份调用同步控制器的某个动作</summary>
    internal async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        Guid cloudAccountId,
        TRequest request,
        Func<SyncController, TRequest, Task<IActionResult>> action)
    {
        var wireRequest = RoundTrip(request);

        await using var context = CreateContext();
        var controller = new SyncController(context, new LedgerMemberService(context))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, cloudAccountId.ToString()) }, "test"))
                }
            }
        };

        var result = await action(controller, wireRequest);
        var ok = Assert.IsType<OkObjectResult>(result);
        return RoundTrip((TResponse)ok.Value!);
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, WebOptions), WebOptions)!;

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();
}

/// <summary>
/// 把 ICloudSyncClient 的调用直接转给 <see cref="FakeCloud"/> 里的 SyncController，
/// 代替真实的 HttpCloudSyncClient（省掉 HTTP/JWT，但保留序列化往返）。
/// </summary>
public sealed class DirectCloudSyncClient(FakeCloud cloud, Guid cloudAccountId) : ICloudSyncClient
{
    public Task<Guid?> GetCloudUserIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(cloudAccountId);

    public Task<SyncPushResponse> PushBillsAsync(Guid userId, IReadOnlyList<BillSyncDto> bills, CancellationToken cancellationToken = default) =>
        cloud.InvokeAsync<SyncPushRequest, SyncPushResponse>(cloudAccountId, new SyncPushRequest { Bills = bills.ToList() },
            (controller, request) => controller.Push(request));

    public async Task<List<BillSyncDto>> PullBillsAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        (await cloud.InvokeAsync<SyncPullRequest, SyncPullResponse>(cloudAccountId, new SyncPullRequest { Since = since },
            (controller, request) => controller.Pull(request))).Bills;

    public Task<CategorySyncPushResponse> PushCategoriesAsync(Guid userId, IReadOnlyList<CategorySyncDto> categories, CancellationToken cancellationToken = default) =>
        cloud.InvokeAsync<CategorySyncPushRequest, CategorySyncPushResponse>(cloudAccountId, new CategorySyncPushRequest { Categories = categories.ToList() },
            (controller, request) => controller.PushCategories(request));

    public async Task<List<CategorySyncDto>> PullCategoriesAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        (await cloud.InvokeAsync<CategorySyncPullRequest, CategorySyncPullResponse>(cloudAccountId, new CategorySyncPullRequest { Since = since },
            (controller, request) => controller.PullCategories(request))).Categories;

    public Task<PaymentChannelSyncPushResponse> PushPaymentChannelsAsync(Guid userId, IReadOnlyList<PaymentChannelSyncDto> channels, CancellationToken cancellationToken = default) =>
        cloud.InvokeAsync<PaymentChannelSyncPushRequest, PaymentChannelSyncPushResponse>(cloudAccountId, new PaymentChannelSyncPushRequest { PaymentChannels = channels.ToList() },
            (controller, request) => controller.PushPaymentChannels(request));

    public async Task<List<PaymentChannelSyncDto>> PullPaymentChannelsAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        (await cloud.InvokeAsync<PaymentChannelSyncPullRequest, PaymentChannelSyncPullResponse>(cloudAccountId, new PaymentChannelSyncPullRequest { Since = since },
            (controller, request) => controller.PullPaymentChannels(request))).PaymentChannels;

    public Task<LedgerSyncPushResponse> PushLedgersAsync(Guid userId, IReadOnlyList<LedgerSyncDto> ledgers, CancellationToken cancellationToken = default) =>
        cloud.InvokeAsync<LedgerSyncPushRequest, LedgerSyncPushResponse>(cloudAccountId, new LedgerSyncPushRequest { Ledgers = ledgers.ToList() },
            (controller, request) => controller.PushLedgers(request));

    public async Task<List<LedgerSyncDto>> PullLedgersAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        (await cloud.InvokeAsync<LedgerSyncPullRequest, LedgerSyncPullResponse>(cloudAccountId, new LedgerSyncPullRequest { Since = since },
            (controller, request) => controller.PullLedgers(request))).Ledgers;

    public async Task<List<LedgerMemberSyncDto>> PullLedgerMembersAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        (await cloud.InvokeAsync<LedgerMemberSyncPullRequest, LedgerMemberSyncPullResponse>(cloudAccountId, new LedgerMemberSyncPullRequest { Since = since },
            (controller, request) => controller.PullLedgerMembers(request))).Members;
}

/// <summary>
/// 装饰器：账单拉取永远返回空（其余原样转发）。
/// 用来单独验证"推送被云端版本覆盖"这条路径——正常流程里紧随其后的整批拉取会把同一笔账单再合并一遍，
/// 掩盖推送路径自己有没有做对（增量拉取以后只会拉到比游标更新的账单，掩盖就不复存在了）。
/// </summary>
public sealed class PullBillsSuppressedClient(ICloudSyncClient inner) : ICloudSyncClient
{
    public Task<Guid?> GetCloudUserIdAsync(CancellationToken cancellationToken = default) =>
        inner.GetCloudUserIdAsync(cancellationToken);

    public Task<SyncPushResponse> PushBillsAsync(Guid userId, IReadOnlyList<BillSyncDto> bills, CancellationToken cancellationToken = default) =>
        inner.PushBillsAsync(userId, bills, cancellationToken);

    public Task<List<BillSyncDto>> PullBillsAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<BillSyncDto>());

    public Task<CategorySyncPushResponse> PushCategoriesAsync(Guid userId, IReadOnlyList<CategorySyncDto> categories, CancellationToken cancellationToken = default) =>
        inner.PushCategoriesAsync(userId, categories, cancellationToken);

    public Task<List<CategorySyncDto>> PullCategoriesAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        inner.PullCategoriesAsync(userId, since, cancellationToken);

    public Task<PaymentChannelSyncPushResponse> PushPaymentChannelsAsync(Guid userId, IReadOnlyList<PaymentChannelSyncDto> channels, CancellationToken cancellationToken = default) =>
        inner.PushPaymentChannelsAsync(userId, channels, cancellationToken);

    public Task<List<PaymentChannelSyncDto>> PullPaymentChannelsAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        inner.PullPaymentChannelsAsync(userId, since, cancellationToken);

    public Task<LedgerSyncPushResponse> PushLedgersAsync(Guid userId, IReadOnlyList<LedgerSyncDto> ledgers, CancellationToken cancellationToken = default) =>
        inner.PushLedgersAsync(userId, ledgers, cancellationToken);

    public Task<List<LedgerSyncDto>> PullLedgersAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        inner.PullLedgersAsync(userId, since, cancellationToken);

    public Task<List<LedgerMemberSyncDto>> PullLedgerMembersAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default) =>
        inner.PullLedgerMembersAsync(userId, since, cancellationToken);
}

/// <summary>
/// 一台设备：独立的本地 SQLite（真实迁移 + 预设数据）+ 真实的业务服务与 <see cref="SyncService"/>。
/// </summary>
public sealed class SyncDevice : IAsyncDisposable
{
    private readonly SqliteTestDatabase _database;

    internal SyncDevice(SqliteTestDatabase database, ICloudSyncClient cloudClient)
    {
        _database = database;
        Sync = new SyncService(database.Context, new AlwaysOnlineNetworkService(), cloudClient, NullLogger<SyncService>.Instance);
        Bills = new BillService(database.Context, NullLogger<BillService>.Instance);
        Ledgers = new LedgerService(database.Context, NullLogger<LedgerService>.Instance);
        Categories = new CategoryService(database.Context, NullLogger<CategoryService>.Instance);
        Members = new LedgerMemberService(database.Context);
    }

    /// <summary>本地库（含 Context / Connection）</summary>
    public SqliteTestDatabase Db => _database;

    /// <summary>本地用户 Id（与云端用户 Id 不同）</summary>
    public Guid UserId => _database.User.Id;

    public SyncService Sync { get; }
    public BillService Bills { get; }
    public LedgerService Ledgers { get; }
    public CategoryService Categories { get; }
    public LedgerMemberService Members { get; }

    /// <summary>录一笔账（支出，预设分类与渠道）</summary>
    public Task<Bill> AddBillAsync(decimal amount = 10m, Guid? ledgerId = null) =>
        Bills.CreateBillAsync(new Bill
        {
            UserId = UserId,
            Amount = amount,
            Type = BillType.Expense,
            CategoryId = _database.ExpenseCategory.Id,
            PaymentChannelId = _database.Channel.Id,
            LedgerId = ledgerId,
            TransactionTime = new DateTime(2026, 3, 1, 12, 0, 0),
            Source = BillSource.Manual
        });

    /// <summary>建一个账本</summary>
    public Task<Ledger> AddLedgerAsync(string name = "家庭") =>
        Ledgers.CreateLedgerAsync(new Ledger { UserId = UserId, Name = name, Icon = "mdi-home" });

    /// <summary>
    /// 绕过 SaveChanges 的自动盖章，直接把本地账单的更新时间改成指定值
    /// （用来构造"这笔账单的编辑发生在很久以前"）。
    /// </summary>
    public async Task SetBillUpdatedAtAsync(Guid billId, DateTime updatedAtUtc)
    {
        await _database.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Bills SET UpdatedAt = {updatedAtUtc} WHERE Id = {billId}");
        _database.Context.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();
}

/// <summary>测试用网络服务：始终在线</summary>
internal sealed class AlwaysOnlineNetworkService : INetworkService
{
    public event EventHandler<bool>? ConnectivityChanged
    {
        add { }
        remove { }
    }

    public bool IsConnected() => true;
}
