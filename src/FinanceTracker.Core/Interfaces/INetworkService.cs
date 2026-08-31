namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 网络服务接口
/// </summary>
public interface INetworkService
{
    /// <summary>
    /// 检查是否有网络连接
    /// </summary>
    /// <returns>是否有网络</returns>
    bool IsConnected();

    /// <summary>
    /// 网络状态变化事件
    /// </summary>
    event EventHandler<bool> ConnectivityChanged;
}
