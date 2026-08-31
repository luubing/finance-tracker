using System.Collections.Concurrent;
using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 同步队列服务实现
/// </summary>
public class SyncQueueService : ISyncQueueService
{
    private readonly ConcurrentQueue<Guid> _queue = new();
    private readonly ConcurrentDictionary<Guid, bool> _enqueued = new();

    public Task EnqueueAsync(Guid billId)
    {
        if (_enqueued.TryAdd(billId, true))
        {
            _queue.Enqueue(billId);
        }
        return Task.CompletedTask;
    }

    public Task<List<Guid>> DequeueAsync(int count = 10)
    {
        var result = new List<Guid>();

        for (int i = 0; i < count && _queue.TryDequeue(out var billId); i++)
        {
            _enqueued.TryRemove(billId, out _);
            result.Add(billId);
        }

        return Task.FromResult(result);
    }

    public Task<int> GetQueueLengthAsync()
    {
        return Task.FromResult(_queue.Count);
    }

    public Task ClearAsync()
    {
        while (_queue.TryDequeue(out _)) { }
        _enqueued.Clear();
        return Task.CompletedTask;
    }
}
