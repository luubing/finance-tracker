namespace FinanceTracker.Web.Services;

/// <summary>
/// 账单变更事件服务（账单新增/编辑/删除后通知订阅页面刷新数据）
/// </summary>
public class BillEventService
{
    /// <summary>
    /// 账单数据变更事件
    /// </summary>
    public event Action? BillChanged;

    /// <summary>
    /// 通知所有订阅者：账单数据已变更
    /// </summary>
    public void NotifyBillChanged() => BillChanged?.Invoke();
}
