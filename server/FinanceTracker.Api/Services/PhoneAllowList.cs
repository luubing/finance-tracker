using FinanceTracker.Shared.Validators;

namespace FinanceTracker.Api.Services;

/// <summary>
/// 云端手机号白名单（可选，<b>默认关闭</b>）。
/// <para>
/// 云端登录只凭手机号、没有验证码（ADR 0003）：公开部署时，任何人拿到一个手机号就能注册并登录对应账号。
/// 配置了名单（<see cref="ConfigurationKey"/>）后，只有名单内的手机号能登录 / 注册，
/// 已经签发出去的令牌也会在下一次请求时被拦下（见 <c>PhoneAllowListMiddleware</c>）——名单改错时马上暴露，
/// 而不是等令牌过期后才集体登录失败。适合自用 / 家用部署。
/// </para>
/// <para>
/// 名单为空 = 关闭 = 与此前完全一致。它只是一道粗粒度的「门」，不是身份验证：名单内的手机号仍然只凭号码就能登录，
/// 真正的验证码 / 口令是单独的产品决策（见 SECURITY.md）。共享账本按手机号邀请成员，被邀请人也必须在名单内才能用云端。
/// </para>
/// </summary>
public sealed class PhoneAllowList
{
    /// <summary>
    /// 配置键：<c>Auth:AllowedPhoneNumbers</c>（环境变量写法 <c>Auth__AllowedPhoneNumbers</c>）。
    /// 值是用英文逗号 / 分号 / 换行分隔的手机号；也支持 JSON 数组写法。
    /// </summary>
    public const string ConfigurationKey = "Auth:AllowedPhoneNumbers";

    /// <summary>被拒绝时返回给客户端的提示（客户端原样展示给用户）</summary>
    public const string DeniedMessage = "该手机号未获当前云端服务的使用许可，请联系服务器管理员";

    private static readonly char[] Separators = [',', ';', '，', '；', '、', '\r', '\n'];

    private readonly HashSet<string> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// 用原始条目构造名单。每个条目本身也可以含分隔符（一整串逗号分隔的值）；
    /// 条目里的空格、连字符、括号会被去掉，开头的 +86 会被去掉（方便直接粘贴「+86 138-0013-8000」）。
    /// </summary>
    public PhoneAllowList(IEnumerable<string?>? rawEntries)
    {
        if (rawEntries == null)
        {
            return;
        }

        foreach (var raw in rawEntries)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var entry = Normalize(part);
                if (entry.Length > 0)
                {
                    _entries.Add(entry);
                }
            }
        }
    }

    /// <summary>未启用的名单（允许所有手机号）</summary>
    public static PhoneAllowList Disabled => new(null);

    /// <summary>
    /// 是否启用：配置里出现了至少一个条目就算启用，哪怕条目格式不对——
    /// 手滑写错时宁可把所有人挡在外面（并在启动日志里告警），也不能悄悄退回"不设防"。
    /// </summary>
    public bool IsEnabled => _entries.Count > 0;

    /// <summary>名单条目数（去重后）</summary>
    public int Count => _entries.Count;

    /// <summary>格式不对的条目数（不是 11 位手机号，永远匹配不上任何人）；用于启动期告警</summary>
    public int InvalidEntryCount => _entries.Count(e => !PhoneValidator.Validate(e).IsValid);

    /// <summary>
    /// 是否允许该手机号使用云端。未启用时一律允许；启用后只有名单内的号码允许，空号码一律拒绝。
    /// </summary>
    public bool IsAllowed(string? phoneNumber)
    {
        if (!IsEnabled)
        {
            return true;
        }

        return !string.IsNullOrEmpty(phoneNumber) && _entries.Contains(phoneNumber);
    }

    /// <summary>
    /// 从配置读取名单。同时支持字符串写法（<c>Auth__AllowedPhoneNumbers=138...,139...</c>）
    /// 与数组写法（JSON 数组，或 <c>Auth__AllowedPhoneNumbers__0=138...</c>）；键不存在或为空 = 未启用。
    /// </summary>
    public static PhoneAllowList FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ConfigurationKey);
        var raw = new List<string?> { section.Value };
        raw.AddRange(section.GetChildren().Select(child => child.Value));
        return new PhoneAllowList(raw);
    }

    private static string Normalize(string part)
    {
        var chars = part.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '－' or '(' or ')' or '（' or '）'));
        var entry = new string(chars.ToArray());

        // +8613800138000 → 13800138000（只在去掉后正好剩 11 位时才去，避免误伤别的写法）
        if (entry.StartsWith("+86", StringComparison.Ordinal) && entry.Length == 14)
        {
            entry = entry[3..];
        }

        return entry;
    }
}
