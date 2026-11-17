namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 可以手动拨快的 <see cref="TimeProvider"/>：测试"过了多久之后会怎样"（退避期结束、请求期限到了）时注入，不用真的等。
/// <para>
/// 支持 <see cref="GetUtcNow"/> 与定时器（<see cref="CreateTimer"/>，<c>CancellationTokenSource(TimeSpan, TimeProvider)</c> 用的就是它）：
/// <see cref="Advance"/> 把"此刻"拨快后，到期的定时器在调用线程上同步触发。
/// 这样"期限到了就取消请求"这类行为可以确定地复现，不依赖真实时钟（真实时钟在慢机器上会让测试偶发失败）。
/// </para>
/// </summary>
public sealed class AdjustableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = new();
    private DateTimeOffset _utcNow = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>把"此刻"向后拨快一段时间，并同步触发其间到期的定时器</summary>
    public void Advance(TimeSpan by)
    {
        List<FakeTimer> due;
        lock (_gate)
        {
            _utcNow += by;
            due = _timers.Where(t => t.IsDue(_utcNow)).ToList();
        }

        // 回调可能反过来读取时间 / 新建定时器，所以放到锁外面执行
        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private void Remove(FakeTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class FakeTimer(AdjustableTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _dueAt;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._utcNow + dueTime;
                _period = period;
                return true;
            }
        }

        public bool IsDue(DateTimeOffset now) => !_disposed && _dueAt is { } dueAt && now >= dueAt;

        public void Fire()
        {
            lock (owner._gate)
            {
                if (_disposed || _dueAt is not { } dueAt)
                {
                    return;
                }

                // 一次性定时器触发后失效；周期定时器顺延到下一个周期
                _dueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : dueAt + _period;
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                _disposed = true;
                _dueAt = null;
            }

            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
