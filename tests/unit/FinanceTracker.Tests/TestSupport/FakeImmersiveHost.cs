using FinanceTracker.Web.Services;
using Microsoft.JSInterop;

namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 沉浸式协调器（<see cref="ImmersiveCoordinator"/>）测试用的假 JS 运行时：
/// 只支持 <c>import</c>，返回一个会记录所有调用的假模块。
/// </summary>
public sealed class FakeImmersiveJsRuntime : IJSRuntime
{
    /// <summary>模块被 import 时是否抛出"电路已断开"（模拟页面已卸载 / 连接断开）</summary>
    public bool ThrowDisconnectedOnImport { get; set; }

    /// <summary>模块被 import 时是否抛出 JS 异常（模拟脚本 404 / 语法不被引擎支持 / 模块加载失败）</summary>
    public bool ThrowScriptErrorOnImport { get; set; }

    public List<string> ImportedPaths { get; } = new();

    public FakeImmersiveModule Module { get; } = new();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier != "import")
        {
            throw new NotSupportedException($"FakeImmersiveJsRuntime 不支持 {identifier}");
        }

        if (ThrowDisconnectedOnImport)
        {
            throw new JSDisconnectedException("电路已断开");
        }

        if (ThrowScriptErrorOnImport)
        {
            throw new JSException("Importing a module script failed.");
        }

        ImportedPaths.Add((string)args![0]!);
        return new ValueTask<TValue>((TValue)(object)Module);
    }
}

/// <summary>记录对 immersive.js 模块的每一次调用（函数名 + 参数）</summary>
public sealed class FakeImmersiveModule : IJSObjectReference
{
    public List<(string Identifier, object?[] Args)> Calls { get; } = new();

    public bool IsDisposed { get; private set; }

    /// <summary>调用到这个函数时抛出 JS 异常（模拟脚本里这个函数执行出错）；调用本身仍会先被记录</summary>
    public string? ThrowScriptErrorOn { get; set; }

    /// <summary>某个函数被调用的次数</summary>
    public int CountOf(string identifier) => Calls.Count(call => call.Identifier == identifier);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Add((identifier, args ?? Array.Empty<object?>()));

        if (identifier == ThrowScriptErrorOn)
        {
            throw new JSException($"{identifier} 执行出错");
        }

        return new ValueTask<TValue>(default(TValue)!);
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>可由测试控制的安全区服务：改 <see cref="Current"/> 并用 <see cref="Raise"/> 模拟原生触发变化</summary>
public sealed class FakeSafeAreaService : ISafeAreaService
{
    public SafeAreaInsets? Current { get; set; }

    public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

    public event EventHandler<SafeAreaInsets>? Changed;

    /// <summary>设置新的安全区并触发 <see cref="Changed"/>（和原生实现一样：先更新缓存值，再通知）</summary>
    public void Raise(SafeAreaInsets insets)
    {
        Current = insets;
        Changed?.Invoke(this, insets);
    }
}

/// <summary>记录收到的状态栏图标风格</summary>
public sealed class FakeStatusBarService : IStatusBarService
{
    public List<StatusBarIconStyle> Styles { get; } = new();

    public void SetIconStyle(StatusBarIconStyle style) => Styles.Add(style);
}
