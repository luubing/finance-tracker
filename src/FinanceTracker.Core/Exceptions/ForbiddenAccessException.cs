namespace FinanceTracker.Core.Exceptions;

/// <summary>
/// 权限拒绝异常（已认证但无权执行该操作，对应 HTTP 403）。
/// 注意：与 UnauthorizedAccessException（认证失败，对应 HTTP 401）语义不同——
/// 客户端收到 401 会触发登出，权限拒绝绝不能使用该异常，否则会导致用户被意外登出。
/// </summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message)
    {
    }
}