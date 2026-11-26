namespace FinanceTracker.Web.Models;

/// <summary>
/// 「名称 + 图标 + 排序」表单（新建 / 编辑 Sheet）要编辑的条目：分类与支付渠道共用，只带表单需要的字段，
/// 调用方的页面行模型（带账单数、是否默认等）不必暴露给表单。
/// </summary>
/// <param name="Id">条目 Id</param>
/// <param name="Name">名称</param>
/// <param name="Icon">图标类名</param>
/// <param name="SortOrder">排序（数字越小越靠前）</param>
public sealed record NameIconTarget(Guid Id, string Name, string Icon, int SortOrder);

/// <summary>
/// 表单点「保存」后交给调用方落库的值：名称已去首尾空白并通过校验，图标类名已规范化（合法才保留）
/// </summary>
/// <param name="Name">名称</param>
/// <param name="Icon">图标类名</param>
/// <param name="SortOrder">排序</param>
public sealed record NameIconValues(string Name, string Icon, int SortOrder);

/// <summary>
/// 分类与支付渠道管理页共用的展示规则（纯函数，单元测试覆盖）：名称校验、账单数文案、图标规范化。
/// 两个页面的结构完全同构（自定义 / 预设分组、默认徽标、编辑 Sheet），各自的图标候选与默认图标在
/// <see cref="CategoryPresentation"/> / <see cref="PaymentChannelPresentation"/>。
/// </summary>
public static class ManagedItemPresentation
{
    /// <summary>
    /// 名称最大长度（与数据库配置 Category.Name / PaymentChannel.Name 的 MaxLength 一致）
    /// </summary>
    public const int NameMaxLength = 50;

    /// <summary>
    /// 预设项右侧的标注（预设项只读：不能改名、不能删除，但可以设为默认）
    /// </summary>
    public const string PresetLabel = "预设";

    /// <summary>
    /// 校验名称，合法返回 null，否则返回给用户看的错误文案
    /// </summary>
    /// <param name="name">用户输入的名称</param>
    /// <param name="noun">条目的叫法，「分类」或「支付渠道」，用在提示里</param>
    public static string? ValidateName(string? name, string noun)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return $"请输入{noun}名称";
        }

        return trimmed.Length > NameMaxLength ? $"{noun}名称最多 {NameMaxLength} 个字" : null;
    }

    /// <summary>
    /// 自定义项右侧的账单数文案，例如「6 笔」；没有账单时是「0 笔」（列表里这一列始终有值，不留空）
    /// </summary>
    public static string UsageText(int billCount) => $"{Math.Max(0, billCount)} 笔";

    /// <summary>
    /// 图标类名合法就原样使用（去掉首尾空白），否则回退到默认图标
    /// （历史 / 用户自建项的图标可能为空或手填错误，不能渲染出空白色块）
    /// </summary>
    public static string NormalizeIcon(string? icon, string fallback) => MdiIcons.Normalize(icon, fallback);
}
