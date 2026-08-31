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
}
