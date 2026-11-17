namespace FinanceTracker.Core.Services;

/// <summary>
/// 同步作用域：作用域内的保存不再由 DbContext 自动盖 <c>CreatedAt/UpdatedAt</c>，时间戳由同步代码自己决定。
///
/// 为什么需要：<c>UpdatedAt</c> 是"后写入优先"的裁决依据，语义应是"这一版内容的时间"，
/// 而不是"这一行最后一次被写库的时刻"。DbContext 平时对每次保存都盖 <c>UtcNow</c>，
/// 在同步里会出两类错：
/// 1. 仅仅把同步状态标成 Synced / Failed，也会把"编辑时间"改写成"同步时刻"；
/// 2. 采用云端版本（拉取合并）时，本地会把云端的更新时间盖成"本机现在"——两端时钟有偏差，
///    或另一台设备离线编辑后才同步时，之后云端更晚发生的编辑会被误判为"比本机旧"而永远被忽略。
///
/// 用法：在同步入口 <c>using var _ = SyncTimestampScope.Begin();</c>；
/// 需要采用云端时间戳的地方（拉取新增/合并）自己给 <c>CreatedAt/UpdatedAt</c> 赋值。
/// 基于 AsyncLocal，只影响当前异步流程；可嵌套。
/// </summary>
public static class SyncTimestampScope
{
    private static readonly AsyncLocal<int> Depth = new();

    /// <summary>当前异步流程是否处于同步作用域内</summary>
    public static bool IsActive => Depth.Value > 0;

    /// <summary>进入同步作用域（用 using 释放）</summary>
    public static IDisposable Begin()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Depth.Value--;
        }
    }
}
