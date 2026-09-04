namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 通知监听权限服务接口（Android 通知使用权，NotificationListenerService）。
/// 该权限无法通过运行时弹窗授予，只能引导用户到系统设置开启。
/// </summary>
public interface INotificationListenerPermissionService
{
    /// <summary>
    /// 检查是否已授予通知使用权
    /// </summary>
    Task<bool> HasPermissionAsync();

    /// <summary>
    /// 跳转到系统"通知使用权"设置页，返回后由调用方重新检查授权状态
    /// </summary>
    Task OpenSettingsAsync();
}
