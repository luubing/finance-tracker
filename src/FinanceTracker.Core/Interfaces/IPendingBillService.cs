using FinanceTracker.Core.Entities;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 待确认账单服务接口：管理自动捕获（通知栏/短信）产生的待确认账单。
/// 实现为 SQLite 持久化（App 进程被杀后未确认记录不丢失），
/// Android 后台服务（BroadcastReceiver / NotificationListenerService）
/// 与 Blazor 页面共享同一单例，保证后台捕获的数据能实时反映到页面。
/// </summary>
public interface IPendingBillService
{
    /// <summary>
    /// 待确认账单列表变化事件（新增/移除时触发），用于页面实时刷新
    /// </summary>
    event EventHandler? PendingBillsChanged;

    /// <summary>
    /// 获取所有待确认账单（按捕获时间倒序）
    /// </summary>
    Task<List<PendingBill>> GetPendingBillsAsync();

    /// <summary>
    /// 添加待确认账单（按来源+交易时间+金额去重，避免同一条支付重复捕获）
    /// </summary>
    /// <returns>是否成功添加（false 表示重复，已忽略）</returns>
    Task<bool> AddAsync(PendingBill pendingBill);

    /// <summary>
    /// 移除待确认账单（确认或忽略后调用）
    /// </summary>
    Task<bool> RemoveAsync(Guid id);

    /// <summary>
    /// 清空所有待确认账单
    /// </summary>
    Task ClearAsync();
}

/// <summary>
/// IPendingBillService 的静态访问入口。
/// Android 后台组件（BroadcastReceiver / NotificationListenerService）不走 DI 容器，
/// 通过该入口获取与 DI 容器中相同的单例实例。
/// </summary>
public static class PendingBillServiceLocator
{
    /// <summary>
    /// 应用启动时（MauiProgram）由 DI 容器注册的实例，后台组件从此读取
    /// </summary>
    public static IPendingBillService? Instance { get; set; }
}

