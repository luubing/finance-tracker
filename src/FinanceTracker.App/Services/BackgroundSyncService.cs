using FinanceTracker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.App.Services;

/// <summary>
/// 后台同步服务
/// </summary>
public class BackgroundSyncService : IDisposable
{
    private readonly ISyncService _syncService;
    private readonly INetworkService _networkService;
    private readonly ILogger<BackgroundSyncService> _logger;
    private Timer? _timer;
    private Guid? _userId;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public BackgroundSyncService(
        ISyncService syncService,
        INetworkService networkService,
        ILogger<BackgroundSyncService> logger)
    {
        _syncService = syncService;
        _networkService = networkService;
        _logger = logger;

        // 监听网络状态变化
        _networkService.ConnectivityChanged += OnConnectivityChanged;
    }

    /// <summary>
    /// 设置当前用户ID
    /// </summary>
    public void SetUserId(Guid userId)
    {
        _userId = userId;
    }

    /// <summary>
    /// 启动后台同步
    /// </summary>
    public void Start()
    {
        // 每5分钟检查一次同步
        _timer = new Timer(async _ => await SyncAsync(), null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// 停止后台同步
    /// </summary>
    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, 0);
    }

    /// <summary>
    /// 手动触发同步
    /// </summary>
    public async Task TriggerSyncAsync()
    {
        await SyncAsync();
    }

    private async Task SyncAsync()
    {
        // 使用 SemaphoreSlim 防止并发同步
        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogInformation("同步正在进行中，跳过");
            return;
        }

        if (!_userId.HasValue)
        {
            _syncLock.Release();
            return;
        }

        if (!_networkService.IsConnected())
        {
            _logger.LogInformation("无网络连接，跳过同步");
            _syncLock.Release();
            return;
        }

        try
        {
            _logger.LogInformation("开始后台同步");

            var result = await _syncService.SyncBillsAsync(_userId.Value);

            if (result.Success)
            {
                _logger.LogInformation("同步完成，成功: {SyncedCount} 条", result.SyncedCount);
            }
            else
            {
                _logger.LogWarning("同步完成，成功: {SyncedCount} 条，失败: {FailedCount} 条",
                    result.SyncedCount, result.FailedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "后台同步失败");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        if (isConnected)
        {
            _logger.LogInformation("网络已恢复，触发同步");
            // 使用 Task.Run 避免在事件处理器中直接 async
            Task.Run(async () =>
            {
                try
                {
                    await SyncAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "网络恢复同步失败");
                }
            });
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _syncLock?.Dispose();
        _networkService.ConnectivityChanged -= OnConnectivityChanged;
    }
}
