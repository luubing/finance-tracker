using FinanceTracker.Core.Models;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 云端同步客户端接口（由客户端同步服务调用真实云端 API）
/// </summary>
public interface ICloudSyncClient
{
    /// <summary>
    /// 云端把当前登录用户识别为哪个用户 Id（取自云端 JWT 的用户声明）。
    /// 本地库与云端库各自独立生成用户 Id，二者通常不同；拉取下来的账单/账本/成员关系带的是云端 Id，
    /// 同步服务需要据此把"云端的我"映射回本地用户，否则这些数据会挂在一个存根用户名下、本地看不到。
    /// 拿不到（未登录、离线、token 无法解析）时返回 null，此时不做映射。
    /// </summary>
    Task<Guid?> GetCloudUserIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量推送账单到云端（服务端按 UpdatedAt 做“后写入优先”冲突裁决，返回权威版本）
    /// </summary>
    Task<SyncPushResponse> PushBillsAsync(Guid userId, IReadOnlyList<BillSyncDto> bills, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拉取云端账单更新（since 为空则拉取全部）
    /// </summary>
    Task<List<BillSyncDto>> PullBillsAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量推送自定义分类到云端（服务端按 UpdatedAt 做“后写入优先”裁决）
    /// </summary>
    Task<CategorySyncPushResponse> PushCategoriesAsync(Guid userId, IReadOnlyList<CategorySyncDto> categories, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拉取云端自定义分类（since 为空则拉取全部，含软删除）
    /// </summary>
    Task<List<CategorySyncDto>> PullCategoriesAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量推送自定义支付渠道到云端（服务端按 UpdatedAt 做“后写入优先”裁决）
    /// </summary>
    Task<PaymentChannelSyncPushResponse> PushPaymentChannelsAsync(Guid userId, IReadOnlyList<PaymentChannelSyncDto> channels, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拉取云端自定义支付渠道（since 为空则拉取全部，含软删除）
    /// </summary>
    Task<List<PaymentChannelSyncDto>> PullPaymentChannelsAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量推送账本到云端（服务端按 UpdatedAt 做"后写入优先"裁决）
    /// </summary>
    Task<LedgerSyncPushResponse> PushLedgersAsync(Guid userId, IReadOnlyList<LedgerSyncDto> ledgers, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拉取云端账本（since 为空则拉取全部，含软删除）
    /// </summary>
    Task<List<LedgerSyncDto>> PullLedgersAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拉取账本成员关系（客户端缓存，用于本地账单可见性判定与"我的共享账本"展示）
    /// </summary>
    Task<List<LedgerMemberSyncDto>> PullLedgerMembersAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default);
}
