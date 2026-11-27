using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinanceTracker.Core.Exceptions;

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

    // 各请求方法的 cancellationToken 用于调用方给云端请求设期限（例如页面加载时顺带刷新成员缓存，
    // 云端连不上就别一直等）：取消会真正中止请求（含换取令牌的那一次），而不是丢下一个后台任务继续跑。

    /// <summary>
    /// 发送 GET 请求
    /// </summary>
    public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync<T>(request, cancellationToken);
    }

    /// <summary>
    /// 发送 POST 请求
    /// </summary>
    public async Task<T?> PostAsync<T>(string url, object? data = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (data != null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(data),
                Encoding.UTF8,
                "application/json");
        }
        return await SendAsync<T>(request, cancellationToken);
    }

    /// <summary>
    /// 发送 PUT 请求
    /// </summary>
    public async Task<T?> PutAsync<T>(string url, object? data = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);
        if (data != null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(data),
                Encoding.UTF8,
                "application/json");
        }
        return await SendAsync<T>(request, cancellationToken);
    }

    /// <summary>
    /// 发送 DELETE 请求
    /// </summary>
    public async Task<T?> DeleteAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        return await SendAsync<T>(request, cancellationToken);
    }

    /// <summary>
    /// 云端把当前登录用户识别为哪个用户 Id（取自 JWT 载荷）。本地没有 token 时先尝试换取；拿不到返回 null。
    /// 用于同步时把云端用户 Id 映射回本地用户（两边各自生成用户 Id，通常不相同）。
    /// </summary>
    public async Task<Guid?> GetCloudUserIdAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRemoteTokenAsync(cancellationToken);
        return JwtClaimReader.TryReadUserId(await _authService.GetTokenAsync());
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 本地未持有 JWT 时，尝试用已登录手机号向云端换取（MAUI/Web 登录均为本地登录，
        // 不会自动产生云端 token；同步等云端接口必须携带 Bearer，否则一律 401）
        await EnsureRemoteTokenAsync(cancellationToken);

        // 添加 JWT Token
        var token = await _authService.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
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
        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new Exception($"请求失败: {response.StatusCode} - {errorContent}");
    }

    /// <summary>
    /// 确保本地持有"属于当前登录手机号"的云端 JWT：若无 token（或 token 属于另一个手机号）
    /// 且本地已保存手机号，则调用云端 /api/auth/login 换取 token 并持久化。
    /// 已持有匹配的 token 或未登录时不做任何事。失败静默（由后续请求报错）；
    /// 调用方主动取消（<paramref name="cancellationToken"/>）不算"失败"，照常向上抛出。
    /// 唯一的例外：云端以 403 拒绝登录（启用了手机号白名单、当前手机号不在名单内）时抛出
    /// <see cref="ForbiddenAccessException"/>——这是"无权使用这个云端"，静默放行的话后续请求没有令牌会 401，
    /// 客户端会把它当成登录过期而登出本地账号。
    /// </summary>
    private async Task EnsureRemoteTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var phoneNumber = await _authService.GetPhoneNumberAsync();

            var existing = await _authService.GetTokenAsync();
            if (!string.IsNullOrEmpty(existing))
            {
                if (TokenBelongsToPhone(existing, phoneNumber))
                {
                    return;
                }

                // 令牌属于另一个手机号（同一设备换号登录、或本地存储里残留旧令牌）：不能再用，
                // 否则新账号的数据会被同步进旧账号，旧账号的数据还会被映射成"自己的"。丢弃后按当前手机号重新换取
                await _authService.ClearTokenAsync();
            }

            if (string.IsNullOrEmpty(phoneNumber))
            {
                return;
            }

            using var content = new StringContent(
                JsonSerializer.Serialize(new { phoneNumber }),
                Encoding.UTF8,
                "application/json");

            using var loginResponse = await _httpClient.PostAsync("api/auth/login", content, cancellationToken);
            if (loginResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // 注意与 401 的区别（见 ForbiddenAccessException）：权限拒绝不能让用户被登出，也不能静默放行
                throw new ForbiddenAccessException(
                    await ReadServerMessageAsync(loginResponse, cancellationToken)
                    ?? "云端拒绝了登录请求（HTTP 403），可能是该手机号未获云端服务的使用许可");
            }

            if (!loginResponse.IsSuccessStatusCode)
            {
                return;
            }

            var loginJson = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消（例如云端刷新的超时到了）：必须继续往上抛，让整次请求立刻结束。
            // 吞掉的话会接着发后面的业务请求（它马上也会因取消而失败），白白多做一轮
            throw;
        }
        catch (ForbiddenAccessException)
        {
            // 云端明确拒绝这个手机号（手机号白名单）：要让调用方看到原因，不能当作"换取失败"吞掉
            throw;
        }
        catch
        {
            // 远端换取 token 失败（离线/服务不可用等）不影响本地功能，后续请求会再试
        }
    }

    /// <summary>
    /// 读取云端错误响应里的提示文案（控制器统一返回 { message }，全局异常中间件返回 { error }）；读不到返回 null。
    /// </summary>
    private static async Task<string?> ReadServerMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in new[] { "message", "error" })
            {
                if (doc.RootElement.TryGetProperty(name, out var element)
                    && element.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(element.GetString()))
                {
                    return element.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // 不是 JSON（例如反向代理 / WAF 返回的 HTML 403 页面）：用调用方的默认文案
        }

        return null;
    }

    /// <summary>
    /// 令牌是否属于当前登录的手机号。令牌里读不到手机号（旧令牌/非本服务签发），
    /// 或本地没有当前手机号时无法判断，按"属于"处理以保持原有行为。
    /// </summary>
    private static bool TokenBelongsToPhone(string token, string? currentPhoneNumber)
    {
        var tokenPhoneNumber = JwtClaimReader.TryReadPhoneNumber(token);
        return tokenPhoneNumber == null
            || string.IsNullOrEmpty(currentPhoneNumber)
            || tokenPhoneNumber == currentPhoneNumber;
    }
}
