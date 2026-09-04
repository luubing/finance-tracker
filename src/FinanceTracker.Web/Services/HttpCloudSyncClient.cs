using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Models;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 云端同步客户端（通过 HTTP 调用 FinanceTracker.Api 的同步端点）
/// </summary>
public class HttpCloudSyncClient : ICloudSyncClient
{
    private readonly HttpService _http;
    private readonly string _baseUrl;

    public HttpCloudSyncClient(HttpService http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// 构造云端 API 地址。
    /// 基址约定为 API 根路径（如 "https://host" 或 "https://host/api"），
    /// 这里统一规整为 "https://host"，再补上 /api/ 前缀，避免出现 /api/api 这类重复路径。
    /// </summary>
    private string BuildApiUrl(string endpoint)
    {
        var baseUrl = _baseUrl;
        if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^4].TrimEnd('/');
        }
        return $"{baseUrl}/api/{endpoint.TrimStart('/')}";
    }

    public async Task<SyncPushResponse> PushBillsAsync(Guid userId, IReadOnlyList<BillSyncDto> bills, CancellationToken cancellationToken = default)
    {
        var request = new SyncPushRequest
        {
            Bills = bills.ToList()
        };

        var response = await _http.PostAsync<SyncPushResponse>(BuildApiUrl("sync/push"), request);
        return response ?? new SyncPushResponse();
    }

    public async Task<List<BillSyncDto>> PullBillsAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var request = new SyncPullRequest
        {
            Since = since
        };

        var response = await _http.PostAsync<SyncPullResponse>(BuildApiUrl("sync/pull"), request);
        return response?.Bills ?? new List<BillSyncDto>();
    }

    public async Task<CategorySyncPushResponse> PushCategoriesAsync(Guid userId, IReadOnlyList<CategorySyncDto> categories, CancellationToken cancellationToken = default)
    {
        var request = new CategorySyncPushRequest
        {
            Categories = categories.ToList()
        };

        var response = await _http.PostAsync<CategorySyncPushResponse>(BuildApiUrl("sync/categories/push"), request);
        return response ?? new CategorySyncPushResponse();
    }

    public async Task<List<CategorySyncDto>> PullCategoriesAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var request = new CategorySyncPullRequest
        {
            Since = since
        };

        var response = await _http.PostAsync<CategorySyncPullResponse>(BuildApiUrl("sync/categories/pull"), request);
        return response?.Categories ?? new List<CategorySyncDto>();
    }

    public async Task<PaymentChannelSyncPushResponse> PushPaymentChannelsAsync(Guid userId, IReadOnlyList<PaymentChannelSyncDto> channels, CancellationToken cancellationToken = default)
    {
        var request = new PaymentChannelSyncPushRequest
        {
            PaymentChannels = channels.ToList()
        };

        var response = await _http.PostAsync<PaymentChannelSyncPushResponse>(BuildApiUrl("sync/paymentchannels/push"), request);
        return response ?? new PaymentChannelSyncPushResponse();
    }

    public async Task<List<PaymentChannelSyncDto>> PullPaymentChannelsAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var request = new PaymentChannelSyncPullRequest
        {
            Since = since
        };

        var response = await _http.PostAsync<PaymentChannelSyncPullResponse>(BuildApiUrl("sync/paymentchannels/pull"), request);
        return response?.PaymentChannels ?? new List<PaymentChannelSyncDto>();
    }

    public async Task<LedgerSyncPushResponse> PushLedgersAsync(Guid userId, IReadOnlyList<LedgerSyncDto> ledgers, CancellationToken cancellationToken = default)
    {
        var request = new LedgerSyncPushRequest
        {
            Ledgers = ledgers.ToList()
        };

        var response = await _http.PostAsync<LedgerSyncPushResponse>(BuildApiUrl("sync/ledgers/push"), request);
        return response ?? new LedgerSyncPushResponse();
    }

    public async Task<List<LedgerSyncDto>> PullLedgersAsync(Guid userId, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var request = new LedgerSyncPullRequest
        {
            Since = since
        };

        var response = await _http.PostAsync<LedgerSyncPullResponse>(BuildApiUrl("sync/ledgers/pull"), request);
        return response?.Ledgers ?? new List<LedgerSyncDto>();
    }
}
