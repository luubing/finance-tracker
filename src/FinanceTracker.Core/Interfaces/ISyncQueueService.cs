namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 同步队列服务接口
/// </summary>
public interface ISyncQueueService
{
    /// <summary>
    /// 添加账单到同步队列
    /// </summary>
    /// <param name="billId">账单ID</param>
    Task EnqueueAsync(Guid billId);

    /// <summary>
    /// 从队列中获取待同步的账单ID
    /// </summary>
    /// <param name="count">获取数量</param>
    /// <returns>账单ID列表</returns>
    Task<List<Guid>> DequeueAsync(int count = 10);

    /// <summary>
    /// 获取队列长度
    /// </summary>
    /// <returns>队列长度</returns>
    Task<int> GetQueueLengthAsync();

    /// <summary>
    /// 清空队列
    /// </summary>
    Task ClearAsync();
}
