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

    public AuthenticationService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// 获取 JWT Token
    /// </summary>
    public async Task<string?> GetTokenAsync()
    {
        if (_cachedToken != null)
        {
            return _cachedToken;
        }

        _cachedToken = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "authToken");
        return _cachedToken;
    }

    /// <summary>
    /// 保存 JWT Token
    /// </summary>
    public async Task SaveTokenAsync(string token)
    {
        _cachedToken = token;
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", token);
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

        var userIdStr = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "userId");
        if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var userId))
        {
            _cachedUserId = userId;
            return userId;
        }

        return null;
    }

    /// <summary>
    /// 保存用户信息
    /// </summary>
    public async Task SaveUserInfoAsync(Guid userId, string phoneNumber)
    {
        _cachedUserId = userId;
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "userId", userId.ToString());
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "phoneNumber", phoneNumber);
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
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "userId");
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "phoneNumber");
    }
}
