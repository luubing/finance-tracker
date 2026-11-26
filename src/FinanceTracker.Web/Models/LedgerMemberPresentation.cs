using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 成员页的一个分组（同一角色的成员）
/// </summary>
/// <param name="Role">角色</param>
/// <param name="Members">该角色下的成员（保持传入顺序）</param>
public sealed record MemberGroup(LedgerMemberRole Role, IReadOnlyList<LedgerMember> Members)
{
    /// <summary>分组标题（与徽标共用角色短标签）</summary>
    public string Title => LedgerPresentation.RoleLabel(Role);
}

/// <summary>
/// 一次失败的成员操作该怎么告诉用户
/// </summary>
/// <param name="Message">Toast 文案</param>
/// <param name="IsWarning">true：预期内的业务拒绝（无权限、已是成员……），用警告样式；false：真正的失败，用错误样式</param>
public sealed record MemberFailure(string Message, bool IsWarning);

/// <summary>
/// 账本成员页的展示与判定规则（纯函数，单元测试覆盖）：分组、角色说明、身份判定、手机号校验、失败文案。
/// 页面只负责取数与交互，规则集中在这里。
/// </summary>
public static partial class LedgerMemberPresentation
{
    /// <summary>
    /// 角色的一句话说明（权限口径见 ADR 0004）
    /// </summary>
    public static string RoleDescription(LedgerMemberRole role) => role switch
    {
        LedgerMemberRole.Owner => "管理成员，拥有账本全部权限",
        LedgerMemberRole.Editor => "可记账，可编辑自己的账单",
        _ => "只读，仅可查看账单",
    };

    /// <summary>
    /// 图标块配色：所有者品牌紫、可编辑绿、只读灰
    /// </summary>
    public static string ToneFor(LedgerMemberRole role) => role switch
    {
        LedgerMemberRole.Owner => "purple",
        LedgerMemberRole.Editor => "green",
        _ => "gray",
    };

    /// <summary>
    /// 分组的 E2E 钩子：member-group-owner / member-group-editor / member-group-viewer
    /// </summary>
    public static string GroupTestId(LedgerMemberRole role) => $"member-group-{role.ToString().ToLowerInvariant()}";

    /// <summary>
    /// 可邀请 / 可改成的角色（不含所有者：所有者只能经「转让所有权」产生）
    /// </summary>
    public static IReadOnlyList<LedgerMemberRole> AssignableRoles { get; } = [LedgerMemberRole.Editor, LedgerMemberRole.Viewer];

    /// <summary>
    /// 在可编辑 / 只读之间切换时的另一个角色
    /// </summary>
    public static LedgerMemberRole OtherRole(LedgerMemberRole role) =>
        role == LedgerMemberRole.Editor ? LedgerMemberRole.Viewer : LedgerMemberRole.Editor;

    /// <summary>
    /// 操作菜单里「改角色」一项的文字
    /// </summary>
    public static string ChangeRoleLabel(LedgerMemberRole current) => $"改为{LedgerPresentation.RoleLabel(OtherRole(current))}成员";

    /// <summary>
    /// 展示名：脱敏手机号；没有手机号或只是同步占位用户时「未知用户」
    /// </summary>
    public static string DisplayName(string? phoneNumber) => LedgerPresentation.MaskPhone(phoneNumber);

    /// <summary>
    /// 当前用户是不是账本所有者。
    /// 成员关系已知（云端同步下来的或本地补建的 Owner 行）时只认成员关系——转让所有权之后，本地账本记录的归属
    /// 可能还停留在原所有者名下，不能再用它兜底，否则原所有者转让后仍会看到所有者才有的入口；
    /// 成员关系未知（云端不可用、尚未同步）时才退回「账本归属 = 所有者」的本地判定（ADR 0004）。
    /// </summary>
    /// <param name="myRole">我在该账本的生效角色；未知为 null</param>
    /// <param name="ledgerOwnerId">本地账本记录的所有者</param>
    /// <param name="userId">当前用户</param>
    public static bool IsOwner(LedgerMemberRole? myRole, Guid? ledgerOwnerId, Guid userId) =>
        myRole is { } role ? role == LedgerMemberRole.Owner : ledgerOwnerId.HasValue && ledgerOwnerId.Value == userId;

    /// <summary>
    /// 所有者能否管理这个成员（改角色 / 转让 / 移除）：不能管理所有者本人，也不能管理自己
    /// </summary>
    public static bool CanManage(bool isOwner, LedgerMember member, Guid userId) =>
        isOwner && member.Role != LedgerMemberRole.Owner && member.UserId != userId;

    /// <summary>
    /// 当前用户能否退出账本：只有已生效的非所有者成员可以（所有者必须先转让所有权）
    /// </summary>
    public static bool CanExit(bool isOwner, LedgerMemberRole? myRole) => !isOwner && myRole is not null;

    /// <summary>
    /// 按角色分组（所有者 → 可编辑 → 只读），空组不返回
    /// </summary>
    public static IReadOnlyList<MemberGroup> Group(IEnumerable<LedgerMember> members)
    {
        var list = members.ToList();
        return new[] { LedgerMemberRole.Owner, LedgerMemberRole.Editor, LedgerMemberRole.Viewer }
            .Select(role => new MemberGroup(role, list.Where(m => m.Role == role).ToList()))
            .Where(group => group.Members.Count > 0)
            .ToList();
    }

    /// <summary>
    /// 校验邀请手机号（去掉首尾空格后按登录同一套规则），合法返回 null，否则返回给用户看的错误文案。
    /// 「是否已注册」只有云端知道，由云端校验（见 <see cref="DescribeFailure"/>）。
    /// </summary>
    public static string? ValidatePhone(string? input) => LoginPresentation.ValidatePhone(input);

    /// <summary>
    /// HttpService 对云端非 2xx 响应抛的异常消息：「请求失败: {状态码名} - {响应体}」
    /// </summary>
    [GeneratedRegex(@"^请求失败: (?<status>[A-Za-z]+) - (?<body>.*)$", RegexOptions.Singleline)]
    private static partial Regex CloudFailurePattern();

    /// <summary>
    /// 把失败的成员操作翻译成给用户看的提示。
    /// 云端接口的业务错误响应体是 <c>{"message":"…"}</c>（手机号未注册、已是成员、不能移除所有者……），把其中的文案取出来，
    /// 而不是把「请求失败: Conflict - {…}」整串露给用户；「无权限」（403）单独成一类，保持与 401 的区分——
    /// 无权限只是告诉用户做不了，绝不会让用户登出（登出只发生在 401，由 HttpService 处理）。
    /// </summary>
    /// <param name="action">操作名，如「邀请」「移除」，用于「{操作}失败：…」</param>
    /// <param name="exception">捕获到的异常</param>
    public static MemberFailure DescribeFailure(string action, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case ForbiddenAccessException forbidden:
                return new MemberFailure($"无权限：{forbidden.Message}", IsWarning: true);

            case UnauthorizedAccessException unauthorized:
                // 认证失败（401）：HttpService 已经登出，文案本身就是「登录已过期，请重新登录」
                return new MemberFailure(unauthorized.Message, IsWarning: false);

            case HttpRequestException or TimeoutException or TaskCanceledException:
                return new MemberFailure($"{action}失败：无法连接云端，请检查网络后重试", IsWarning: false);

            case InvalidOperationException or ArgumentException:
                // 服务层的业务校验，消息本身就是面向用户的中文
                return new MemberFailure($"{action}失败：{exception.Message}", IsWarning: true);
        }

        var cloud = CloudFailurePattern().Match(exception.Message);
        if (cloud.Success)
        {
            var serverMessage = TryReadServerMessage(cloud.Groups["body"].Value);

            switch (cloud.Groups["status"].Value)
            {
                case "Forbidden":
                    return new MemberFailure(serverMessage is null ? "无权限" : $"无权限：{serverMessage}", IsWarning: true);

                case "BadRequest" or "NotFound" or "Conflict" or "UnprocessableEntity":
                    return new MemberFailure($"{action}失败：{serverMessage ?? "请求未被接受，请刷新后重试"}", IsWarning: true);

                default:
                    // 5xx 等：响应体可能带内部细节，不原样展示
                    return new MemberFailure($"{action}失败：云端服务暂时不可用，请稍后重试", IsWarning: false);
            }
        }

        return new MemberFailure($"{action}失败：{exception.GetUserMessage()}", IsWarning: false);
    }

    /// <summary>
    /// 取云端错误响应体里的 message；不是本应用约定的 JSON 就返回 null（框架生成的 ProblemDetails 标题是英文，不展示）
    /// </summary>
    private static string? TryReadServerMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString()?.Trim();
                return string.IsNullOrEmpty(text) ? null : text;
            }
        }
        catch (JsonException)
        {
            // 不是 JSON（例如网关返回的 HTML 错误页）：当作没有文案
        }

        return null;
    }
}
