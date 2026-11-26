using System.Text.RegularExpressions;
using Masa.Blazor.Presets.PageStack;

namespace FinanceTracker.Web.Shell;

/// <summary>
/// 一个 Tab：键、显示名、根页面路径，以及成对的 MDI 图标（线框 = 未选中，实心 = 选中）。
/// </summary>
public sealed record TabDefinition(string Key, string Title, string RootPath, string OutlineIcon, string FilledIcon);

/// <summary>
/// 再次点按 Tab 栏上某一项时要执行的动作。
/// </summary>
public enum TabTapKind
{
    /// <summary>点的是另一个 Tab：切换过去。</summary>
    SwitchTab,

    /// <summary>点的是当前 Tab，且正停在它的根页面：滚回顶部。</summary>
    ScrollToTop,

    /// <summary>点的是当前 Tab，但正停在它的二级页面：先退回根页面（与 iOS 一致，再点一次才回顶）。</summary>
    PopToRoot,
}

/// <param name="Kind">动作类型。</param>
/// <param name="Tab">被点按的 Tab（切换 / 回到根页面的目标）。</param>
public readonly record struct TabTapPlan(TabTapKind Kind, TabDefinition Tab);

/// <summary>
/// 应用壳的 Tab 定义与导航规则（纯逻辑，无 UI 依赖，便于单元测试）。
/// Tab 栏组件与页面栈的常驻规则都从这里取值，保证两边永远一致。
/// </summary>
public static class TabNavigation
{
    /// <summary>四个 Tab，顺序即 Tab 栏从左到右的顺序。</summary>
    public static IReadOnlyList<TabDefinition> Tabs { get; } =
    [
        new("home", "首页", "/", "home-variant-outline", "home-variant"),
        new("bills", "账单", "/bills", "text-box-multiple-outline", "text-box-multiple"),
        new("statistics", "统计", "/statistics", "chart-pie", "chart-pie"),
        new("profile", "我的", "/profile", "account-circle-outline", "account-circle"),
    ];

    /// <summary>
    /// 页面栈的 Tab 常驻规则：每个根页面一条，匹配整条路径。
    /// 命中规则的页面切走后保留在 DOM 中，滚动位置与筛选状态因此得以保持。
    /// </summary>
    public static HashSet<TabRule> CreateTabRules() =>
        Tabs.Select(tab => new TabRule($"^{Regex.Escape(tab.RootPath)}$")).ToHashSet();

    /// <summary>
    /// 把绝对 URI 或应用内路径规整成「小写无查询串、无片段、无末尾斜杠的绝对路径」，根路径为 "/"。
    /// 绝对 URI 不限协议：MAUI Blazor 宿主的根地址随平台而异（见 <see cref="ExtractPath"/>）。
    /// </summary>
    public static string NormalizePath(string? uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath))
        {
            return "/";
        }

        var path = ExtractPath(uriOrPath);

        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            path = path[..cut];
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        path = path.TrimEnd('/');
        return path.Length == 0 ? "/" : path.ToLowerInvariant();
    }

    /// <summary>
    /// 取出地址里的路径部分（带不带查询串 / 片段由调用方接着处理）。
    /// <list type="bullet">
    /// <item>以 "/" 开头的是应用内路径，原样返回，不交给 <see cref="Uri"/>：类 Unix 系统（Android / iOS / Linux）上
    /// <c>Uri.TryCreate("/bills", Absolute)</c> 会把它当成 <c>file://</c> 绝对路径，查询串还会被转义。</item>
    /// <item>其余能解析成绝对 URI 的取它的路径，<b>协议不能写死 http(s)</b>：MAUI Blazor 宿主给 <c>NavigationManager</c> 的根地址随平台而异——
    /// Android 是 <c>https://0.0.0.0/</c>、Windows 是 <c>https://0.0.0.1/</c>，而 iOS / Mac Catalyst 是 <c>app://0.0.0.0/</c>。
    /// 这里曾经只认 http(s)，iOS 真机上 <c>app://0.0.0.0/bills</c> 被当成普通字符串，规整成「/app://0.0.0.0/bills」，
    /// 不属于任何 Tab：Tab 栏在四个根页面上也一直隐藏，登录之后整个应用壳没法用（Android 与 Web.Server 都正常，E2E 看不出来）。</item>
    /// <item>都不是（"bills"、"bills?x=1" 这类相对写法）原样返回。</item>
    /// </list>
    /// </summary>
    private static string ExtractPath(string uriOrPath)
    {
        if (uriOrPath.StartsWith('/'))
        {
            return uriOrPath;
        }

        return Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : uriOrPath;
    }

    /// <summary>路径正好是某个 Tab 的根页面时返回该 Tab，否则（二级页面 / 登录页 / 未知路径）返回 null。</summary>
    public static TabDefinition? ResolveRoot(string? uriOrPath)
    {
        var path = NormalizePath(uriOrPath);
        return Tabs.FirstOrDefault(tab => tab.RootPath == path);
    }

    /// <summary>是否处于四个 Tab 根页面之一（Tab 栏只在这些页面显示）。</summary>
    public static bool IsTabRoot(string? uriOrPath) => ResolveRoot(uriOrPath) != null;

    /// <summary>按键查找 Tab。</summary>
    public static TabDefinition? FindByKey(string? key) =>
        Tabs.FirstOrDefault(tab => tab.Key == key);

    /// <summary>Tab 在 Tab 栏中的序号（从 0 开始）；未知键返回 0。</summary>
    public static int IndexOf(string? key)
    {
        for (var i = 0; i < Tabs.Count; i++)
        {
            if (Tabs[i].Key == key)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// 决定点按 <paramref name="tapped"/> 时做什么。
    /// </summary>
    /// <param name="tapped">被点按的 Tab。</param>
    /// <param name="activeKey">当前激活的 Tab 键。</param>
    /// <param name="currentPath">当前所在路径（绝对 URI 或应用内路径均可）。</param>
    public static TabTapPlan PlanTap(TabDefinition tapped, string? activeKey, string? currentPath)
    {
        if (tapped.Key != activeKey)
        {
            return new TabTapPlan(TabTapKind.SwitchTab, tapped);
        }

        return IsTabRoot(currentPath)
            ? new TabTapPlan(TabTapKind.ScrollToTop, tapped)
            : new TabTapPlan(TabTapKind.PopToRoot, tapped);
    }
}
