using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FinanceTracker.Web.Services;

/// <summary>
/// HTTP 服务
/// </summary>
public class HttpService
{
    private readonly HttpClient _httpClient;
    private readonly AuthenticationService _authService;

    public HttpService(HttpClient httpClient, AuthenticationService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    /// <summary>
    /// 发送 GET 请求
    /// </summary>
    public async Task<T?> GetAsync<T>(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync<T>(request);
    }

    /// <summary>
    /// 发送 POST 请求
    /// </summary>
    public async Task<T?> PostAsync<T>(string url, object? data = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (data != null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(data),
                Encoding.UTF8,
                "application/json");
        }
        return await SendAsync<T>(request);
    }

    /// <summary>
    /// 发送 PUT 请求
    /// </summary>
    public async Task<T?> PutAsync<T>(string url, object? data = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);
        if (data != null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(data),
                Encoding.UTF8,
                "application/json");
        }
        return await SendAsync<T>(request);
    }

    /// <summary>
    /// 发送 DELETE 请求
    /// </summary>
    public async Task<T?> DeleteAsync<T>(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        return await SendAsync<T>(request);
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request)
    {
        // 本地未持有 JWT 时，尝试用已登录手机号向云端换取（MAUI/Web 登录均为本地登录，
        // 不会自动产生云端 token；同步等云端接口必须携带 Bearer，否则一律 401）
        await EnsureRemoteTokenAsync();

        // 添加 JWT Token
        var token = await _authService.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        // 处理 401 未授权
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await _authService.LogoutAsync();
            throw new UnauthorizedAccessException("登录已过期，请重新登录");
        }

        // 处理其他错误
        var errorContent = await response.Content.ReadAsStringAsync();
        throw new Exception($"请求失败: {response.StatusCode} - {errorContent}");
    }

    /// <summary>
    /// 确保本地已持有云端 JWT：若无 token 且本地已保存手机号，则调用云端 /api/auth/login
    /// 换取 token 并持久化。已持有 token 或未登录时不做任何事。失败静默（由后续请求报错）。
    /// </summary>
    private async Task EnsureRemoteTokenAsync()
    {
        try
        {
            var existing = await _authService.GetTokenAsync();
            if (!string.IsNullOrEmpty(existing))
            {
                return;
            }

            var phoneNumber = await _authService.GetPhoneNumberAsync();
            if (string.IsNullOrEmpty(phoneNumber))
            {
                return;
            }

            using var content = new StringContent(
                JsonSerializer.Serialize(new { phoneNumber }),
                Encoding.UTF8,
                "application/json");

            using var loginResponse = await _httpClient.PostAsync("api/auth/login", content);
            if (!loginResponse.IsSuccessStatusCode)
            {
                return;
            }

            var loginJson = await loginResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(loginJson);
            if (doc.RootElement.TryGetProperty("token", out var tokenElement) &&
                tokenElement.ValueKind == JsonValueKind.String)
            {
                var remoteToken = tokenElement.GetString();
                if (!string.IsNullOrEmpty(remoteToken))
                {
                    await _authService.SaveTokenAsync(remoteToken);
                }
            }
        }
        catch
        {
            // 远端换取 token 失败（离线/服务不可用等）不影响本地功能，后续请求会再试
        }
    }
}
