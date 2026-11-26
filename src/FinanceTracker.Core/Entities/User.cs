namespace FinanceTracker.Core.Entities;

/// <summary>
/// 用户实体
/// </summary>
public class User : BaseEntity
{
    /// <summary>
    /// 本地用户存根的占位手机号前缀：同步拿不到真实手机号（或真实手机号已被本机另一个账号占用）时，
    /// 存根用 <c>stub_</c> + 15 位 hex 当手机号。界面展示手机号前先用 <see cref="IsStubPhoneNumber"/> 判定，
    /// 不能把占位值当作真实手机号显示给用户。
    /// </summary>
    public const string StubPhonePrefix = "stub_";

    /// <summary>
    /// 手机号是否是用户存根的占位值（不是真实手机号）
    /// </summary>
    public static bool IsStubPhoneNumber(string? phoneNumber) =>
        phoneNumber != null && phoneNumber.StartsWith(StubPhonePrefix, StringComparison.Ordinal);

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 手机号（唯一标识）
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// 是否已删除（软删除）
    /// </summary>
    public bool IsDeleted { get; set; }

    // 导航属性
    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<PaymentChannel> PaymentChannels { get; set; } = new List<PaymentChannel>();
    public ICollection<Ledger> Ledgers { get; set; } = new List<Ledger>();
    public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
    public ICollection<LedgerMember> LedgerMemberships { get; set; } = new List<LedgerMember>();
}
