using System.Globalization;
using System.Text.RegularExpressions;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Design;
using FinanceTracker.Web.Models;
using Masa.Blazor;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 设计令牌：CSS 里的令牌（wwwroot/css/tokens.css）与 C# 侧取值（DesignTokens / MASA 主题）必须一致，
/// 且两个宿主（Web.Server 的 _Layout.cshtml 与 MAUI 的 index.html）加载的是同一份、同一顺序的样式。
/// </summary>
public sealed class DesignTokensTests
{
    private static readonly Lazy<string> TokensCss = new(() => File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/tokens.css")));

    /// <summary>读取 tokens.css 里某个自定义属性的值（取 :root 里的第一处定义）。</summary>
    private static string Token(string name)
    {
        var match = Regex.Match(TokensCss.Value, $@"^\s*{Regex.Escape(name)}\s*:\s*(?<value>[^;]+);", RegexOptions.Multiline);
        Assert.True(match.Success, $"tokens.css 缺少令牌 {name}");
        return match.Groups["value"].Value.Trim();
    }

    [Fact]
    public void 品牌色令牌与C_侧常量一致()
    {
        Assert.Equal(DesignTokens.BrandColor, Token("--ft-brand"), ignoreCase: true);
    }

    [Fact]
    public void 品牌色RGB令牌与品牌色一致()
    {
        var hex = DesignTokens.BrandColor.TrimStart('#');
        var expected = string.Join(", ", Enumerable.Range(0, 3)
            .Select(i => int.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

        Assert.Equal(expected, Token("--ft-brand-rgb"));
    }

    [Theory]
    [InlineData("--ft-danger", DesignTokens.DangerColor)]
    [InlineData("--ft-warning", DesignTokens.WarningColor)]
    [InlineData("--ft-info", DesignTokens.InfoColor)]
    [InlineData("--ft-success", DesignTokens.SuccessColor)]
    [InlineData("--ft-expense", DesignTokens.ExpenseColor)]
    [InlineData("--ft-income", DesignTokens.IncomeColor)]
    public void 语义色令牌与C_侧常量一致(string token, string expected)
    {
        Assert.Equal(expected, Token(token), ignoreCase: true);
    }

    [Fact]
    public void 图表主色浅底是主色百分之十叠在白底上的实色()
    {
        // ECharts 画在 canvas 上，没法用半透明的 --ft-brand-soft 叠在卡片上；热力图最浅一档取它叠白底的结果
        var expected = Over(Rgb(Token("--ft-brand")), 0.10, White);
        var tint = Rgb(DesignTokens.BrandTintColor);

        Assert.True(Math.Abs(tint.R - expected.R) <= 1 && Math.Abs(tint.G - expected.G) <= 1 && Math.Abs(tint.B - expected.B) <= 1,
            $"浅底 {DesignTokens.BrandTintColor}，主色 10% 叠白底应为 ({expected.R:0.#}, {expected.G:0.#}, {expected.B:0.#})");
    }

    [Theory]
    [InlineData("--ft-label-2", DesignTokens.ChartAxisTextColor)]
    [InlineData("--ft-separator", DesignTokens.ChartGridLineColor)]
    public void 图表的坐标轴文字与网格线颜色与令牌同值(string token, string chartValue)
    {
        // ECharts 读不到 CSS 变量，C# 侧写了一份同值的字面量。23 号把 --ft-label-2 从 .62 加深到 .76 时，
        // 图表坐标轴文字这一份没跟着改（12px 的刻度文字停在约 3.6:1），所以两边必须同值，改令牌漏改这边会被拦住
        var (expectedColor, expectedAlpha) = Rgba(Token(token));
        var (actualColor, actualAlpha) = Rgba(chartValue);

        Assert.Equal(expectedColor, actualColor);
        Assert.Equal(expectedAlpha, actualAlpha, 3);
    }

    [Fact]
    public void 日历热力图里没有数据的格子与页面底色同值()
    {
        Assert.Equal(DesignTokens.ChartEmptyCellColor, Token("--ft-bg"), ignoreCase: true);
    }

    [Fact]
    public void 图表坐标轴文字在卡片白底上达到AA对比度()
    {
        // 坐标轴刻度是 12px 小字，要求 ≥ 4.5:1（canvas 里的文字扫不到 DOM，对比度只能在这里按令牌值算）
        var (color, alpha) = Rgba(DesignTokens.ChartAxisTextColor);
        var text = Over(color, alpha, White);

        var ratio = Contrast(text, White);

        Assert.True(ratio >= 4.5, $"图表坐标轴文字在白底上的对比度 {ratio:0.00}:1，低于 4.5:1");
    }

    [Fact]
    public void MASA主题的主色与语义色取自设计令牌()
    {
        var theme = new Theme(new ThemeOptions(), new ThemeOptions());

        MasaBlazorDesignExtensions.ApplyTheme(theme);

        Assert.Equal(DesignTokens.BrandColor, theme.Themes.Light.Primary);
        Assert.Equal(DesignTokens.DangerColor, theme.Themes.Light.Error);
        Assert.Equal(DesignTokens.WarningColor, theme.Themes.Light.Warning);
        Assert.Equal(DesignTokens.InfoColor, theme.Themes.Light.Info);
        Assert.Equal(DesignTokens.SuccessColor, theme.Themes.Light.Success);
    }

    [Theory]
    [InlineData("--ft-tabbar-height", "62px")]
    [InlineData("--ft-tabbar-side", "16px")]
    [InlineData("--ft-tabbar-bottom", "21px")]
    [InlineData("--ft-radius-card", "26px")]
    [InlineData("--ft-radius-sheet", "40px")]
    [InlineData("--ft-radius-hero", "40px")]
    [InlineData("--ft-radius-input", "18px")]
    [InlineData("--ft-bg", "#F2F2F7")]
    [InlineData("--ft-expense", "#E8362C")]
    [InlineData("--ft-income", "#1FA544")]
    public void 关键令牌取值与设计稿06一致(string token, string expected)
    {
        Assert.Equal(expected, Token(token), ignoreCase: true);
    }

    [Theory]
    [InlineData("--ft-spring-response", ".42")]
    [InlineData("--ft-spring-damping", ".62")]
    [InlineData("--ft-stretch-max", ".7")]
    [InlineData("--ft-squash-max", ".07")]
    [InlineData("--ft-lift-scale", "1.08")]
    [InlineData("--ft-lift-icon-scale", ".9")]
    [InlineData("--ft-fab-press-scale", ".92")]
    [InlineData("--ft-collapse-threshold", "24")]
    [InlineData("--ft-page-enter-scale", ".985")]
    [InlineData("--ft-dur-press", "120ms")]
    [InlineData("--ft-dur-icon", "160ms")]
    [InlineData("--ft-dur-page", "180ms")]
    [InlineData("--ft-dur-reduced", "100ms")]
    public void Tab滑块动效参数取值与设计稿03一致(string token, string expected)
    {
        Assert.Equal(expected, Token(token));
    }

    [Fact]
    public void 动效令牌都被脚本读取_改令牌就能统一调整手感()
    {
        var script = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/js/tabBar.js"));

        foreach (var token in new[]
                 {
                     "--ft-spring-response", "--ft-spring-damping", "--ft-stretch-max", "--ft-squash-max",
                     "--ft-lift-scale", "--ft-collapse-threshold", "--ft-dur-reduced",
                 })
        {
            Assert.Contains(token, script);
        }
    }

    [Fact]
    public void 玻璃材质令牌齐全_浅色玻璃与Hero之上的深色玻璃()
    {
        // 浅色玻璃：白 58% + 模糊 22 + 饱和 200%；深色玻璃：白 17% + 模糊 20
        Assert.Equal("rgba(255, 255, 255, .58)", Token("--ft-glass-bg"));
        Assert.Contains("blur(22px)", Token("--ft-glass-filter"));
        Assert.Contains("saturate(200%)", Token("--ft-glass-filter"));
        Assert.Equal("rgba(255, 255, 255, .17)", Token("--ft-glass-dark-bg"));
        Assert.Contains("blur(20px)", Token("--ft-glass-dark-filter"));
    }

    private static int Px(string value) => int.Parse(value.Replace("px", string.Empty), CultureInfo.InvariantCulture);

    [Fact]
    public void 页面背景RGB令牌与页面背景色一致_导航栏渐隐用它()
    {
        var hex = Token("--ft-bg").TrimStart('#');
        var expected = string.Join(", ", Enumerable.Range(0, 3)
            .Select(i => int.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

        Assert.Equal(expected, Token("--ft-bg-rgb"));
    }

    [Theory]
    [InlineData("--ft-alert-width", "286px")]
    [InlineData("--ft-alert-radius", "22px")]
    [InlineData("--ft-tile-size", "40px")]
    [InlineData("--ft-swipe-action-width", "76px")]
    [InlineData("--ft-nav-fade", "14px")]
    [InlineData("--ft-cell-height", "56px")]
    [InlineData("--ft-cell-height-2", "66px")]
    public void 二级页面与弹层令牌取值与设计稿06一致(string token, string expected)
    {
        Assert.Equal(expected, Token(token));
    }

    [Fact]
    public void 带图标块的单元格分隔线缩进等于内边距加图标块加间距()
    {
        var expected = Px(Token("--ft-cell-pad")) + Px(Token("--ft-tile-size")) + Px(Token("--ft-cell-gap"));

        Assert.Equal(expected, Px(Token("--ft-cell-inset")));
    }

    [Fact]
    public void 分段控件每一段的点击区域不小于点击热区下限()
    {
        // 每一段的高度就是整条轨道的高度（滑块比轨道矮，但点击区域不是滑块）
        Assert.True(Px(Token("--ft-seg-height")) >= Px(Token("--ft-hit")), "分段控件每一段的点击区域不能小于 44 × 44");
    }

    [Fact]
    public void 标签的视觉高度不超过点击热区_命中区域由按钮本体的最小高度保证()
    {
        // 标签视觉上是 36 高的紧凑胶囊，但读屏 / 键盘 / 命中审计看到的按钮必须是 44：胶囊用伪元素画在按钮中间
        Assert.True(Px(Token("--ft-chip-height")) <= Px(Token("--ft-hit")), "标签的视觉高度不应超过按钮本体");

        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        foreach (var selector in new[] { @"\.ft-chip", @"\.ft-chip__main,\s*\.ft-chip__remove" })
        {
            Assert.Matches($@"(?m)^{selector}\s*\{{[^}}]*min-height:\s*var\(--ft-hit\)", ui);
        }
    }

    [Fact]
    public void 多选操作栏的按钮放得进栏高_且栏高不小于点击热区()
    {
        Assert.True(Px(Token("--ft-actionbar-height")) >= Px(Token("--ft-hit")), "操作栏里的按钮是 44 高，栏高不能比它矮");
    }

    [Fact]
    public void 层级令牌的先后关系_操作栏_Tab栏_Sheet_弹出菜单_语音覆盖层_Alert_Toast()
    {
        var actionBar = int.Parse(Token("--ft-z-actionbar"), CultureInfo.InvariantCulture);
        var tabBar = int.Parse(Token("--ft-z-tabbar"), CultureInfo.InvariantCulture);
        var sheet = int.Parse(Token("--ft-z-sheet"), CultureInfo.InvariantCulture);
        var popover = int.Parse(Token("--ft-z-popover"), CultureInfo.InvariantCulture);
        var voice = int.Parse(Token("--ft-z-voice"), CultureInfo.InvariantCulture);
        var alert = int.Parse(Token("--ft-z-alert"), CultureInfo.InvariantCulture);
        var toast = int.Parse(Token("--ft-z-toast"), CultureInfo.InvariantCulture);

        Assert.True(actionBar < tabBar, "多选操作栏和 Tab 栏上下排开、本不重叠；真重叠时 Tab 栏优先");
        Assert.True(tabBar < sheet, "Sheet 要盖住 Tab 栏");
        Assert.True(sheet < popover, "弹出菜单（记一笔里的账本 / 支付渠道菜单）从 Sheet 里的行展开，要盖在 Sheet 上");
        Assert.True(popover < voice, "语音覆盖层在记一笔 Sheet 里启动，要盖住 Sheet 与它的弹出菜单");
        Assert.True(voice < alert, "Alert 要盖住语音覆盖层");
        Assert.True(popover < alert, "Alert 要盖住弹出菜单");
        Assert.True(sheet < alert, "Alert / 操作面板要盖住 Sheet（Sheet 里删除会弹 Alert）");
        Assert.True(alert < toast, "Toast 在最上层，提示不能被弹层挡住");
        Assert.True(toast > 1048, "Toast 要高于 MASA 排队消息容器本身的层级（1048）");
    }

    [Theory]
    [InlineData("src/FinanceTracker.Web/wwwroot/css/tokens.css")]
    [InlineData("src/FinanceTracker.Web/wwwroot/css/ui.css")]
    [InlineData("src/FinanceTracker.Web/wwwroot/css/boot.css")]
    public void 令牌与组件样式不使用iOS14不支持的特性(string file)
    {
        // 注释里会解释"为什么不用"，先去掉注释再检查
        var css = Regex.Replace(File.ReadAllText(RepoRoot.Combine(file)), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        foreach (var banned in new[] { ":has(", "aspect-ratio", "color-mix(", "dvh", "svh", "lvh" })
        {
            Assert.DoesNotContain(banned, css);
        }
    }

    [Fact]
    public void 组件样式引用的令牌都有定义()
    {
        // 这几个不在 tokens.css：安全区变量由 app.css 声明、immersive.js 在运行时覆盖；其余是脚本或组件的 style 属性在运行时写入的变量
        var runtimeOrAppLevel = new HashSet<string>
        {
            "--ft-safe-top", "--ft-safe-right", "--ft-safe-bottom", "--ft-safe-left",
            "--ft-tab-x", "--ft-sheet-scrim-opacity",
            "--ft-ptr-offset", "--ft-ptr-progress",
            "--ft-seg-index", "--ft-seg-count",
            "--ft-wave-h", "--ft-wave-delay",
            "--ft-amount-chars", // AmountDisplay 的 FitWidth 写在 style 属性上的金额字符数（样式表里带着默认值 1）
        };
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        var defined = Regex.Matches(TokensCss.Value, @"^\s*(?<name>--ft-[a-z0-9-]+)\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet();

        var undefinedTokens = Regex.Matches(ui, @"var\((?<name>--ft-[a-z0-9-]+)")
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .Where(name => !defined.Contains(name) && !runtimeOrAppLevel.Contains(name))
            .ToList();

        Assert.True(undefinedTokens.Count == 0, $"ui.css 引用了没有定义的令牌：{string.Join(", ", undefinedTokens)}");
    }

    // ---- Tab 栏距底：设计稿 01 里 Tab 栏下沿离屏幕底 21，Home 指示条在它下面（Tab 栏压住底部安全区里的指示条） ----

    [Fact]
    public void Tab栏距底令牌是_21_加底部安全区里必须整个避开的部分_而不是整个底部安全区()
    {
        var offset = Token("--ft-tabbar-offset");

        Assert.Contains("var(--ft-tabbar-bottom)", offset);
        Assert.Contains("var(--ft-safe-bottom-solid", offset);
        // 把整个 --ft-safe-bottom（iPhone 上 34）加进去，Tab 栏就比设计稿高出 34、与 Home 指示条之间空出一大截
        Assert.DoesNotMatch(@"--ft-safe-bottom(?!-)", offset);
    }

    [Theory]
    [InlineData("--ft-tabbar-clearance")]
    [InlineData("--ft-toast-bottom")]
    public void 依赖Tab栏位置的令牌都以_tabbar_offset_为准(string token)
    {
        var value = Token(token);

        Assert.Contains("var(--ft-tabbar-offset)", value);
        Assert.DoesNotMatch(@"--ft-tabbar-bottom|--ft-safe-bottom(?!-)", value);
    }

    [Fact]
    public void 组件样式里Tab栏的距底只引用_tabbar_offset_不再自己拼安全区()
    {
        // Tab 栏本身、Toast、多选操作栏、页面底部留白都要和 Tab 栏的真实位置一致。谁再把 --ft-tabbar-bottom 和 --ft-safe-bottom 拼在一起，
        // iPhone 上 Tab 栏就会重新高出一截（用户反馈过「Tab 距离 iOS 底部安全域间距太大」）
        var ui = UiCssWithoutComments();

        Assert.DoesNotContain("var(--ft-tabbar-bottom", ui);
        Assert.Matches(@"\.ft-tabwrap\s*\{[^}]*\bbottom:\s*var\(--ft-tabbar-offset\)\s*;", ui);
    }

    [Fact]
    public void 底部安全区的实心部分等于整个底部边距减去指示条_且不会小于0()
    {
        var app = Regex.Replace(
            File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/app.css")), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        // 指示条：原生宿主注入 → env(safe-area-inset-bottom) → 0（Web.Server 在真 iPhone 的 Safari 里也是同一口径）
        Assert.Matches(@"--ft-safe-indicator:\s*var\(--ft-native-safe-indicator,\s*env\(safe-area-inset-bottom,\s*0px\)\)\s*;", app);
        Assert.Matches(@"--ft-safe-bottom-solid:\s*max\(0px,\s*calc\(var\(--ft-safe-bottom\)\s*-\s*var\(--ft-safe-indicator\)\)\)\s*;", app);
    }

    // ---- 文字对比度（WCAG AA：正文 ≥ 4.5:1）。本组只覆盖首页重做新增 / 调整的文字色令牌；全局审计见无障碍加固 ticket ----

    private static (double R, double G, double B) Rgb(string hex)
    {
        var value = hex.TrimStart('#');
        return (
            int.Parse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>半透明色叠在不透明底色上之后的实际颜色</summary>
    private static (double R, double G, double B) Over((double R, double G, double B) top, double alpha, (double R, double G, double B) bottom) =>
        (top.R * alpha + bottom.R * (1 - alpha), top.G * alpha + bottom.G * (1 - alpha), top.B * alpha + bottom.B * (1 - alpha));

    private static double Luminance((double R, double G, double B) color)
    {
        static double Channel(double value)
        {
            var c = value / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static double Contrast((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var (light, dark) = (Luminance(a), Luminance(b));
        if (light < dark)
        {
            (light, dark) = (dark, light);
        }

        return (light + 0.05) / (dark + 0.05);
    }

    private static readonly (double R, double G, double B) White = (255, 255, 255);

    [Theory]
    [InlineData("--ft-income-text")]
    [InlineData("--ft-warning-text")]
    [InlineData("--ft-danger-text")]
    public void 金额与状态文字色在白色卡片上达到AA对比度(string token)
    {
        var ratio = Contrast(Rgb(Token(token)), White);

        Assert.True(ratio >= 4.5, $"{token} 在白底上的对比度 {ratio:0.00}:1，低于 4.5:1");
    }

    [Fact]
    public void 预警文字色在16百分比橙浅底的徽标上也达到AA对比度()
    {
        // .ft-badge--warning 的底是 rgba(255, 149, 0, .16)
        var badgeBackground = Over((255, 149, 0), .16, White);

        var ratio = Contrast(Rgb(Token("--ft-warning-text")), badgeBackground);

        Assert.True(ratio >= 4.5, $"预警徽标文字对比度 {ratio:0.00}:1，低于 4.5:1");
    }

    [Fact]
    public void 角标红底上的白字达到AA对比度()
    {
        var ratio = Contrast(White, Rgb(Token("--ft-badge-solid")));

        Assert.True(ratio >= 4.5, $"角标白字对比度 {ratio:0.00}:1，低于 4.5:1");
    }

    // ---- 记一笔 Sheet（15 号）：数字键盘 / 紧凑单元格 / 分类网格的尺寸与对比度 ----

    [Fact]
    public void 数字键盘的键高_紧凑单元格与弹出菜单项的行高都不小于点击热区()
    {
        var hit = Px(Token("--ft-hit"));

        Assert.True(Px(Token("--ft-key-height")) >= hit, "数字键的高度不能小于 44");
        Assert.True(Px(Token("--ft-popover-item-height")) >= hit, "弹出菜单项整行都是点击区域，行高不能小于 44");
        Assert.True(Px(Token("--ft-cell-height-compact")) >= hit, "紧凑单元格整行都是点击区域，行高不能小于 44");
        Assert.True(Px(Token("--ft-tile-size-compact")) < Px(Token("--ft-cell-height-compact")), "紧凑单元格的图标块要小于行高");
    }

    [Fact]
    public void 分类网格一行放得下图标圆和名称()
    {
        // 一格 = 上内边距 6 + 图标圆 + 间距 6 + 名称一行（取自名称字体令牌里的行高）
        var lineHeight = int.Parse(Regex.Match(Token("--ft-type-cat-label"), @"/(?<h>\d+)px").Groups["h"].Value, CultureInfo.InvariantCulture);

        Assert.True(Px(Token("--ft-cat-row-height")) >= 6 + Px(Token("--ft-cat-size")) + 6 + lineHeight, "分类网格一行装不下图标圆和名称");
        Assert.True(Px(Token("--ft-cat-size")) <= Px(Token("--ft-tabbar-height")), "分类图标圆比 Tab 栏还高，网格比例失衡");
    }

    /// <summary>解析 rgb(r, g, b) / rgba(r, g, b, a) 写法的令牌</summary>
    private static ((double R, double G, double B) Color, double Alpha) Rgba(string value)
    {
        var match = Regex.Match(value, @"^rgba?\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)\s*(?:,\s*(?<a>\d*\.?\d+))?\s*\)$");
        Assert.True(match.Success, $"不是 rgb() / rgba() 写法：{value}");

        double Channel(string name) => int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture);
        var alpha = match.Groups["a"].Success ? double.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture) : 1d;

        return ((Channel("r"), Channel("g"), Channel("b")), alpha);
    }

    [Fact]
    public void 保存键置灰时的文字达到大号文字的AA对比度()
    {
        // 置灰的「保存」键：浅灰键面（半透明）叠在页面底色上，文字是次级文字色（半透明）叠在键面上；
        // 它仍可点按（引导补全），文字是 20px 粗体（≥ 18.66px 粗体算大号文字），要求 ≥ 3:1
        var (faceColor, faceAlpha) = Rgba(Token("--ft-key-save-off-bg"));
        var face = Over(faceColor, faceAlpha, Rgb(Token("--ft-bg")));
        var (textColor, textAlpha) = Rgba(Token("--ft-label-2"));
        var text = Over(textColor, textAlpha, face);

        var ratio = Contrast(text, face);

        Assert.True(ratio >= 3.0, $"置灰保存键文字对比度 {ratio:0.00}:1，低于 3:1");
    }

    [Theory]
    [InlineData("--ft-income-text")]
    [InlineData("--ft-danger-text")]
    public void 记一笔大号金额的收入与支出颜色在页面底色上达到大号文字的AA对比度(string token)
    {
        // 金额输入区没有白色卡片衬底，直接叠在 Sheet 的页面底色（--ft-bg）上；
        // 它是 56px 粗体（≥ 24px 即算大号文字），AA 要求 ≥ 3:1（深绿在 #F2F2F7 上约 4.2:1，红约 4.7:1）
        var ratio = Contrast(Rgb(Token(token)), Rgb(Token("--ft-bg")));

        Assert.True(ratio >= 3.0, $"{token} 在页面底色上的对比度 {ratio:0.00}:1，低于 3:1");
    }

    [Theory]
    [InlineData("server/FinanceTracker.Web.Server/Pages/_Layout.cshtml")]
    [InlineData("src/FinanceTracker.App/wwwroot/index.html")]
    public void 两个宿主加载同一份令牌_顺序是令牌_全局_组件层(string hostPage)
    {
        var html = File.ReadAllText(RepoRoot.Combine(hostPage));

        var masa = html.IndexOf("masa-blazor.min.css", StringComparison.Ordinal);
        var tokens = html.IndexOf("_content/FinanceTracker.Web/css/tokens.css", StringComparison.Ordinal);
        var app = html.IndexOf("_content/FinanceTracker.Web/css/app.css", StringComparison.Ordinal);
        var ui = html.IndexOf("_content/FinanceTracker.Web/css/ui.css", StringComparison.Ordinal);

        Assert.True(masa >= 0 && tokens > masa && app > tokens && ui > app,
            $"{hostPage} 的样式加载顺序应为 masa → tokens → app → ui（实际位置 masa={masa} tokens={tokens} app={app} ui={ui}）");
    }

    // ---- 语音记账覆盖层（17 号；第 2 轮评审按用户决定改成贴近设计稿的浅磨砂） ----

    /// <summary>字体令牌「700 19px/24px var(--ft-font)」里的字重与字号</summary>
    private static (int Weight, double Size) FontWeightAndSize(string token)
    {
        var match = Regex.Match(Token(token), @"^(?<weight>\d+)\s+(?<size>\d+(?:\.\d+)?)px/");
        Assert.True(match.Success, $"{token} 不是「字重 字号px/行高」写法：{Token(token)}");

        return (
            int.Parse(match.Groups["weight"].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>WCAG 的大号文字：≥ 24px，或 ≥ 18.66px（14pt）且 ≥ 700。大号文字只要求 3:1，其余按正文要求 4.5:1</summary>
    private static bool IsLargeText(int weight, double size) => size >= 24 || (size >= 18.66 && weight >= 700);

    [Theory]
    [InlineData("--ft-voice-bg")]
    [InlineData("--ft-voice-bg-cancel")]
    public void 语音覆盖层的白字与淡紫高亮在最亮的底上也达到大号文字的AA对比度(string backgroundToken)
    {
        // 覆盖层的底是半透明的浅磨砂，浅色页面会透上来；最不利的情况是透上来的页面纯白。
        // 这样的底上白字只有约 4:1、淡紫高亮约 3.3:1，到不了正文的 4.5:1——所以压在它上面的字必须是大号文字（见下一条），按 3:1 要求
        var (color, alpha) = Rgba(Token(backgroundToken));
        var background = Over(color, alpha, White);

        var text = Contrast(Rgb(Token("--ft-voice-text")), background);
        Assert.True(text >= 3, $"白字在 {backgroundToken}（叠在纯白上）的对比度 {text:0.00}:1，低于大号文字的 3:1");

        var (secondaryColor, secondaryAlpha) = Rgba(Token("--ft-voice-text-2"));
        var secondary = Contrast(Over(secondaryColor, secondaryAlpha, background), background);
        Assert.True(secondary >= 3, $"提示文字在 {backgroundToken}（叠在纯白上）的对比度 {secondary:0.00}:1，低于大号文字的 3:1");

        var mark = Contrast(Rgb(Token("--ft-voice-mark")), background);
        Assert.True(mark >= 3, $"淡紫高亮在 {backgroundToken}（叠在纯白上）的对比度 {mark:0.00}:1，低于大号文字的 3:1");
    }

    [Fact]
    public void 语音覆盖层里直接压在浅磨砂上的文字都是大号文字_转写_状态_占位_提示_说明_取消()
    {
        // 浅磨砂上的白字过不了 4.5:1，只能靠「大号文字」把要求降到 3:1：这三档字体令牌必须保持大号
        foreach (var token in new[] { "--ft-type-voice-transcript", "--ft-type-voice-status", "--ft-type-voice-note" })
        {
            var (weight, size) = FontWeightAndSize(token);
            Assert.True(IsLargeText(weight, size), $"{token} 是 {weight} {size}px，不是大号文字（≥ 18.66px 且 ≥ 700，或 ≥ 24px）");
        }

        // 样式规则确实用了这三档令牌，没有在某处另写一个小字号绕过去
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        var expected = new (string Selector, string Token)[]
        {
            (".ft-voice__status", "--ft-type-voice-status"),
            (".ft-voice__placeholder", "--ft-type-voice-status"),
            (".ft-voice__text", "--ft-type-voice-transcript"),
            (".ft-voice__hint", "--ft-type-voice-note"),
            (".ft-voice__foot", "--ft-type-voice-note"),
            (".ft-voice__cancel", "--ft-type-voice-note"),
        };

        foreach (var (selector, token) in expected)
        {
            var rule = Regex.Match(ui, $@"^{Regex.Escape(selector)}\s*\{{(?<body>[^{{}}]*)\}}", RegexOptions.Multiline);
            Assert.True(rule.Success, $"ui.css 里找不到 {selector} 规则");
            Assert.Contains($"font: var({token})", rule.Groups["body"].Value);
        }
    }

    [Theory]
    [InlineData("--ft-voice-bg")]
    [InlineData("--ft-voice-bg-cancel")]
    public void 语音覆盖层的已识别芯片白字在深色芯片底上也达到AA对比度_最亮的底也一样(string backgroundToken)
    {
        // 芯片文字比其它字小（15px 半粗体，不是大号文字），所以不靠放大，靠深色半透明的芯片底把对比度抬到正文要求的 4.5:1：
        // 芯片底叠在覆盖层上、覆盖层再叠在纯白上
        var (color, alpha) = Rgba(Token(backgroundToken));
        var overlay = Over(color, alpha, White);
        var (chipColor, chipAlpha) = Rgba(Token("--ft-voice-chip-bg"));
        var chip = Over(chipColor, chipAlpha, overlay);

        var (weight, size) = FontWeightAndSize("--ft-type-voice-chip");
        var required = IsLargeText(weight, size) ? 3 : 4.5;
        var ratio = Contrast(Rgb(Token("--ft-voice-text")), chip);

        Assert.True(ratio >= required, $"芯片白字在 {backgroundToken} 之上的芯片底上的对比度 {ratio:0.00}:1，低于 {required}:1");
    }

    [Theory]
    [InlineData("--ft-voice-bg-solid")]
    [InlineData("--ft-voice-bg-cancel-solid")]
    public void 语音覆盖层在降低透明度的不透明底上白字与淡紫高亮达到AA对比度(string backgroundToken)
    {
        var background = Rgb(Token(backgroundToken));

        Assert.True(Contrast(Rgb(Token("--ft-voice-text")), background) >= 4.5, $"白字在 {backgroundToken} 上低于 4.5:1");
        Assert.True(Contrast(Rgb(Token("--ft-voice-mark")), background) >= 4.5, $"淡紫高亮在 {backgroundToken} 上低于 4.5:1");
    }

    [Fact]
    public void 语音覆盖层的降低透明度分支改用不透明底并关掉模糊()
    {
        var match = Regex.Match(
            TokensCss.Value,
            @"@media \(prefers-reduced-transparency: reduce\)\s*\{\s*:root\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);

        Assert.True(match.Success, "tokens.css 缺少 prefers-reduced-transparency 分支");
        Assert.Contains("--ft-voice-bg: var(--ft-voice-bg-solid)", match.Groups["body"].Value);
        Assert.Contains("--ft-voice-bg-cancel: var(--ft-voice-bg-cancel-solid)", match.Groups["body"].Value);
        Assert.Contains("--ft-voice-filter: none", match.Groups["body"].Value);
    }

    [Fact]
    public void 语音覆盖层的转写区与芯片区按固定行数预留高度_最窄屏也放得下转写字数上限()
    {
        var transcript = Token("--ft-type-voice-transcript");
        var lineHeight = int.Parse(Regex.Match(transcript, @"/(?<h>\d+)px").Groups["h"].Value, CultureInfo.InvariantCulture);
        var fontSize = int.Parse(Regex.Match(transcript, @"(?<s>\d+)px/").Groups["s"].Value, CultureInfo.InvariantCulture);

        Assert.Equal(3 * lineHeight, Px(Token("--ft-voice-transcript-height")));

        // 最窄按 320px 宽的屏算：内容区左右各留 20px，一行放得下几个字、3 行放得下几个字
        var perLine = (320 - 40) / fontSize;
        Assert.True(
            VoicePresentation.TranscriptMaxChars <= 3 * perLine,
            $"转写字数上限 {VoicePresentation.TranscriptMaxChars} 超过最窄屏 3 行能放的 {3 * perLine} 个字");

        Assert.Equal((2 * Px(Token("--ft-voice-chip-height"))) + 10, Px(Token("--ft-voice-chips-height")));
    }

    [Fact]
    public void 语音覆盖层的淡出时长与淡入淡出令牌一致_大麦克风不小于点击热区()
    {
        Assert.Equal($"{VoicePresentation.LeaveMs}ms", Token("--ft-dur-fade"));
        Assert.True(Px(Token("--ft-voice-mic-size")) >= Px(Token("--ft-hit")), "点按形态的大麦克风是「说完了」按钮，不能小于 44");
    }

    [Fact]
    public void 语音覆盖层整屏只占一个玻璃层_芯片与按钮都不模糊()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        var withBlur = Regex.Matches(ui, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Where(m => m.Groups["selector"].Value.Contains(".ft-voice", StringComparison.Ordinal)
                        && m.Groups["body"].Value.Contains("backdrop-filter", StringComparison.Ordinal))
            .Select(m => m.Groups["selector"].Value.Trim())
            .ToList();

        // 去掉前面规则留下的注释再比较选择器
        var selectors = withBlur.Select(s => Regex.Replace(s, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline).Trim()).ToList();

        Assert.Equal([".ft-voice"], selectors);
    }

    // ---- 预算页（18 号） ----

    [Fact]
    public void 日均可用提示条的警示文字在浅橙底叠在页面底色上也达到AA对比度()
    {
        // 提示条没有白色衬底：16% 橙叠在页面底色（--ft-bg）上；文字是 15px 半粗体，按正文要求 ≥ 4.5:1。
        // 23 号把通用的 --ft-warning-text 加深到在这块底上也达标，提示条不再需要单独的文字色令牌
        var (color, alpha) = Rgba(Token("--ft-callout-warning-bg"));
        var background = Over(color, alpha, Rgb(Token("--ft-bg")));

        var ratio = Contrast(Rgb(Token("--ft-warning-text")), background);

        Assert.True(ratio >= 4.5, $"日均可用提示条文字对比度 {ratio:0.00}:1，低于 4.5:1");
        Assert.DoesNotContain("--ft-callout-warning-text", TokensCss.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void 预算页的总预算额度字号随屏宽收缩_长额度档比最小档还小或相等()
    {
        // 窄屏（320）：clamp 的下限就是最小字号；长额度档不能比它大，否则缩了等于没缩
        var clamp = Regex.Match(Token("--ft-type-amount-total"), @"clamp\((?<min>\d+)px,\s*[\d.]+vw,\s*(?<max>\d+)px\)");
        Assert.True(clamp.Success, "总预算额度字号应是 clamp(最小, 屏宽比例, 最大) 的写法");

        var min = int.Parse(clamp.Groups["min"].Value, CultureInfo.InvariantCulture);
        var max = int.Parse(clamp.Groups["max"].Value, CultureInfo.InvariantCulture);
        var longSize = int.Parse(Regex.Match(Token("--ft-type-amount-total-long"), @"800 (?<size>\d+)px").Groups["size"].Value, CultureInfo.InvariantCulture);

        Assert.True(min <= max, "clamp 的下限不能大于上限");
        Assert.True(longSize <= min, "长额度档（≥ 12 个字符）要不大于最小字号");
    }

    [Fact]
    public void 预算页的进度环孔够放百分比文字_环宽与半径在viewBox内()
    {
        // 几何：中线半径 + 环宽 / 2 = 外缘，必须正好撑满 viewBox（不被裁、不留白）；孔的直径占外径的比例要给文字留位
        Assert.Equal(RingGeometry.ViewBox / 2, RingGeometry.Radius + RingGeometry.StrokeWidth / 2, precision: 6);

        var holeRatio = (RingGeometry.Radius - RingGeometry.StrokeWidth / 2) * 2 / RingGeometry.ViewBox;
        Assert.InRange(holeRatio, 0.6, 0.8);

        // 外径取 120px 与 33vw 中较小者（窄屏 320 时是 105px）；环中心的百分比字号同样随屏宽收缩（clamp），不会在窄屏挤出孔
        Assert.Contains("120px", Token("--ft-ring-size"));
        Assert.Contains("clamp(", Token("--ft-type-ring-percent"));
    }

    [Fact]
    public void 预算页自己的表面不使用玻璃_整页的玻璃层只来自导航栏的圆钮()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        var withBlur = Regex.Matches(ui, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Where(m => m.Groups["body"].Value.Contains("backdrop-filter", StringComparison.Ordinal))
            .Select(m => Regex.Replace(m.Groups["selector"].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline).Trim())
            .ToList();

        foreach (var prefix in new[] { ".ft-ring", ".ft-budgetcard", ".ft-callout", ".ft-budgetform", ".ft-budgetrow", ".ft-budgets", ".ft-amountentry" })
        {
            Assert.DoesNotContain(withBlur, selector => selector.Contains(prefix, StringComparison.Ordinal));
        }
    }

    // ---- 待确认账单 / 短信识别（19 号） ----

    [Fact]
    public void 均已开启状态条的深绿文字在浅绿底叠在页面底色上也达到AA对比度()
    {
        // 状态条没有白色衬底：15% 绿叠在页面底色（--ft-bg）上；文字是 14px 半粗体，按正文要求 ≥ 4.5:1
        var (color, alpha) = Rgba(Token("--ft-capture-ready-bg"));
        var background = Over(color, alpha, Rgb(Token("--ft-bg")));

        var ratio = Contrast(Rgb(Token("--ft-capture-ready-text")), background);

        Assert.True(ratio >= 4.5, $"状态条文字对比度 {ratio:0.00}:1，低于 4.5:1");

        // 通用的深绿图标色 --ft-ink-green 在这块底上不够——这正是状态条要单独一个文字色令牌的原因
        var generic = Contrast(Rgb(Token("--ft-ink-green")), background);
        Assert.True(generic < 4.5, "若通用深绿已经够用，就不需要单独的状态条文字色令牌了");
    }

    [Fact]
    public void 账本芯片没选账本时的红字在浅红底叠在白色卡片上达到AA对比度()
    {
        var (color, alpha) = Rgba(Token("--ft-pcard-missing-bg"));
        var background = Over(color, alpha, White);

        var ratio = Contrast(Rgb(Token("--ft-danger-text")), background);

        Assert.True(ratio >= 4.5, $"账本芯片红字对比度 {ratio:0.00}:1，低于 4.5:1");
    }

    [Fact]
    public void 导航栏的全部忽略红字在最暗的玻璃底上也达到AA对比度()
    {
        // 玻璃是半透明白叠在页面底色上；红字用加深的 --ft-danger-text。最不利的是玻璃下方恰好是最深的页面底色
        var ratio = Contrast(Rgb(Token("--ft-danger-text")), Rgb(Token("--ft-bg")));

        Assert.True(ratio >= 4.5, $"全部忽略红字在页面底色上的对比度 {ratio:0.00}:1，低于 4.5:1");
    }

    [Fact]
    public void 待确认账单与短信识别的表面不使用玻璃_整页的玻璃层只来自导航栏的钮与Sheet自己的关闭钮()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        var withBlur = Regex.Matches(ui, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Where(m => m.Groups["body"].Value.Contains("backdrop-filter", StringComparison.Ordinal))
            .Select(m => Regex.Replace(m.Groups["selector"].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline).Trim())
            .ToList();

        foreach (var prefix in new[] { ".ft-pending", ".ft-pcard", ".ft-capture", ".ft-caprow", ".ft-confirm", ".ft-sms", ".ft-smscard", ".ft-pagefoot" })
        {
            Assert.DoesNotContain(withBlur, selector => selector.Contains(prefix, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void 页面底部固定主按钮区放得下大号按钮_高度令牌与内容的底部留白一致()
    {
        // 按钮 52（大号）+ 上下各 12 的留白 = 76；SMS 页的内容用同一个令牌留出底部空间，最后一张卡片不会被固定区挡住
        Assert.Equal(52 + 12 + 12, Px(Token("--ft-pagefoot-height")));

        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        Assert.Matches(@"\.ft-sms\s*\{[^}]*padding-bottom:\s*calc\(var\(--ft-pagefoot-height\)", ui);
    }

    // ---- 导入账单（20 号） ----

    [Fact]
    public void 导入账单页的内容底部留白与短信识别页一样取主按钮区的高度令牌()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        Assert.Matches(@"\.ft-import\s*\{[^}]*padding-bottom:\s*calc\(var\(--ft-pagefoot-height\)", ui);
    }

    [Fact]
    public void 导入账单页的错误提示是白卡上的危险文字色_不另加浅红底()
    {
        // 浅红底叠在页面底色（比白色暗）上，--ft-danger-text 只有 ≈4.1:1，达不到 4.5:1；所以错误提示的底只能是白色卡片
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        Assert.Matches(@"\.ft-import__error\s*\{[^}]*color:\s*var\(--ft-danger-text\)", ui);
        Assert.Matches(@"\.ft-import__error--card\s*\{[^}]*background:\s*var\(--ft-card\)", ui);

        var onCard = Contrast(Rgb(Token("--ft-danger-text")), White);
        Assert.True(onCard >= 4.5, $"导入错误提示红字在白卡上的对比度 {onCard:0.00}:1，低于 4.5:1");

        // 反过来锁住「为什么不加浅红底」：10% 红叠在页面底色上，对比度确实不够
        var tinted = Over((255, 59, 48), .10, Rgb(Token("--ft-bg")));
        Assert.True(Contrast(Rgb(Token("--ft-danger-text")), tinted) < 4.5, "若浅红底已经够用，错误提示也可以加底色了");
    }

    [Fact]
    public void 导入账单页自己的表面不使用玻璃_整页的玻璃层只来自导航栏的圆钮()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        var withBlur = Regex.Matches(ui, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Where(m => m.Groups["body"].Value.Contains("backdrop-filter", StringComparison.Ordinal))
            .Select(m => Regex.Replace(m.Groups["selector"].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline).Trim())
            .ToList();

        Assert.DoesNotContain(withBlur, selector => selector.Contains(".ft-import", StringComparison.Ordinal));
    }

    // ---- 自定义报表（21 号） ----

    [Fact]
    public void 自定义报表页的内容底部留白与短信识别页一样取主按钮区的高度令牌()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        Assert.Matches(@"\.ft-report\s*\{[^}]*padding-bottom:\s*calc\(var\(--ft-pagefoot-height\)", ui);
    }

    [Fact]
    public void 环比变化的语义色用加深的文字令牌_持平用主文字色_都在白卡上达到AA对比度()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        // 支出变多 = 坏事（红）、变少 = 好事（绿）、持平 = 主文字色（与同比卡一致）；
        // 不用 --ft-expense / --ft-income 这两个图形色：它们在白底上只有 4.2:1 / 3.2:1，文字不够
        Assert.Matches(@"\.ft-delta--bad\s*\{[^}]*color:\s*var\(--ft-danger-text\)", ui);
        Assert.Matches(@"\.ft-delta--good\s*\{[^}]*color:\s*var\(--ft-income-text\)", ui);
        Assert.Matches(@"\.ft-delta\s*\{[^}]*color:\s*var\(--ft-label\)", ui);

        foreach (var token in new[] { "--ft-danger-text", "--ft-income-text", "--ft-label" })
        {
            var ratio = Contrast(Rgb(Token(token)), White);
            Assert.True(ratio >= 4.5, $"环比变化文字 {token} 在白卡上的对比度 {ratio:0.00}:1，低于 4.5:1");
        }
    }

    [Fact]
    public void 表单校验提示是加深的危险文字色_在白底与页面底色上都达到AA对比度()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        // 校验提示是 caption 字号的小字，不能用只有约 4.2:1 的 --ft-expense（图形 / 大字号的红）
        Assert.Matches(@"\.ft-form-error\s*\{[^}]*color:\s*var\(--ft-danger-text\)", ui);

        // 它既出现在白色的 Sheet 里，也出现在页面底色上（自定义报表的参数面板下面）
        foreach (var (name, background) in new[] { ("白底", White), ("页面底色", Rgb(Token("--ft-bg"))) })
        {
            var ratio = Contrast(Rgb(Token("--ft-danger-text")), background);
            Assert.True(ratio >= 4.5, $"表单校验提示在{name}上的对比度 {ratio:0.00}:1，低于 4.5:1");
        }
    }

    [Fact]
    public void 自定义报表页自己的表面不使用玻璃_整页的玻璃层只来自导航栏的圆钮()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        var withBlur = Regex.Matches(ui, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Where(m => m.Groups["body"].Value.Contains("backdrop-filter", StringComparison.Ordinal))
            .Select(m => Regex.Replace(m.Groups["selector"].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline).Trim())
            .ToList();

        foreach (var prefix in new[] { ".ft-report", ".ft-delta" })
        {
            Assert.DoesNotContain(withBlur, selector => selector.Contains(prefix, StringComparison.Ordinal));
        }
    }

    // ---- 登录页与错误日志（22 号） ----

    [Fact]
    public void 登录页渐变上的标语白字在面板上沿处的渐变色上达到AA对比度()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        // 渐变是 162° 的线性渐变（#起点 0% → #中点 46% → #终点 100%）上叠几个半透明的高光；可见区域是上半部分（下面被白色面板盖住），
        // 面板上沿大约对应渐变线上 65% 的位置：取中点与终点之间 65% 处的颜色作为「可见区域里最亮的底」。
        var gradient = Regex.Match(
            TokensCss.Value,
            @"linear-gradient\(162deg,\s*(?<a>#[0-9A-Fa-f]{6})\s+0%,\s*(?<b>#[0-9A-Fa-f]{6})\s+46%,\s*(?<c>#[0-9A-Fa-f]{6})\s+100%\)");
        Assert.True(gradient.Success, "tokens.css 的 --ft-hero-gradient 里找不到 162° 的三段线性渐变");

        var middle = Rgb(gradient.Groups["b"].Value);
        var end = Rgb(gradient.Groups["c"].Value);
        var t = (65d - 46d) / (100d - 46d);
        var background = (
            R: middle.R + (end.R - middle.R) * t,
            G: middle.G + (end.G - middle.G) * t,
            B: middle.B + (end.B - middle.B) * t);

        var slogan = Regex.Match(ui, @"p\.ft-login__slogan\s*\{[^}]*color:\s*rgba\(255,\s*255,\s*255,\s*(?<alpha>[\d.]+)\)");
        Assert.True(slogan.Success, "ui.css 的 p.ft-login__slogan 应当是白色半透明文字色");
        var alpha = double.Parse(slogan.Groups["alpha"].Value, CultureInfo.InvariantCulture);

        var text = Over((255, 255, 255), alpha, background);
        var ratio = Contrast(text, background);
        Assert.True(ratio >= 4.5, $"登录页标语（白字 {alpha:0.##} 不透明度）在渐变 65% 处的对比度 {ratio:0.00}:1，低于 4.5:1");

        // 应用名是 38px / 800 的大号文字：纯白 ≥ 3:1 即可，这里取最亮的终点色做最不利的验证
        Assert.True(Contrast(White, end) >= 3.0, "登录页应用名（大号白字）在渐变终点色上的对比度低于 3:1");
    }

    [Fact]
    public void 登录页的输入框字号不小于16px_iOS聚焦时不放大页面()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        var match = Regex.Match(ui, @"\.ft-login__input\s*\{[^}]*font:\s*(?:[a-z0-9]+\s+)?(?<size>\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "ui.css 里找不到 .ft-login__input 的 font 简写（应是「字重 字号/行高 字体」）");
        Assert.True(
            double.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture) >= 16,
            ".ft-login__input 的字号小于 16px：iOS 聚焦时会自动放大页面");
    }

    [Fact]
    public void 登录页的校验提示与出错描边都是加深的危险文字色()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));

        Assert.Matches(@"p\.ft-login__error\s*\{[^}]*color:\s*var\(--ft-danger-text\)", ui);
        Assert.Matches(@"\.ft-login__field--error[^{]*\{[^}]*var\(--ft-danger-text\)", ui);

        // 提示是 13px 的小字，在白色面板上必须 ≥ 4.5:1；输入框出错描边是非文字的界面元素，在灰色输入条上 ≥ 3:1
        Assert.True(Contrast(Rgb(Token("--ft-danger-text")), White) >= 4.5, "登录页校验提示在白色面板上低于 4.5:1");
        var inputFill = Over((120, 120, 128), .12, White);
        Assert.True(Contrast(Rgb(Token("--ft-danger-text")), inputFill) >= 3.0, "登录页输入框的出错描边在灰色输入条上低于 3:1");
    }

    [Fact]
    public void 登录页与错误日志页自己的表面不使用玻璃_登录页只有Logo一层()
    {
        var ui = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css"));
        var withBlur = Regex.Matches(ui, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Where(m => m.Groups["body"].Value.Contains("backdrop-filter", StringComparison.Ordinal))
            .Select(m => Regex.Replace(m.Groups["selector"].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline).Trim())
            .ToList();

        // 登录页的 Logo 用共用的 .ft-glass-dark 取得玻璃（页面上唯一的一层），页面自己的类（圆球、面板、输入条、按钮）都不模糊；
        // 错误日志页：概览卡、日志行、详情都是白色卡片，整页的玻璃层只来自导航栏的圆钮与 Sheet 自己的关闭钮
        foreach (var prefix in new[] { ".ft-login", ".ft-logs", ".ft-logdetail" })
        {
            Assert.DoesNotContain(withBlur, selector => selector.Contains(prefix, StringComparison.Ordinal));
        }
    }

    // ---- 无障碍加固（23 号）：对比度。全页面的实测在 tests/e2e/a11y_degradation_test.py（像素取样）；这里锁住令牌层的取值与用法 ----

    private static string UiCssWithoutComments() =>
        Regex.Replace(File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css")), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    [Fact]
    public void 次文字色在白卡_页面底色_搜索框底上都达到AA对比度_占位文字也用它()
    {
        var (color, alpha) = Rgba(Token("--ft-label-2"));
        var page = Rgb(Token("--ft-bg"));
        var (fill, fillAlpha) = Rgba(Token("--ft-fill"));

        // 搜索框 / 输入条的底是页面底色上的 12% 灰，是次文字最不利的底之一
        foreach (var (name, background) in new[] { ("白卡", White), ("页面底色", page), ("页面底色上的填充（搜索框）", Over(fill, fillAlpha, page)) })
        {
            var ratio = Contrast(Over(color, alpha, background), background);
            Assert.True(ratio >= 4.5, $"次文字色在{name}上的对比度 {ratio:0.00}:1，低于 4.5:1");
        }

        // 占位文字（::placeholder）也是文字：一律用次文字色，不能用弱提示色
        var placeholders = Regex.Matches(UiCssWithoutComments(), @"(?<selector>[^{}]*::placeholder)\s*\{(?<body>[^{}]*)\}");
        Assert.NotEmpty(placeholders);
        foreach (Match match in placeholders)
        {
            Assert.DoesNotContain("--ft-label-3", match.Groups["body"].Value);
        }
    }

    [Fact]
    public void 弱提示色只用于图形_不用作文字颜色()
    {
        // --ft-label-3 在白底上只有约 1.8:1：行尾箭头、空状态大图标这类图形可以用；文字（含占位文字、还没输入时的金额「0」）不行
        var asTextColor = Regex.Matches(UiCssWithoutComments(), @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Where(m => Regex.IsMatch(m.Groups["body"].Value, @"(?<![-\w])color:\s*var\(--ft-label-3\)"))
            .Select(m => m.Groups["selector"].Value.Trim())
            .OrderBy(selector => selector, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { ".ft-cell__chevron", ".ft-panel__empty > i" }, asTextColor);
    }

    [Fact]
    public void 文字颜色不使用只作图形的语义色令牌_文字用加深的文字色()
    {
        // --ft-expense / --ft-income / --ft-success / --ft-danger / --ft-warning / --ft-info 在白底上都不到 4.5:1，只能给图标、进度条、色点；
        // 文字要用 --ft-danger-text / --ft-income-text / --ft-warning-text / --ft-success-text。
        // 选择器以 __icon 结尾的是图标（Toast 的类型图标），属于图形，不算文字
        var offenders = Regex.Matches(UiCssWithoutComments(), @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")
            .Where(m => Regex.IsMatch(m.Groups["body"].Value, @"(?<![-\w])color:\s*var\(--ft-(expense|income|success|danger|warning|info)\)"))
            .Select(m => m.Groups["selector"].Value.Trim())
            .Where(selector => !selector.EndsWith("__icon", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, $"这些规则把图形色令牌当文字色用了：{string.Join(" | ", offenders)}");
    }

    [Fact]
    public void 成功徽标文字在16百分比绿浅底上达到AA对比度_白卡与页面底色上都是()
    {
        // .ft-badge--success 的底是 rgba(52, 199, 89, .16)；收入文字色在这块底上只有约 4.1:1，所以徽标用单独的 --ft-success-text
        Assert.Matches(@"\.ft-badge--success\s*\{[^}]*color:\s*var\(--ft-success-text\)", UiCssWithoutComments());

        foreach (var (name, surface) in new[] { ("白底", White), ("页面底色", Rgb(Token("--ft-bg"))) })
        {
            var ratio = Contrast(Rgb(Token("--ft-success-text")), Over((52, 199, 89), .16, surface));
            Assert.True(ratio >= 4.5, $"成功徽标文字在{name}上的浅绿底上对比度 {ratio:0.00}:1，低于 4.5:1");
        }

        var generic = Contrast(Rgb(Token("--ft-income-text")), Over((52, 199, 89), .16, White));
        Assert.True(generic < 4.5, "若收入文字色在浅绿底上已经够用，成功徽标不需要单独的令牌");
    }

    [Fact]
    public void 首页Hero小卡带暗纱_说明文字是纯白()
    {
        // 小卡压在渐变最亮的一角时，白色 90% 的说明文字只有约 3.9:1；暗纱 + 纯白把它抬到 ≥ 4.5:1（实测在 a11y_degradation_test 的像素取样里）。
        // 这里只锁住「暗纱存在、文字纯白」，不在单测里推算渐变上的最亮像素
        var ui = UiCssWithoutComments();

        Assert.Matches(@"\.ft-herocard\s*\{[^}]*background:\s*linear-gradient\(var\(--ft-herocard-scrim\),\s*var\(--ft-herocard-scrim\)\),\s*var\(--ft-glass-dark-bg\)", ui);
        Assert.Matches(@"\.ft-herocard__label\s*\{[^}]*color:\s*#fff\s*;", ui);

        var (_, alpha) = Rgba(Token("--ft-herocard-scrim"));
        Assert.InRange(alpha, .08, .20);
    }

    [Fact]
    public void 首页Hero金额按版心收缩_字号上限与排版令牌一致_余量系数覆盖130百分比字体放大()
    {
        // Hero 的金额不换行、不省略，必须完整放得下：字号 = min(档位字号, 可用宽度 ÷ (字符数 × 每字符宽度 × 放大余量))（AmountDisplay 的 FitWidth，见 ui.css 首页一节）。
        // 档位字号在规则里写成字面量，必须与排版令牌的字号一致，否则位数少的金额会与设计字号对不上；余量系数 = 每字符宽度（em）× 1.3，
        // 少了这 1.3 倍，系统字体放大到 130% 时 14 个字符的金额（¥99,999,999.99）就会越出版心（实测在 a11y_degradation_test 的 Z 小节）
        var ui = UiCssWithoutComments();
        var xl = Regex.Match(ui, @"\.ft-hero \.ft-amount--xl\s*\{[^}]*font-size:\s*min\((?<max>\d+)px,\s*calc\(");
        var card = Regex.Match(ui, @"\.ft-herocard \.ft-amount--lg\s*\{[^}]*font-size:\s*min\((?<max>\d+)px,\s*calc\(");
        Assert.True(xl.Success && card.Success, "找不到 Hero 金额的收缩规则");
        Assert.Equal(Regex.Match(Token("--ft-type-amount-xl"), @"800 (?<size>\d+)px").Groups["size"].Value, xl.Groups["max"].Value);
        Assert.Equal(Regex.Match(Token("--ft-type-amount-card"), @"700 (?<size>\d+)px").Groups["size"].Value, card.Groups["max"].Value);

        Assert.True(double.Parse(Token("--ft-amount-fit-xl"), CultureInfo.InvariantCulture) >= .476 * 1.3 - .005, "特大档每字符约 .476em，要再乘 1.3 的字体放大余量");
        Assert.True(double.Parse(Token("--ft-amount-fit-card"), CultureInfo.InvariantCulture) >= .534 * 1.3 - .005, "卡片档每字符最宽约 .534em（全是宽数字），要再乘 1.3 的字体放大余量");

        // 字符数由 AmountDisplay 写在 --ft-amount-chars 上；Hero 的三个金额都要打开它
        Assert.Contains("--ft-amount-chars:", ReadRepoFile("src/FinanceTracker.Web/Components/Ui/AmountDisplay.razor"));
        var index = ReadRepoFile("src/FinanceTracker.Web/Pages/Index.razor");
        foreach (var testId in new[] { "home-expense", "home-income", "home-balance" })
        {
            Assert.Matches($@"<AmountDisplay[^>]*FitWidth=""true""[^>]*TestId=""{testId}""", index);
        }
    }

    // ---- 无障碍加固（23 号）：减弱动态效果。全应用的实测在 a11y_degradation_test.py 的 R 节（静态扫描 + 录制）；这里锁住样式表的结构 ----

    /// <summary>样式表里的一条普通规则（不含 @keyframes 内部的 from / to）：在文件里的先后次序、选择器、声明块、是否在 prefers-reduced-motion: reduce 块里。</summary>
    private sealed record CssRule(int Order, string Selector, string Body, bool InReducedMotion);

    private static List<CssRule> OrderedCssRules(string css)
    {
        var rules = new List<CssRule>();
        var blocks = new Stack<(bool Reduce, bool Keyframes)>();
        var prelude = new System.Text.StringBuilder();
        var order = 0;
        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];
            if (c == '{')
            {
                var text = prelude.ToString().Trim();
                prelude.Clear();
                if (text.StartsWith('@'))
                {
                    var parent = blocks.Count > 0 ? blocks.Peek() : (Reduce: false, Keyframes: false);
                    blocks.Push((
                        parent.Reduce || (text.StartsWith("@media", StringComparison.Ordinal) && text.Contains("prefers-reduced-motion: reduce", StringComparison.Ordinal)),
                        parent.Keyframes || text.Contains("keyframes", StringComparison.Ordinal)));
                    continue;
                }

                var end = css.IndexOf('}', i);
                Assert.True(end > i, $"样式表里 {text} 的声明块没有结束");
                if (blocks.Count == 0 || !blocks.Peek().Keyframes)
                {
                    rules.Add(new CssRule(order++, Regex.Replace(text, @"\s+", " "), css[(i + 1)..end], blocks.Count > 0 && blocks.Peek().Reduce));
                }

                i = end;
                continue;
            }

            if (c == '}')
            {
                if (blocks.Count > 0)
                {
                    blocks.Pop();
                }

                prelude.Clear();
                continue;
            }

            prelude.Append(c);
        }

        return rules;
    }

    private static readonly Regex MotionDeclaration = new(@"(?<![-\w])(?<name>animation|transition)(?<longhand>-[a-z-]+)?\s*:", RegexOptions.Compiled);

    private static HashSet<string> MotionProperties(string body) =>
        MotionDeclaration.Matches(body).Select(m => m.Value.TrimEnd(':', ' ')).ToHashSet(StringComparer.Ordinal);

    /// <summary>两组动效声明会不会争同一个属性：任何一边写了简写（animation / transition）就会，否则要有同名的长写属性。</summary>
    private static bool MotionConflicts(HashSet<string> a, HashSet<string> b) =>
        a.Contains("animation") && b.Any(p => p.StartsWith("animation", StringComparison.Ordinal))
        || b.Contains("animation") && a.Any(p => p.StartsWith("animation", StringComparison.Ordinal))
        || a.Contains("transition") && b.Any(p => p.StartsWith("transition", StringComparison.Ordinal))
        || b.Contains("transition") && a.Any(p => p.StartsWith("transition", StringComparison.Ordinal))
        || a.Overlaps(b);

    [Fact]
    public void 减弱动态效果的覆盖规则写在被覆盖的基础规则之后_同优先级时后写的赢()
    {
        // 「减弱动态效果」的覆盖规则和基础规则选择器相同、优先级相同时，只有写在后面的才生效。曾经预算页的金额光标就栽在这：
        // reduce 块写在光标基础规则前面，系统开了「减弱动态效果」光标照样闪（a11y_degradation_test 的静态扫描抓到）。
        // 这里按「选择器一模一样、动效属性会冲突、基础规则在 reduce 规则之后」逐条找出来。
        var rules = OrderedCssRules(UiCssWithoutComments());
        var reduced = rules.Where(r => r.InReducedMotion && MotionProperties(r.Body).Count > 0).ToList();
        Assert.True(reduced.Count >= 20, $"只找到 {reduced.Count} 条 reduce 里的动效覆盖规则——解析器或样式表有问题");

        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var rule in reduced)
        {
            var selectors = rule.Selector.Split(',').Select(s => s.Trim()).ToHashSet(StringComparer.Ordinal);
            var overridden = MotionProperties(rule.Body);
            foreach (var later in rules.Where(r => !r.InReducedMotion && r.Order > rule.Order))
            {
                var laterProps = MotionProperties(later.Body);
                if (laterProps.Count == 0 || !MotionConflicts(overridden, laterProps))
                {
                    continue;
                }

                foreach (var selector in later.Selector.Split(',').Select(s => s.Trim()).Where(selectors.Contains))
                {
                    offenders.Add(selector);
                }
            }
        }

        Assert.True(offenders.Count == 0, $"这些选择器的 reduce 覆盖规则写在基础规则前面，会被基础规则盖掉：{string.Join(" | ", offenders)}");
    }

    [Fact]
    public void 减弱动态效果下所有动效令牌取同一个简短时长_光标与语音循环周期除外()
    {
        // 组件的时长都取自 --ft-dur-* 令牌；reduce 下在令牌层一处压成 --ft-dur-reduced（≤ 150 ms 的「简短」口径）
        var tokens = TokensCss.Value;
        var block = Regex.Match(tokens, @"@media \(prefers-reduced-motion: reduce\)\s*\{\s*:root\s*\{(?<body>[^{}]*)\}\s*\}");
        Assert.True(block.Success, "tokens.css 里找不到 prefers-reduced-motion 的令牌覆盖块");
        var overridden = Regex.Matches(block.Groups["body"].Value, @"(?<name>--ft-dur-[a-z-]+)\s*:\s*(?<value>[^;]+);")
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value.Trim(), StringComparer.Ordinal);

        var reducedMs = double.Parse(Regex.Match(Token("--ft-dur-reduced"), @"^(?<n>\d+)ms$").Groups["n"].Value, CultureInfo.InvariantCulture);
        Assert.InRange(reducedMs, 1, 150);

        // 不压的：本身就是「简短」时长的、循环周期（压成 100 ms 一圈反而成了频闪）
        var exempt = new HashSet<string>(StringComparer.Ordinal) { "--ft-dur-reduced", "--ft-dur-caret", "--ft-dur-voice-bar", "--ft-dur-voice-ring", "--ft-dur-voice-dot" };
        var defined = Regex.Matches(tokens, @"^\s*(?<name>--ft-dur-[a-z-]+)\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .Where(name => !exempt.Contains(name))
            .Distinct()
            .ToList();
        Assert.True(defined.Count >= 12, $"只找到 {defined.Count} 个动效时长令牌");

        var missing = defined.Where(name => !overridden.ContainsKey(name) && double.Parse(Regex.Match(Token(name), @"^(?<n>\d+)ms$").Groups["n"].Value, CultureInfo.InvariantCulture) > 150).ToList();
        Assert.True(missing.Count == 0, $"这些动效时长令牌超过 150 ms，reduce 下没有压成 --ft-dur-reduced：{string.Join("、", missing)}");
        Assert.All(overridden, pair => Assert.Equal("var(--ft-dur-reduced)", pair.Value));
        Assert.DoesNotContain(overridden.Keys, exempt.Contains);
    }

    // ---- 冷启动（docs/design/cold-start）：系统启动画面 → 加载页 → 首页 / 登录页是一次「同一张画布的接力」，
    //      下面这些颜色 / 尺寸 / 图形散在令牌、csproj、XAML、SVG、两个宿主页里，改一处漏另一处，真机上就是一道色差或一次跳变 ----

    private const string BootCssPath = "src/FinanceTracker.Web/wwwroot/css/boot.css";
    private const string CsprojPath = "src/FinanceTracker.App/FinanceTracker.App.csproj";
    private const string SplashSvgPath = "src/FinanceTracker.App/Resources/Splash/splash.svg";
    private const string AppIconSvgPath = "src/FinanceTracker.App/Resources/AppIcon/appicon.svg";
    private const string AppIconForegroundSvgPath = "src/FinanceTracker.App/Resources/AppIcon/appiconfg.svg";
    private const string ServerHostPath = "server/FinanceTracker.Web.Server/Pages/_Layout.cshtml";
    private const string AppHostPath = "src/FinanceTracker.App/wwwroot/index.html";

    private static string ReadRepoFile(string relativePath) => File.ReadAllText(RepoRoot.Combine(relativePath));

    private static string BootCssWithoutComments() =>
        Regex.Replace(ReadRepoFile(BootCssPath), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    /// <summary>取某条规则（选择器恰好是 <paramref name="selector"/>）的声明块；取第一处。</summary>
    private static string RuleBody(string css, string selector)
    {
        var match = Regex.Match(css, $@"(?:^|\}})\s*{Regex.Escape(selector)}\s*\{{(?<body>[^{{}}]*)\}}", RegexOptions.Multiline);
        Assert.True(match.Success, $"找不到规则 {selector}");
        return match.Groups["body"].Value;
    }

    /// <summary>宿主页里启动加载页的标记：从根节点到「正在加载…」那一行（含）。去掉空白差异后两个宿主应当一字不差。</summary>
    private static string BootMarkup(string hostPage)
    {
        var html = ReadRepoFile(hostPage);
        var match = Regex.Match(html, @"<div id=""ft-boot"".*?<span>正在加载…</span>", RegexOptions.Singleline);
        Assert.True(match.Success, $"{hostPage} 里找不到启动加载页的标记（#ft-boot）");
        return Regex.Replace(match.Value, @"\s+", " ");
    }

    [Fact]
    public void 启动画面的底色在令牌_原生启动图_应用图标_MainPage之间一致()
    {
        var csproj = ReadRepoFile(CsprojPath);
        var splash = Regex.Match(csproj, @"<MauiSplashScreen\b[^>]*\bColor=""(?<color>#[0-9A-Fa-f]{6})""");
        var icon = Regex.Match(csproj, @"<MauiIcon\b[^>]*\bColor=""(?<color>#[0-9A-Fa-f]{6})""");
        var mainPage = Regex.Match(ReadRepoFile("src/FinanceTracker.App/MainPage.xaml"), @"\bBackgroundColor=""(?<color>#[0-9A-Fa-f]{6})""");
        Assert.True(splash.Success && icon.Success && mainPage.Success, "csproj 的 MauiSplashScreen / MauiIcon 与 MainPage.xaml 都要写明 Color / BackgroundColor");

        // 系统启动画面的底色（csproj）→ 原生页面容器的底色（MainPage.xaml，WebView 还没画出来时露出的就是它）→ 加载页的底色（令牌），是同一个颜色
        var expected = Token("--ft-splash-bg");
        Assert.Equal(expected, splash.Groups["color"].Value, ignoreCase: true);
        Assert.Equal(expected, icon.Groups["color"].Value, ignoreCase: true);
        Assert.Equal(expected, mainPage.Groups["color"].Value, ignoreCase: true);
    }

    /// <summary>去掉 C# 的 // 与 /* */ 注释（只给下面两个原生宿主文件用，这两个文件里没有含 // 的字符串）。</summary>
    private static string CSharpWithoutComments(string relativePath) =>
        Regex.Replace(ReadRepoFile(relativePath), @"//[^\r\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    // 冷启动白屏闪动的原生根因：启动画面是紫色，但系统换主题 / WebView 自己画默认底色时会各露出一帧白色——
    // Android：MauiAppCompatActivity.OnCreate 把窗口换成 Maui.MainTheme（窗口背景变白），WebView 在网页首次绘制前画 Chromium 的白色；
    // iOS：WKWebView 默认不透明，MAUI 只把背景色设成 Clear，首次绘制前画的是白色。
    // 这些只能在真机 / 模拟器上看到，自动化只能守住「做了、顺序对、颜色取自同一处、没有写死」。
    [Fact]
    public void 冷启动不闪白_原生WebView的底色取自MainPage的启动底色_两个平台都处理()
    {
        var code = CSharpWithoutComments("src/FinanceTracker.App/MainPage.xaml.cs");

        // 事件两个平台都要接（曾经只接了 iOS，Android 的 WebView 一直是白底）
        Assert.Matches(@"#if ANDROID \|\| IOS\s+blazorWebView\.BlazorWebViewInitialized \+= OnBlazorWebViewInitialized;", code);

        // Android：WebView 底色（分支以单独一行的 #if ANDROID 开头，区别于构造函数里的 #if ANDROID || IOS）
        var android = Regex.Match(code, @"#if ANDROID\r?\n.*?#elif IOS\b", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(android), "MainPage.xaml.cs 里找不到 Android 分支");
        Assert.Matches(@"e\.WebView\.SetBackgroundColor\(", android);

        // iOS：关掉不透明，并把 WebView / ScrollView / 页面下方（iOS 15+）的底色都设上
        var ios = Regex.Match(code, @"#elif IOS\b.*?#endif", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(ios), "MainPage.xaml.cs 里找不到 iOS 分支");
        Assert.Matches(@"\.Opaque\s*=\s*false\s*;", ios);
        Assert.Matches(@"webView\.BackgroundColor\s*=\s*color\s*;", ios);
        Assert.Matches(@"webView\.ScrollView\.BackgroundColor\s*=\s*color\s*;", ios);
        Assert.Matches(@"IsIOSVersionAtLeast\(15\)\)\s*webView\.UnderPageBackgroundColor\s*=\s*color\s*;", ios);

        // 颜色一律取自页面自己的背景色（MainPage.xaml 的 BackgroundColor，上一个测试已把它与令牌 / 启动图钉在同一个值），代码里不再写第二份
        Assert.Equal(2, Regex.Matches(code, @"BackgroundColor is (?:not )?\{ \} launchColor").Count);
        Assert.Equal(2, Regex.Matches(code, @"launchColor\.ToPlatform\(\)").Count);
        Assert.DoesNotMatch(@"#[0-9A-Fa-f]{6}\b|0x[0-9A-Fa-f]{6}|Color\.FromRgb|Color\.FromArgb|UIColor\.(?:FromRGB|FromRGBA)|Color\.Rgb\(|Color\.Argb\(", code);
    }

    [Fact]
    public void 冷启动不闪白_Android窗口背景在主题切换之后立刻压成启动画面的底色()
    {
        var code = CSharpWithoutComments("src/FinanceTracker.App/Platforms/Android/MainActivity.cs");

        // 顺序：主题切换发生在 base.OnCreate 里，之后立刻压窗口背景（早于边到边设置，也早于第一帧）
        Assert.Matches(@"base\.OnCreate\(savedInstanceState\)\s*;\s*ApplyLaunchBackground\(\)\s*;", code);

        // 颜色来自 MAUI 按 csproj 的 MauiSplashScreen Color 生成的 maui_splash_color——与系统启动画面是同一个资源，不写第二份
        var method = Regex.Match(code, @"private void ApplyLaunchBackground\(\)\s*\{(?<body>.*?)\n\t\}", RegexOptions.Singleline);
        Assert.True(method.Success, "找不到 ApplyLaunchBackground");
        Assert.Matches(@"Resources\?\.GetColor\(Resource\.Color\.maui_splash_color,\s*Theme\)", method.Groups["body"].Value);
        Assert.Matches(@"Window\?\.SetBackgroundDrawable\(new ColorDrawable\(", method.Groups["body"].Value);
        Assert.DoesNotMatch(@"#[0-9A-Fa-f]{6}\b|0x[0-9A-Fa-f]{6}|Color\.Rgb\(|Color\.Argb\(|Color\.ParseColor", code);
    }

    [Fact]
    public void 启动画面里的Logo方块与加载页的Logo同色同尺寸同圆角()
    {
        var svg = ReadRepoFile(SplashSvgPath);
        var rect = Regex.Match(svg, @"<rect\b(?<attrs>[^>]*)/>");
        Assert.True(rect.Success, "splash.svg 里找不到 Logo 方块");

        string Attr(string name)
        {
            var match = Regex.Match(rect.Groups["attrs"].Value, $@"\b{name}=""(?<value>[^""]*)""");
            Assert.True(match.Success, $"splash.svg 的方块缺少 {name}");
            return match.Groups["value"].Value;
        }

        // 方块色 = 深色玻璃压在渐变上的实测色（令牌），静态图不能做模糊与半透明，只能写实色
        Assert.Equal(Token("--ft-splash-tile"), Attr("fill"), ignoreCase: true);

        // 方块 96、圆角 30、居中——与加载页的 .ft-boot__logo 一致，系统启动画面 → 加载页之间 Logo 不动
        Assert.Equal("96", Attr("width"));
        Assert.Equal("96", Attr("height"));
        Assert.Equal("30", Attr("rx"));
        var bootLogo = RuleBody(BootCssWithoutComments(), ".ft-boot__logo");
        Assert.Matches(@"(?<![-\w])width:\s*96px\s*;", bootLogo);
        Assert.Matches(@"(?<![-\w])height:\s*96px\s*;", bootLogo);
        Assert.Matches(@"(?<![-\w])border-radius:\s*30px\s*;", bootLogo);

        // 画布 108、方块四周各留 6：Android 12+ 的启动图 drawable 把图固定拉成 108dp（MAUI 生成），画布正好 108 才没有尺寸跳变；csproj 的 BaseSize 同为 108
        Assert.Matches(@"<svg\b[^>]*\bwidth=""108""[^>]*\bheight=""108""[^>]*\bviewBox=""0 0 108 108""", svg);
        Assert.Equal("6", Attr("x"));
        Assert.Equal("6", Attr("y"));
        Assert.Matches(@"<MauiSplashScreen\b[^>]*\bBaseSize=""108,108""", ReadRepoFile(CsprojPath));
    }

    [Fact]
    public void 应用图标的渐变取自主渐变令牌_Logo图形与启动画面和加载页是同一个()
    {
        var background = ReadRepoFile(AppIconSvgPath);
        var stops = Regex.Matches(background, @"<stop\b[^>]*\bstop-color=""(?<color>#[0-9A-Fa-f]{6})""")
            .Select(m => m.Groups["color"].Value)
            .ToList();
        Assert.Equal(2, stops.Count);
        Assert.Equal(Token("--ft-brand-2"), stops[0], ignoreCase: true);
        Assert.Equal(Token("--ft-brand"), stops[1], ignoreCase: true);

        // 钱包图形（MDI wallet-outline）在四个地方各抄了一份：应用图标前景、启动图、两个宿主页的加载页——必须是同一条路径
        static string Glyph(string content)
        {
            var match = Regex.Match(content, @"<path\b[^>]*\bd=""(?<d>M5,3C3\.89[^""]*)""");
            Assert.True(match.Success, "找不到钱包图形的路径");
            return match.Groups["d"].Value;
        }

        var expected = Glyph(ReadRepoFile(AppIconForegroundSvgPath));
        Assert.Equal(expected, Glyph(ReadRepoFile(SplashSvgPath)));
        Assert.Equal(expected, Glyph(ReadRepoFile(ServerHostPath)));
        Assert.Equal(expected, Glyph(ReadRepoFile(AppHostPath)));
    }

    [Fact]
    public void 启动加载页样式引用的令牌都有定义()
    {
        // 这几个不在 tokens.css：安全区变量由 app.css 声明、immersive.js 在运行时覆盖；--ft-boot-* 由 boot.js 量好之后写在 #ft-boot 的行内样式上
        var runtime = new HashSet<string> { "--ft-safe-bottom", "--ft-boot-dx", "--ft-boot-dy", "--ft-boot-hero-h" };
        var defined = Regex.Matches(TokensCss.Value, @"^\s*(?<name>--ft-[a-z0-9-]+)\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet();

        var undefinedTokens = Regex.Matches(BootCssWithoutComments(), @"var\((?<name>--ft-[a-z0-9-]+)")
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .Where(name => !defined.Contains(name) && !runtime.Contains(name))
            .ToList();

        Assert.True(undefinedTokens.Count == 0, $"boot.css 引用了没有定义的令牌：{string.Join(", ", undefinedTokens)}");
    }

    [Fact]
    public void 加载页整屏只有Logo方块一层玻璃_三条降级路径下方块都改用与启动图同色的实色()
    {
        var css = BootCssWithoutComments();

        // 材质来自共用的 .ft-glass-dark（与登录页同一块玻璃）；boot.css 自己不写任何模糊，只在降级时把它关掉
        // （先去掉 @supports 的条件部分：条件里写着 backdrop-filter: blur(1px)，那是探测，不是声明）
        var declarations = Regex.Replace(css, @"@supports[^{]*\{", "@supports {");
        var blurs = Regex.Matches(declarations, @"(?<![-\w])backdrop-filter:\s*(?<value>[^;]+);")
            .Select(m => m.Groups["value"].Value.Trim())
            .ToList();
        Assert.All(blurs, value => Assert.Equal("none", value));

        // 令牌层有三条降级触发路径（tokens.css 的材质降级块：系统降低透明度 / 根开关 data-ft-solid / 不支持 backdrop-filter），
        // 都把深色玻璃换成另一个实色（#6A42F2）。系统启动画面里的方块只能是 --ft-splash-tile，换色放在加载页上就是一次色跳；
        // 根开关还是宿主在 Blazor 启动之后才写的，加载页还在时就会翻转（E2E boot_test 的 I 节在浏览器里验证了这一点）。
        // 所以加载页的方块在每条路径下都要回到 --ft-splash-tile
        var degradePaths = Regex.Matches(ReadRepoFile("src/FinanceTracker.Web/wwwroot/css/tokens.css"),
            @"--ft-glass-dark-bg:\s*var\(--ft-glass-dark-solid-bg\)").Count;
        Assert.True(degradePaths == 3,
            $"tokens.css 里把深色玻璃换成实色的降级路径现在是 {degradePaths} 条（预期 3 条）——触发路径增删了，boot.css 里加载页方块的降级覆盖要同步");

        var overrides = new Dictionary<string, string>
        {
            ["系统降低透明度"] = @"@media \(prefers-reduced-transparency: reduce\)\s*\{\s*\.ft-boot__logo\s*\{(?<body>[^{}]*)\}",
            ["根开关 data-ft-solid"] = @":root\[data-ft-solid=""true""\]\s+\.ft-boot__logo\s*\{(?<body>[^{}]*)\}",
            ["不支持 backdrop-filter"] =
                @"@supports not \(\(-webkit-backdrop-filter: blur\(1px\)\) or \(backdrop-filter: blur\(1px\)\)\)\s*\{\s*\.ft-boot__logo\s*\{(?<body>[^{}]*)\}",
        };
        foreach (var (path, pattern) in overrides)
        {
            var match = Regex.Match(css, pattern);
            Assert.True(match.Success, $"boot.css 缺少加载页方块在「{path}」下的降级覆盖");
            Assert.Matches(@"(?<![-\w])background:\s*var\(--ft-splash-tile\)\s*;", match.Groups["body"].Value);
            Assert.Matches(@"(?<![-\w])backdrop-filter:\s*none\s*;", match.Groups["body"].Value);
        }

        foreach (var host in new[] { ServerHostPath, AppHostPath })
        {
            Assert.Matches(@"class=""ft-boot__logo ft-glass-dark""", ReadRepoFile(host));
        }
    }

    [Fact]
    public void 两个宿主的启动加载页标记一字不差()
    {
        Assert.Equal(BootMarkup(ServerHostPath), BootMarkup(AppHostPath));
    }

    [Fact]
    public void 图表一律经EChartsView渲染_不直接用MECharts_ECharts是延后注入的_没到就初始化会让整个界面失效()
    {
        // ECharts 由 boot.js 在页面 load 之后才注入。MASA 的 MECharts 不检查全局 echarts 是否存在就直接 init，
        // 脚本没到时抛 ReferenceError，Blazor 当成未处理异常——界面整个失效（E2E boot_test 的 H 节验证过这条因果）。
        // EChartsView 先等 FtBoot.chartsReady() 再渲染图表，所以页面里不能绕过它
        var separator = System.IO.Path.DirectorySeparatorChar;
        var wrapper = $"{separator}Components{separator}Ui{separator}EChartsView.razor";
        var offenders = Directory
            .EnumerateFiles(RepoRoot.Combine("src"), "*.razor", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .Where(file => !file.EndsWith(wrapper, StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"<MECharts\b"))
            .Select(file => System.IO.Path.GetRelativePath(RepoRoot.Path, file))
            .ToList();
        Assert.True(offenders.Count == 0, $"这些文件直接使用了 <MECharts>，请改用 <EChartsView>：{string.Join(", ", offenders)}");

        // 封装本身没有被改成空壳：仍然包着 MECharts，并且先等图表库就绪
        var view = ReadRepoFile("src/FinanceTracker.Web/Components/Ui/EChartsView.razor");
        Assert.Matches(@"<MECharts\b", view);
        Assert.Contains("FtBoot.chartsReady", view);
    }

    [Theory]
    [InlineData(ServerHostPath)]
    [InlineData(AppHostPath)]
    public void 宿主页的启动顺序_加载页样式在组件层之后_CDN资源不在head里_启动脚本先于Blazor(string hostPage)
    {
        var html = ReadRepoFile(hostPage);

        // 根节点自带初始状态，样式在 Blazor 起来之前就能按它生效
        Assert.Matches(@"<html\b[^>]*\bdata-ft-boot=""booting""", html);

        var ui = html.IndexOf("_content/FinanceTracker.Web/css/ui.css", StringComparison.Ordinal);
        var boot = html.IndexOf("_content/FinanceTracker.Web/css/boot.css", StringComparison.Ordinal);
        Assert.True(ui >= 0 && boot > ui, $"{hostPage} 的 boot.css 应当加载在 ui.css 之后（实际位置 ui={ui} boot={boot}）");

        // 图标字体与 ECharts 是 CDN 资源：写在 head 里是同步阻塞的，会挡住首次绘制（慢网 / 离线时连加载页都出不来），
        // 也拖住 window load（MAUI 的 BlazorWebView 在页面加载完成之后才启动 Blazor）——改由启动脚本在 load 之后注入
        var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        Assert.True(headEnd > 0);
        Assert.DoesNotContain("cdn.jsdelivr.net", html[..headEnd]);

        var script = Regex.Match(html, @"<script\b(?<attrs>[^>]*\bsrc=""_content/FinanceTracker\.Web/js/boot\.js""[^>]*)>");
        Assert.True(script.Success, $"{hostPage} 缺少启动脚本 boot.js");
        Assert.Matches(@"data-css=""https://cdn\.jsdelivr\.net/[^""]*materialdesignicons[^""]*\.css""", script.Groups["attrs"].Value);
        Assert.Contains("data-font=\"Material Design Icons\"", script.Groups["attrs"].Value);
        Assert.Matches(@"data-scripts=""https://cdn\.jsdelivr\.net/[^""]*echarts[^""]*\.js""", script.Groups["attrs"].Value);

        // 启动脚本（同步引入）在 Blazor 脚本之前，计时与观察才能尽早开始；加载页标记也在 Blazor 脚本之前（随 HTML 一起就位，不等脚本）
        var blazor = Regex.Match(html, @"_framework/blazor\.(server|webview)\.js");
        Assert.True(blazor.Success && script.Index < blazor.Index, $"{hostPage} 的 boot.js 应当在 Blazor 脚本之前");
        var marker = html.IndexOf("id=\"ft-boot\"", StringComparison.Ordinal);
        Assert.True(marker > 0 && marker < script.Index, $"{hostPage} 的加载页标记应当在启动脚本之前");
    }
}
