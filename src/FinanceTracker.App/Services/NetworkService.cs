using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.App.Services;

/// <summary>
/// 网络服务实现（MAUI）
/// </summary>
public class NetworkService : INetworkService
{
    public event EventHandler<bool>? ConnectivityChanged;

    public NetworkService()
    {
        // 监听网络状态变化
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsConnected()
    {
        return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var isConnected = e.NetworkAccess == NetworkAccess.Internet;
        ConnectivityChanged?.Invoke(this, isConnected);
    }
}
