namespace FinanceTracker.Web.Services;

/// <summary>
/// 地址里的「打开记一笔 Sheet」请求：<see cref="BillId"/> 为空是新建，有值是编辑那一笔账单。
/// </summary>
/// <param name="BillId">要编辑的账单 Id；新建为 null</param>
public readonly record struct AddBillOpenRequest(Guid? BillId)
{
    /// <summary>是否是编辑账单</summary>
    public bool IsEdit => BillId.HasValue;
}

/// <summary>
/// 「记一笔」Sheet 的开关（新建与编辑共用同一个 Sheet）。
/// <para>
/// Sheet 是挂在 <c>MainLayout</c> 里的布局级覆盖层（<c>AddBillSheet</c>），不是页面栈里的一页：它从底部升起、背景页面轻微缩小，
/// 这两点靠 PageStack 的路由页做不到（页面栈只提供整页向左滑入的转场，且无法让下面那页缩小），所以改由这个服务驱动。
/// 打开它的入口——Tab 栏的「记一笔」圆钮、首页 / 账单页的空状态引导、旧地址 <c>/add-bill</c> 的跳转——只需要 <see cref="Open"/>；
/// 编辑入口——账单行点按、左滑「编辑」、首页最近账单、旧地址 <c>/add-bill/{BillId}</c> 的跳转——只需要 <see cref="OpenEdit"/>。
/// 入口不关心 Sheet 的内部状态；Sheet 订阅 <see cref="Changed"/> 渲染。
/// </para>
/// <para>
/// Scoped：MAUI 里等同应用级单例，Blazor Server 里每条电路一份。非线程安全——只在 UI 线程（渲染同步上下文）里调用，
/// 与同目录下其它 Scoped 的 UI 状态服务一致。
/// </para>
/// </summary>
public sealed class AddBillSheetService
{
    /// <summary>旧的「记一笔」页面地址（书签、外部链接、旧版本留下的跳转）：现在只剩服务端 302</summary>
    public const string LegacyRoute = "/add-bill";

    /// <summary>旧的「编辑账单」页面地址模板（<c>/add-bill/{billId}</c>）：同样只剩服务端 302（Web.Server 用它注册路由，限定为 GUID）</summary>
    public const string LegacyEditRoute = "/add-bill/{billId:guid}";

    /// <summary>「进入应用就升起记一笔」的地址（<see cref="LegacyRoute"/> 的 302 目标；首页 + 查询参数）</summary>
    public const string OpenUri = "/?add-bill=1";

    /// <summary>地址里表示「升起记一笔」的查询参数名</summary>
    public const string QueryKey = "add-bill";

    /// <summary>「进入应用就升起这笔账单的编辑 Sheet」的地址（<see cref="LegacyEditRoute"/> 的 302 目标）</summary>
    public static string EditUri(Guid billId) => $"/?{QueryKey}={billId:D}";

    /// <summary>Sheet 当前是否被要求打开</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// 累计打开次数。Sheet 用它区分「又打开了一次」与「同一次打开期间的重绘」：
    /// 每一次打开都从空白表单（编辑则是那笔账单的原貌）开始，上一次没保存的内容不会带到下一次。
    /// </summary>
    public int OpenCount { get; private set; }

    /// <summary>当前这一次打开要编辑的账单 Id；新建（或没有打开）时为 null。Sheet 在打开那一刻读取它</summary>
    public Guid? EditingBillId { get; private set; }

    /// <summary>打开 / 关闭状态变化（订阅者在 UI 线程里重绘）</summary>
    public event Action? Changed;

    /// <summary>
    /// 打开「记一笔」Sheet（新建账单）。已经打开时什么也不做——连点两下圆钮不会重置用户刚输入的内容。
    /// </summary>
    public void Open() => OpenCore(null);

    /// <summary>
    /// 打开同一个 Sheet 的编辑模式（标题「编辑账单」，各字段预填，带「删除」）。已经打开时什么也不做。
    /// 账单是否存在、是否是自己的、有没有写权限都由 Sheet 读到账单之后判断（不通过就提示并收起），入口不需要预先检查。
    /// </summary>
    /// <param name="billId">要编辑的账单 Id；不能为空 Guid</param>
    public void OpenEdit(Guid billId)
    {
        if (billId == Guid.Empty)
        {
            throw new ArgumentException("账单 Id 不能为空", nameof(billId));
        }

        OpenCore(billId);
    }

    private void OpenCore(Guid? billId)
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        EditingBillId = billId;
        OpenCount++;
        Changed?.Invoke();
    }

    /// <summary>
    /// 关闭 Sheet（用户点 ✕ / 遮罩 / 拖拽收起，或保存 / 删除成功后）。已经关闭时什么也不做。
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        EditingBillId = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// 读地址里的「打开记一笔」请求：<c>add-bill=1</c>（或 <c>true</c> / 不带值）是新建，<c>add-bill=&lt;账单 Id&gt;</c> 是编辑那一笔。
    /// 旧地址 <c>/add-bill</c>、<c>/add-bill/{BillId}</c> 由服务端 302 到这两种地址（见 Web.Server 的 Program.cs），Sheet 在电路接管后的第一次渲染里读它。
    /// 这里不用 <c>QueryHelpers</c>：MAUI 宿主不一定带 <c>Microsoft.AspNetCore.WebUtilities</c>，几行字符串扫描就够了。
    /// </summary>
    /// <param name="uri">当前绝对地址（<c>NavigationManager.Uri</c>）；空或不是合法地址返回 null</param>
    /// <returns>没有这个参数、或参数值不认识（<c>0</c> / <c>false</c> / 不是 Id 的文字 / 全 0 的 Id）返回 null</returns>
    public static AddBillOpenRequest? ReadOpenRequest(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        foreach (var pair in parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (!string.Equals(key, QueryKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];

            if (value.Length == 0
                || string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return new AddBillOpenRequest(null);
            }

            return Guid.TryParse(value, out var billId) && billId != Guid.Empty
                ? new AddBillOpenRequest(billId)
                : null;
        }

        return null;
    }

    /// <summary>地址是否要求升起记一笔 Sheet（新建或编辑都算）</summary>
    public static bool IsOpenRequest(string? uri) => ReadOpenRequest(uri) is not null;
}
