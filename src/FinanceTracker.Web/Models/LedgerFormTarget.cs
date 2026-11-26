namespace FinanceTracker.Web.Models;

/// <summary>
/// 账本表单（新建 / 编辑 Sheet）要编辑的账本：只带表单需要的字段，调用方的页面行模型（带账单数、成员数等）不必暴露给表单。
/// </summary>
/// <param name="Id">账本 Id</param>
/// <param name="Name">名称</param>
/// <param name="Icon">图标类名</param>
/// <param name="SortOrder">排序（数字越小越靠前）</param>
public sealed record LedgerFormTarget(Guid Id, string Name, string Icon, int SortOrder);
