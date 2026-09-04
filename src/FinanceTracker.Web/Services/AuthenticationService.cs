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
    /// 认证状态变化事件（登录/登出后触发，页面可订阅以刷新用户信息）
    /// </summary>
    public event Action? AuthStateChanged;

    private void NotifyAuthStateChanged() => AuthStateChanged?.Invoke();

    // 注意：Blazor Server 的 IJSRuntime 是 RemoteJSRuntime（永远不是 IJSInProcessRuntime），
    // 不能用类型判断来检测"预渲染阶段"。预渲染期间调用 JS interop 会抛异常，电路断开时会抛
    // JSDisconnectedException，因此统一用 try/catch 安全封装：预渲染阶段静默失败（仅丢失
    // 本地持久化，不影响电路级缓存），交互阶段正常读写 localStorage。

    /// <summary>
    /// 安全写入 localStorage（预渲染/电路断开时静默忽略）
    /// </summary>
    private async Task TrySetItemAsync(string key, string? value)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch (Exception)
        {
            // 预渲染阶段或电路已断开，无法访问 localStorage；电路级缓存仍然有效
        }
    }

    /// <summary>
    /// 安全读取 localStorage（预渲染/电路断开时返回 null）
    /// </summary>
    private async Task<string?> TryGetItemAsync(string key)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 安全删除 localStorage 项
    /// </summary>
    private async Task TryRemoveItemAsync(string key)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch (Exception)
        {
        }
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

        _cachedToken = await TryGetItemAsync("authToken");
        return _cachedToken;
    }

    /// <summary>
    /// 保存 JWT Token
    /// </summary>
    public async Task SaveTokenAsync(string token)
    {
        _cachedToken = token;
        await TrySetItemAsync("authToken", token);
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

        var userIdStr = await TryGetItemAsync("userId");
        if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var userId))
        {
            _cachedUserId = userId;
            return userId;
        }

        return null;
    }

    /// <summary>
    /// 获取当前登录手机号（用于向云端换取 JWT）
    /// </summary>
    public async Task<string?> GetPhoneNumberAsync()
    {
        return await TryGetItemAsync("phoneNumber");
    }

    /// <summary>
    /// 保存用户信息
    /// </summary>
    public async Task SaveUserInfoAsync(Guid userId, string phoneNumber)
    {
        _cachedUserId = userId;
        await TrySetItemAsync("userId", userId.ToString());
        await TrySetItemAsync("phoneNumber", phoneNumber);

        // 通知订阅者（如首页）刷新用户信息
        NotifyAuthStateChanged();
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
        await TryRemoveItemAsync("authToken");
        await TryRemoveItemAsync("userId");
        await TryRemoveItemAsync("phoneNumber");

        // 通知订阅者刷新（如首页将回到登录页）
        NotifyAuthStateChanged();
    }
}
