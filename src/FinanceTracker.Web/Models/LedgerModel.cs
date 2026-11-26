using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 账本视图模型：页面中的账本下拉筛选、列表展示与编辑对话框共用。
/// <see cref="Id"/> / <see cref="Name"/> / <see cref="Icon"/> 为通用字段，
/// <see cref="IsShared"/> / <see cref="IsDefault"/> / <see cref="SortOrder"/> 按页面需要选用。
/// </summary>
public class LedgerModel
{
    /// <summary>
    /// 账本ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 账本名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 是否为他人共享给我的账本
    /// </summary>
    public bool IsShared { get; set; }

    /// <summary>
    /// 是否为用户默认账本
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 下拉显示文本：共享账本追加（共享）标记
    /// </summary>
    public string DisplayText => IsShared ? $"{Name}（共享）" : Name;

    /// <summary>
    /// 由账本实体构造视图模型
    /// </summary>
    /// <param name="ledger">账本实体</param>
    /// <param name="isShared">是否为他人共享给我的账本</param>
    public static LedgerModel FromEntity(Core.Entities.Ledger ledger, bool isShared = false) => new()
    {
        Id = ledger.Id,
        Name = ledger.Name,
        Icon = ledger.Icon,
        IsShared = isShared,
        SortOrder = ledger.SortOrder
    };

    /// <summary>
    /// 加载当前用户自己的账本，并给出应选中的账本：
    /// 当前选中项仍然有效就沿用；否则默认账本优先，再取第一个。
    /// 供不支持记入共享账本的录入页面（待确认账单、短信识别、账单导入）使用。
    /// </summary>
    /// <param name="ledgerService">账本服务</param>
    /// <param name="userDefaultService">用户默认值服务</param>
    /// <param name="userId">用户ID</param>
    /// <param name="currentLedgerId">页面当前选中的账本（可为空）</param>
    public static async Task<(List<LedgerModel> Ledgers, Guid? SelectedLedgerId)> LoadOwnAsync(
        ILedgerService ledgerService,
        IUserDefaultService userDefaultService,
        Guid userId,
        Guid? currentLedgerId = null)
    {
        var ledgers = (await ledgerService.GetLedgersAsync(userId))
            .Select(l => FromEntity(l))
            .ToList();

        if (currentLedgerId.HasValue && ledgers.Any(l => l.Id == currentLedgerId.Value))
        {
            return (ledgers, currentLedgerId);
        }

        var userDefault = await userDefaultService.GetUserDefaultAsync(userId);

        return (ledgers, PickDefault(ledgers, userDefault?.DefaultLedgerId));
    }

    /// <summary>
    /// 挑选录入账单时默认选中的账本（账本必填，所以尽量给出一个）：
    /// 用户设置的默认账本优先，否则取第一个自己的账本；一个都没有时返回 null。
    /// 只考虑自己的账本：共享账本不能设为默认，也不应在用户没选的情况下替他往别人的账本里记账。
    /// </summary>
    /// <param name="ledgers">可选账本（含共享账本）</param>
    /// <param name="preferredLedgerId">用户设置的默认账本（可为空）</param>
    public static Guid? PickDefault(IEnumerable<LedgerModel> ledgers, Guid? preferredLedgerId)
    {
        var ownLedgers = ledgers.Where(l => !l.IsShared).ToList();

        return ownLedgers.FirstOrDefault(l => l.Id == preferredLedgerId)?.Id
               ?? ownLedgers.FirstOrDefault()?.Id;
    }
}
