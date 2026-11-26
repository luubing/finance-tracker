using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 账单服务接口
/// </summary>
public interface IBillService
{
    /// <summary>
    /// 按条件获取账单列表（按交易时间倒序分页）。
    /// 与 <see cref="GetBillCountAsync(Guid, BillFilter)"/>、<see cref="GetBillSummaryAsync"/> 共用同一份 <see cref="BillFilter"/> 与同一个查询构造，
    /// 列表、总数、汇总的口径一定一致；仍遵守软删除与账本可见性（含共享账本）。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="filter">查询条件</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页数量</param>
    Task<List<Bill>> GetBillsAsync(Guid userId, BillFilter filter, int page = 1, int pageSize = 20);

    /// <summary>
    /// 按条件统计符合条件的账单总数
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="filter">查询条件</param>
    Task<int> GetBillCountAsync(Guid userId, BillFilter filter);

    /// <summary>
    /// 账单汇总：符合条件的<b>全部</b>账单（不受分页影响）的支出合计、收入合计与笔数。
    /// 用的是列表同一份条件，账单页顶部汇总卡显示它，而不是「已加载那几页」的合计。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="filter">查询条件</param>
    Task<BillSummary> GetBillSummaryAsync(Guid userId, BillFilter filter);

    /// <summary>
    /// 获取账单列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="categoryId">分类ID</param>
    /// <param name="paymentChannelId">支付渠道ID</param>
    /// <param name="ledgerId">账本ID</param>
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
        Guid? ledgerId = null,
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
    /// <exception cref="Exceptions.ForbiddenAccessException">
    /// 账单现在所在的账本里，当前用户是只读成员（ADR 0004：只读成员不能修改任何账单；403 语义，不是登录失效）。
    /// 校验的是修改前的账本；目标账本的写权限仍由调用方（界面 / 控制器）经 <c>ILedgerMemberService.EnsureCanWriteAsync</c> 校验
    /// </exception>
    Task<Bill> UpdateBillAsync(Bill bill);

    /// <summary>
    /// 删除账单（软删除）
    /// </summary>
    /// <param name="billId">账单ID</param>
    /// <param name="userId">用户ID</param>
    /// <returns>是否成功</returns>
    /// <exception cref="Exceptions.ForbiddenAccessException">账单所在的账本里，当前用户是只读成员（403 语义，不是登录失效）</exception>
    Task<bool> DeleteBillAsync(Guid billId, Guid userId);

    /// <summary>
    /// 批量归属账本。账本必填，因此只能归属到某个账本、不支持"移出账本"；
    /// 也是给"账本必填"之前创建的、未归属账本的历史账单补上账本的批量入口
    /// </summary>
    /// <param name="billIds">账单ID列表</param>
    /// <param name="userId">用户ID</param>
    /// <param name="ledgerId">目标账本ID（必须是当前用户自己的账本）</param>
    /// <returns>成功归属的账单数量</returns>
    /// <exception cref="ArgumentException">未选择账本，或账本不存在/不属于当前用户</exception>
    /// <exception cref="Exceptions.ForbiddenAccessException">
    /// 所选账单里有账单现在位于当前用户只是只读成员的共享账本（403 语义）；整批拒绝，不会只改一部分
    /// </exception>
    Task<int> AssignBillsToLedgerAsync(List<Guid> billIds, Guid userId, Guid ledgerId);

    /// <summary>
    /// 获取账单总数
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="categoryId">分类ID</param>
    /// <param name="paymentChannelId">支付渠道ID</param>
    /// <param name="ledgerId">账本ID</param>
    /// <param name="type">账单类型</param>
    /// <returns>账单总数</returns>
    Task<int> GetBillCountAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        Guid? categoryId = null,
        Guid? paymentChannelId = null,
        Guid? ledgerId = null,
        BillType? type = null);
}
