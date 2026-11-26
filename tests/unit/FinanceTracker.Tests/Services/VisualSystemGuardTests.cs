using System.Text.RegularExpressions;
using FinanceTracker.Tests.TestSupport;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 视觉体系的守卫（iOS UI 重构 · 24 号）：迁移完成后，防止旧视觉的遗留——MASA 控件、静态内联样式、写死的颜色、
/// 依赖页面栈内部 DOM 的全局兼容规则——被悄悄加回来。只扫描源码，不跑页面。
/// 视觉体系的规则见 docs/adr/0007-ios-visual-system.md：令牌唯一来源是 tokens.css，复用组件层在 Components/Ui 与 ui.css。
/// </summary>
public sealed class VisualSystemGuardTests
{
    /// <summary>
    /// 仍然允许出现的 MASA 标记：应用壳与错误边界（两个布局）、页面栈（二级页面的承载与顶栏容器）、图表容器（只在 EChartsView 里，由另一条守卫保证）。
    /// 需要引入别的 MASA 控件时先确认视觉与降级（23 号）都由 ui.css 的组件层接管，再把它加进这里并在 ADR 里记一笔。
    /// </summary>
    private static readonly HashSet<string> AllowedMasaTags = new(StringComparer.Ordinal)
    {
        "MApp", "MErrorHandler", "PPageStack", "PStackPageBar", "MECharts",
    };

    private static IEnumerable<string> RazorFiles() =>
        Directory
            .EnumerateFiles(RepoRoot.Combine("src"), "*.razor", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string file)
    {
        var separator = Path.DirectorySeparatorChar;
        return file.Contains($"{separator}obj{separator}") || file.Contains($"{separator}bin{separator}");
    }

    private static string Relative(string file) => Path.GetRelativePath(RepoRoot.Path, file);

    [Fact]
    public void 页面与组件只使用白名单里的MASA标记()
    {
        // MASA 标记的特征：M 或 P 开头、第二个字母大写（<MButton>、<MRow>、<PPageStack>）；自家组件（MonthlyStatisticsView、PendingBillCard……）第二个字母是小写
        var tag = new Regex(@"<(?<name>[MP][A-Z][A-Za-z0-9]*)\b");

        var offenders = RazorFiles()
            .SelectMany(file => tag.Matches(File.ReadAllText(file))
                .Select(match => (file, name: match.Groups["name"].Value)))
            .Where(entry => !AllowedMasaTags.Contains(entry.name))
            .Select(entry => $"{Relative(entry.file)}: <{entry.name}>")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "这些位置使用了白名单之外的 MASA 控件，请改用 Components/Ui 里的复用组件（或先扩充组件层）：\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void 没有静态内联样式_动态值除外()
    {
        // style="padding: 20px" 这类写死的样式属于旧视觉遗留，应当放进 ui.css 用令牌写；
        // 带 @ 的（style="width: @percent%"、style="@Style"）是运行时算出来的值，允许
        var staticStyle = new Regex(@"\s[Ss]tyle=""[^""@]*""");

        var offenders = RazorFiles()
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, line, number: index + 1))
                .Where(entry => staticStyle.IsMatch(entry.line)))
            .Select(entry => $"{Relative(entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "这些位置写了静态内联样式，请改成 ui.css 里的类（只引用令牌）：\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void 共享组件库的源码里没有写死的颜色_颜色只来自令牌或DesignTokens常量()
    {
        // ECharts 画在 canvas 上读不到 CSS 变量，所以 C# 侧需要同值的字面量——它们只允许出现在 DesignTokens.cs，并由 DesignTokensTests 校验与 tokens.css 一致
        var designTokens = Path.Combine("Design", "DesignTokens.cs");
        var color = new Regex(@"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b|\brgba?\(");

        var offenders = Directory
            .EnumerateFiles(RepoRoot.Combine("src/FinanceTracker.Web"), "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !IsBuildOutput(file) && !file.EndsWith(designTokens, StringComparison.Ordinal))
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, line, number: index + 1))
                .Where(entry => color.IsMatch(entry.line)))
            .Select(entry => $"{Relative(entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "这些位置写死了颜色，请改用令牌（样式）或 DesignTokens 常量（图表）：\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void 全局样式里没有依赖页面栈内部DOM的迁移期兼容规则()
    {
        // 迁移期兼容层（24 号清理）：让旧页面在沉浸式下正常显示的 .m-page-stack-item__bar / .container 规则。
        // 现在每个页面都自己使用 --ft-safe-*（二级页面的顶栏与内容区在 ui.css 的「二级页面」一节）
        var app = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/app.css"));

        Assert.DoesNotContain("m-page-stack-item", app, StringComparison.Ordinal);
        Assert.DoesNotContain("p-page-container", app, StringComparison.Ordinal);
        Assert.DoesNotContain(".container", app, StringComparison.Ordinal);
        Assert.DoesNotContain(".m-toolbar", app, StringComparison.Ordinal);
    }

    [Fact]
    public void 二级页面的内容区上内边距是正式规则_顶栏高度加状态栏安全区()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        Assert.Matches(
            @"\.m-page-stack-item__content\s*\{\s*padding-top:\s*calc\(var\(--ft-nav-height\)\s*\+\s*var\(--ft-safe-top,\s*0px\)\)\s*!important;\s*\}",
            ui);
    }

    [Fact]
    public void Tab栏脚本不再保留旧页面的容器兜底()
    {
        var script = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/js/tabBar.js"));

        Assert.DoesNotContain(".container", script, StringComparison.Ordinal);
        Assert.DoesNotContain("LEGACY_SCROLL_SELECTOR", script, StringComparison.Ordinal);
    }
}
