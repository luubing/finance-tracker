using System.Net;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 成员页的展示与判定规则：规则集中在 <see cref="LedgerMemberPresentation"/>，页面只负责取数与交互。
/// 其中最要紧的是两条：谁算「所有者」（决定管理入口是否出现）、云端失败怎么翻译成提示（「无权限」≠ 登录过期）
/// </summary>
public class LedgerMemberPresentationTests
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static LedgerMember Member(LedgerMemberRole role, Guid? userId = null) =>
        new() { Id = Guid.NewGuid(), LedgerId = Guid.NewGuid(), UserId = userId ?? Guid.NewGuid(), Role = role, Status = LedgerMemberStatus.Active };

    // ───────────── 谁是所有者 ─────────────

    [Theory]
    [InlineData(LedgerMemberRole.Owner, true)]
    [InlineData(LedgerMemberRole.Editor, false)]
    [InlineData(LedgerMemberRole.Viewer, false)]
    public void 成员关系已知时只认成员关系(LedgerMemberRole role, bool expected) =>
        Assert.Equal(expected, LedgerMemberPresentation.IsOwner(role, ledgerOwnerId: Me, userId: Me));

    [Fact]
    public void 转让所有权后本地账本归属仍是自己_也不再是所有者()
    {
        // 云端转让后我是可编辑成员；本地账本记录的归属可能还停留在我名下——不能再用它兜底，否则转让后仍看到所有者入口
        Assert.False(LedgerMemberPresentation.IsOwner(LedgerMemberRole.Editor, ledgerOwnerId: Me, userId: Me));
    }

    [Fact]
    public void 成员关系未知时退回账本归属判定()
    {
        Assert.True(LedgerMemberPresentation.IsOwner(null, ledgerOwnerId: Me, userId: Me));
        Assert.False(LedgerMemberPresentation.IsOwner(null, ledgerOwnerId: Other, userId: Me));
        Assert.False(LedgerMemberPresentation.IsOwner(null, ledgerOwnerId: null, userId: Me));
    }

    [Fact]
    public void 所有者可以管理其他成员_但不能管理所有者和自己()
    {
        Assert.True(LedgerMemberPresentation.CanManage(true, Member(LedgerMemberRole.Editor), Me));
        Assert.True(LedgerMemberPresentation.CanManage(true, Member(LedgerMemberRole.Viewer), Me));
        Assert.False(LedgerMemberPresentation.CanManage(true, Member(LedgerMemberRole.Owner), Me));
        Assert.False(LedgerMemberPresentation.CanManage(true, Member(LedgerMemberRole.Editor, userId: Me), Me));
    }

    [Fact]
    public void 非所有者没有任何管理入口()
    {
        Assert.False(LedgerMemberPresentation.CanManage(false, Member(LedgerMemberRole.Editor), Me));
        Assert.False(LedgerMemberPresentation.CanManage(false, Member(LedgerMemberRole.Viewer), Me));
    }

    [Theory]
    [InlineData(false, LedgerMemberRole.Editor, true)]
    [InlineData(false, LedgerMemberRole.Viewer, true)]
    [InlineData(false, null, false)]                                       // 不是成员（已被移除 / 尚未同步）：没有可退出的
    [InlineData(true, LedgerMemberRole.Owner, false)]                      // 所有者不能直接退出，须先转让所有权
    public void 退出账本只对生效的非所有者成员开放(bool isOwner, LedgerMemberRole? myRole, bool expected) =>
        Assert.Equal(expected, LedgerMemberPresentation.CanExit(isOwner, myRole));

    // ───────────── 分组与文案 ─────────────

    [Fact]
    public void 成员按所有者_可编辑_只读分组_空组不出现_组内保持原顺序()
    {
        var viewer = Member(LedgerMemberRole.Viewer);
        var editorA = Member(LedgerMemberRole.Editor);
        var owner = Member(LedgerMemberRole.Owner);
        var editorB = Member(LedgerMemberRole.Editor);

        var groups = LedgerMemberPresentation.Group(new[] { viewer, editorA, owner, editorB });

        Assert.Equal(new[] { LedgerMemberRole.Owner, LedgerMemberRole.Editor, LedgerMemberRole.Viewer }, groups.Select(g => g.Role));
        Assert.Equal(new[] { "所有者", "可编辑", "只读" }, groups.Select(g => g.Title));
        Assert.Equal(new[] { editorA, editorB }, groups[1].Members);

        var onlyOwner = LedgerMemberPresentation.Group(new[] { owner });
        Assert.Equal(LedgerMemberRole.Owner, Assert.Single(onlyOwner).Role);
        Assert.Empty(LedgerMemberPresentation.Group(Array.Empty<LedgerMember>()));
    }

    [Theory]
    [InlineData(LedgerMemberRole.Owner, "member-group-owner")]
    [InlineData(LedgerMemberRole.Editor, "member-group-editor")]
    [InlineData(LedgerMemberRole.Viewer, "member-group-viewer")]
    public void 分组的测试钩子按角色命名(LedgerMemberRole role, string expected) =>
        Assert.Equal(expected, LedgerMemberPresentation.GroupTestId(role));

    [Fact]
    public void 可邀请与可改成的角色不含所有者_所有者只能经转让产生()
    {
        Assert.Equal(new[] { LedgerMemberRole.Editor, LedgerMemberRole.Viewer }, LedgerMemberPresentation.AssignableRoles);
    }

    [Theory]
    [InlineData(LedgerMemberRole.Editor, LedgerMemberRole.Viewer, "改为只读成员")]
    [InlineData(LedgerMemberRole.Viewer, LedgerMemberRole.Editor, "改为可编辑成员")]
    public void 改角色是在可编辑与只读之间切换(LedgerMemberRole current, LedgerMemberRole expectedOther, string expectedLabel)
    {
        Assert.Equal(expectedOther, LedgerMemberPresentation.OtherRole(current));
        Assert.Equal(expectedLabel, LedgerMemberPresentation.ChangeRoleLabel(current));
    }

    [Fact]
    public void 每个角色都有说明和配色()
    {
        foreach (var role in Enum.GetValues<LedgerMemberRole>())
        {
            Assert.False(string.IsNullOrWhiteSpace(LedgerMemberPresentation.RoleDescription(role)), $"{role} 缺少说明");
            Assert.Contains(LedgerMemberPresentation.ToneFor(role), new[] { "purple", "green", "gray" });
        }
    }

    [Theory]
    [InlineData("13800138000", "138****8000")]
    [InlineData(null, "未知用户")]
    [InlineData("", "未知用户")]
    [InlineData("stub_0123456789abcde", "未知用户")]                        // 同步占位用户：不能把占位手机号露给用户
    public void 展示名是脱敏手机号(string? phone, string expected) =>
        Assert.Equal(expected, LedgerMemberPresentation.DisplayName(phone));

    // ───────────── 邀请手机号校验 ─────────────

    [Theory]
    [InlineData("13800138000")]
    [InlineData("  13800138000  ")]                                         // 首尾空格不算错（粘贴常带空格）
    public void 合法手机号通过(string input) => Assert.Null(LedgerMemberPresentation.ValidatePhone(input));

    [Theory]
    [InlineData(null, "手机号不能为空")]
    [InlineData("", "手机号不能为空")]
    [InlineData("   ", "手机号不能为空")]
    [InlineData("1380013800", "手机号必须是11位")]
    [InlineData("138001380001", "手机号必须是11位")]
    [InlineData("23800138000", "手机号格式不正确")]
    public void 非法手机号给出与登录一致的提示(string? input, string expected) =>
        Assert.Equal(expected, LedgerMemberPresentation.ValidatePhone(input));

    // ───────────── 失败提示 ─────────────

    private static Exception Cloud(HttpStatusCode status, string body) => new($"请求失败: {status} - {body}");

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"message":"该手机号尚未注册"}""", "邀请失败：该手机号尚未注册")]
    [InlineData(HttpStatusCode.Conflict, """{"message":"该用户已是账本成员"}""", "邀请失败：该用户已是账本成员")]
    [InlineData(HttpStatusCode.NotFound, """{"message":"成员不存在"}""", "邀请失败：成员不存在")]
    public void 云端的业务错误取出其中的message_并算警告(HttpStatusCode status, string body, string expected)
    {
        var failure = LedgerMemberPresentation.DescribeFailure("邀请", Cloud(status, body));

        Assert.Equal(expected, failure.Message);
        Assert.True(failure.IsWarning);
    }

    [Fact]
    public void 云端403是无权限_不是登录过期()
    {
        var failure = LedgerMemberPresentation.DescribeFailure(
            "邀请", Cloud(HttpStatusCode.Forbidden, """{"message":"只有账本所有者可以邀请成员"}"""));

        Assert.Equal("无权限：只有账本所有者可以邀请成员", failure.Message);
        Assert.True(failure.IsWarning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"title":"Forbidden"}""")]
    [InlineData("""{"message":"   "}""")]
    public void 云端403没有可用的message时只说无权限(string body)
    {
        var failure = LedgerMemberPresentation.DescribeFailure("移除", Cloud(HttpStatusCode.Forbidden, body));

        Assert.Equal("无权限", failure.Message);
    }

    [Fact]
    public void 本地服务层抛出的ForbiddenAccessException同样是无权限()
    {
        var failure = LedgerMemberPresentation.DescribeFailure("修改", new ForbiddenAccessException("只有账本所有者可以执行此操作"));

        Assert.Equal("无权限：只有账本所有者可以执行此操作", failure.Message);
        Assert.True(failure.IsWarning);
    }

    [Fact]
    public void 认证失败保持服务给出的文案_作为错误而不是警告()
    {
        var failure = LedgerMemberPresentation.DescribeFailure("邀请", new UnauthorizedAccessException("登录已过期，请重新登录"));

        Assert.Equal("登录已过期，请重新登录", failure.Message);
        Assert.False(failure.IsWarning);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"message":"System.NullReferenceException at Foo.Bar() in C:\\secret\\Path.cs"}""")]
    [InlineData(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    public void 云端5xx不原样展示响应内容(HttpStatusCode status, string body)
    {
        var failure = LedgerMemberPresentation.DescribeFailure("移除", Cloud(status, body));

        Assert.Equal("移除失败：云端服务暂时不可用，请稍后重试", failure.Message);
        Assert.False(failure.IsWarning);
        Assert.DoesNotContain("secret", failure.Message);
    }

    [Fact]
    public void 云端4xx但响应体不是约定的JSON时给通用提示()
    {
        var failure = LedgerMemberPresentation.DescribeFailure("转让", Cloud(HttpStatusCode.BadRequest, "<html>Bad Request</html>"));

        Assert.Equal("转让失败：请求未被接受，请刷新后重试", failure.Message);
        Assert.True(failure.IsWarning);
    }

    [Theory]
    [MemberData(nameof(ConnectivityFailures))]
    public void 连不上云端时提示检查网络_不露出底层异常消息(Exception exception)
    {
        var failure = LedgerMemberPresentation.DescribeFailure("邀请", exception);

        Assert.Equal("邀请失败：无法连接云端，请检查网络后重试", failure.Message);
        Assert.False(failure.IsWarning);
    }

    public static IEnumerable<object[]> ConnectivityFailures() =>
    [
        [new HttpRequestException("No connection could be made because the target machine actively refused it. (localhost:5270)")],
        [new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.")],
        [new TimeoutException()],
    ];

    [Fact]
    public void 服务层的业务校验消息直接展示_并算警告()
    {
        var failure = LedgerMemberPresentation.DescribeFailure("邀请", new InvalidOperationException("云端没有返回邀请结果，请稍后重试"));

        Assert.Equal("邀请失败：云端没有返回邀请结果，请稍后重试", failure.Message);
        Assert.True(failure.IsWarning);
    }

    [Fact]
    public void 其它未知异常保持原有的失败前缀加原消息()
    {
        var failure = LedgerMemberPresentation.DescribeFailure("加载成员", new FormatException("奇怪的错误"));

        Assert.Equal("加载成员失败：奇怪的错误", failure.Message);
        Assert.False(failure.IsWarning);
    }
}
