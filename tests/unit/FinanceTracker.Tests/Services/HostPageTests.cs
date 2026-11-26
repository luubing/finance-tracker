using System.Text.RegularExpressions;
using FinanceTracker.Tests.TestSupport;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 两个宿主页（Web.Server 的 <c>_Layout.cshtml</c>、MAUI 的 <c>wwwroot/index.html</c>）之间的一致性守卫。
/// E2E 只能访问 Web.Server，MAUI 宿主页（iOS 真机 / Android）没有任何自动化覆盖——只在其中一个宿主里漏掉的东西，
/// E2E 全绿、真机上才暴露。这里只扫描源码，把「两个宿主必须一致」的约定钉住。
/// </summary>
public sealed class HostPageTests
{
    private const string ServerHostPath = "server/FinanceTracker.Web.Server/Pages/_Layout.cshtml";
    private const string AppHostPath = "src/FinanceTracker.App/wwwroot/index.html";
    private const string AppCssPath = "src/FinanceTracker.Web/wwwroot/css/app.css";

    private static string Read(string relativePath) => File.ReadAllText(RepoRoot.Combine(relativePath));

    private static string WithoutCssComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string WithoutHtmlComments(string html) =>
        Regex.Replace(html, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

    [Fact]
    public void 错误提示条在共用样式里默认隐藏_并固定在屏幕底部不占文档流()
    {
        // iOS 真机冷启动之后「An unhandled error has occurred」一直挂在登录页最底部：
        // 当时隐藏规则只写在 Web.Server 的 _Layout.cshtml 里，MAUI 的 index.html 没有，元素成了文档末尾的普通文字
        var css = WithoutCssComments(Read(AppCssPath));

        var match = Regex.Match(css, @"(?:^|\})\s*#blazor-error-ui\s*\{(?<body>[^{}]*)\}", RegexOptions.Multiline);
        Assert.True(match.Success, "app.css 里找不到 #blazor-error-ui 规则");

        var body = match.Groups["body"].Value;
        Assert.Matches(@"(?<![-\w])display:\s*none\s*;", body);
        Assert.Matches(@"(?<![-\w])position:\s*fixed\s*;", body);
    }

    [Theory]
    [InlineData(ServerHostPath)]
    [InlineData(AppHostPath)]
    public void 宿主页不自己写错误提示条的样式_统一用共用样式(string hostPage)
    {
        // 两处各写一份迟早又漏一处：样式只在 app.css 里，宿主页的内联样式（<style> 块与 style 属性）里都不能出现这个元素
        var html = WithoutHtmlComments(Read(hostPage));

        var inlineStyles = Regex.Matches(html, @"<style\b[^>]*>(?<css>.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Select(m => m.Groups["css"].Value);

        Assert.DoesNotContain(inlineStyles, css => css.Contains("blazor-error-ui", StringComparison.Ordinal));
        Assert.DoesNotMatch(@"<[^>]*\bid=""blazor-error-ui""[^>]*\bstyle=", html);
    }

    [Theory]
    [InlineData(ServerHostPath)]
    [InlineData(AppHostPath)]
    public void 宿主页都有错误提示条元素_带重新加载与关闭(string hostPage)
    {
        // Blazor 的脚本按 #blazor-error-ui 找到它并显示，再给里面的 .reload / .dismiss 接上「刷新页面 / 收起」：
        // 元素缺失时未处理异常没有任何提示，结构不全时两个按钮点了没反应
        var html = WithoutHtmlComments(Read(hostPage));

        var bar = Regex.Match(html, @"<div\b[^>]*\bid=""blazor-error-ui""[^>]*>(?<inner>.*?)</div>", RegexOptions.Singleline);
        Assert.True(bar.Success, $"{hostPage} 里找不到 #blazor-error-ui");
        Assert.Contains("role=\"alert\"", bar.Value, StringComparison.Ordinal);
        Assert.Matches(@"<a\b[^>]*\bclass=""reload""[^>]*>[^<]*重新加载[^<]*</a>", bar.Groups["inner"].Value);
        Assert.Matches(@"<a\b[^>]*\bclass=""dismiss""[^>]*\baria-label=""关闭""[^>]*>", bar.Groups["inner"].Value);
    }
}
