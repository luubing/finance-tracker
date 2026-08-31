using FinanceTracker.Core.Entities;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 支付渠道服务接口
/// </summary>
public interface IPaymentChannelService
{
    /// <summary>
    /// 获取支付渠道列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>支付渠道列表</returns>
    Task<List<PaymentChannel>> GetPaymentChannelsAsync(Guid? userId);

    /// <summary>
    /// 根据ID获取支付渠道
    /// </summary>
    /// <param name="channelId">渠道ID</param>
    /// <returns>支付渠道信息</returns>
    Task<PaymentChannel?> GetPaymentChannelByIdAsync(Guid channelId);

    /// <summary>
    /// 创建自定义支付渠道
    /// </summary>
    /// <param name="channel">支付渠道信息</param>
    /// <returns>创建的支付渠道</returns>
    Task<PaymentChannel> CreatePaymentChannelAsync(PaymentChannel channel);

    /// <summary>
    /// 更新自定义支付渠道
    /// </summary>
    /// <param name="channel">支付渠道信息</param>
    /// <returns>更新的支付渠道</returns>
    Task<PaymentChannel> UpdatePaymentChannelAsync(PaymentChannel channel);

    /// <summary>
    /// 删除自定义支付渠道（软删除）
    /// </summary>
    /// <param name="channelId">渠道ID</param>
    /// <param name="userId">用户ID</param>
    /// <returns>是否成功</returns>
    Task<bool> DeletePaymentChannelAsync(Guid channelId, Guid userId);
}
