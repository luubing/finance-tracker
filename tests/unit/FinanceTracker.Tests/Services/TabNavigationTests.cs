using System.Text.RegularExpressions;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Shell;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 应用壳的 Tab 定义与导航规则（TabNavigation）：四个 Tab 的顺序与根页面、页面栈常驻规则、
/// 路径规整，以及「再次点按当前 Tab」的决策。纯逻辑，不依赖浏览器。
/// </summary>
public sealed class TabNavigationTests
{
    private static TabDefinition Tab(string key) => TabNavigation.FindByKey(key)!;

    [Fact]
    public void 四个Tab的顺序与根页面_首页_账单_统计_我的()
    {
        Assert.Equal(new[] { "home", "bills", "statistics", "profile" }, TabNavigation.Tabs.Select(tab => tab.Key));
        Assert.Equal(new[] { "首页", "账单", "统计", "我的" }, TabNavigation.Tabs.Select(tab => tab.Title));
        Assert.Equal(new[] { "/", "/bills", "/statistics", "/profile" }, TabNavigation.Tabs.Select(tab => tab.RootPath));
    }

    [Fact]
    public void 每个Tab都有成对的线框与实心图标()
    {
        foreach (var tab in TabNavigation.Tabs)
        {
            Assert.False(string.IsNullOrWhiteSpace(tab.OutlineIcon), tab.Key);
            Assert.False(string.IsNullOrWhiteSpace(tab.FilledIcon), tab.Key);
        }
    }

    [Fact]
    public void 账单加入页面栈的Tab常驻规则_每个根页面各一条且只命中自己()
    {
        var rules = TabNavigation.CreateTabRules();

        Assert.Equal(TabNavigation.Tabs.Count, rules.Count);
        foreach (var tab in TabNavigation.Tabs)
        {
            var matching = rules.Where(rule => rule.Regex.IsMatch(tab.RootPath)).ToList();
            Assert.Single(matching);
            Assert.True(matching[0].Persistent, $"{tab.Key} 必须常驻页面栈，切走再切回才能保持滚动位置与筛选状态");
        }
    }

    [Theory]
    [InlineData("/bills/123")]
    [InlineData("/ledgers")]
    [InlineData("/add-bill")]
    [InlineData("/statistics/trend")]
    [InlineData("/login")]
    public void 二级页面与登录页不命中任何Tab规则(string path)
    {
        var rules = TabNavigation.CreateTabRules();

        Assert.DoesNotContain(rules, rule => rule.Regex.IsMatch(path));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    [InlineData("/bills", "/bills")]
    [InlineData("/Bills/", "/bills")]
    [InlineData("/bills?type=expense", "/bills")]
    [InlineData("/bills#top", "/bills")]
    [InlineData("bills", "/bills")]
    [InlineData("https://localhost:5999/statistics?month=2026-11", "/statistics")]
    [InlineData("https://0.0.0.0/", "/")]
    [InlineData("https://0.0.0.0", "/")]
    [InlineData("https://0.0.0.0/ledgers/abc/members", "/ledgers/abc/members")]
    public void 路径规整_小写_无查询串_无片段_无末尾斜杠(string? input, string expected)
    {
        Assert.Equal(expected, TabNavigation.NormalizePath(input));
    }

    [Theory]
    [InlineData("/", "home")]
    [InlineData("https://0.0.0.0/", "home")]
    [InlineData("/bills", "bills")]
    [InlineData("/Statistics?month=2026-11", "statistics")]
    [InlineData("/profile/", "profile")]
    public void 根页面路径解析出所属Tab(string path, string expectedKey)
    {
        Assert.Equal(expectedKey, TabNavigation.ResolveRoot(path)?.Key);
        Assert.True(TabNavigation.IsTabRoot(path));
    }

    [Theory]
    [InlineData("/ledgers")]
    [InlineData("/ledgers/abc/members")]
    [InlineData("/add-bill")]
    [InlineData("/login")]
    [InlineData("/bills/edit")]
    public void 二级页面与登录页不是Tab根页面_Tab栏随之隐藏(string path)
    {
        Assert.Null(TabNavigation.ResolveRoot(path));
        Assert.False(TabNavigation.IsTabRoot(path));
    }

    [Fact]
    public void Tab序号与键对应_未知键回落到第一个()
    {
        Assert.Equal(0, TabNavigation.IndexOf("home"));
        Assert.Equal(1, TabNavigation.IndexOf("bills"));
        Assert.Equal(2, TabNavigation.IndexOf("statistics"));
        Assert.Equal(3, TabNavigation.IndexOf("profile"));
        Assert.Equal(0, TabNavigation.IndexOf("nope"));
        Assert.Equal(0, TabNavigation.IndexOf(null));
    }

    [Fact]
    public void 点按另一个Tab_切换过去()
    {
        var plan = TabNavigation.PlanTap(Tab("bills"), activeKey: "home", currentPath: "/");

        Assert.Equal(TabTapKind.SwitchTab, plan.Kind);
        Assert.Equal("/bills", plan.Tab.RootPath);
    }

    [Fact]
    public void 再次点按当前Tab且停在根页面_滚动到顶部()
    {
        var plan = TabNavigation.PlanTap(Tab("statistics"), activeKey: "statistics", currentPath: "/statistics");

        Assert.Equal(TabTapKind.ScrollToTop, plan.Kind);
    }

    [Fact]
    public void 再次点按当前Tab但停在它的二级页面_先回到根页面()
    {
        var plan = TabNavigation.PlanTap(Tab("profile"), activeKey: "profile", currentPath: "/ledgers");

        Assert.Equal(TabTapKind.PopToRoot, plan.Kind);
        Assert.Equal("/profile", plan.Tab.RootPath);
    }

    [Fact]
    public void 停在二级页面时点按另一个Tab_直接切换而不是回退()
    {
        var plan = TabNavigation.PlanTap(Tab("home"), activeKey: "profile", currentPath: "/ledgers");

        Assert.Equal(TabTapKind.SwitchTab, plan.Kind);
    }

    /// <summary>
    /// 切换到 Tab 用 Navigation.NavigateTo，绝不能把 Tab 根页面 Push 进页面栈（会得到两份同一个根页面，
    /// 还让 Tab 栏高亮与页面脱节）。这条守卫扫描共享组件库里的 .razor，防止以后有人把入口写回 Push。
    /// </summary>
    [Fact]
    public void 页面里不把Tab根页面Push进页面栈()
    {
        var roots = TabNavigation.Tabs.Select(tab => Regex.Escape(tab.RootPath)).ToList();
        var pushOfTabRoot = new Regex($@"\.Push\(\s*\$?""(?:{string.Join("|", roots)})""", RegexOptions.IgnoreCase);

        var offenders = Directory
            .EnumerateFiles(RepoRoot.Combine("src/FinanceTracker.Web"), "*.razor", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, line, number: index + 1))
                .Where(entry => pushOfTabRoot.IsMatch(entry.line)))
            .Select(entry => $"{Path.GetRelativePath(RepoRoot.Path, entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "以下位置把 Tab 根页面 Push 进了页面栈，应改用 NavigationManager.NavigateTo：\n" + string.Join("\n", offenders));
    }
}
