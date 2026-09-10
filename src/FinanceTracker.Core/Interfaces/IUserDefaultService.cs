using FinanceTracker.Core.Entities;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 用户默认值服务接口
/// </summary>
public interface IUserDefaultService
{
    /// <summary>
    /// 获取用户默认值
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>用户默认值</returns>
    Task<UserDefault?> GetUserDefaultAsync(Guid userId);

    /// <summary>
    /// 设置默认支出分类
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="categoryId">分类ID</param>
    /// <returns>是否成功</returns>
    Task<bool> SetDefaultExpenseCategoryAsync(Guid userId, Guid? categoryId);

    /// <summary>
    /// 设置默认收入分类
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="categoryId">分类ID</param>
    /// <returns>是否成功</returns>
    Task<bool> SetDefaultIncomeCategoryAsync(Guid userId, Guid? categoryId);

    /// <summary>
    /// 设置默认支付渠道
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="channelId">渠道ID</param>
    /// <returns>是否成功</returns>
    Task<bool> SetDefaultPaymentChannelAsync(Guid userId, Guid? channelId);

    /// <summary>
    /// 设置默认账本
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="ledgerId">账本ID</param>
    /// <returns>是否成功</returns>
    Task<bool> SetDefaultLedgerAsync(Guid userId, Guid? ledgerId);
}
