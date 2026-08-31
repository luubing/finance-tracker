using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 账单服务接口
/// </summary>
public interface IBillService
{
    /// <summary>
    /// 获取账单列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="categoryId">分类ID</param>
    /// <param name="paymentChannelId">支付渠道ID</param>
    /// <param name="type">账单类型</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>账单列表</returns>
    Task<List<Bill>> GetBillsAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? categoryId = null,
        Guid? paymentChannelId = null,
        BillType? type = null,
        int page = 1,
        int pageSize = 20);

    /// <summary>
    /// 根据ID获取账单
    /// </summary>
    /// <param name="billId">账单ID</param>
    /// <returns>账单信息</returns>
    Task<Bill?> GetBillByIdAsync(Guid billId);

    /// <summary>
    /// 创建账单
    /// </summary>
    /// <param name="bill">账单信息</param>
    /// <returns>创建的账单</returns>
    Task<Bill> CreateBillAsync(Bill bill);

    /// <summary>
    /// 更新账单
    /// </summary>
    /// <param name="bill">账单信息</param>
    /// <returns>更新的账单</returns>
    Task<Bill> UpdateBillAsync(Bill bill);

    /// <summary>
    /// 删除账单（软删除）
    /// </summary>
    /// <param name="billId">账单ID</param>
    /// <param name="userId">用户ID</param>
    /// <returns>是否成功</returns>
    Task<bool> DeleteBillAsync(Guid billId, Guid userId);

    /// <summary>
    /// 获取账单总数
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="categoryId">分类ID</param>
    /// <param name="paymentChannelId">支付渠道ID</param>
    /// <param name="type">账单类型</param>
    /// <returns>账单总数</returns>
    Task<int> GetBillCountAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? categoryId = null,
        Guid? paymentChannelId = null,
        BillType? type = null);
}
