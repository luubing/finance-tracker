using FinanceTracker.Core.Models;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 云端同步客户端接口（由客户端同步服务调用真实云端 API）
/// </summary>
public interface ICloudSyncClient
{
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
}
