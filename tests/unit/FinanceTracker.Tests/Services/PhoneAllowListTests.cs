using FinanceTracker.Api.Services;
using FinanceTracker.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 云端手机号白名单（可选，默认关闭）：云端登录只凭手机号、没有验证码，公开部署时任何人拿到一个号码就能注册并登录对应账号。
/// 名单是一道粗粒度的「门」，规则要点：
/// <list type="bullet">
/// <item>名单为空 = 关闭 = 与此前完全一致（默认）</item>
/// <item>启用后精确匹配；手滑写错（格式不对）宁可把所有人挡在外面，也不能悄悄退回"不设防"</item>
/// <item>配置既能写成逗号分隔的字符串（环境变量），也能写成 JSON 数组</item>
/// </list>
/// </summary>
public sealed class PhoneAllowListTests
{
    private const string Alice = "13800138000";
    private const string Bob = "13900139000";

    // ---------- 关闭（默认） ----------

    [Fact]
    public void 默认关闭_不拦任何人()
    {
        var list = PhoneAllowList.Disabled;

        Assert.False(list.IsEnabled);
        Assert.Equal(0, list.Count);
        Assert.True(list.IsAllowed(Alice));
        Assert.True(list.IsAllowed("18600000000"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ; ，；、\n ")]
    public void 空配置视为关闭(string? raw)
    {
        var list = new PhoneAllowList(new[] { raw });

        Assert.False(list.IsEnabled);
        Assert.True(list.IsAllowed(Alice));
    }

    [Fact]
    public void 条目集合为null视为关闭()
    {
        Assert.False(new PhoneAllowList(null).IsEnabled);
    }

    // ---------- 启用后的匹配 ----------

    [Fact]
    public void 启用后只放行名单内的号码()
    {
        var list = new PhoneAllowList(new[] { $"{Alice},{Bob}" });

        Assert.True(list.IsEnabled);
        Assert.Equal(2, list.Count);
        Assert.True(list.IsAllowed(Alice));
        Assert.True(list.IsAllowed(Bob));
        Assert.False(list.IsAllowed("13700137000"));
    }

    [Theory]
    [InlineData("13800138001")]   // 只差一位
    [InlineData("1380013800")]    // 少一位
    [InlineData("138001380000")]  // 多一位
    [InlineData(" 13800138000")]  // 带空格的请求号码不算同一个号码（登录接口本身会先拒绝这种格式）
    public void 匹配是精确的(string phoneNumber)
    {
        var list = new PhoneAllowList(new[] { Alice });

        Assert.False(list.IsAllowed(phoneNumber));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 启用后空号码一律拒绝(string? phoneNumber)
    {
        var list = new PhoneAllowList(new[] { Alice });

        Assert.False(list.IsAllowed(phoneNumber));
    }

    // ---------- 写法容错 ----------

    [Theory]
    [InlineData("13800138000,13900139000")]            // 英文逗号
    [InlineData("13800138000;13900139000")]            // 分号
    [InlineData("13800138000\n13900139000")]           // 换行
    [InlineData("13800138000\r\n13900139000\r\n")]     // Windows 换行
    [InlineData("13800138000，13900139000")]           // 中文逗号
    [InlineData("13800138000、13900139000")]           // 顿号
    [InlineData(" 13800138000 ,  13900139000 ,")]      // 多余空白与结尾分隔符
    [InlineData("138 0013 8000, 139-0013-9000")]       // 空格 / 连字符
    [InlineData("+86 138-0013-8000;+8613900139000")]   // +86 前缀
    [InlineData("(138)0013-8000,13900139000")]         // 括号 / 连字符
    public void 各种常见写法都能解析出两个号码(string raw)
    {
        var list = new PhoneAllowList(new[] { raw });

        Assert.Equal(2, list.Count);
        Assert.Equal(0, list.InvalidEntryCount);
        Assert.True(list.IsAllowed(Alice));
        Assert.True(list.IsAllowed(Bob));
    }

    [Fact]
    public void 重复条目去重()
    {
        var list = new PhoneAllowList(new[] { $"{Alice},{Alice}", "+86 138 0013 8000" });

        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void 多个条目来源会合并()
    {
        var list = new PhoneAllowList(new[] { Alice, null, "", Bob });

        Assert.Equal(2, list.Count);
    }

    // ---------- 手滑写错：宁可挡住所有人 ----------

    [Fact]
    public void 用空格分隔多个号码是常见的手滑_仍视为启用并挡住所有人_同时计入格式错误()
    {
        // 只认逗号 / 分号 / 换行分隔；空格分隔会被当成一个很长的条目
        var list = new PhoneAllowList(new[] { "13800138000 13900139000" });

        Assert.True(list.IsEnabled);
        Assert.Equal(1, list.InvalidEntryCount);
        Assert.False(list.IsAllowed(Alice));
        Assert.False(list.IsAllowed(Bob));
    }

    [Fact]
    public void 格式错误的条目与正确条目并存_只统计错误的()
    {
        var list = new PhoneAllowList(new[] { $"{Alice},abc,2345" });

        Assert.Equal(3, list.Count);
        Assert.Equal(2, list.InvalidEntryCount);
        Assert.True(list.IsAllowed(Alice));
    }

    // ---------- 从配置读取 ----------

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void 配置键是Auth_AllowedPhoneNumbers()
    {
        Assert.Equal("Auth:AllowedPhoneNumbers", PhoneAllowList.ConfigurationKey);
    }

    [Fact]
    public void 从配置读取_字符串写法()
    {
        var list = PhoneAllowList.FromConfiguration(Config((PhoneAllowList.ConfigurationKey, $"{Alice},{Bob}")));

        Assert.True(list.IsEnabled);
        Assert.Equal(2, list.Count);
        Assert.True(list.IsAllowed(Bob));
    }

    [Fact]
    public void 从配置读取_数组写法()
    {
        // JSON 数组 / 环境变量 Auth__AllowedPhoneNumbers__0 的写法
        var list = PhoneAllowList.FromConfiguration(Config(
            ($"{PhoneAllowList.ConfigurationKey}:0", Alice),
            ($"{PhoneAllowList.ConfigurationKey}:1", Bob)));

        Assert.Equal(2, list.Count);
        Assert.True(list.IsAllowed(Alice));
        Assert.True(list.IsAllowed(Bob));
    }

    [Fact]
    public void 从配置读取_空字符串视为关闭()
    {
        var list = PhoneAllowList.FromConfiguration(Config((PhoneAllowList.ConfigurationKey, "")));

        Assert.False(list.IsEnabled);
    }

    [Fact]
    public void 从配置读取_键不存在视为关闭()
    {
        var list = PhoneAllowList.FromConfiguration(Config(("Something:Else", "x")));

        Assert.False(list.IsEnabled);
    }

    /// <summary>
    /// 把「默认关闭」绑定到仓库里真实的 appsettings.json：有人在默认配置里填了名单（比如把自己的号码提交进仓库）、
    /// 或者改了键名而没同步代码时，这里会失败，而不是让名单悄悄失效 / 默认就拦人。
    /// </summary>
    [Fact]
    public void 仓库appsettings_json默认不启用名单且键名与代码一致()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepoRoot.Combine("server/FinanceTracker.Api/appsettings.json"), optional: false)
            .Build();

        Assert.True(
            configuration.GetSection(PhoneAllowList.ConfigurationKey).Exists(),
            "appsettings.json 里应保留 Auth:AllowedPhoneNumbers 这一项（默认留空），方便部署者发现这个开关");
        Assert.False(PhoneAllowList.FromConfiguration(configuration).IsEnabled);
    }
}
