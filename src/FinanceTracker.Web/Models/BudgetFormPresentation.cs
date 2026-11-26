namespace FinanceTracker.Web.Models;

/// <summary>
/// 新建 / 编辑预算表单的初始值（页面打开 Sheet 时交给表单；表单只在 Sheet 由隐藏变为显示的那一刻读取它）
/// </summary>
/// <param name="Id">要编辑的预算 Id；新建时为空</param>
/// <param name="Year">预算年份</param>
/// <param name="Month">预算月份</param>
/// <param name="Amount">预算金额；新建时为空（等用户用键盘输入）</param>
/// <param name="LedgerId">账本（空 = 全部账本）</param>
/// <param name="CategoryId">分类（总预算时为空）</param>
/// <param name="IsCategory">是分类预算（false = 总预算）。新建时由入口决定默认选哪一段</param>
public sealed record BudgetFormTarget(
    Guid? Id,
    int Year,
    int Month,
    decimal? Amount,
    Guid? LedgerId,
    Guid? CategoryId,
    bool IsCategory)
{
    /// <summary>编辑已有预算（false = 新建）</summary>
    public bool IsEditing => Id is not null;
}

/// <summary>
/// 预算表单与删除确认的文案和校验（纯函数，单元测试覆盖）。
/// 服务层校验（金额必须大于 0、同月同范围同分类不可重复）保持不变，这里只负责在提交前先拦下显而易见的缺项，
/// 并沿用旧页面的提示文字。
/// </summary>
public static class BudgetFormPresentation
{
    /// <summary>「总预算」一段的取值</summary>
    public const string ScopeTotal = "total";

    /// <summary>「分类预算」一段的取值</summary>
    public const string ScopeCategory = "category";

    /// <summary>表单顶部的分段：总预算 / 分类预算</summary>
    public static IReadOnlyList<(string Value, string Label)> Scopes { get; } =
    [
        (ScopeTotal, "总预算"),
        (ScopeCategory, "分类预算"),
    ];

    /// <summary>分段取值</summary>
    public static string ScopeOf(bool isCategory) => isCategory ? ScopeCategory : ScopeTotal;

    /// <summary>Sheet 标题</summary>
    public static string Title(bool editing) => editing ? "编辑预算" : "新建预算";

    /// <summary>
    /// 提交前的校验：没填金额（或不大于 0）→「请输入预算金额」；选了「分类预算」却没选分类 →「请选择分类」。
    /// 通过返回空。
    /// </summary>
    public static string? Validate(decimal? amount, bool isCategory, Guid? categoryId)
    {
        if (amount is not > 0m)
        {
            return "请输入预算金额";
        }

        return isCategory && categoryId is null ? "请选择分类" : null;
    }

    /// <summary>保存成功的提示</summary>
    public static string SavedMessage(bool editing) => editing ? "预算更新成功" : "预算创建成功";

    /// <summary>删除成功的提示</summary>
    public const string DeletedMessage = "预算删除成功";

    /// <summary>删除时预算已经不存在的提示</summary>
    public const string MissingMessage = "预算不存在";

    /// <summary>删除确认的标题</summary>
    public static string DeleteTitle(string name) => $"删除预算「{name}」？";

    /// <summary>删除确认的说明</summary>
    public const string DeleteMessage = "删除后这条预算不再统计，也不再提醒超支。已记的账单不受影响。";

    /// <summary>账本选项里「全部账本」的文字</summary>
    public const string AllLedgersText = "全部账本";
}
