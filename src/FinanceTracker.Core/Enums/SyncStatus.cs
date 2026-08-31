namespace FinanceTracker.Core.Enums;

/// <summary>
/// 同步状态
/// </summary>
public enum SyncStatus
{
    /// <summary>
    /// 待同步
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已同步
    /// </summary>
    Synced = 1,

    /// <summary>
    /// 同步失败
    /// </summary>
    Failed = 2
}
