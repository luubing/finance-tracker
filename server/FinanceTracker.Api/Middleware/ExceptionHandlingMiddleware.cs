using System.Net;
using System.Text.Json;
using FinanceTracker.Core.Exceptions;

namespace FinanceTracker.Api.Middleware;

/// <summary>
/// 全局异常处理中间件
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发生未处理的异常");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            ArgumentException argEx => (HttpStatusCode.BadRequest, argEx.Message),
            // 权限拒绝（已认证但无权操作）→ 403；绝不能映射为 401，否则客户端会误判为登录过期而登出
            ForbiddenAccessException => (HttpStatusCode.Forbidden, exception.Message),
            // 认证失败 → 401（客户端据此重新登录）
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "未授权访问"),
            KeyNotFoundException => (HttpStatusCode.NotFound, "资源不存在"),
            _ => (HttpStatusCode.InternalServerError, "服务器内部错误")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            error = message,
            requestId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
