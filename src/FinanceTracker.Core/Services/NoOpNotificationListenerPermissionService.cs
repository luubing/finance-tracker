using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 通知使用权权限服务的空实现（Web / iOS / Windows 等不支持的平台）。
/// 用于避免那些平台上 DI 无法解析 INotificationListenerPermissionService 导致页面崩溃。
/// </summary>
public class NoOpNotificationListenerPermissionService : INotificationListenerPermissionService
{
    public Task<bool> HasPermissionAsync() => Task.FromResult(false);

    public Task OpenSettingsAsync() => Task.CompletedTask;
}
