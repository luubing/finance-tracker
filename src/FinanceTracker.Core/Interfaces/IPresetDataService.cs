using FinanceTracker.Core.Entities;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 预设数据服务接口
/// </summary>
public interface IPresetDataService
{
    /// <summary>
    /// 初始化预设数据
    /// </summary>
    Task InitializePresetDataAsync();

    /// <summary>
    /// 获取所有分类（预设 + 用户自定义）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>分类列表</returns>
    Task<List<Category>> GetCategoriesAsync(Guid? userId);

    /// <summary>
    /// 获取所有支付渠道（预设 + 用户自定义）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>支付渠道列表</returns>
    Task<List<PaymentChannel>> GetPaymentChannelsAsync(Guid? userId);
}
