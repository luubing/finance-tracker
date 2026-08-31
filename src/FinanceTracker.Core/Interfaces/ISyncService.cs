using FinanceTracker.Core.Entities;

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
