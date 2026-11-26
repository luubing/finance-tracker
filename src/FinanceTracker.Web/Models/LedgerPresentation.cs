using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Services;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 可选图标（图标类名 + 读屏用的名称）
/// </summary>
/// <param name="Icon">MDI 图标类名，形如 mdi-book</param>
/// <param name="Label">读屏名称（图标按钮没有可见文字）</param>
public sealed record IconChoice(string Icon, string Label);

/// <summary>
/// 账本页的展示规则（纯函数，单元测试覆盖）：副标题 / 徽标文案、图标色块配色、图标选项、名称校验。
/// 页面只负责取数与交互，文案与规则集中在这里，页面里不散落字符串拼接。
/// </summary>
public static class LedgerPresentation
{
    /// <summary>
    /// 新建账本的默认图标，也是历史数据里图标缺失 / 非法时的回退
    /// </summary>
    public const string DefaultIcon = "mdi-book";

    /// <summary>
    /// 账本名称最大长度（与数据库配置 Ledger.Name 的 MaxLength 一致）
    /// </summary>
    public const int NameMaxLength = 50;

    /// <summary>
    /// 图标选择器里的候选图标。名称均已对照应用内置的 Material Design Icons（7.2.x）核对存在。
    /// </summary>
    public static IReadOnlyList<IconChoice> IconChoices { get; } =
    [
        new("mdi-book", "账本"),
        new("mdi-book-open-variant", "翻开的书"),
        new("mdi-wallet", "钱包"),
        new("mdi-home-heart", "家庭"),
        new("mdi-airplane", "旅行"),
        new("mdi-account-group", "小组"),
        new("mdi-cart", "购物"),
        new("mdi-silverware-fork-knife", "餐饮"),
        new("mdi-car", "出行"),
        new("mdi-briefcase", "工作"),
        new("mdi-school", "学习"),
        new("mdi-gift", "礼物"),
        new("mdi-piggy-bank", "储蓄"),
        new("mdi-cash-multiple", "现金"),
        new("mdi-baby-carriage", "育儿"),
        new("mdi-paw", "宠物"),
        new("mdi-dumbbell", "健身"),
        new("mdi-hospital-box", "医疗"),
    ];

    /// <summary>
    /// 图标色块配色（对应 ui.css 里的 .ft-tile--{色}）。按账本 Id 取色：同一个账本在各设备、各次进入时颜色一致，
    /// 调整排序也不会换色
    /// </summary>
    public static IReadOnlyList<string> Tones { get; } = ["purple", "teal", "pink", "orange", "blue", "green"];

    /// <summary>
    /// 账本的图标色块配色
    /// </summary>
    public static string ToneFor(Guid ledgerId)
    {
        // Guid.GetHashCode 由 16 个字节决定、不随进程变化（不像 string.GetHashCode 每次启动都不同）
        var index = (uint)ledgerId.GetHashCode() % (uint)Tones.Count;
        return Tones[(int)index];
    }

    /// <summary>
    /// 图标类名合法就原样使用，否则回退默认图标（历史数据里的图标可能为空或手填错误，不能渲染出空白色块）
    /// </summary>
    public static string NormalizeIcon(string? icon) => MdiIcons.Normalize(icon, DefaultIcon);

    /// <summary>
    /// 成员角色的短标签（徽标与副标题共用）
    /// </summary>
    public static string RoleLabel(LedgerMemberRole role) => role switch
    {
        LedgerMemberRole.Owner => "所有者",
        LedgerMemberRole.Editor => "可编辑",
        _ => "只读",
    };

    /// <summary>
    /// 展示用的脱敏手机号：11 位保留前 3 后 4；没有手机号或只是用户存根的占位值时显示「未知用户」
    /// （占位值是同步为满足外键临时造的，不是真实手机号，不能原样露给用户）
    /// </summary>
    public static string MaskPhone(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || User.IsStubPhoneNumber(phoneNumber))
        {
            return "未知用户";
        }

        return PhoneNumberMasker.Mask(phoneNumber);
    }

    /// <summary>
    /// 「我的账本」行的副标题：共享账本显示成员数与身份，否则显示账单数
    /// </summary>
    /// <param name="billCount">账本里的账单数</param>
    /// <param name="memberCount">已生效成员数（含所有者；未知或没有成员行时为 0）</param>
    public static string OwnedSubtitle(int billCount, int memberCount)
    {
        if (memberCount >= 2)
        {
            return $"{memberCount} 位成员 · 你是所有者";
        }

        return billCount > 0 ? $"{billCount} 笔账单" : "暂无账单";
    }

    /// <summary>
    /// 「加入的共享账本」行的副标题：账本所有者
    /// </summary>
    public static string JoinedSubtitle(string? ownerPhoneNumber) => $"所有者 {MaskPhone(ownerPhoneNumber)}";

    /// <summary>
    /// 邀请卡的标题
    /// </summary>
    public static string InvitationTitle(string? ledgerName) =>
        $"「{(string.IsNullOrWhiteSpace(ledgerName) ? "未知账本" : ledgerName)}」邀请你加入";

    /// <summary>
    /// 邀请卡的副标题：邀请人与角色
    /// </summary>
    public static string InvitationSubtitle(string? inviterPhoneNumber, LedgerMemberRole role) =>
        $"来自 {MaskPhone(inviterPhoneNumber)} · 角色：{RoleLabel(role)}";

    /// <summary>
    /// 校验账本名称，合法返回 null，否则返回给用户看的错误文案
    /// </summary>
    public static string? ValidateName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return "请输入账本名称";
        }

        return trimmed.Length > NameMaxLength ? $"账本名称最多 {NameMaxLength} 个字" : null;
    }
}
