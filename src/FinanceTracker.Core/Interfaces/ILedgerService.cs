using FinanceTracker.Core.Entities;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 账本服务接口
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// 获取用户的账本列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>账本列表</returns>
    Task<List<Ledger>> GetLedgersAsync(Guid userId);

    /// <summary>
    /// 根据ID获取账本
    /// </summary>
    /// <param name="ledgerId">账本ID</param>
    /// <returns>账本信息</returns>
    Task<Ledger?> GetLedgerByIdAsync(Guid ledgerId);

    /// <summary>
    /// 创建账本
    /// </summary>
    /// <param name="ledger">账本信息</param>
    /// <returns>创建的账本</returns>
    Task<Ledger> CreateLedgerAsync(Ledger ledger);

    /// <summary>
    /// 更新账本
    /// </summary>
    /// <param name="ledger">账本信息</param>
    /// <returns>更新的账本</returns>
    Task<Ledger> UpdateLedgerAsync(Ledger ledger);

    /// <summary>
    /// 删除账本（软删除）
    /// </summary>
    /// <param name="ledgerId">账本ID</param>
    /// <param name="userId">用户ID</param>
    /// <returns>是否成功</returns>
    Task<bool> DeleteLedgerAsync(Guid ledgerId, Guid userId);
}
