using System.Text.RegularExpressions;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 渲染档位（23 号 ticket）：低端设备判定准则、空实现，以及设计令牌里「材质降级为不透明」的三处覆盖块必须一致、
/// 不漏掉任何一个玻璃令牌——这是「令牌层一处切换」这条承诺的单元测试。
/// </summary>
public sealed class RenderingProfileTests
{
    // ---- 低端设备判定 ----

    [Theory]
    [InlineData(true, 8L * 1024 * 1024 * 1024, true)] // 系统标了低内存设备：不看内存大小
    [InlineData(false, 1_800_000_000L, true)] // 标称 2 GB
    [InlineData(false, 2_900_000_000L, true)] // 标称 3 GB 的机型实际约报 2.7–2.9 GiB 之内
    [InlineData(false, 3_221_225_471L, true)] // 3 GiB 减 1 字节
    [InlineData(false, 3_221_225_472L, false)] // 恰好 3 GiB：不算低端
    [InlineData(false, 3_800_000_000L, false)] // 标称 4 GB 约报 3.6–3.8 GiB
    [InlineData(false, 12L * 1024 * 1024 * 1024, false)]
    [InlineData(false, 0L, false)] // 读不到内存：宁可保持完整质感，不误降级
    [InlineData(false, -1L, false)]
    public void 低端设备判定_低内存标记或总内存低于3GiB(bool lowRamFlag, long totalMemoryBytes, bool expected)
    {
        Assert.Equal(expected, LowEndDeviceCriteria.IsLowEnd(lowRamFlag, totalMemoryBytes));
    }

    [Fact]
    public void 空实现恒为不降级_也不触发变化事件()
    {
        var service = new NoOpRenderingProfileService();
        var raised = 0;
        EventHandler<bool> handler = (_, _) => raised++;

        service.PreferSolidMaterialsChanged += handler;
        service.PreferSolidMaterialsChanged -= handler;

        Assert.False(service.PreferSolidMaterials);
        Assert.Equal(0, raised);
    }

    // ---- 令牌层的降级覆盖 ----

    private static readonly Lazy<string> TokensCss = new(() =>
        Regex.Replace(File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/tokens.css")), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline));

    private static readonly Lazy<string> UiCss = new(() =>
        Regex.Replace(File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/css/ui.css")), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline));

    /// <summary>把一个声明块的正文拆成「名字 → 值」（值里的空白折叠成单个空格）。</summary>
    private static Dictionary<string, string> Declarations(string body) =>
        body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split(':', 2))
            .ToDictionary(p => p[0].Trim(), p => Regex.Replace(p[1], @"\s+", " ").Trim());

    private static Dictionary<string, string> OverrideBlock(string pattern)
    {
        var match = Regex.Match(TokensCss.Value, pattern, RegexOptions.Singleline);
        Assert.True(match.Success, $"tokens.css 缺少降级覆盖块：{pattern}");
        return Declarations(match.Groups["body"].Value);
    }

    private static Dictionary<string, string> MediaOverride() =>
        OverrideBlock(@"@media \(prefers-reduced-transparency: reduce\)\s*\{\s*:root\s*\{(?<body>[^}]*)\}");

    private static Dictionary<string, string> RootSwitchOverride() =>
        OverrideBlock(@":root\[data-ft-solid=""true""\]\s*\{(?<body>[^}]*)\}");

    private static Dictionary<string, string> SupportsOverride() =>
        OverrideBlock(@"@supports not \(\(-webkit-backdrop-filter: blur\(1px\)\) or \(backdrop-filter: blur\(1px\)\)\)\s*\{\s*:root\s*\{(?<body>[^}]*)\}");

    /// <summary>主 :root 里第一处定义的令牌值。</summary>
    private static string Token(string name)
    {
        var match = Regex.Match(TokensCss.Value, $@"^\s*{Regex.Escape(name)}\s*:\s*(?<value>[^;]+);", RegexOptions.Multiline);
        Assert.True(match.Success, $"tokens.css 缺少令牌 {name}");
        return Regex.Replace(match.Groups["value"].Value, @"\s+", " ").Trim();
    }

    [Fact]
    public void 三处降级覆盖块的声明逐条一致()
    {
        var media = MediaOverride();
        var rootSwitch = RootSwitchOverride();
        var supports = SupportsOverride();

        Assert.Equal(media.OrderBy(p => p.Key), rootSwitch.OrderBy(p => p.Key));
        Assert.Equal(media.OrderBy(p => p.Key), supports.OrderBy(p => p.Key));
    }

    [Fact]
    public void 降级覆盖块覆盖了每一个玻璃令牌_不漏()
    {
        // 主 :root 里所有「会透出下层内容」的玻璃令牌：玻璃的底色 / 模糊 / 投影，以及语音覆盖层的底色 / 模糊。
        // 高光块（thumb）叠在玻璃之上、本身不透出下层，不在此列；实色令牌（solid）是覆盖用的取值而不是被覆盖对象
        var glassTokens = Regex.Matches(TokensCss.Value, @"^\s*(?<name>--ft-(?:glass-(?:dark-)?(?:bg|filter|shadow)|glass-alert-bg|voice-bg(?:-cancel)?|voice-filter))\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .ToList();
        Assert.Equal(10, glassTokens.Count);

        var overridden = MediaOverride().Keys.ToHashSet();
        var forgotten = glassTokens.Where(name => !overridden.Contains(name)).ToList();
        Assert.True(forgotten.Count == 0, $"降级覆盖块漏了玻璃令牌：{string.Join(", ", forgotten)}");
    }

    [Fact]
    public void 降级后模糊一律为none_底色一律取不透明的实色令牌()
    {
        foreach (var (name, value) in MediaOverride())
        {
            if (name.EndsWith("-filter", StringComparison.Ordinal))
            {
                Assert.Equal("none", value);
            }
            else if (name.EndsWith("-bg", StringComparison.Ordinal) || name.EndsWith("-bg-cancel", StringComparison.Ordinal))
            {
                var token = Regex.Match(value, @"^var\((?<ref>--ft-[a-z-]+)\)$");
                Assert.True(token.Success, $"{name} 应当引用一个实色令牌，实际是 {value}");

                // 被引用的令牌必须是不透明的十六进制实色（没有透明度）
                Assert.Matches(@"^#[0-9A-Fa-f]{6}$", Token(token.Groups["ref"].Value));
            }
        }
    }

    [Fact]
    public void 组件样式里的backdrop_filter只能是none或引用模糊令牌_降级才能一处切换()
    {
        var allowed = new[] { "none", "var(--ft-glass-filter)", "var(--ft-glass-dark-filter)", "var(--ft-voice-filter)" };

        var values = Regex.Matches(UiCss.Value, @"(?<![-\w])backdrop-filter\s*:\s*(?<value>[^;}]+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups["value"].Value.Trim())
            .Distinct()
            .ToList();

        Assert.NotEmpty(values);
        var offenders = values.Where(v => !allowed.Contains(v)).ToList();
        Assert.True(offenders.Count == 0, $"ui.css 里有直接写死的 backdrop-filter：{string.Join(" | ", offenders)}——模糊必须来自令牌，降级才能一处切换");
    }

    [Fact]
    public void 玻璃表面的底色只引用令牌_不写死半透明颜色()
    {
        // 三个玻璃基类 / Alert / Toast / 语音覆盖层的 background 必须引用令牌；写死 rgba(...) 就绕过了降级
        foreach (var selector in new[] { @"\.ft-glass", @"\.ft-glass-dark", @"\.ft-alert", @"\.ft-voice" })
        {
            var rule = Regex.Match(UiCss.Value, $@"(?m)^(?:[^{{}}\n]*,\s*)?{selector}\s*(?:,[^{{}}]*)?\{{(?<body>[^}}]*)\}}");
            Assert.True(rule.Success, $"ui.css 里找不到 {selector} 的规则");
            Assert.Matches(@"background\s*:\s*var\(--ft-", rule.Groups["body"].Value);
        }
    }

    [Fact]
    public void 根开关与_Sheet_升起标记由_ui_js_导出和维护()
    {
        var js = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/wwwroot/js/ui.js"));
        var runtime = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Web/Components/UiRuntime.razor"));

        Assert.Contains("export function setSolid", js);
        Assert.Contains("data-ft-solid", js);
        Assert.Contains("data-ft-sheet-open", js);
        Assert.Contains("\"setSolid\"", runtime);
        Assert.Contains("IRenderingProfileService", runtime);
    }
}
