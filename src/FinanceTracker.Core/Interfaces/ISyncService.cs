using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Models;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 同步服务接口
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// 同步账单到云端
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>同步结果</returns>
    Task<SyncResult> SyncBillsAsync(Guid userId);

    /// <summary>
    /// 同步自定义分类到云端（双向：推送本地变更 + 拉取云端变更）
    /// </summary>
    Task SyncCategoriesAsync(Guid userId);

    /// <summary>
    /// 同步自定义支付渠道到云端（双向）
    /// </summary>
    Task SyncPaymentChannelsAsync(Guid userId);

    /// <summary>
    /// 同步账本到云端（双向）
    /// </summary>
    Task SyncLedgersAsync(Guid userId);

    /// <summary>
    /// 同步账本成员关系到本地缓存（共享账本可见性判定用）
    /// </summary>
    /// <param name="userId">本地当前用户 ID</param>
    /// <param name="cancellationToken">
    /// 取消令牌，只约束联网的那部分（换取云端令牌 + 拉取成员关系）：取消时请求被真正中止并抛出
    /// <see cref="OperationCanceledException"/>，本地缓存保持不变；拉取完成后的本地合并不会被打断
    /// </param>
    Task SyncLedgerMembersAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把已经拿到手的云端成员关系（例如邀请接口刚返回的那一条）合并到本地缓存。
    /// 与 <see cref="SyncLedgerMembersAsync"/> 共用同一套合并规则：云端用户 Id 映射回本地用户、
    /// 补建用户/账本存根、按 (账本, 用户) 对账、沿用云端的更新时间。
    /// </summary>
    /// <param name="userId">本地当前用户 ID</param>
    /// <param name="members">云端成员关系</param>
    Task MergeLedgerMembersAsync(Guid userId, IReadOnlyList<LedgerMemberSyncDto> members);

    /// <summary>
    /// 获取待同步的账单
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>待同步账单列表</returns>
    Task<List<Bill>> GetPendingBillsAsync(Guid userId);

    /// <summary>
    /// 标记账单为已同步
    /// </summary>
    /// <param name="billIds">账单ID列表</param>
    /// <returns>是否成功</returns>
    Task<bool> MarkBillsAsSyncedAsync(List<Guid> billIds);

    /// <summary>
    /// 标记账单同步失败
    /// </summary>
    /// <param name="billIds">账单ID列表</param>
    /// <returns>是否成功</returns>
    Task<bool> MarkBillsAsSyncFailedAsync(List<Guid> billIds);

    /// <summary>
    /// 检查是否可以同步
    /// </summary>
    /// <returns>是否可以同步</returns>
    Task<bool> CanSyncAsync();

    /// <summary>
    /// 获取离线缓存的账单数量
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>离线缓存数量</returns>
    Task<int> GetOfflineCacheCountAsync(Guid userId);
}

/// <summary>
/// 同步结果
/// </summary>
public class SyncResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 同步的账单数量
    /// </summary>
    public int SyncedCount { get; set; }

    /// <summary>
    /// 失败的账单数量
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
}
