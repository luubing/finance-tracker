using Microsoft.JSInterop;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 认证服务
/// </summary>
public class AuthenticationService
{
    private readonly IJSRuntime _jsRuntime;
    private string? _cachedToken;
    private Guid? _cachedUserId;
    private bool _isInitialized;

    public AuthenticationService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// 检查是否在预渲染模式
    /// </summary>
    private bool IsPrerendering => _jsRuntime is not IJSInProcessRuntime;

    /// <summary>
    /// 获取 JWT Token
    /// </summary>
    public async Task<string?> GetTokenAsync()
    {
        if (_cachedToken != null)
        {
            return _cachedToken;
        }

        if (IsPrerendering)
        {
            return null;
        }

        try
        {
            _cachedToken = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "authToken");
            return _cachedToken;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 保存 JWT Token
    /// </summary>
    public async Task SaveTokenAsync(string token)
    {
        _cachedToken = token;

        if (!IsPrerendering)
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", token);
        }
    }

    /// <summary>
    /// 获取当前用户 ID
    /// </summary>
    public async Task<Guid?> GetUserIdAsync()
    {
        if (_cachedUserId.HasValue)
        {
            return _cachedUserId;
        }

        if (IsPrerendering)
        {
            return null;
        }

        try
        {
            var userIdStr = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "userId");
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var userId))
            {
                _cachedUserId = userId;
                return userId;
            }
        }
        catch
        {
            // 忽略错误
        }

        return null;
    }

    /// <summary>
    /// 保存用户信息
    /// </summary>
    public async Task SaveUserInfoAsync(Guid userId, string phoneNumber)
    {
        _cachedUserId = userId;

        if (!IsPrerendering)
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "userId", userId.ToString());
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "phoneNumber", phoneNumber);
        }
    }

    /// <summary>
    /// 检查是否已登录
    /// </summary>
    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await GetTokenAsync();
        return !string.IsNullOrEmpty(token);
    }

    /// <summary>
    /// 登出
    /// </summary>
    public async Task LogoutAsync()
    {
        _cachedToken = null;
        _cachedUserId = null;

        if (!IsPrerendering)
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "userId");
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "phoneNumber");
        }
    }
}
