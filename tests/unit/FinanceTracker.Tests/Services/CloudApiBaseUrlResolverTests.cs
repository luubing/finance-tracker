using System.Reflection;
using System.Text.RegularExpressions;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 云端 API 基址不再写死在源码里（此前 MauiProgram 内置了一个线上域名，仓库一公开就等于公开了线上服务地址）：
/// 打包时由 MSBuild 属性 CloudApiBaseUrl 注入程序集元数据，运行时经 <see cref="CloudApiBaseUrlResolver"/> 读出并校验。
/// 这里覆盖规整 / 校验规则，并把"源码里不得再硬编码地址"和"项目文件确实注入了元数据"绑定到真实文件，
/// 有人把它们改回去时在这里先红。
/// </summary>
public sealed class CloudApiBaseUrlResolverTests
{
    [Theory]
    [InlineData("https://finance.example.com", "https://finance.example.com/")]
    [InlineData("https://finance.example.com/", "https://finance.example.com/")]
    [InlineData("  https://finance.example.com/  ", "https://finance.example.com/")]
    [InlineData("HTTPS://Finance.Example.COM/", "https://finance.example.com/")]
    [InlineData("http://10.0.2.2:5065", "http://10.0.2.2:5065/")]
    [InlineData("http://localhost:5065/", "http://localhost:5065/")]
    [InlineData("http://[::1]:5065", "http://[::1]:5065/")]
    // 结尾的 /api 去掉：HttpCloudSyncClient 统一补 /api/，HttpClient 的相对路径请求（api/auth/login）也一样，
    // 不去掉就会拼出 /api/api/...
    [InlineData("https://finance.example.com/api", "https://finance.example.com/")]
    [InlineData("https://finance.example.com/API/", "https://finance.example.com/")]
    // 反向代理子路径部署：保留子路径
    [InlineData("https://example.com/finance", "https://example.com/finance/")]
    [InlineData("https://example.com/finance/api", "https://example.com/finance/")]
    [InlineData("https://example.com:8443", "https://example.com:8443/")]
    [InlineData("https://example.com:65535/", "https://example.com:65535/")]
    public void 合法地址被规整为以斜杠结尾的基址(string raw, string expected)
    {
        Assert.Equal(expected, CloudApiBaseUrlResolver.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空值_报错并说明怎么指定(string? raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CloudApiBaseUrlResolver.Normalize(raw));

        Assert.Contains("CloudApiBaseUrl", ex.Message);
        Assert.Contains("-p:CloudApiBaseUrl=", ex.Message);
    }

    [Theory]
    [InlineData("finance.example.com")]               // 没有协议
    [InlineData("//finance.example.com/")]            // 协议相对
    [InlineData("/relative/path")]                    // 相对路径
    [InlineData("ftp://finance.example.com/")]        // 不是 http(s)
    [InlineData("file:///etc/passwd")]
    [InlineData("https://")]                          // 没有主机名
    [InlineData("https://finance.example.com/?x=1")]  // 查询参数
    [InlineData("https://finance.example.com/#top")]  // 片段
    [InlineData("https://user:secret@finance.example.com/")] // 凭据会随安装包公开
    [InlineData("https://finance.example.com:65536/")]       // 端口越界：装好包后一启动就崩，必须在构建期拦下
    [InlineData("https://finance.example.com:99999/")]
    [InlineData("https://finance.example.com:abc/")]
    public void 不合法的地址被拒绝(string raw)
    {
        Assert.Throws<InvalidOperationException>(() => CloudApiBaseUrlResolver.Normalize(raw));
    }

    [Fact]
    public void 带凭据的地址_报错信息里不回显密码()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CloudApiBaseUrlResolver.Normalize("https://user:secret@finance.example.com/"));

        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public void 从程序集元数据读取并规整()
    {
        var metadata = new[]
        {
            new AssemblyMetadataAttribute("SomethingElse", "ignored"),
            new AssemblyMetadataAttribute(CloudApiBaseUrlResolver.MetadataKey, "https://a.example.com/api"),
        };

        Assert.Equal("https://a.example.com/", CloudApiBaseUrlResolver.FromMetadata(metadata));
    }

    [Fact]
    public void 元数据里没有云端地址_报错而不是悄悄连到一个猜出来的地址()
    {
        var metadata = new[] { new AssemblyMetadataAttribute("SomethingElse", "x") };

        var ex = Assert.Throws<InvalidOperationException>(() => CloudApiBaseUrlResolver.FromMetadata(metadata));

        Assert.Contains("CloudApiBaseUrl", ex.Message);
    }

    [Fact]
    public void 没有注入元数据的程序集_报错()
    {
        // 测试程序集本身没有注入 CloudApiBaseUrl
        Assert.Throws<InvalidOperationException>(() => CloudApiBaseUrlResolver.FromAssembly(typeof(CloudApiBaseUrlResolverTests).Assembly));
    }

    // ---------- 与真实源码绑定 ----------

    [Fact]
    public void MauiProgram不再硬编码任何http地址()
    {
        var path = RepoRoot.Combine("src/FinanceTracker.App/MauiProgram.cs");

        var offending = File.ReadAllLines(path)
            .Select((line, index) => (Line: line.Trim(), Number: index + 1))
            .Where(x => !x.Line.StartsWith("//") && Regex.IsMatch(x.Line, "\"https?://", RegexOptions.IgnoreCase))
            .Select(x => $"MauiProgram.cs:{x.Number}: {x.Line}")
            .ToList();

        Assert.True(
            offending.Count == 0,
            "云端 API 地址必须经 CloudApiBaseUrlResolver 从构建时注入的元数据读取，不得在源码里写死：\n" +
            string.Join('\n', offending));
    }

    [Fact]
    public void MauiProgram经解析器读取云端地址()
    {
        var source = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.App/MauiProgram.cs"));

        Assert.Contains("CloudApiBaseUrlResolver.FromAssembly(", source);
    }

    [Fact]
    public void App项目文件按解析器约定的键注入云端地址元数据()
    {
        var csproj = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.App/FinanceTracker.App.csproj"));

        Assert.Contains("System.Reflection.AssemblyMetadataAttribute", csproj);
        Assert.Contains($"<_Parameter1>{CloudApiBaseUrlResolver.MetadataKey}</_Parameter1>", csproj);
        Assert.Contains("<_Parameter2>$(CloudApiBaseUrl)</_Parameter2>", csproj);
    }

    [Fact]
    public void App项目文件在发布时要求显式指定云端地址()
    {
        var csproj = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.App/FinanceTracker.App.csproj"));

        Assert.Contains("_IsPublishing", csproj);
        Assert.Contains("ValidateCloudApiBaseUrl", csproj);
    }

    [Fact]
    public void 构建期校验的错误信息不回显地址本身()
    {
        // 地址可能是手滑写进了用户名密码的 URL；Error 文本会进构建日志（公开仓库的 Actions 日志任何人都能看）
        var csproj = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.App/FinanceTracker.App.csproj"));

        var errorTexts = Regex.Matches(csproj, "<Error [^>]*?Text=\"(?<text>[^\"]*)\"", RegexOptions.Singleline)
            .Select(m => m.Groups["text"].Value)
            .ToList();

        Assert.NotEmpty(errorTexts);
        Assert.DoesNotContain(errorTexts, text => text.Contains("$(CloudApiBaseUrl)", StringComparison.Ordinal));
    }

    // ---------- 三处校验规则不漂移 ----------
    // 「地址长什么样」在三个地方各有一份：运行时 CloudApiBaseUrlResolver.Normalize（权威，基于 Uri）、
    // App 项目文件的 ValidateCloudApiBaseUrl（构建期）、iOS 工作流预检的 url_pattern（打包前）。
    // 后两处只能写成正则，不可能和 Uri 解析完全等价，所以只守两件事：
    //  1) 安全方向：正则放行的地址，Normalize 必须也认——否则就是"构建通过、装好后一启动就崩"（MauiProgram 启动时读取）
    //  2) 常见写法不被误拒——否则合法地址在 CI 里白白失败

    /// <summary>所有样例（含首尾空白、大写协议这类 Normalize 容忍、工作流更严的写法）</summary>
    private static readonly string[] AddressSamples =
    [
        "https://finance.example.com", "https://finance.example.com/", "https://finance.example.com/api",
        "http://10.0.2.2:5065/", "http://localhost:5065", "http://[::1]:5065/", "https://example.com/finance/api",
        "https://example.com:8443", "https://example.com:65535/",
        "  https://finance.example.com/  ", "HTTPS://Finance.Example.COM/",
        "finance.example.com", "//finance.example.com/", "/relative/path", "ftp://finance.example.com/",
        "file:///etc/passwd", "https://", "https:///path", "https://finance.example.com/?x=1",
        "https://finance.example.com/#top", "https://user:secret@finance.example.com/",
        "https://exa mple.com/", "http://[::1/", "http://[zzz]/",
        "https://example.com:65536/", "https://example.com:99999/", "https://example.com:123456/",
        "https://example.com:abc/", "https://example.com:-1/", "https://example.com:/",
    ];

    /// <summary>常见的合法写法：三处都必须放行</summary>
    private static readonly string[] CommonValidAddresses =
    [
        "https://finance.example.com", "https://finance.example.com/", "https://finance.example.com/api",
        "http://10.0.2.2:5065/", "http://localhost:5065", "http://[::1]:5065/", "https://example.com/finance/api",
        "https://example.com:8443", "https://example.com:65535/",
    ];

    private static Regex CsprojShapeRegex()
    {
        var csproj = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.App/FinanceTracker.App.csproj"));
        var match = Regex.Match(csproj, @"Regex\]::IsMatch\('\$\(CloudApiBaseUrl\)', '(?<pattern>[^']+)'");
        Assert.True(match.Success, "FinanceTracker.App.csproj 里找不到 ValidateCloudApiBaseUrl 的地址形状正则");
        return new Regex(match.Groups["pattern"].Value, RegexOptions.IgnoreCase);
    }

    private static Regex WorkflowShapeRegex()
    {
        var yaml = File.ReadAllText(RepoRoot.Combine(".github/workflows/build-ios.yml"));
        var match = Regex.Match(yaml, @"url_pattern='(?<pattern>[^']+)'");
        Assert.True(match.Success, "build-ios.yml 里找不到预检的 url_pattern");
        // POSIX 字符类换成 .NET 写法：这条规则只用到分组 / 区间 / 方括号，ERE 与 .NET 语义一致
        return new Regex(match.Groups["pattern"].Value.Replace("[:space:]", @"\s", StringComparison.Ordinal));
    }

    private static bool RuntimeAccepts(string raw)
    {
        try
        {
            CloudApiBaseUrlResolver.Normalize(raw);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [Fact]
    public void 构建期与打包前的正则不会放行运行时拒绝的地址()
    {
        var layers = new[] { ("App 项目文件 ValidateCloudApiBaseUrl", CsprojShapeRegex()), ("iOS 工作流预检 url_pattern", WorkflowShapeRegex()) };

        var unsafeAccepts = (
            from sample in AddressSamples
            where !RuntimeAccepts(sample)
            from layer in layers
            where layer.Item2.IsMatch(sample)
            select $"{layer.Item1} 放行了运行时会拒绝的地址：'{sample}'").ToList();

        Assert.True(
            unsafeAccepts.Count == 0,
            "构建通过但运行时 CloudApiBaseUrlResolver 拒绝，意味着装好的包一启动就崩：\n" + string.Join('\n', unsafeAccepts));
    }

    [Fact]
    public void 常见合法写法不被构建期与打包前的正则误拒()
    {
        var layers = new[] { ("App 项目文件 ValidateCloudApiBaseUrl", CsprojShapeRegex()), ("iOS 工作流预检 url_pattern", WorkflowShapeRegex()) };

        var falseRejects = (
            from sample in CommonValidAddresses
            from layer in layers
            where !layer.Item2.IsMatch(sample)
            select $"{layer.Item1} 误拒了合法地址：'{sample}'").ToList();

        Assert.True(falseRejects.Count == 0, string.Join('\n', falseRejects));
        Assert.All(CommonValidAddresses, sample => Assert.True(RuntimeAccepts(sample), $"运行时应当认可：'{sample}'"));
    }
}
